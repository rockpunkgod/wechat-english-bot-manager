import { createServer } from 'node:http';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { config } from './config.js';
import { connectionStatus } from './status.js';

type TokenUsage = {
  inputTokens?: number;
  cachedInputTokens?: number;
  outputTokens?: number;
  reasoningTokens?: number;
  totalTokens?: number;
};

type UsageRecord = TokenUsage & {
  at: string;
  model: string;
  inputChars: number;
  outputChars: number;
  images: number;
  elapsedMs: number;
  source: 'codex-reported' | 'estimated';
  costUsd: number;
};

type UsageState = { records: UsageRecord[] };
type ExchangeRateState = {
  rate?: number;
  fetchedAt?: string;
  sourceDate?: string;
  error?: string;
};
const usagePath = path.join(config.stateDir, 'usage.json');
const exchangeRatePath = path.join(config.stateDir, 'usd-cny-rate.json');
const dashboardStartedAt = Date.now();
const LUNA_INPUT_USD_PER_MILLION = 0.20;
const LUNA_CACHED_INPUT_USD_PER_MILLION = 0.02;
const LUNA_OUTPUT_USD_PER_MILLION = 1.20;
let usageState: UsageState = { records: [] };
let exchangeRateState: ExchangeRateState = {};
let usageLoaded = false;

export type InvocationMetrics = TokenUsage & {
  inputChars: number;
  outputChars: number;
  images: number;
  elapsedMs: number;
  source: 'codex-reported' | 'estimated';
};

async function loadUsage() {
  if (usageLoaded) return;
  await mkdir(config.stateDir, { recursive: true });
  try {
    const parsed = JSON.parse(await readFile(usagePath, 'utf8')) as UsageState;
    usageState = { records: Array.isArray(parsed.records) ? parsed.records : [] };
  } catch { /* First run has no history. */ }
  try {
    const parsed = JSON.parse(await readFile(exchangeRatePath, 'utf8')) as ExchangeRateState;
    if (positiveNumber(parsed.rate)) exchangeRateState = parsed;
  } catch { /* First run has no cached exchange rate. */ }
  usageLoaded = true;
}

async function refreshExchangeRate(): Promise<void> {
  try {
    const response = await fetch('https://api.exchangerate.fun/latest?base=USD', {
      headers: { Accept: 'application/json', 'User-Agent': 'weixin-codex-usage-dashboard/1.0' },
      signal: AbortSignal.timeout(5_000),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json() as { date?: string; rates?: Record<string, unknown> };
    const rate = positiveNumber(data.rates?.CNY);
    if (!rate || rate < 5 || rate > 10) throw new Error('返回的 USD/CNY 汇率无效');
    exchangeRateState = {
      rate,
      fetchedAt: new Date().toISOString(),
      sourceDate: data.date,
    };
    await writeFile(exchangeRatePath, JSON.stringify(exchangeRateState, null, 2), { encoding: 'utf8', mode: 0o600 });
  } catch (error) {
    exchangeRateState = {
      ...exchangeRateState,
      error: error instanceof Error ? error.message : String(error),
    };
  }
}

function positiveNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : undefined;
}

function estimatedTokens(characters: number): number {
  // A deliberately conservative display estimate for Chinese/English mixed text.
  // It excludes the hidden system prompt, images and reasoning tokens.
  return Math.max(1, Math.ceil(characters / 1.5));
}

function lunaCostUsd(inputTokens = 0, outputTokens = 0, cachedInputTokens = 0): number {
  const cached = Math.min(inputTokens, Math.max(0, cachedInputTokens));
  const uncached = Math.max(0, inputTokens - cached);
  return (
    uncached * LUNA_INPUT_USD_PER_MILLION
    + cached * LUNA_CACHED_INPUT_USD_PER_MILLION
    + outputTokens * LUNA_OUTPUT_USD_PER_MILLION
  ) / 1_000_000;
}

export async function recordUsage(metrics: InvocationMetrics): Promise<void> {
  await loadUsage();
  const reported = metrics.totalTokens !== undefined || metrics.inputTokens !== undefined || metrics.outputTokens !== undefined;
  const inputTokens = reported ? metrics.inputTokens : estimatedTokens(metrics.inputChars);
  const outputTokens = reported ? metrics.outputTokens : estimatedTokens(metrics.outputChars);
  const totalTokens = reported
    ? metrics.totalTokens ?? (inputTokens ?? 0) + (outputTokens ?? 0) + (metrics.reasoningTokens ?? 0)
    : (inputTokens ?? 0) + (outputTokens ?? 0);
  const costUsd = lunaCostUsd(inputTokens, outputTokens, metrics.cachedInputTokens);
  usageState.records.push({
    at: new Date().toISOString(),
    model: config.model,
    inputChars: metrics.inputChars,
    outputChars: metrics.outputChars,
    images: metrics.images,
    elapsedMs: metrics.elapsedMs,
    source: reported ? 'codex-reported' : 'estimated',
    inputTokens,
    cachedInputTokens: metrics.cachedInputTokens,
    outputTokens,
    reasoningTokens: metrics.reasoningTokens,
    totalTokens,
    costUsd,
  });
  usageState.records = usageState.records.slice(-1000);
  await writeFile(usagePath, JSON.stringify(usageState, null, 2), { encoding: 'utf8', mode: 0o600 });
}

function dashboardHtml() {
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>微信答疑 Bot 用量</title><style>
body{font-family:"Microsoft YaHei",system-ui,sans-serif;background:#f6f7fb;color:#171a22;margin:0;padding:28px;max-width:1080px}h1{margin:0 0 8px}.muted{color:#606777}.cards{display:flex;gap:12px;flex-wrap:wrap;margin:22px 0}.card{background:#fff;border-radius:12px;padding:16px;min-width:150px;box-shadow:0 1px 4px #00000012}.number{font-size:27px;font-weight:700;margin-top:6px}table{width:100%;border-collapse:collapse;background:#fff;border-radius:12px;overflow:hidden}th,td{padding:10px;text-align:left;border-bottom:1px solid #edf0f5;font-size:14px}th{background:#f0f3f8}#note{margin:16px 0;padding:12px;background:#fff4d6;border-radius:10px;color:#715600}</style></head><body><h1>微信答疑 Bot 用量</h1><p class="muted">每 30 秒自动刷新。仅本机可访问。</p><div id="cards" class="cards"></div><div id="note"></div><table><thead><tr><th>时间</th><th>模型</th><th>输入</th><th>输出</th><th>图片</th><th>耗时</th><th>API 等价成本</th><th>来源</th></tr></thead><tbody id="rows"></tbody></table><script>
const n=x=>Number(x||0).toLocaleString();const usd=x=>'$'+Number(x||0).toFixed(6);const cny=(x,rate)=>rate?'¥'+(Number(x||0)*rate).toFixed(4):'汇率获取中';const cost=x=>{const i=Number(x.inputTokens||0),c=Math.min(i,Number(x.cachedInputTokens||0)),o=Number(x.outputTokens||0);return x.costUsd??((i-c)*.2+c*.02+o*1.2)/1e6};async function render(){const d=await fetch('/api/usage').then(x=>x.json());const r=d.records||[],fx=d.exchangeRate||{},rate=Number(fx.rate||0);const total=r.reduce((s,x)=>s+Number(x.totalTokens||0),0);const totalCost=r.reduce((s,x)=>s+cost(x),0);const today=new Date().toDateString();const todays=r.filter(x=>new Date(x.at).toDateString()===today);const todayTotal=todays.reduce((s,x)=>s+Number(x.totalTokens||0),0);const todayCost=todays.reduce((s,x)=>s+cost(x),0);document.querySelector('#cards').innerHTML='<div class="card">累计请求<div class="number">'+n(r.length)+'</div></div><div class="card">累计 token<div class="number">'+n(total)+'</div></div><div class="card">今日 token<div class="number">'+n(todayTotal)+'</div></div><div class="card">累计 API 等价成本<div class="number">'+cny(totalCost,rate)+'</div><div class="muted">'+usd(totalCost)+'</div></div><div class="card">今日 API 等价成本<div class="number">'+cny(todayCost,rate)+'</div><div class="muted">'+usd(todayCost)+'</div></div>';const accuracy=r.some(x=>x.source==='estimated')?'部分记录为可见文字估算，不含系统提示、图片与推理 token。':'token 均来自 Codex 返回的 usage 字段。';const fxText=rate?' 当前 USD/CNY='+rate.toFixed(4)+'，获取时间 '+new Date(fx.fetchedAt).toLocaleString()+'。':' 当前未获取到汇率，暂只显示美元。';document.querySelector('#note').textContent='Luna medium 按公开 API 单价换算：输入 $0.20、缓存输入 $0.02、输出 $1.20 / 百万 token。'+fxText+accuracy+' 此数值不是 ChatGPT 套餐实际账单。';document.querySelector('#rows').innerHTML=r.slice(-100).reverse().map(x=>'<tr><td>'+new Date(x.at).toLocaleString()+'</td><td>'+x.model+' medium</td><td>'+n(x.inputTokens)+'</td><td>'+n(x.outputTokens)+'</td><td>'+n(x.images)+'</td><td>'+Math.ceil(x.elapsedMs/1000)+' 秒</td><td>'+cny(cost(x),rate)+'<br><span class="muted">'+usd(cost(x))+'</span></td><td>'+ (x.source==='codex-reported'?'准确':'估算')+'</td></tr>').join('')};render();setInterval(render,30000);</script></body></html>`;
}

export async function startUsageDashboard(): Promise<void> {
  await loadUsage();
  const port = config.usagePort;
  const server = createServer((request, response) => {
    if (request.url === '/health') {
      response.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
      response.end(JSON.stringify({ ok: true, botId: config.botId, connection: connectionStatus, uptimeSeconds: Math.floor((Date.now() - dashboardStartedAt) / 1000) }));
      return;
    }
    if (request.url === '/api/usage') {
      response.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
      response.end(JSON.stringify({ ...usageState, exchangeRate: exchangeRateState }));
      return;
    }
    response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    response.end(dashboardHtml());
  });
  server.on('error', (error) => console.warn(`用量面板未启动：${error.message}`));
  server.listen(port, '127.0.0.1', () => {
    console.log(`用量面板：http://127.0.0.1:${port}`);
    void refreshExchangeRate();
  });
  setInterval(() => void refreshExchangeRate(), 60 * 60_000).unref();
}

export function usageFromEvent(value: unknown): TokenUsage | undefined {
  if (!value || typeof value !== 'object') return undefined;
  const object = value as Record<string, unknown>;
  const candidate = object.usage;
  if (candidate && typeof candidate === 'object') {
    const usage = candidate as Record<string, unknown>;
    const inputDetail = usage.input_tokens_details as Record<string, unknown> | undefined;
    const detail = usage.output_tokens_details as Record<string, unknown> | undefined;
    const result: TokenUsage = {
      inputTokens: positiveNumber(usage.input_tokens),
      cachedInputTokens: positiveNumber(inputDetail?.cached_tokens) ?? positiveNumber(usage.cached_input_tokens),
      outputTokens: positiveNumber(usage.output_tokens),
      totalTokens: positiveNumber(usage.total_tokens),
      reasoningTokens: positiveNumber(detail?.reasoning_tokens),
    };
    if (Object.values(result).some((item) => item !== undefined)) return result;
  }
  for (const child of Object.values(object)) {
    const found = usageFromEvent(child);
    if (found) return found;
  }
  return undefined;
}
