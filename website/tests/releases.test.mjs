import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import {
  normalizeReleases,
  latestRelease,
  officialUrl,
  notesHtml,
  releaseHtml,
  escapeHtml,
  loadReleaseFeed,
  TTL,
} from '../src/releases.mjs';

const source = JSON.parse(
  await readFile(new URL('../data/releases.json', import.meta.url), 'utf8'),
);
const real = source.releases[0];

test('verified snapshot contains the expected official executable and real dates', () => {
  const releases = normalizeReleases(source.releases);
  assert.ok(releases.length >= 12);
  assert.equal(releases.length, source.releases.length);
  assert.ok(latestRelease(releases).assets.some((a) => a.name === 'Holm.exe'));
  assert.ok(Date.parse(source.fetched_at) <= Date.now() + 60000);
});
test('prereleases and drafts never replace a stable download', () => {
  const results = normalizeReleases([
    { ...real, tag_name: 'future-preview', prerelease: true, published_at: '2099-01-01T00:00:00Z' },
    { ...real, tag_name: 'draft', draft: true, published_at: '2098-01-01T00:00:00Z' },
    real,
  ]);
  assert.equal(results.length, 2);
  assert.equal(results[0].prerelease, true);
  assert.equal(latestRelease(results).tag_name, real.tag_name);
});
test('missing Windows executable is skipped for the primary download', () => {
  const results = normalizeReleases([
    { ...real, tag_name: 'no-exe', published_at: '2099-01-01T00:00:00Z', assets: [] },
    real,
  ]);
  assert.equal(latestRelease(results).tag_name, real.tag_name);
  assert.equal(latestRelease([]), undefined);
});
test('malformed API results cannot erase a valid snapshot', () => {
  assert.throws(() => normalizeReleases({ message: 'API rate limit exceeded' }));
  assert.deepEqual(normalizeReleases([null, {}, { ...real, published_at: 'not a date' }]), []);
});
test('release and download destinations stay on the official repository', () => {
  for (const url of [
    'javascript:alert(1)',
    'https://github.com.evil.test/ademerci/Holm/releases/tag/v1',
    'https://evil.test/ademerci/Holm/releases/tag/v1',
    'https://github.com/another/Holm/releases/tag/v1',
    'https://user:pass@github.com/ademerci/Holm/releases/tag/v1',
    'http://github.com/ademerci/Holm/releases/tag/v1',
    'https://github.com/ademerci/Holm/releases/tag/../../other',
  ])
    assert.equal(officialUrl(url), null, url);
  const results = normalizeReleases([
    {
      ...real,
      assets: [{ name: 'Holm.exe', size: 123, browser_download_url: 'javascript:alert(1)' }],
    },
  ]);
  assert.deepEqual(results[0].assets, []);
});
test('untrusted release HTML and Markdown are escaped', () => {
  const html = notesHtml(
    '## Added\n- **Real feature**\n\n<script>alert(1)</script>\n![image](https://evil.test/track)\n\n`Holm.exe`',
  );
  assert.ok(html.includes('<h3>Added</h3>'));
  assert.ok(html.includes('<strong>Real feature</strong>'));
  assert.ok(html.includes('&lt;script&gt;'));
  assert.ok(!html.includes('<script>'));
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('<code>Holm.exe</code>'));
  assert.equal(escapeHtml('"<>&\''), '&quot;&lt;&gt;&amp;&#39;');
});
test('prereleases are visibly labelled and notes use native disclosure', () => {
  const html = releaseHtml({ ...real, prerelease: true }, 0);
  assert.ok(html.includes('Prerelease'));
  assert.ok(html.includes('data-prerelease="true"'));
  assert.ok(html.includes('<details open>'));
});

test('a fresh cache avoids an API call', async () => {
  const now = 2000000;
  const result = await loadReleaseFeed({
    now,
    readCache: () => ({ fetched: now - 500, releases: [real] }),
    fetcher: () => {
      assert.fail('Fresh cache must not fetch');
    },
  });
  assert.equal(result.source, 'cache');
  assert.equal(result.releases[0].tag_name, real.tag_name);
});
test('expired cache refreshes, and unavailable storage does not break live data', async () => {
  let requests = 0;
  const result = await loadReleaseFeed({
    now: 2000000,
    readCache: () => {
      throw new Error('Storage disabled');
    },
    writeCache: () => {
      throw new Error('Quota exceeded');
    },
    fetcher: async () => {
      requests++;
      return { ok: true, json: async () => [real] };
    },
  });
  assert.equal(requests, 1);
  assert.equal(result.source, 'live');
  assert.equal(result.releases.length, 1);
});
test('rate limiting preserves stale data and marks it as stale', async () => {
  const result = await loadReleaseFeed({
    now: 2000000,
    readCache: () => ({ fetched: 2000000 - TTL - 1, releases: [real] }),
    fetcher: async () => ({ ok: false, status: 403 }),
  });
  assert.equal(result.source, 'stale');
  assert.equal(result.releases[0].tag_name, real.tag_name);
});
test('older caches cannot replace a newer static snapshot during an outage', async () => {
  const result = await loadReleaseFeed({
    now: 2000000,
    snapshotAt: 1900000,
    readCache: () => ({ fetched: 1800000, releases: [real] }),
    fetcher: async () => {
      throw new Error('Offline');
    },
  });
  assert.equal(result.source, 'snapshot');
  assert.equal(result.releases, null);
});
test('malformed API data and corrupted cache fall back without destroying markup', async () => {
  const result = await loadReleaseFeed({
    readCache: () => ({ fetched: Date.now() - TTL - 1, releases: { bad: true } }),
    fetcher: async () => ({ ok: true, json: async () => ({ message: 'bad' }) }),
  });
  assert.equal(result.source, 'snapshot');
  assert.equal(result.releases, null);
});
