import { readFile, writeFile } from 'node:fs/promises';
import { API, normalizeReleases } from '../src/releases.mjs';
const destination = new URL('../data/releases.json', import.meta.url);
try {
  const response = await fetch(API, {
    headers: {
      Accept: 'application/vnd.github+json',
      ...(process.env.GITHUB_TOKEN ? { Authorization: `Bearer ${process.env.GITHUB_TOKEN}` } : {}),
    },
    signal: AbortSignal.timeout(10000),
  });
  if (!response.ok) throw new Error(`GitHub HTTP ${response.status}`);
  const releases = normalizeReleases(await response.json());
  if (!releases.length) throw new Error('No usable public releases returned');
  await writeFile(
    destination,
    JSON.stringify({ fetched_at: new Date().toISOString(), releases }, null, 2) + '\n',
  );
  console.log(`Updated ${releases.length} verified public releases.`);
} catch (error) {
  const snapshot = JSON.parse(await readFile(destination, 'utf8'));
  if (!normalizeReleases(snapshot.releases).length) throw error;
  console.warn(
    `Release refresh unavailable (${error.message}). Keeping verified snapshot from ${snapshot.fetched_at}.`,
  );
}
