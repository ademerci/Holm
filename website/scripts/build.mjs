import { readFile, writeFile, mkdir, rm, copyFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { createPages, dimensions, setSiteOrigin } from '../src/pages.mjs';
import { normalizeReleases } from '../src/releases.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const out = path.join(root, 'dist');
const originValue = process.env.SITE_URL || 'https://holmnotch.ch';
const origin = new URL(originValue);
if (
  !['https:', 'http:'].includes(origin.protocol) ||
  origin.pathname !== '/' ||
  origin.search ||
  origin.hash ||
  origin.username ||
  origin.password
)
  throw new Error('SITE_URL must be an origin; use BASE_PATH for a path');
setSiteOrigin(origin.origin);
const rawBase = process.env.BASE_PATH ?? '/';
if (!/^\/[a-zA-Z0-9/_-]*$/.test(rawBase)) throw new Error('Invalid BASE_PATH');
const base = rawBase === '/' ? '/' : `/${rawBase.split('/').filter(Boolean).join('/')}/`;
const snapshot = JSON.parse(await readFile(path.join(root, 'data/releases.json'), 'utf8'));
snapshot.releases = normalizeReleases(snapshot.releases);
if (!snapshot.releases.length || !Number.isFinite(Date.parse(snapshot.fetched_at)))
  throw new Error('A verified release snapshot is required');
const store = JSON.parse(await readFile(path.join(root, 'data/store.json'), 'utf8'));
if (store.url && !/^https:\/\/apps\.microsoft\.com\/detail\/[a-zA-Z0-9-]+(?:\/)?$/.test(store.url))
  throw new Error('Use a verified official Store listing');
// Only remove our generated output, never a user-provided path.
if (path.resolve(out) !== path.resolve(root, 'dist')) throw new Error('Unsafe output path');
await rm(out, { recursive: true, force: true });
await mkdir(path.join(out, 'assets'), { recursive: true });
const screenshotDirectory = path.join(root, 'src/assets/screenshots');
const manifest = JSON.parse(
  await readFile(path.join(screenshotDirectory, 'manifest.json'), 'utf8'),
);
const hash = (bytes) => createHash('sha256').update(bytes).digest('hex');
for (const entry of manifest.screenshots) {
  if (!/^[a-z-]+\.png$/.test(entry.name)) throw new Error('Invalid screenshot filename');
  const original = await readFile(path.join(root, '../docs', entry.name));
  const source = path.join(screenshotDirectory, entry.name);
  const bytes = await readFile(source);
  if (hash(original) !== entry.source_sha256 || hash(bytes) !== entry.output_sha256)
    throw new Error(`Regenerate transparent captures: ${entry.name} changed`);
  dimensions[entry.name.slice(0, -4)] = [bytes.readUInt32BE(16), bytes.readUInt32BE(20)];
  await copyFile(source, path.join(out, 'assets', entry.name));
}
// Small icons are checked-in derivatives of the authentic repository icon.
for (const size of [96, 192, 512])
  await copyFile(
    path.join(root, `src/assets/icon-${size}.png`),
    path.join(out, `assets/icon-${size}.png`),
  );
await copyFile(path.join(screenshotDirectory, 'expanded.png'), path.join(out, 'assets/social.png'));
for (const name of [
  'style.css',
  'noscript.css',
  'client.js',
  'releases.mjs',
  'home.css',
  'home.js',
])
  await copyFile(path.join(root, 'src', name), path.join(out, 'assets', name));
await writeFile(path.join(out, 'assets/releases.json'), JSON.stringify(snapshot));
const pages = createPages({ base, snapshot, store });
for (const [route, html] of pages) {
  const destination = path.join(out, route.endsWith('.html') ? route : `${route}index.html`);
  await mkdir(path.dirname(destination), { recursive: true });
  await writeFile(destination, html);
}
const urls = [...pages.keys()]
  .filter((p) => p !== '404.html')
  .map((p) => `${origin.origin}${base}${p}`);
await writeFile(
  path.join(out, 'sitemap.xml'),
  `<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${urls.map((url) => `<url><loc>${url}</loc></url>`).join('')}</urlset>`,
);
await writeFile(
  path.join(out, 'robots.txt'),
  `User-agent: *\nAllow: /\nSitemap: ${origin.origin}${base}sitemap.xml\n`,
);
await writeFile(path.join(out, '.nojekyll'), '');
await writeFile(
  path.join(out, '_headers'),
  `/*\n  X-Content-Type-Options: nosniff\n  Referrer-Policy: strict-origin-when-cross-origin\n  X-Frame-Options: DENY\n  Content-Security-Policy: default-src 'self'; img-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self' https://api.github.com; base-uri 'none'; frame-ancestors 'none'; form-action 'none'\n/assets/*\n  Cache-Control: public, max-age=3600\n`,
);
await writeFile(
  path.join(out, 'build.json'),
  JSON.stringify({ base, origin: origin.origin, pages: pages.size }),
);
console.log(
  `Built ${pages.size} pages → ${out}\nBase: ${base}\nNo framework runtime. No external fonts. No third-party scripts.`,
);
