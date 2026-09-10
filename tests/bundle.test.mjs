import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtempSync, existsSync, readFileSync, rmSync } from 'node:fs';
import { spawn } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

test('standalone bundle creates PNG login QR and reports actual login phase using a local fake server', async () => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'wechat-bundle-test-'));
  let requests = 0;
  const server = createServer((request, response) => {
    requests++;
    response.setHeader('Content-Type', 'application/json');
    response.end(JSON.stringify(request.url.includes('get_bot_qrcode')
      ? { qrcode: 'LOCAL-TEST-ONLY', qrcode_img_content: 'https://example.invalid/local-qr-fixture' }
      : { status: 'waiting' }));
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const reservation = createServer(); await new Promise((resolve) => reservation.listen(0, '127.0.0.1', resolve));
  const port = reservation.address().port; await new Promise((resolve) => reservation.close(resolve));
  const bundle = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../build/payload/bot.mjs');
  let output = '';
  const child = spawn(process.execPath, [bundle], {
    cwd: root, windowsHide: true,
    env: { ...process.env, BOT_STATE_DIR: root, BOT_ID: 'bundle-test', CODEX_WORKSPACE: root, CODEX_BIN: 'never-executed-in-this-test', WEIXIN_BASE_URL: 'http://127.0.0.1:' + server.address().port, USAGE_PORT: String(port) },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  child.stderr.on('data', (chunk) => { output += chunk; });
  child.stdout.on('data', (chunk) => { output += chunk; });
  try {
    const qr = path.join(root, 'wechat-login-qr.png');
    let png;
    for (let i = 0; i < 80; i++) {
      try {
        const candidate = readFileSync(qr);
        // File creation precedes the PNG encoder finishing its stream. Wait for IEND,
        // not merely path existence, so this test cannot race an in-progress write.
        if (candidate.length >= 20 && candidate.subarray(0, 8).toString('hex') === '89504e470d0a1a0a' && candidate.subarray(-8, -4).toString('ascii') === 'IEND') { png = candidate; break; }
      } catch (error) { if (error.code !== 'ENOENT') throw error; }
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    assert.ok(png, 'Complete local QR PNG was not ready: ' + output);
    const health = await (await fetch('http://127.0.0.1:' + port + '/health')).json();
    assert.equal(health.ok, true); assert.equal(health.botId, 'bundle-test');
    assert.equal(health.connection.phase, 'awaiting-scan');
    assert.ok(requests >= 1); assert.equal(existsSync(path.join(root, 'ilink-account.json')), false);
  } finally {
    child.kill(); await new Promise((resolve) => { if (child.exitCode !== null) resolve(); else child.once('exit', resolve); });
    await new Promise((resolve) => { server.close(resolve); server.closeAllConnections(); });
    assert.equal(path.dirname(root), os.tmpdir());
    rmSync(root, { recursive: true, force: true });
  }
});
