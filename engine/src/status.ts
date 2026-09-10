import { writeFileSync, renameSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { config } from './config.js';

export const connectionStatus: Record<string, unknown> = {
  botId: config.botId, phase: 'starting', message: '正在启动',
  startedAt: new Date().toISOString(), lastPollAt: null, lastReplyAt: null,
};

export function setStatus(phase: string, message: string, extra: Record<string, unknown> = {}) {
  Object.assign(connectionStatus, extra, { phase, message, updatedAt: new Date().toISOString() });
  try {
    mkdirSync(config.stateDir, { recursive: true });
    const target = path.join(config.stateDir, 'connection-status.json');
    writeFileSync(target + '.tmp', JSON.stringify(connectionStatus), { mode: 0o600 });
    renameSync(target + '.tmp', target);
  } catch (error) { console.warn('状态暂时无法保存。'); }
}
