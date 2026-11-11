export const REPO = 'https://github.com/ademerci/Holm';
export const API = 'https://api.github.com/repos/ademerci/Holm/releases?per_page=30';
export const TTL = 15 * 60 * 1000;
export const escapeHtml = (value = '') =>
  String(value).replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
  );
export function officialUrl(value, asset = false) {
  try {
    const u = new URL(value);
    return u.protocol === 'https:' &&
      u.hostname === 'github.com' &&
      !u.username &&
      !u.password &&
      !u.port &&
      u.pathname.startsWith(`/ademerci/Holm/releases/${asset ? 'download/' : 'tag/'}`)
      ? u.href
      : null;
  } catch {
    return null;
  }
}
export function normalizeReleases(data) {
  if (!Array.isArray(data)) throw new Error('Expected a release list');
  return data
    .filter(
      (r) =>
        r &&
        !r.draft &&
        typeof r.tag_name === 'string' &&
        officialUrl(r.html_url) &&
        Number.isFinite(Date.parse(r.published_at)),
    )
    .map((r) => ({
      tag_name: r.tag_name,
      name: String(r.name || r.tag_name),
      published_at: r.published_at,
      html_url: officialUrl(r.html_url),
      prerelease: r.prerelease === true,
      body: typeof r.body === 'string' ? r.body : '',
      assets: (Array.isArray(r.assets) ? r.assets : [])
        .filter(
          (a) =>
            a &&
            typeof a.name === 'string' &&
            Number.isFinite(a.size) &&
            a.size >= 0 &&
            officialUrl(a.browser_download_url, true),
        )
        .map((a) => ({
          name: a.name,
          size: a.size,
          browser_download_url: officialUrl(a.browser_download_url, true),
        })),
    }))
    .sort((a, b) => Date.parse(b.published_at) - Date.parse(a.published_at));
}
export const latestRelease = (releases) =>
  releases.find((r) => !r.prerelease && r.assets.some((a) => a.name === 'Holm.exe'));
export const releaseAsset = (r) => r?.assets.find((a) => a.name === 'Holm.exe');
export const dateLabel = (value) =>
  new Intl.DateTimeFormat('en-GB', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(value));
export const sizeLabel = (bytes) => `${(bytes / 1_000_000).toFixed(1)} MB`;
export async function loadReleaseFeed({
  fetcher = fetch,
  readCache = () => null,
  writeCache = () => {},
  now = Date.now(),
  snapshotAt = 0,
} = {}) {
  let cached, saved;
  try {
    cached = readCache();
    if (
      cached &&
      Number.isFinite(cached.fetched) &&
      cached.fetched <= now &&
      cached.fetched >= snapshotAt
    ) {
      const normalized = normalizeReleases(cached.releases);
      if (normalized.length) saved = normalized;
    }
  } catch {}
  if (saved && now - cached.fetched < TTL)
    return { releases: saved, source: 'cache', checkedAt: cached.fetched };
  try {
    const response = await fetcher(API, {
      headers: { Accept: 'application/vnd.github+json' },
      signal: AbortSignal.timeout(8000),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const releases = normalizeReleases(await response.json());
    if (!releases.length) throw new Error('No usable release data');
    try {
      writeCache({ fetched: now, releases });
    } catch {}
    return { releases, source: 'live', checkedAt: now };
  } catch {
    return saved
      ? { releases: saved, source: 'stale', checkedAt: cached.fetched }
      : { releases: null, source: 'snapshot' };
  }
}
// Deliberately small, escaped Markdown renderer. Raw HTML and remote images never run.
export function notesHtml(text) {
  const inline = (s) =>
    escapeHtml(s)
      .replace(/`([^`]+)`/g, '<code>$1</code>')
      .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
      .replace(/(?<!\*)\*([^*]+)\*(?!\*)/g, '<em>$1</em>');
  let html = '',
    list = false,
    paragraph = [];
  const flush = () => {
    if (paragraph.length) {
      html += `<p>${inline(paragraph.join(' '))}</p>`;
      paragraph = [];
    }
  };
  for (const line of text.split(/\r?\n/)) {
    if (/^!\[/.test(line) || /^\|/.test(line)) continue;
    if (/^- /.test(line)) {
      flush();
      if (!list) {
        html += '<ul>';
        list = true;
      }
      html += `<li>${inline(line.slice(2))}</li>`;
      continue;
    }
    if (list) {
      html += '</ul>';
      list = false;
    }
    if (/^#{1,6} /.test(line)) {
      flush();
      html += `<h3>${inline(line.replace(/^#+ /, ''))}</h3>`;
    } else if (!line.trim()) flush();
    else paragraph.push(line);
  }
  flush();
  return html + (list ? '</ul>' : '');
}
export function releaseHtml(r, index) {
  return `<article class="release" data-prerelease="${r.prerelease}"><div class="release-meta"><time datetime="${escapeHtml(r.published_at)}">${dateLabel(r.published_at)}</time><span class="tag ${r.prerelease ? 'preview' : ''}">${r.prerelease ? 'Prerelease' : 'Public release'}</span></div><div><h2>${escapeHtml(r.name)}</h2><p class="release-channel">GitHub release</p><details ${index === 0 ? 'open' : ''}><summary>What’s changed</summary><div class="release-notes">${notesHtml(r.body || 'Read the release notes on GitHub.')}</div></details><div class="release-links"><a href="${r.html_url}">Full release notes ↗</a>${r.assets
    .filter((a) => a.name === 'Holm.exe' || /^Holm-.*-win-x64\.zip$/.test(a.name))
    .map((a) => `<a href="${a.browser_download_url}">${escapeHtml(a.name)} ↓</a>`)
    .join('')}</div></div></article>`;
}
