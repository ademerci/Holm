import { docs, featureGroups } from './content.mjs';
import { homePage } from './home.mjs';
import {
  REPO,
  escapeHtml as esc,
  dateLabel,
  sizeLabel,
  latestRelease,
  releaseAsset,
  releaseHtml,
} from './releases.mjs';

export function createPages({ base, snapshot, store }) {
  const link = (path) => `${base}${path}`;
  const latest = latestRelease(snapshot.releases);
  const download = latest ? releaseAsset(latest).browser_download_url : `${REPO}/releases/latest`;
  const version = latest?.tag_name || 'GitHub releases';
  const arrow = '<span aria-hidden="true">↗</span>';
  const icon =
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true"><path d="M12 3v12m-5-5 5 5 5-5M5 16v5h14v-5"/></svg>';
  const win =
    '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="m2 4 9-1v8H2zm11-1 9-1v9h-9zM2 13h9v8l-9-1zm11 0h9v9l-9-1z"/></svg>';
  const brand = `<img src="${link('assets/icon-96.png')}" alt="" width="32" height="32"><span>Holm</span>`;
  const button = (text, href, secondary = false, attributes = '') =>
    `<a ${attributes} class="button ${secondary ? 'secondary' : ''}" href="${href}">${text}</a>`;
  const picture = (name, alt, opts = '') =>
    `<img src="${link(`assets/${name}.png`)}" alt="${esc(alt)}" width="${dimensions[name]?.[0] || 1120}" height="${dimensions[name]?.[1] || 594}" loading="lazy" decoding="async" ${opts}>`;
  const intro = (eyebrow, title, text) =>
    `<header class="page-intro wrap"><p class="eyebrow">${eyebrow}</p><h1>${title}</h1><p class="lede">${text}</p></header>`;
  const closing = /* HTML */ `<section class="closing wrap">
    <img
      src="${link('assets/icon-192.png')}"
      alt="Holm app icon"
      width="68"
      height="68"
      loading="lazy"
    />
    <p class="eyebrow">A LITTLE MORE HOLM.</p>
    <h2>Make yourself at home.</h2>
    <p>A small addition to your Windows desktop.<br />A lot of little things, brought together.</p>
    ${button(`${win} Get Holm for Windows`, link('download/'))}<span class="fine"
      >Free & open source · Windows 10 / 11 · x64</span
    >
  </section>`;

  const navItems = [
    ['', 'Overview'],
    ['features/', 'Features'],
    ['docs/', 'Guide'],
    ['changelog/', 'What’s new'],
    ['about/', 'About'],
  ];
  const layout = (
    path,
    title,
    description,
    content,
    { noindex = false, head = '', page = '' } = {},
  ) => {
    const canonical = `${siteOrigin}${link(path)}`;
    return /* HTML */ `<!doctype html>
      <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>${esc(title)}</title>
          <meta name="description" content="${esc(description)}" />
          <meta name="theme-color" content="#f7f8fa" />
          <meta name="color-scheme" content="light dark" />
          ${noindex ? '<meta name="robots" content="noindex">' : ''}
          <link rel="canonical" href="${canonical}" />
          <meta property="og:type" content="website" />
          <meta property="og:site_name" content="Holm" />
          <meta property="og:title" content="${esc(title)}" />
          <meta property="og:description" content="${esc(description)}" />
          <meta property="og:url" content="${canonical}" />
          <meta property="og:image" content="${siteOrigin}${link('assets/social.png')}" />
          <meta property="og:image:alt" content="The authentic Holm expanded player for Windows" />
          <meta name="twitter:card" content="summary_large_image" />
          <link rel="icon" type="image/png" href="${link('assets/icon-96.png')}" />
          <link rel="apple-touch-icon" href="${link('assets/icon-192.png')}" />
          <link rel="stylesheet" href="${link('assets/style.css')}" />
          <noscript><link rel="stylesheet" href="${link('assets/noscript.css')}" /></noscript>
          <script type="module" src="${link('assets/client.js')}"></script>
          ${head}
        </head>
        <body data-base="${base}" ${page ? `class="${page}"` : ''}>
          <a class="skip" href="#main">Skip to content</a>
          <header class="site-header">
            <nav class="nav wrap" aria-label="Main navigation">
              <a class="brand" href="${link('')}" aria-label="Holm home">${brand}</a
              ><button class="menu-toggle" aria-expanded="false" aria-controls="navigation">
                Menu <span aria-hidden="true">☰</span>
              </button>
              <div id="navigation" class="nav-links">
                ${navItems.map(([p, n]) => `<a href="${link(p)}" ${path === p || (p === 'docs/' && path.startsWith('docs/')) ? 'aria-current="page"' : ''}>${n}</a>`).join('')}
              </div>
              <a
                class="nav-download"
                href="${link('download/')}"
                ${path === 'download/' ? 'aria-current="page"' : ''}
                >Download ${icon}</a
              >
            </nav>
          </header>
          <main id="main" tabindex="-1">${content}</main>
          <footer class="footer">
            <div class="wrap">
              <div class="footer-top">
                <div>
                  <a class="brand" href="${link('')}">${brand}</a>
                  <p>A little space for your everyday.</p>
                </div>
                <div class="footer-columns">
                  <div>
                    <span>Discover</span><a href="${link('features/')}">Features</a
                    ><a href="${link('download/')}">Download</a
                    ><a href="${link('changelog/')}">Release history</a>
                  </div>
                  <div>
                    <span>Find your way</span><a href="${link('docs/')}">Getting started</a
                    ><a href="${link('docs/privacy/')}">Privacy & permissions</a
                    ><a href="${link('docs/troubleshooting/')}">Help & FAQ</a>
                  </div>
                  <div>
                    <span>Made in the open</span><a href="${REPO}">GitHub ${arrow}</a
                    ><a href="${REPO}/issues">Report an issue ${arrow}</a
                    ><a href="${REPO}/blob/main/LICENSE">MIT licence ${arrow}</a>
                  </div>
                </div>
              </div>
              <div class="footer-bottom">
                <span>© ${new Date().getUTCFullYear()} Holm contributors</span
                ><span>Built for Windows. Made with care.</span
                ><button class="theme-toggle" aria-label="Switch colour theme">
                  Appearance: <span data-theme-label>System</span>
                </button>
              </div>
            </div>
          </footer>
        </body>
      </html>`;
  };

  const channels = (full = false) =>
    `<div class="channels"><article class="channel github"><div class="channel-top"><span class="channel-symbol" aria-hidden="true">↗</span><span class="tag">${full ? 'Available now' : 'Newest public builds'}</span></div><h${full ? '2' : '3'}>Direct from GitHub.</h${full ? '2' : '3'}><p>New releases land here first. Get the latest published build, with release notes that tell you what changed.</p>${full ? `<div class="release-current" data-release-current><strong data-version>${esc(version)}</strong><span data-release-date>${latest ? dateLabel(latest.published_at) : 'Browse current releases'}</span><span data-release-size>${latest ? `${sizeLabel(releaseAsset(latest).size)} · Holm.exe` : ''}</span></div>` : ''}${button(`${icon} ${full ? 'Download Holm.exe' : 'Get the latest release'}`, full ? download : link('download/'), false, full ? 'data-download' : '')}${
      full
        ? `<div data-release-assets class="asset-links">${
            latest?.assets
              .filter((a) => /\.zip$/.test(a.name))
              .map(
                (a) =>
                  `<a href="${a.browser_download_url}">Windows x64 ZIP · ${sizeLabel(a.size)} ↓</a>`,
              )
              .join('') || ''
          }</div><a class="text-link" data-release-notes href="${latest?.html_url || `${REPO}/releases`}">Read release notes ${arrow}</a>`
        : ''
    }<span class="channel-foot">Windows x64 · Self-contained · In-app updates</span></article><article class="channel store"><div class="channel-top"><span class="store-symbol" aria-hidden="true">${win}</span><span class="tag neutral">${store.url ? 'Available now' : 'Coming soon'}</span></div><h${full ? '2' : '3'}>At the Store’s pace.</h${full ? '2' : '3'}><p>The more conservative channel: less frequent releases, with additional validation before publication. It may be behind GitHub.</p>${store.url ? `${store.version ? `<div class="release-current"><strong>v${esc(store.version)}</strong><span>Microsoft Store release</span><span>Verified ${dateLabel(store.verified_at)}</span></div>` : ''}${button('Get it from Microsoft Store ↗', store.url, true)}` : '<p class="store-state">Microsoft Store<br><span>A verified public listing is not available yet.</span></p><span class="button unavailable">Microsoft Store · Coming soon</span>'}<span class="channel-foot">Store-managed updates · ${store.url ? esc(store.publisher || 'Microsoft Store') : 'No Store version announced'}</span></article></div>`;

  const home = homePage({ link, win, download, version, store, repo: REPO, esc });
  const features = /* HTML */ `${intro('SMALL SPACE. MANY POSSIBILITIES.', 'Little things.<br>Thoughtfully together.', 'A closer look at what fits inside Holm, and how it fits into your day.')}
    <nav class="feature-index wrap" aria-label="Feature categories">
      ${featureGroups.map((g) => `<a href="#${g.id}">${g.label} <span aria-hidden="true">↓</span></a>`).join('')}
    </nav>
    ${featureGroups.map((g, i) => `<section id="${g.id}" class="feature-section ${i % 2 ? 'section-tint' : ''}"><div class="wrap"><div class="feature-lead reveal"><div><p class="eyebrow">0${i + 1} / ${g.label.toUpperCase()}</p><h2>${g.title}</h2><p>${g.description}</p><a class="text-link" href="${link(`docs/${g.doc}/`)}">Read the guide <span aria-hidden="true">→</span></a></div><div class="feature-visual">${picture(g.image, g.alt)}</div></div><div class="three-columns">${g.items.map(([title, text]) => `<div><h3>${title}</h3><p>${text}</p></div>`).join('')}</div></div></section>`).join('')}
    <section class="wrap additional">
      <p class="eyebrow">A FEW MORE LITTLE DETAILS</p>
      <h2>There’s more up here.</h2>
      <div class="detail-grid">
        ${[
          [
            'Battery at a glance',
            'Laptop and Bluetooth device levels, using the values Windows reports.',
          ],
          ['Calls, noticed', 'See the call’s app and duration, with a muted microphone indicator.'],
          ['Privacy, visible', 'Microphone and camera dots for activity reported by Windows.'],
          [
            'Room to focus',
            'Hide during fullscreen games and videos. Let the resting notch step aside for browser tabs.',
          ],
          [
            'Your favourite assistant',
            'An optional button opens Claude, Gemini, ChatGPT or an app you choose.',
          ],
          [
            'Updates, on your terms',
            'The portable app checks for stable GitHub releases. You choose when to install.',
          ],
        ]
          .map(([t, p]) => `<article><h3>${t}</h3><p>${p}</p></article>`)
          .join('')}
      </div>
      <p class="fine">
        Capabilities vary with Windows, connected devices and third-party apps.
        <a href="${link('docs/troubleshooting/')}">See requirements and limitations →</a>
      </p>
    </section>
    ${closing}`;

  const downloads = /* HTML */ `${intro('MAKE YOURSELF AT HOME.', 'A little more Holm.<br>A lot within reach.', 'Holm for Windows 10 (2004 or newer) and Windows 11. x64. Free and open source.')}
    <section
      class="wrap download-options"
      data-releases
      data-snapshot-at="${Date.parse(snapshot.fetched_at)}"
    >
      ${channels(true)}
      <p class="release-status fine" role="status" data-release-status>
        Release snapshot checked ${dateLabel(snapshot.fetched_at)}.
        <a href="${REPO}/releases/latest">Verify the latest on GitHub ↗</a>
      </p>
      <noscript
        ><p class="fine">
          JavaScript is off. Version information is from the build snapshot; follow GitHub for the
          newest release.
        </p></noscript
      >
    </section>
    <section class="wrap install-section">
      <div>
        <p class="eyebrow">UP AND RUNNING</p>
        <h2>Three small steps.</h2>
        <a class="text-link" href="${link('docs/installation/')}"
          >The installation guide <span aria-hidden="true">→</span></a
        >
      </div>
      <ol class="steps">
        <li>
          <span>01</span>
          <div>
            <h3>Download.</h3>
            <p>
              Choose the self-contained Holm.exe, or get the ZIP and extract it. No separate .NET
              runtime needed.
            </p>
          </div>
        </li>
        <li>
          <span>02</span>
          <div>
            <h3>Make a home for it.</h3>
            <p>
              Keep Holm.exe in a folder your account can write to. That lets the portable updater do
              its job.
            </p>
          </div>
        </li>
        <li>
          <span>03</span>
          <div>
            <h3>Look up.</h3>
            <p>
              Run Holm.exe. Open the notch to explore. Right-click for Settings and make it yours.
            </p>
          </div>
        </li>
      </ol>
    </section>
    <aside class="wrap security-note">
      <h2>A note on first launch.</h2>
      <p>
        The current GitHub build is unsigned. Windows SmartScreen may warn, and Smart App Control
        may block an unsigned app. Review the official release before running it.
        <a href="${link('docs/installation/')}">Read the installation notes →</a>
      </p>
    </aside>
    <section class="section-tint">
      <div class="wrap channel-comparison">
        <p class="eyebrow">FIND YOUR PACE</p>
        <h2>Same Holm.<br />Different release rhythms.</h2>
        <div class="table-scroll">
          <table>
            <caption class="sr-only">
              Holm release channel comparison
            </caption>
            <thead>
              <tr>
                <th scope="col">What to expect</th>
                <th scope="col">GitHub</th>
                <th scope="col">Microsoft Store</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <th scope="row">Availability</th>
                <td>Available now</td>
                <td>${store.url ? 'See official listing' : 'Coming soon · listing unverified'}</td>
              </tr>
              <tr>
                <th scope="row">Release pace</th>
                <td>New public builds first</td>
                <td>Less frequent, with additional validation</td>
              </tr>
              <tr>
                <th scope="row">Updates</th>
                <td>Holm’s in-app updater</td>
                <td>Managed by Microsoft Store</td>
              </tr>
              <tr>
                <th scope="row">Version</th>
                <td data-version>${esc(version)}</td>
                <td>${store.version ? esc(store.version) : 'Not announced'}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="fine">
          Newer does not automatically mean unstable. Prereleases are labelled separately.
          Additional validation does not guarantee a bug-free release.
        </p>
      </div>
    </section>`;

  const docsNav = (slug) =>
    `<aside class="docs-sidebar"><label for="doc-search">Find your way</label><div class="search-wrap"><span aria-hidden="true">⌕</span><input id="doc-search" type="search" placeholder="Search the guide…" autocomplete="off" aria-describedby="search-status"></div><p id="search-status" class="fine" role="status" aria-live="polite"></p><nav aria-label="Documentation">${[
      ...new Set(docs.map((d) => d.group)),
    ]
      .map(
        (group) =>
          `<div class="doc-group"><span>${group}</span>${docs
            .filter((d) => d.group === group)
            .map(
              (d) =>
                `<a data-doc-link data-search="${esc(`${d.title} ${d.intro} ${d.sections.flat().join(' ')}`.toLowerCase())}" href="${link(`docs/${d.slug === 'getting-started' ? '' : `${d.slug}/`}`)}" ${slug === d.slug ? 'aria-current="page"' : ''}>${d.title}</a>`,
            )
            .join('')}</div>`,
      )
      .join(
        '',
      )}</nav><a class="source-link" href="${REPO}/blob/main/README.md">Full project README ${arrow}</a></aside>`;
  const docPage = (doc) => {
    const index = docs.indexOf(doc);
    const slug = doc.slug === 'getting-started' ? '' : `${doc.slug}/`;
    return layout(
      `docs/${slug}`,
      `${doc.title} — Holm Guide`,
      doc.intro,
      `<div class="docs-layout wrap">${docsNav(doc.slug)}<article class="doc-body"><p class="eyebrow">THE HOLM GUIDE / ${doc.group.toUpperCase()}</p><h1>${doc.title}</h1><p class="lede">${doc.intro}</p><nav class="on-this-page" aria-label="On this page"><span>On this page</span>${doc.sections.map(([t], i) => `<a href="#section-${i}">${t}</a>`).join('')}</nav>${doc.sections.map(([t, p], i) => `<section id="section-${i}"><h2>${t}</h2><p>${p}</p></section>`).join('')}<div class="doc-reference">Based on the <a href="${REPO}/blob/main/README.md">project README</a> and published release notes. Instructions describe the current GitHub app; older releases may differ.</div><nav class="doc-pagination" aria-label="Guide pages">${index > 0 ? `<a href="${link(`docs/${index === 1 ? '' : `${docs[index - 1].slug}/`}`)}">← ${docs[index - 1].title}</a>` : '<span></span>'}${index < docs.length - 1 ? `<a href="${link(`docs/${docs[index + 1].slug}/`)}">${docs[index + 1].title} →</a>` : `<a href="${REPO}/issues">Get help on GitHub ↗</a>`}</nav></article></div>`,
    );
  };

  const about = /* HTML */ `${intro('A SMALL IDEA. AN OPEN PROJECT.', 'A little more care.<br>For the everyday.', 'Holm brings useful things closer, without asking your desktop to become something else.')}
    <section class="about-story wrap">
      <div class="about-icon">
        <img
          src="${link('assets/icon-512.png')}"
          width="300"
          height="300"
          alt="The official Holm icon"
        />
      </div>
      <div>
        <p class="eyebrow">WHY HOLM EXISTS</p>
        <h2>A home for<br />the in-between.</h2>
        <p>
          The song you want to skip. A file to send. A thought to remember. Holm gives those small
          moments a place at the top of your screen.
        </p>
        <p>
          It is a Dynamic Island-style notch, built natively for Windows with WPF and .NET 10. It
          was called WinNotch until version 1.5.0; existing settings carry over.
        </p>
      </div>
    </section>
    <section class="section-tint">
      <div class="wrap about-principles">
        <p class="eyebrow">HOW IT’S MADE</p>
        <h2>Open source.<br />Open to a little better.</h2>
        <div class="three-columns">
          <div>
            <h3>For Windows.</h3>
            <p>
              A native desktop app that works with the media, audio devices and notifications
              already on your PC.
            </p>
          </div>
          <div>
            <h3>For real days.</h3>
            <p>
              Small tools, sharing and a little awareness. The direction is guided by actual use,
              feedback and the project’s public development.
            </p>
          </div>
          <div>
            <h3>For everyone to explore.</h3>
            <p>
              Holm is free and open source under the MIT licence. Read the code, report an issue or
              contribute an improvement.
            </p>
          </div>
        </div>
        <div class="actions">
          ${button('Explore Holm on GitHub ↗', REPO)}<a
            class="text-link"
            href="${REPO}/blob/main/LICENSE"
            >Read the MIT licence ↗</a
          >
        </div>
      </div>
    </section>
    <section class="wrap honest-note">
      <p class="eyebrow">PEOPLE & TOOLS</p>
      <h2>Made with a human point of view.</h2>
      <p>
        Holm began with Claude in Claude Code. The project now also uses OpenAI’s Codex and other AI
        development tools for implementation, review and testing. Human maintainers guide
        development and test it on real machines.
      </p>
      <p>
        It’s an actively developed project, with room to improve. Issues and contributions are
        welcome.
      </p>
      <a class="text-link" href="${REPO}/blob/main/CONTRIBUTORS.md">Meet the contributors ↗</a>
    </section>
    ${closing}`;

  const changelog = /* HTML */ `${intro('THE LITTLE IMPROVEMENTS ADD UP.', 'Always a little<br>more Holm.', 'Real releases, straight from GitHub. See what’s new, what changed and what was fixed.')}
    <section
      class="wrap changelog"
      data-releases
      data-snapshot-at="${Date.parse(snapshot.fetched_at)}"
    >
      <div class="changelog-toolbar">
        <span>GitHub release history</span
        ><label class="prerelease-filter"
          ><input type="checkbox" id="show-prereleases" checked /> Include prereleases</label
        >
      </div>
      <p class="fine" role="status" data-release-status>
        Release snapshot checked ${dateLabel(snapshot.fetched_at)}.
        <a href="${REPO}/releases">View all on GitHub ↗</a>
      </p>
      <div data-release-list>${snapshot.releases.map(releaseHtml).join('')}</div>
      <div class="history-end">
        <p>
          This is a selection of recent GitHub releases. Microsoft Store currently offers Holm
          ${esc(store.version || '—')} and follows its own release schedule.
        </p>
        ${button('Browse the full history on GitHub ↗', `${REPO}/releases`, true)}
      </div>
    </section>`;
  const pages = new Map([
    [
      '',
      layout(
        '',
        'Holm — A little space for your everyday',
        'A beautifully useful notch for Windows. Your music, everyday tools, notifications and Beam file sharing, right at the top of your screen.',
        home,
        {
          page: 'is-home',
          head: `<link rel="stylesheet" href="${link('assets/home.css')}" /><script type="module" src="${link('assets/home.js')}"></script>`,
        },
      ),
    ],
    [
      'features/',
      layout(
        'features/',
        'Features — Holm',
        'Explore Holm: Windows media controls, notes, reminders, clipboard history, Beam file sharing, chat and quiet desktop awareness.',
        features,
      ),
    ],
    [
      'download/',
      layout(
        'download/',
        'Download Holm for Windows',
        'Download the latest Holm release for Windows x64 from GitHub. Compare the latest-build channel with the Microsoft Store release.',
        downloads,
      ),
    ],
    [
      'about/',
      layout(
        'about/',
        'About — Holm',
        'The idea behind Holm: a native Windows notch, built with care and developed in the open under the MIT licence.',
        about,
      ),
    ],
    [
      'changelog/',
      layout(
        'changelog/',
        'What’s new — Holm',
        'Read real Holm release notes, publication dates and downloads from the official GitHub repository.',
        changelog,
      ),
    ],
    [
      '404.html',
      layout(
        '404.html',
        'Page not found — Holm',
        'Find your way back to Holm.',
        `${intro('A LITTLE DETOUR.', 'Nothing up here.<br>Yet.', 'This page may have moved. Let’s get you back to familiar ground.')}<div class="wrap error-actions">${button('Back to Holm', link(''))}<a class="text-link" href="${link('docs/')}">Open the guide →</a></div>`,
        { noindex: true },
      ),
    ],
  ]);
  for (const doc of docs)
    pages.set(`docs/${doc.slug === 'getting-started' ? '' : `${doc.slug}/`}`, docPage(doc));
  return pages;
}

// Set by the builder after reading each source image's PNG header.
export const dimensions = {};
export let siteOrigin = 'https://holmnotch.ch';
export function setSiteOrigin(value) {
  siteOrigin = value;
}
