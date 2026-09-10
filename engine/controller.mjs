import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';
import { appendFileSync, createWriteStream, existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export async function startSupervisor(options) {
  const stateDir = path.resolve(options.stateDir);
  mkdirSync(stateDir, { recursive: true });
  const token = randomBytes(32).toString('hex');
  const controlPath = path.join(stateDir, 'desktop-control.json');
  const logPath = path.join(stateDir, 'supervisor.log');
  let child, restartTimer, stopping = false, childStreams = [];
  let delay = options.restartDelayMs ?? 3000, restarts = 0;
  const startedAt = new Date().toISOString();
  const log = (message) => appendFileSync(logPath, `[${new Date().toISOString()}] ${message}\n`);
  const readStatus = () => {
    try { return JSON.parse(readFileSync(path.join(stateDir, 'connection-status.json'), 'utf8')); }
    catch { return { phase: 'starting', message: '正在启动' }; }
  };
  function rotate(file) {
    try {
      if (statSync(file).size > 5 * 1024 * 1024) {
        rmSync(file + '.previous', { force: true });
        renameSync(file, file + '.previous');
      }
    } catch (error) { if (error.code !== 'ENOENT') log('Log rotation deferred: ' + error.message); }
  }
  async function killChild() {
    const target = child;
    if (!target || target.exitCode !== null) return;
    await new Promise((resolve) => {
      target.once('exit', resolve);
      if (process.platform === 'win32') {
        // Only this supervisor's live child tree is targeted; never all node/codex processes.
        const killer = spawn(path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'taskkill.exe'),
          ['/PID', String(target.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
        killer.once('error', () => target.kill());
      } else { target.kill('SIGTERM'); }
      setTimeout(() => { if (target.exitCode === null) target.kill(); resolve(); }, 3500).unref();
    });
  }
  function launch() {
    if (stopping) return;
    rotate(logPath);
    const stdoutPath = path.join(stateDir, 'bridge.stdout.log');
    const stderrPath = path.join(stateDir, 'bridge.stderr.log');
    rotate(stdoutPath); rotate(stderrPath);
    const out = createWriteStream(stdoutPath, { flags: 'a' });
    const err = createWriteStream(stderrPath, { flags: 'a' });
    childStreams = [out, err];
    const began = Date.now();
    log('Starting managed Bot child.');
    writeFileSync(path.join(stateDir, 'connection-status.json'), JSON.stringify({ phase: 'starting', message: '正在连接微信', updatedAt: new Date().toISOString() }));
    child = spawn(process.execPath, [options.entry], {
      cwd: options.workspace, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
      env: { ...process.env, ...options.env, BOT_STATE_DIR: stateDir, BOT_ID: options.id },
    });
    child.stdout.pipe(out); child.stderr.pipe(err);
    let finished = false;
    const onEnd = (reason) => {
      if (finished) return;
      finished = true; child = undefined; out.end(); err.end();
      if (stopping) return;
      if (Date.now() - began > 300000) delay = options.restartDelayMs ?? 3000;
      log(reason + '; restart in ' + delay + ' ms.');
      writeFileSync(path.join(stateDir, 'connection-status.json'), JSON.stringify({ phase: 'restarting', message: '进程已退出，正在自动恢复', updatedAt: new Date().toISOString() }));
      restarts++;
      restartTimer = setTimeout(() => { restartTimer = undefined; launch(); }, delay);
      delay = Math.min(60000, delay * 2);
    };
    child.once('error', (error) => onEnd('Start error: ' + error.message));
    child.once('exit', (code) => onEnd('Child exit ' + code));
  }
  let stopPromise;
  function stop() {
    if (stopPromise) return stopPromise;
    stopPromise = (async () => {
      stopping = true;
      clearTimeout(restartTimer);
      await killChild();
      childStreams.forEach((stream) => stream.end());
      await new Promise((resolve) => { server.close(resolve); server.closeIdleConnections(); });
      try {
        if (JSON.parse(readFileSync(controlPath, 'utf8')).token === token) rmSync(controlPath, { force: true });
      } catch { /* Not ours, or already gone. */ }
      writeFileSync(path.join(stateDir, 'connection-status.json'), JSON.stringify({ phase: 'stopped', message: '已停止', updatedAt: new Date().toISOString() }));
      log('Supervisor stopped.');
    })();
    return stopPromise;
  }
  const server = createServer(async (request, response) => {
    const respond = (code, data) => { response.writeHead(code, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store', 'Connection': 'close' }); response.end(JSON.stringify(data)); };
    if (request.headers.authorization !== 'Bearer ' + token) { respond(401, { error: 'Unauthorized' }); return; }
    if (request.method === 'GET' && request.url === '/status') {
      respond(200, { kind: 'wechat-bot-desktop', id: options.id, pid: process.pid, childPid: child?.pid ?? null, startedAt, restarts, stopping, connection: readStatus() });
    } else if (request.method === 'POST' && request.url === '/stop') {
      respond(200, { ok: true }); setTimeout(() => { void stop(); }, 30);
    } else if (request.method === 'POST' && request.url === '/pair') {
      let raw = ''; let oversized = false;
      for await (const chunk of request) { raw += chunk; if (raw.length > 128) { oversized = true; break; } }
      if (oversized) { respond(413, { error: 'Too large' }); return; }
      if (!/^\d{4,12}$/.test(raw.trim())) { respond(400, { error: '请输入 4–12 位配对数字' }); return; }
      if (readStatus().phase !== 'awaiting-pair') { respond(409, { error: '当前无需配对数字，请查看登录状态' }); return; }
      writeFileSync(path.join(stateDir, 'pairing-code.txt'), raw.trim(), { mode: 0o600 });
      respond(200, { ok: true });
    } else { respond(404, { error: 'Not found' }); }
  });
  server.requestTimeout = 5000;
  server.headersTimeout = 5000;
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen({ host: '127.0.0.1', port: options.port, exclusive: true }, resolve);
  });
  // Port ownership is obtained before touching a prior supervisor's control token.
  if (existsSync(controlPath)) {
    try {
      const previous = JSON.parse(readFileSync(controlPath, 'utf8'));
      if (previous.pid && previous.pid !== process.pid) {
        try { process.kill(previous.pid, 0); server.close(); throw new Error('该数据目录可能仍被另一进程使用，请先停止原 Bot。'); }
        catch (error) { if (error.code !== 'ESRCH') throw error; }
      }
    } catch (error) { if (!(error instanceof SyntaxError)) { server.close(); throw error; } }
  }
  writeFileSync(controlPath, JSON.stringify({ token, pid: process.pid, port: server.address().port, id: options.id, startedAt }), { mode: 0o600 });
  launch();
  return { stop, port: server.address().port, token, get childPid() { return child?.pid; } };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const options = JSON.parse(readFileSync(process.argv[2], 'utf8').replace(/^\uFEFF/, ''));
  try {
    const supervisor = await startSupervisor(options);
    const stop = () => void supervisor.stop().then(() => process.exit(0));
    process.on('SIGINT', stop); process.on('SIGTERM', stop);
  } catch (error) {
    console.error(error.message);
    process.exitCode = error.code === 'EADDRINUSE' ? 0 : 1;
  }
}
