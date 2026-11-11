import {
  loadReleaseFeed,
  latestRelease,
  releaseAsset,
  dateLabel,
  sizeLabel,
  releaseHtml,
} from './releases.mjs';
const base = document.body.dataset.base;
const menu = document.querySelector('.menu-toggle');
const nav = document.querySelector('#navigation');
const closeMenu = () => {
  menu.setAttribute('aria-expanded', 'false');
  nav.classList.remove('is-open');
};
menu.addEventListener('click', () => {
  const open = menu.getAttribute('aria-expanded') !== 'true';
  menu.setAttribute('aria-expanded', String(open));
  nav.classList.toggle('is-open', open);
});
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && menu.getAttribute('aria-expanded') === 'true') {
    closeMenu();
    menu.focus();
  }
});
document.addEventListener('click', (e) => {
  if (!e.target.closest('.nav')) closeMenu();
});
nav.addEventListener('click', (e) => {
  if (e.target.closest('a')) closeMenu();
});
matchMedia('(min-width: 761px)').addEventListener('change', (e) => {
  if (e.matches) closeMenu();
});

// A persistent user choice is optional; storage may be disabled by the browser.
const themes = ['system', 'light', 'dark'];
let theme = 'system';
try {
  const saved = localStorage.getItem('holm-theme');
  if (themes.includes(saved)) theme = saved;
} catch {}
function applyTheme() {
  if (theme === 'system') delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = theme;
  document.querySelector('[data-theme-label]').textContent =
    theme[0].toUpperCase() + theme.slice(1);
}
applyTheme();
document.querySelector('.theme-toggle').addEventListener('click', () => {
  theme = themes[(themes.indexOf(theme) + 1) % themes.length];
  applyTheme();
  try {
    localStorage.setItem('holm-theme', theme);
  } catch {}
});

const tabs = [...document.querySelectorAll('[data-showcase]')];
function selectCapture(tab) {
  tabs.forEach((t) => {
    t.setAttribute('aria-selected', String(t === tab));
    t.tabIndex = t === tab ? 0 : -1;
  });
  const capture = document.querySelector('#showcase-image');
  capture.src = `${base}assets/${tab.dataset.showcase}.png`;
  capture.alt = tab.dataset.alt;
  document.querySelector('#showcase-panel').setAttribute('aria-labelledby', tab.id);
  document.querySelector('[data-showcase-caption]').textContent = tab.dataset.caption;
  document.querySelector('.stage-counter').textContent = `0${tabs.indexOf(tab) + 1} — 03`;
}
tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => selectCapture(tab));
  tab.addEventListener('keydown', (e) => {
    let next = index;
    if (e.key === 'ArrowRight') next = (index + 1) % tabs.length;
    else if (e.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
    else if (e.key === 'Home') next = 0;
    else if (e.key === 'End') next = tabs.length - 1;
    else return;
    e.preventDefault();
    selectCapture(tabs[next]);
    tabs[next].focus();
  });
});

// Search the guide's titles and full article text, without a service or tracking.
const search = document.querySelector('#doc-search');
if (search) {
  const links = [...document.querySelectorAll('[data-doc-link]')];
  search.addEventListener('input', () => {
    const terms = search.value.toLocaleLowerCase().trim().split(/\s+/).filter(Boolean);
    let count = 0;
    links.forEach((a) => {
      a.hidden = !terms.every((t) => a.dataset.search.includes(t));
      if (!a.hidden) count++;
    });
    document.querySelectorAll('.doc-group').forEach((group) => {
      group.hidden = ![...group.querySelectorAll('a')].some((a) => !a.hidden);
    });
    document.querySelector('#search-status').textContent = terms.length
      ? `${count} ${count === 1 ? 'article' : 'articles'} found${count ? '' : '. Try music, Beam or updates.'}`
      : '';
  });
}

if ('IntersectionObserver' in window && !matchMedia('(prefers-reduced-motion: reduce)').matches) {
  const observer = new IntersectionObserver(
    (entries) =>
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          observer.unobserve(entry.target);
        }
      }),
    { threshold: 0.08, rootMargin: '0px 0px 20px 0px' },
  );
  document.querySelectorAll('.reveal').forEach((el) => {
    el.classList.add('pending-reveal');
    observer.observe(el);
  });
  // Never leave content hidden after an observer failure or a mid-session preference change.
  const showAll = () =>
    document.querySelectorAll('.pending-reveal').forEach((el) => el.classList.add('is-visible'));
  matchMedia('(prefers-reduced-motion: reduce)').addEventListener('change', showAll);
  window.addEventListener('beforeprint', showAll);
}

const filter = document.querySelector('#show-prereleases');
function applyReleaseFilter() {
  document.querySelectorAll('[data-prerelease]').forEach((el) => {
    el.hidden = el.dataset.prerelease === 'true' && !filter?.checked;
  });
}
filter?.addEventListener('change', applyReleaseFilter);

// One request per visit, at most once per 15 minutes per browser. Never store tokens.
if (document.querySelector('[data-releases]')) void updateReleases();
async function updateReleases() {
  const status = document.querySelector('[data-release-status]');
  const initialStatus = status.innerHTML;
  status.textContent = 'Checking the latest release information…';
  const result = await loadReleaseFeed({
    readCache: () => JSON.parse(localStorage.getItem('holm-releases-v1')),
    writeCache: (value) => localStorage.setItem('holm-releases-v1', JSON.stringify(value)),
    snapshotAt: Number(document.querySelector('[data-releases]').dataset.snapshotAt || 0),
  });
  if (result.releases) render(result.releases);
  if (result.source === 'live')
    status.textContent =
      'Release information checked just now with GitHub. Prereleases are labelled separately.';
  else if (result.source === 'cache')
    status.textContent = `Release information checked ${dateLabel(new Date(result.checkedAt).toISOString())} at ${new Date(result.checkedAt).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}. Cached for 15 minutes.`;
  else if (result.source === 'stale')
    status.textContent = `GitHub is unavailable. Showing saved release information from ${dateLabel(new Date(result.checkedAt).toISOString())}. Verify the latest release on GitHub.`;
  else {
    status.innerHTML = initialStatus;
    status.prepend('Live check unavailable. ');
  }
}
function render(releases) {
  const list = document.querySelector('[data-release-list]');
  if (list) {
    list.innerHTML = releases.map(releaseHtml).join('');
    applyReleaseFilter();
  }
  const latest = latestRelease(releases);
  if (!latest) return;
  document.querySelectorAll('[data-version]').forEach((el) => (el.textContent = latest.tag_name));
  const date = document.querySelector('[data-release-date]');
  if (date) date.textContent = dateLabel(latest.published_at);
  const size = document.querySelector('[data-release-size]');
  if (size) size.textContent = `${sizeLabel(releaseAsset(latest).size)} · Holm.exe`;
  document
    .querySelector('[data-download]')
    ?.setAttribute('href', releaseAsset(latest).browser_download_url);
  document.querySelector('[data-release-notes]')?.setAttribute('href', latest.html_url);
  const assets = document.querySelector('[data-release-assets]');
  if (assets) {
    assets.replaceChildren();
    for (const a of latest.assets.filter((a) => /^Holm-.*-win-x64\.zip$/.test(a.name))) {
      const anchor = document.createElement('a');
      anchor.href = a.browser_download_url;
      anchor.textContent = `Windows x64 ZIP · ${sizeLabel(a.size)} ↓`;
      assets.append(anchor);
    }
  }
}
