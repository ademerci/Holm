import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, readdir, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';
import { createPages, setSiteOrigin } from '../src/pages.mjs';
const root = fileURLToPath(new URL('../dist/', import.meta.url));
const { base, origin } = JSON.parse(await readFile(path.join(root, 'build.json'), 'utf8'));
async function files(dir) {
  const all = [];
  for (const item of await readdir(dir, { withFileTypes: true })) {
    const file = path.join(dir, item.name);
    if (item.isDirectory()) all.push(...(await files(file)));
    else all.push(file);
  }
  return all;
}
const pages = (await files(root)).filter((f) => f.endsWith('.html'));
const textByFile = new Map(
  await Promise.all(pages.map(async (f) => [f, await readFile(f, 'utf8')])),
);

test('all six experiences and ten guide articles are built', () => {
  assert.equal(pages.length, 16);
  for (const route of [
    'index.html',
    'features/index.html',
    'download/index.html',
    'docs/index.html',
    'about/index.html',
    'changelog/index.html',
    '404.html',
    'docs/installation/index.html',
  ])
    assert.ok(textByFile.has(path.join(root, route)));
});
test('every page has unique SEO, one primary heading and accessible image dimensions', () => {
  const titles = new Set();
  for (const [file, html] of textByFile) {
    const title = html.match(/<title>(.*?)<\/title>/)?.[1];
    assert.ok(title, file);
    assert.ok(!titles.has(title), title);
    titles.add(title);
    assert.equal((html.match(/<h1[ >]/g) || []).length, 1, file);
    assert.ok(html.includes('<html lang="en">'), file);
    assert.ok(html.includes('name="description"'), file);
    assert.ok(html.includes('rel="canonical"'), file);
    assert.ok(html.includes('property="og:image"'), file);
    assert.ok(html.includes('Skip to content'), file);
    for (const img of html.matchAll(/<img\b[^>]*>/g)) {
      for (const attr of ['alt=', 'width=', 'height='])
        assert.ok(img[0].includes(attr), `${file}: ${img[0]}`);
    }
  }
});
test('every local link, fragment, script, stylesheet and image resolves', async () => {
  for (const [file, html] of textByFile) {
    for (const match of html.matchAll(/(?:href|src)="([^"]+)"/g)) {
      const href = match[1];
      if (/^(https?:|mailto:)/.test(href)) continue;
      if (href.startsWith('#')) {
        assert.ok(html.includes(`id="${href.slice(1)}"`), `${file} ${href}`);
        continue;
      }
      assert.ok(href.startsWith(base), `${file}: ${href}`);
      const [route, hash] = href.slice(base.length).split('#');
      let destination = path.join(root, route);
      if (!path.extname(destination)) destination = path.join(destination, 'index.html');
      assert.ok((await stat(destination)).isFile(), `${file} → ${href}`);
      if (hash) assert.ok(textByFile.get(destination)?.includes(`id="${hash}"`), `${file} ${href}`);
    }
  }
});
test('Microsoft Store is honest and portable downloads use verified assets', async () => {
  const html = await readFile(path.join(root, 'download/index.html'), 'utf8');
  assert.ok(html.includes('href="https://apps.microsoft.com/detail/9NZF994ZPFMH"'));
  assert.ok(html.includes('1.9.0'));
  assert.ok(html.includes('Store-managed updates'));
  assert.ok(!html.includes('Coming soon'));
  assert.ok(!html.includes('No Store version announced'));
  assert.ok(html.includes('/Holm.exe"'));
  assert.ok(html.includes('data-download'));
  assert.ok(html.includes('data-release-date'));
  assert.ok(html.includes('data-release-assets'));
});
test('build output stays within practical payload budgets', async () => {
  for (const name of ['client.js', 'releases.mjs'])
    assert.ok((await stat(path.join(root, 'assets', name))).size < 16000, name);
  const css = await readFile(path.join(root, 'assets/style.css'));
  assert.ok(css.length < 64000);
  assert.ok(gzipSync(css).length < 12000, 'Compressed CSS must stay under 12 KB');
  assert.ok((await stat(path.join(root, 'assets/expanded.png'))).size < 200000);
  for (const [file, html] of textByFile) assert.ok(Buffer.byteLength(html) < 200000, file);
});
test('sitemap covers public pages and robots declares its actual origin', async () => {
  const sitemap = await readFile(path.join(root, 'sitemap.xml'), 'utf8');
  assert.equal((sitemap.match(/<loc>/g) || []).length, 15);
  assert.ok(sitemap.includes(`${origin}${base}docs/`));
  assert.ok(!sitemap.includes('404.html'));
  assert.ok(
    (await readFile(path.join(root, 'robots.txt'), 'utf8')).includes(`${origin}${base}sitemap.xml`),
  );
});
test('custom-domain root hosting and release outage still render all pages', async () => {
  const snapshot = JSON.parse(
    await readFile(new URL('../data/releases.json', import.meta.url), 'utf8'),
  );
  setSiteOrigin('https://holm.example');
  const generated = createPages({
    base: '/',
    snapshot: { ...snapshot, releases: [] },
    store: { url: null },
  });
  assert.equal(generated.size, 16);
  assert.ok(
    generated.get('download/').includes('https://github.com/ademerci/Holm/releases/latest'),
  );
  assert.ok(generated.get('').includes('href="/features/"'));
  assert.ok(generated.get('').includes('https://holm.example/assets/social.png'));
  setSiteOrigin(origin);
});
