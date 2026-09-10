import { existsSync, writeFileSync } from 'node:fs';
import path from 'node:path';
const root = process.env.BOT_STATE_DIR;
if (process.env.CRASH_ONCE === '1' && !existsSync(path.join(root, 'crashed'))) {
  writeFileSync(path.join(root, 'crashed'), '1');
  process.exit(7);
}
writeFileSync(path.join(root, 'connection-status.json'), JSON.stringify({ phase: process.env.NEED_PAIR === '1' ? 'awaiting-pair' : 'online', botId: process.env.BOT_ID, message: 'test fixture' }));
setInterval(() => {}, 1000);
