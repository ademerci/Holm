import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { inflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';

// Decode the lossless RGB/RGBA captures so these tests check actual pixels,
// rather than trusting the alpha declaration or the generation manifest.
function pixels(png) {
  const width = png.readUInt32BE(16);
  const height = png.readUInt32BE(20);
  const channels = png[25] === 6 ? 4 : png[25] === 2 ? 3 : 0;
  assert.equal(png[24], 8, '8-bit capture');
  assert.ok(channels, 'RGB or RGBA capture');
  const chunks = [];
  for (let offset = 8; offset < png.length;) {
    const length = png.readUInt32BE(offset);
    if (png.toString('ascii', offset + 4, offset + 8) === 'IDAT')
      chunks.push(png.subarray(offset + 8, offset + 8 + length));
    offset += length + 12;
  }
  const raw = inflateSync(Buffer.concat(chunks));
  const stride = width * channels;
  const result = Buffer.alloc(stride * height);
  const paeth = (a, b, c) => {
    const p = a + b - c;
    const pa = Math.abs(p - a),
      pb = Math.abs(p - b),
      pc = Math.abs(p - c);
    return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
  };
  for (let y = 0; y < height; y++) {
    const source = y * (stride + 1);
    const filter = raw[source];
    assert.ok(filter <= 4);
    for (let x = 0; x < stride; x++) {
      const index = y * stride + x;
      const left = x >= channels ? result[index - channels] : 0;
      const up = y ? result[index - stride] : 0;
      const upperLeft = y && x >= channels ? result[index - stride - channels] : 0;
      const prediction = [0, left, up, Math.floor((left + up) / 2), paeth(left, up, upperLeft)][
        filter
      ];
      result[index] = (raw[source + 1 + x] + prediction) & 255;
    }
  }
  return { width, height, channels, data: result };
}
const assetRoot = new URL('../src/assets/screenshots/', import.meta.url);
const manifest = JSON.parse(await readFile(new URL('manifest.json', assetRoot), 'utf8'));
const hash = (bytes) => createHash('sha256').update(bytes).digest('hex');

test('every repository screenshot has a verified transparent counterpart', async () => {
  const originals = (await readdir(new URL('../../docs/', import.meta.url)))
    .filter((name) => name.endsWith('.png') && name !== 'icon.png')
    .sort();
  assert.deepEqual(manifest.screenshots.map((item) => item.name).sort(), originals);
  for (const entry of manifest.screenshots) {
    const original = await readFile(new URL(`../../docs/${entry.name}`, import.meta.url));
    const output = await readFile(new URL(entry.name, assetRoot));
    assert.equal(hash(original), entry.source_sha256, entry.name);
    assert.equal(hash(output), entry.output_sha256, entry.name);
    assert.equal(output[25], 6, `${entry.name} must contain real alpha`);
  }
});

test('transparent captures clear the backdrop and retain opaque interface pixels exactly', async () => {
  for (const entry of manifest.screenshots) {
    const original = pixels(await readFile(new URL(`../../docs/${entry.name}`, import.meta.url)));
    const output = pixels(await readFile(new URL(entry.name, assetRoot)));
    assert.equal(output.width, original.width, entry.name);
    assert.equal(output.height, original.height, entry.name);
    assert.equal(output.data[3], 0, `${entry.name}: clear outside corner`);
    let transparent = 0,
      opaque = 0,
      edges = 0,
      lastVisibleRow = -1;
    for (let p = 0; p < output.width * output.height; p++) {
      const index = p * 4;
      const alpha = output.data[index + 3];
      if (alpha > 0) lastVisibleRow = Math.floor(p / output.width);
      if (alpha === 0) {
        transparent++;
        assert.equal(
          output.data[index] + output.data[index + 1] + output.data[index + 2],
          0,
          `${entry.name}: no hidden blue matte`,
        );
      } else if (alpha === 255) {
        opaque++;
        const sourceIndex = p * original.channels;
        for (let c = 0; c < 3; c++)
          assert.equal(
            output.data[index + c],
            original.data[sourceIndex + c],
            `${entry.name}: original UI colour`,
          );
      } else edges++;
    }
    assert.equal(transparent, entry.transparent_pixels, entry.name);
    assert.equal(opaque, entry.opaque_pixels, entry.name);
    assert.equal(edges, entry.edge_pixels, entry.name);
    assert.ok(transparent > 0 && opaque > 0, entry.name);
    if (entry.name === 'airdrop-share.png')
      assert.ok(
        lastVisibleRow < output.height - 8,
        'Beam must show its complete rounded bottom with clear space below',
      );
  }
});
