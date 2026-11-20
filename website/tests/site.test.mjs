import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const read = (p) => readFile(new URL(`../site/${p}`, import.meta.url), 'utf8');

test('about page names the developers, without photos', async () => {
  const about = await read('about/index.html');
  for (const name of ['ademerci', 'ALeandro08', 'Diekartoffel19', 'DjordjeMojsilovic', 'NoelMoser'])
    assert.ok(about.includes(`<h3>${name}</h3>`), name);
  assert.ok(!about.includes('maelTBZ'));
  assert.ok(!about.includes('assets/team/'));
});

test('no blue-to-purple gradients', async () => {
  for (const css of ['assets/home.css', 'assets/style.css'])
    assert.doesNotMatch(await read(css), /#bf5af2|#5b247a|120, 80, 255|106, 60, 176/);
});
