import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { config } from './config.js';
import { replyFor, resetConversation } from './codex.js';
import { startUsageDashboard } from './usage.js';
import { setStatus } from './status.js';
import {
  downloadImages,
  ensureAccount,
  getUpdates,
  loadSyncCursor,
  saveSyncCursor,
  sendText,
  type ILinkMessage,
} from './ilink.js';

type PendingBatch = { images: string[]; notes: string[] };
type PendingState = { batches: Record<string, PendingBatch> };

const MAX_PENDING_IMAGES = 12;
const pendingStatePath = path.join(config.stateDir, 'pending-batches.json');
let pendingState: PendingState = { batches: {} };

function textFrom(message: ILinkMessage): string {
  return (message.item_list ?? [])
    .filter((item) => item.type === 1)
    .map((item) => item.text_item?.text ?? '')
    .join('\n')
    .trim();
}

async function loadPendingState() {
  await mkdir(config.stateDir, { recursive: true });
  try { pendingState = JSON.parse(await readFile(pendingStatePath, 'utf8')) as PendingState; }
  catch { pendingState = { batches: {} }; }
}

async function savePendingState() {
  await writeFile(pendingStatePath, JSON.stringify(pendingState, null, 2), { encoding: 'utf8', mode: 0o600 });
}

function pendingFor(senderId: string): PendingBatch {
  return pendingState.batches[senderId] ?? { images: [], notes: [] };
}

async function appendPending(senderId: string, images: string[], note = ''): Promise<PendingBatch> {
  const current = pendingFor(senderId);
  if (current.images.length + images.length > MAX_PENDING_IMAGES) {
    throw new Error(`每批最多上传 ${MAX_PENDING_IMAGES} 张图片，请先发送“/回答”，或发送“/新题”重新开始。`);
  }
  const trimmedNote = note.trim();
  const next = {
    images: [...current.images, ...images],
    notes: trimmedNote ? [...current.notes, trimmedNote] : current.notes,
  };
  pendingState.batches[senderId] = next;
  await savePendingState();
  return next;
}

async function clearPending(senderId: string): Promise<void> {
  const current = pendingFor(senderId);
  delete pendingState.batches[senderId];
  await savePendingState();
  await Promise.all(current.images.map((imagePath) => rm(imagePath, { force: true }).catch(() => undefined)));
}

await loadPendingState();
setStatus('starting', '正在启动微信连接');
await startUsageDashboard();

const account = await ensureAccount(config.baseUrl, config.stateDir);
let cursor = await loadSyncCursor(config.stateDir);
let failures = 0;
const seenMessageIds = new Set<number>();

console.log(`微信 iLink 已连接：${account.accountId}`);
console.log('正在等待新消息；多图上传完成后发送“/回答”。');

while (true) {
  try {
    const update = await getUpdates(account, cursor);
    failures = 0;
    if (update.ret && update.ret !== 0) {
      if (update.errcode === -14) {
        setStatus('login-expired', '微信登录已过期，请点击重新扫码');
        await new Promise((resolve) => setTimeout(resolve, 30_000));
        continue;
      }
      throw new Error(`微信消息接口错误：${update.errmsg ?? update.ret}`);
    }
    setStatus('online', '微信已连接，等待消息', { lastPollAt: new Date().toISOString() });
    if (update.get_updates_buf !== undefined) {
      cursor = update.get_updates_buf;
      await saveSyncCursor(config.stateDir, cursor);
    }

    for (const message of update.msgs ?? []) {
      if (message.message_type !== 1 || message.message_state === 1) continue;
      if (message.message_id !== undefined) {
        if (seenMessageIds.has(message.message_id)) continue;
        seenMessageIds.add(message.message_id);
        if (seenMessageIds.size > 500) seenMessageIds.clear();
      }
      if (message.group_id && !config.allowGroups) continue;
      const senderId = message.from_user_id;
      if (!senderId || senderId !== account.ownerId) continue;

      let incoming = textFrom(message);
      const hasImage = (message.item_list ?? []).some((item) => item.type === 2 && item.image_item);
      if ((!incoming && !hasImage) || !message.context_token) continue;

      console.log(`收到消息：${incoming.slice(0, 80)}${hasImage ? ' [图片]' : ''}`);
      let downloadedPaths: string[] = [];
      try {
        if (/^[\/／]新题(?:\s|$)/.test(incoming)) {
          await clearPending(senderId);
          await resetConversation(senderId);
          incoming = incoming.replace(/^[\/／]新题\s*/, '').trim();
          if (!incoming && !hasImage) {
            await sendText(account, senderId, message.context_token, '已清除上一题和未提交图片。请发送新题；多张图片传完后发送“/回答”。');
            console.log('已清除该用户的答题上下文和待处理图片。');
            continue;
          }
        }

        const shouldAnswer = /^[\/／]回答(?:\s|$)/.test(incoming);
        if (shouldAnswer) incoming = incoming.replace(/^[\/／]回答\s*/, '').trim();

        const isFollowUp = /^[\/／]追问(?:\s|$)/.test(incoming);
        if (isFollowUp) {
          incoming = incoming.replace(/^[\/／]追问\s*/, '').trim();
          if (!incoming && !hasImage && pendingFor(senderId).images.length === 0) {
            await sendText(account, senderId, message.context_token, '请在“/追问”后面写上要追问的内容。');
            continue;
          }
        }

        let noteStored = false;
        if (hasImage) {
          downloadedPaths = await downloadImages(message, config.stateDir);
          if (!downloadedPaths.length) throw new Error('没有找到可读取的图片内容。');
          const batch = await appendPending(senderId, downloadedPaths, incoming);
          noteStored = Boolean(incoming);
          const receivedNow = downloadedPaths.length;
          downloadedPaths = [];
          if (!shouldAnswer) {
            await sendText(account, senderId, message.context_token, `已收到 ${receivedNow} 张，目前共 ${batch.images.length} 张。全部上传完成后发送“/回答”。`);
            console.log(`图片已暂存：共 ${batch.images.length} 张。`);
            continue;
          }
        } else if (!shouldAnswer && pendingFor(senderId).images.length > 0 && incoming) {
          const batch = await appendPending(senderId, [], incoming);
          await sendText(account, senderId, message.context_token, `补充说明已记录，目前共 ${batch.images.length} 张图片。全部上传完成后发送“/回答”。`);
          continue;
        }

        if (shouldAnswer) {
          if (incoming && !noteStored) await appendPending(senderId, [], incoming);
          const batch = pendingFor(senderId);
          if (!batch.images.length) {
            await sendText(account, senderId, message.context_token, '还没有收到待处理的图片。请先发送题图，全部上传后再发送“/回答”。');
            continue;
          }
          const prompt = batch.notes.length
            ? batch.notes.join('\n')
            : '请按图片顺序识别其中的高考英语题目并完整讲解。';
          console.log(`收到“/回答”，开始识别 ${batch.images.length} 张图片。`);
          setStatus('working', `正在处理 ${batch.images.length} 张图片`);
          const reply = await replyFor(senderId, prompt, batch.images, isFollowUp);
          await sendText(account, senderId, message.context_token, reply);
          await clearPending(senderId);
          console.log('批量图片回复已发送。');
          setStatus('online', '答案已发送，等待消息', { lastReplyAt: new Date().toISOString() });
          continue;
        }

        setStatus('working', '正在生成回复');
        const reply = await replyFor(senderId, incoming, [], isFollowUp);
        await sendText(account, senderId, message.context_token, reply);
        console.log('回复已发送。');
        setStatus('online', '回复已发送，等待消息', { lastReplyAt: new Date().toISOString() });
      } catch (error) {
        setStatus('reply-error', '本次处理失败，请查看日志或重新发题');
        console.error('处理消息失败：', error);
        const detail = error instanceof Error ? error.message : '未知错误';
        await sendText(account, senderId, message.context_token, `处理失败：${detail.slice(0, 180)}`);
      } finally {
        await Promise.all(downloadedPaths.map((imagePath) => rm(imagePath, { force: true }).catch(() => undefined)));
      }
    }
  } catch (error) {
    failures += 1;
    setStatus('reconnecting', '连接中断，正在自动重连');
    const delay = Math.min(30_000, 1000 * 2 ** Math.min(failures, 5));
    console.error(`微信连接异常，${Math.ceil(delay / 1000)} 秒后重试：`, error);
    await new Promise((resolve) => setTimeout(resolve, delay));
  }
}
