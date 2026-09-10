import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, existsSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { startSupervisor } from '../engine/controller.mjs';

const entry = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fake-child.mjs');
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
async function eventually(condition) {
  for (let i = 0; i < 70; i++) { if (await condition()) return; await sleep(100); }
  assert.fail('Timed out waiting for lifecycle condition');
}
function request(s, route = '/status', method = 'GET', body, token = s.token) {
  return fetch('http://127.0.0.1:' + s.port + route, { method, headers: { authorization: 'Bearer ' + token }, body, signal: AbortSignal.timeout(2000) });
}
test('controller: state isolation, authentication, pairing, single instance, crash recovery and stop', async (t) => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'wechat-desktop-controller-test-'));
  const base = { id: 'test-a', stateDir: path.join(root, 'a'), workspace: root, port: 0, entry, restartDelayMs: 50, env: { NEED_PAIR: '1' } };
  const a = await startSupervisor(base);
  let b;
  try {
    await eventually(async () => (await (await request(a)).json()).connection.phase === 'awaiting-pair');
    await t.test('unauthenticated requests cannot stop a Bot', async () => {
      assert.equal((await request(a, '/stop', 'POST', '', 'wrong')).status, 401);
      assert.equal((await request(a)).status, 200);
    });
    await t.test('pairing codes are validated and written only to this Bot', async () => {
      assert.equal((await request(a, '/pair', 'POST', 'not-digits')).status, 400);
      assert.equal((await request(a, '/pair', 'POST', '123456')).status, 200);
      assert.equal(readFileSync(path.join(base.stateDir, 'pairing-code.txt'), 'utf8'), '123456');
    });
    await t.test('duplicate port does not replace the live control token', async () => {
      const before = readFileSync(path.join(base.stateDir, 'desktop-control.json'), 'utf8');
      await assert.rejects(startSupervisor({ ...base, port: a.port }), { code: 'EADDRINUSE' });
      assert.equal(readFileSync(path.join(base.stateDir, 'desktop-control.json'), 'utf8'), before);
    });
    await t.test('crashed child restarts and another Bot remains isolated', async () => {
      b = await startSupervisor({ ...base, id: 'test-b', stateDir: path.join(root, 'b'), env: { CRASH_ONCE: '1' } });
      await eventually(async () => { const state = await (await request(b)).json(); return state.restarts >= 1 && state.connection.phase === 'online'; });
      assert.notEqual(a.port, b.port); assert.notEqual(a.token, b.token);
      assert.equal(existsSync(path.join(root, 'b', 'pairing-code.txt')), false);
      assert.equal((await request(b, '/stop', 'POST', '', a.token)).status, 401);
    });
    await t.test('stopping one Bot releases its lock without stopping another', async () => {
      assert.equal((await request(a, '/stop', 'POST', '')).status, 200);
      await eventually(() => !existsSync(path.join(root, 'a', 'desktop-control.json')));
      assert.equal((await request(b)).status, 200);
      assert.equal(JSON.parse(readFileSync(path.join(root, 'a', 'connection-status.json'))).phase, 'stopped');
    });
  } finally {
    await a.stop(); if (b) await b.stop();
    // Only the mkdtemp directory owned by this test is removed.
    assert.equal(path.dirname(root), os.tmpdir());
    assert.ok(path.basename(root).startsWith('wechat-desktop-controller-test-'));
    rmSync(root, { recursive: true, force: true });
  }
});
