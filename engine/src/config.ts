import 'dotenv/config';
import path from 'node:path';
import { z } from 'zod';

const schema = z.object({
  WEIXIN_BASE_URL: z.string().url().default('https://ilinkai.weixin.qq.com'),
  CODEX_BIN: z.string().min(1).default('codex'),
  CODEX_MODEL: z.string().min(1).default('gpt-5.6-luna'),
  CODEX_REASONING_EFFORT: z.enum(['none', 'low', 'medium', 'high', 'xhigh', 'max']).default('medium'),
  CODEX_WORKSPACE: z.string().min(1),
  CODEX_SANDBOX: z.enum(['read-only', 'workspace-write']).default('read-only'),
  WECHAT_ALLOW_GROUPS: z.enum(['true', 'false']).default('false'),
  USAGE_PORT: z.coerce.number().int().min(1).max(65535).default(3210),
  BOT_STATE_DIR: z.string().default('.runtime'),
  BOT_ID: z.string().default('bot'),
  BOT_SKILL_PATH: z.string().default('')
});

const raw = schema.parse(process.env);

export const config = {
  baseUrl: raw.WEIXIN_BASE_URL,
  codexBin: raw.CODEX_BIN,
  model: raw.CODEX_MODEL,
  reasoningEffort: raw.CODEX_REASONING_EFFORT,
  workspace: path.resolve(raw.CODEX_WORKSPACE),
  sandbox: raw.CODEX_SANDBOX,
  allowGroups: raw.WECHAT_ALLOW_GROUPS === 'true',
  usagePort: raw.USAGE_PORT,
  stateDir: path.resolve(raw.BOT_STATE_DIR),
  botId: raw.BOT_ID,
  skillPath: raw.BOT_SKILL_PATH
};
