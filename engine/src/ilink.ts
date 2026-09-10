import crypto from 'node:crypto';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import QRCode from 'qrcode';
import { setStatus } from './status.js';

const APP_ID = 'bot';
const CHANNEL_VERSION = '2.4.6';
const CLIENT_VERSION = (2 << 16) | (4 << 8) | 6;
const BOT_TYPE = '3';

export interface ILinkMessageItem {
  type?: number;
  text_item?: { text?: string };
  image_item?: {
    aeskey?: string;
    media?: ILinkMedia;
    thumb_media?: ILinkMedia;
  };
}

export interface ILinkMedia {
  aes_key?: string;
  encrypt_query_param?: string;
  full_url?: string;
}

export interface ILinkMessage {
  message_id?: number;
  from_user_id?: string;
  to_user_id?: string;
  group_id?: string;
  message_type?: number;
  message_state?: number;
  context_token?: string;
  item_list?: ILinkMessageItem[];
}

export interface UpdateResponse {
  ret?: number;
  errcode?: number;
  errmsg?: string;
  msgs?: ILinkMessage[];
  get_updates_buf?: string;
  longpolling_timeout_ms?: number;
}

export interface ILinkAccount {
  token: string;
  accountId: string;
  ownerId: string;
  baseUrl: string;
}

const WEIXIN_CDN_BASE_URL = 'https://novac2c.cdn.weixin.qq.com/c2c';
const MAX_IMAGE_BYTES = 12 * 1024 * 1024;

function imageExtension(buffer: Buffer): string {
  if (buffer.subarray(0, 3).equals(Buffer.from([0xff, 0xd8, 0xff]))) return '.jpg';
  if (buffer.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]))) return '.png';
  if (buffer.subarray(0, 6).toString('ascii') === 'GIF87a' || buffer.subarray(0, 6).toString('ascii') === 'GIF89a') return '.gif';
  if (buffer.subarray(0, 4).toString('ascii') === 'RIFF' && buffer.subarray(8, 12).toString('ascii') === 'WEBP') return '.webp';
  return '.jpg';
}

function decodeAesKey(image: NonNullable<ILinkMessageItem['image_item']>): Buffer | undefined {
  if (image.aeskey) return Buffer.from(image.aeskey, 'hex');
  const encoded = image.media?.aes_key;
  if (!encoded) return undefined;
  const decoded = Buffer.from(encoded, 'base64');
  if (decoded.length === 16) return decoded;
  if (decoded.length === 32 && /^[0-9a-fA-F]{32}$/.test(decoded.toString('ascii'))) {
    return Buffer.from(decoded.toString('ascii'), 'hex');
  }
  throw new Error('微信图片的加密密钥格式无效。');
}

/** Download and decrypt inbound WeChat images into short-lived local files for Codex vision input. */
export async function downloadImages(message: ILinkMessage, stateDir: string): Promise<string[]> {
  const imageItems = (message.item_list ?? []).filter((item) => item.type === 2 && item.image_item);
  const saved: string[] = [];
  const imageDir = path.join(stateDir, 'inbound-images');
  await mkdir(imageDir, { recursive: true });

  try {
    for (const item of imageItems.slice(0, 4)) {
      const image = item.image_item!;
      const media = image.media;
      if (!media?.full_url && !media?.encrypt_query_param) continue;
      const url = media.full_url || `${WEIXIN_CDN_BASE_URL}/download?encrypted_query_param=${encodeURIComponent(media.encrypt_query_param!)}`;
      const response = await fetch(url);
      if (!response.ok) throw new Error(`微信图片下载失败（HTTP ${response.status}）。`);
      const length = Number(response.headers.get('content-length') ?? 0);
      if (length > MAX_IMAGE_BYTES) throw new Error('图片超过 12 MB，请发送更小或更清晰的截图。');
      const encrypted = Buffer.from(await response.arrayBuffer());
      if (encrypted.length > MAX_IMAGE_BYTES) throw new Error('图片超过 12 MB，请发送更小或更清晰的截图。');
      const key = decodeAesKey(image);
      let plaintext = encrypted;
      if (key) {
        const decipher = crypto.createDecipheriv('aes-128-ecb', key, null);
        plaintext = Buffer.concat([decipher.update(encrypted), decipher.final()]);
      }
      if (!plaintext.length) throw new Error('微信图片内容为空。');
      const filePath = path.join(imageDir, `${crypto.randomUUID()}${imageExtension(plaintext)}`);
      await writeFile(filePath, plaintext, { mode: 0o600 });
      saved.push(filePath);
    }
  } catch (error) {
    await Promise.all(saved.map((filePath) => rm(filePath, { force: true }).catch(() => undefined)));
    throw error;
  }

  return saved;
}

function baseInfo() {
  return { channel_version: CHANNEL_VERSION, bot_agent: 'CodexWeixinBridge/0.2.0' };
}

function commonHeaders(): Record<string, string> {
  return {
    'iLink-App-Id': APP_ID,
    'iLink-App-ClientVersion': String(CLIENT_VERSION),
  };
}

function authHeaders(token?: string): Record<string, string> {
  const value = crypto.randomBytes(4).readUInt32BE(0);
  return {
    ...commonHeaders(),
    'Content-Type': 'application/json',
    AuthorizationType: 'ilink_bot_token',
    'X-WECHAT-UIN': Buffer.from(String(value), 'utf8').toString('base64'),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };
}

function endpoint(baseUrl: string, value: string): string {
  return new URL(value, baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`).toString();
}

async function postJson<T>(baseUrl: string, route: string, body: unknown, token?: string, timeoutMs = 15_000): Promise<T> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(endpoint(baseUrl, route), {
      method: 'POST',
      headers: authHeaders(token),
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    const text = await response.text();
    if (!response.ok) throw new Error(`${route} HTTP ${response.status}: ${text.slice(0, 300)}`);
    return JSON.parse(text) as T;
  } finally {
    clearTimeout(timer);
  }
}

async function getJson<T>(baseUrl: string, route: string, timeoutMs = 35_000): Promise<T> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(endpoint(baseUrl, route), {
      headers: commonHeaders(),
      signal: controller.signal,
    });
    const text = await response.text();
    if (!response.ok) throw new Error(`${route} HTTP ${response.status}: ${text.slice(0, 300)}`);
    return JSON.parse(text) as T;
  } finally {
    clearTimeout(timer);
  }
}

function accountPath(stateDir: string) {
  return path.join(stateDir, 'ilink-account.json');
}

export async function ensureAccount(baseUrl: string, stateDir: string): Promise<ILinkAccount> {
  await mkdir(stateDir, { recursive: true });
  try {
    const saved = JSON.parse(await readFile(accountPath(stateDir), 'utf8')) as ILinkAccount;
    if (saved.token && saved.accountId && saved.ownerId && saved.baseUrl) return saved;
  } catch { /* First login. */ }

  const qrPath = path.join(stateDir, 'wechat-login-qr.png');
  async function requestQr() {
    const next = await postJson<{ qrcode: string; qrcode_img_content: string }>(
      baseUrl,
      `ilink/bot/get_bot_qrcode?bot_type=${BOT_TYPE}`,
      { local_token_list: [] },
    );
    if (!next.qrcode || !next.qrcode_img_content) throw new Error('微信服务器没有返回登录二维码。');
    await QRCode.toFile(qrPath, next.qrcode_img_content, { width: 520, margin: 2 });
    setStatus('awaiting-scan', '请扫描二维码并在手机确认', { qrCreatedAt: new Date().toISOString() });
    console.log(`LOGIN_QR=${qrPath}`);
    console.log('请使用要接入的微信账号扫码并在手机上确认。');
    return next;
  }

  let qr = await requestQr();

  let pollingBaseUrl = baseUrl;
  let verifyCode: string | undefined;
  const deadline = Date.now() + 8 * 60_000;
  while (Date.now() < deadline) {
    try {
      let route = `ilink/bot/get_qrcode_status?qrcode=${encodeURIComponent(qr.qrcode)}`;
      if (verifyCode) route += `&verify_code=${encodeURIComponent(verifyCode)}`;
      const status = await getJson<{
        status: string;
        bot_token?: string;
        ilink_bot_id?: string;
        ilink_user_id?: string;
        baseurl?: string;
        redirect_host?: string;
      }>(pollingBaseUrl, route);

      if (status.status === 'scaned') {
        setStatus('awaiting-confirmation', '已扫码，请在手机确认');
        verifyCode = undefined;
        console.log('二维码已扫描，等待手机确认。');
      }
      if (status.status === 'scaned_but_redirect' && status.redirect_host) {
        pollingBaseUrl = `https://${status.redirect_host}`;
      }
      if (status.status === 'need_verifycode') {
        setStatus('awaiting-pair', '请在程序中输入手机显示的配对数字');
        const pairPath = path.join(stateDir, 'pairing-code.txt');
        while (Date.now() < deadline) {
          try {
            const value = (await readFile(pairPath, 'utf8')).trim();
            await rm(pairPath, { force: true });
            if (/^\d{4,12}$/.test(value)) { verifyCode = value; break; }
          } catch { /* Wait for the desktop pairing field. */ }
          await new Promise((resolve) => setTimeout(resolve, 750));
        }
        continue;
      }
      if (status.status === 'expired') {
        console.log('二维码已过期，正在自动刷新。');
        qr = await requestQr();
        pollingBaseUrl = baseUrl;
        verifyCode = undefined;
        continue;
      }
      if (status.status === 'verify_code_blocked') throw new Error('配对数字多次输入错误，请稍后重试。');
      if (status.status === 'confirmed') {
        if (!status.bot_token || !status.ilink_bot_id || !status.ilink_user_id) {
          throw new Error('扫码成功，但微信服务器返回的登录信息不完整。');
        }
        const account: ILinkAccount = {
          token: status.bot_token,
          accountId: status.ilink_bot_id,
          ownerId: status.ilink_user_id,
          baseUrl: status.baseurl || pollingBaseUrl,
        };
        await writeFile(accountPath(stateDir), JSON.stringify(account, null, 2), { encoding: 'utf8', mode: 0o600 });
        setStatus('connecting', '登录成功，正在连接微信');
        console.log('微信扫码登录成功，凭据已安全保存在本机。');
        return account;
      }
    } catch (error) {
      if (!(error instanceof Error) || error.name !== 'AbortError') throw error;
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error('等待微信扫码登录超时。');
}

export async function getUpdates(account: ILinkAccount, cursor: string): Promise<UpdateResponse> {
  try {
    return await postJson<UpdateResponse>(account.baseUrl, 'ilink/bot/getupdates', {
      get_updates_buf: cursor,
      base_info: baseInfo(),
    }, account.token, 40_000);
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') {
      return { ret: 0, msgs: [], get_updates_buf: cursor };
    }
    throw error;
  }
}

export async function sendText(account: ILinkAccount, to: string, contextToken: string, text: string): Promise<void> {
  const result = await postJson<{ ret?: number; errmsg?: string }>(account.baseUrl, 'ilink/bot/sendmessage', {
    msg: {
      from_user_id: '',
      to_user_id: to,
      client_id: `codex-${crypto.randomUUID()}`,
      message_type: 2,
      message_state: 2,
      context_token: contextToken,
      item_list: [{ type: 1, text_item: { text: text.slice(0, 4000) } }],
    },
    base_info: baseInfo(),
  }, account.token);
  if (result.ret && result.ret !== 0) throw new Error(`微信发送失败：${result.errmsg ?? result.ret}`);
}

export async function loadSyncCursor(stateDir: string): Promise<string> {
  try { return (await readFile(path.join(stateDir, 'ilink-sync.txt'), 'utf8')).trim(); }
  catch { return ''; }
}

export async function saveSyncCursor(stateDir: string, cursor: string): Promise<void> {
  await writeFile(path.join(stateDir, 'ilink-sync.txt'), cursor, { encoding: 'utf8', mode: 0o600 });
}
