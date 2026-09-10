import { createRequire } from 'node:module';
import { copyFile, cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const root = path.dirname(fileURLToPath(import.meta.url));
const output = path.join(root, 'build', 'payload');
const deps = process.env.BOT_BUILD_DEPS || path.join(root, 'node_modules');
const { build } = createRequire(import.meta.url)(path.join(deps, 'esbuild'));
const nodeSource = process.env.BOT_NODE_SOURCE || process.execPath;
const skillSource = process.env.BOT_SKILL_SOURCE || path.join(root, 'skills', 'gaokao-english');
await mkdir(output, { recursive: true });
const bundled = await build({
  absWorkingDir: root,
  tsconfigRaw: { compilerOptions: { target: 'ES2023', module: 'ESNext' } },
  entryPoints: [path.join(root, 'engine', 'src', 'index.ts')],
  outfile: path.join(output, 'bot.mjs'), bundle: true, platform: 'node', target: 'node24',
  format: 'esm', nodePaths: [deps], metafile: true,
  banner: { js: 'import { createRequire as __createRequire } from "node:module"; const require = __createRequire(import.meta.url);' },
});
await copyFile(nodeSource, path.join(output, 'node.exe'));
await copyFile(path.join(path.dirname(nodeSource), 'LICENSE'), path.join(output, 'NODE-LICENSE.txt'));
for (const name of ['controller.mjs', 'legacy-handoff.ps1']) await copyFile(path.join(root, 'engine', name), path.join(output, name));
await cp(skillSource, path.join(output, 'skills', 'gaokao-english'), { recursive: true, dereference: true });
await mkdir(path.join(output, 'source'), { recursive: true });
// Whitelist source files. No .env, login tokens, conversations, images or runtime backups are packaged.
for (const name of ['Manager.cs', 'app.manifest', 'build.mjs', 'Build.ps1', 'package.json', 'README.md']) await copyFile(path.join(root, name), path.join(output, 'source', name));
await cp(path.join(root, 'engine'), path.join(output, 'source', 'engine'), { recursive: true });
await cp(path.join(root, 'tests'), path.join(output, 'source', 'tests'), { recursive: true });
await cp(skillSource, path.join(output, 'source', 'skills', 'gaokao-english'), { recursive: true, dereference: true });
await mkdir(path.join(output, 'licenses'), { recursive: true });
const packages = new Map();
for (const input of Object.keys(bundled.metafile.inputs)) {
  const absolute = path.resolve(root, input).replaceAll('\\', '/');
  const marker = absolute.lastIndexOf('/node_modules/');
  const match = marker >= 0 && absolute.slice(marker + 14).match(/^((?:@[^/]+\/)?[^/]+)/);
  if (match) packages.set(match[1], absolute.slice(0, marker + 14) + match[1]);
}
for (const [name, packageDir] of packages) {
  for (const file of ['LICENSE', 'LICENSE.md', 'LICENSE.txt', 'license', 'LICENCE', 'package.json']) {
    try { await copyFile(path.join(packageDir, file), path.join(output, 'licenses', name.replaceAll('/', '-') + '-' + file)); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
}
const skill = await readFile(path.join(skillSource, 'SKILL.md'));
await writeFile(path.join(output, 'build-info.json'), JSON.stringify({ version: '1.0.0', builtAt: new Date().toISOString(), node: process.version, skillSha256: createHash('sha256').update(skill).digest('hex'), dependencies: [...packages.keys()] }, null, 2));
console.log(JSON.stringify({ ok: true, payload: output, dependencies: [...packages.keys()], skillSha256: createHash('sha256').update(skill).digest('hex') }));
