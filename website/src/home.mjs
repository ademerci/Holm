// The home page: the notch told as a film you scroll through, drawn in HTML and CSS like the launch video.
// Everything here is plain HTML first. home.js only adds the motion: the pinned screen follows the chapter you
// read, the hero recedes and a paragraph lights up word by word. Without JavaScript, or with reduced motion,
// the page reads top to bottom with the notch at rest.

const paths = {
  cloud: 'M7 18h10a4 4 0 0 0 .5-7.97A6 6 0 0 0 6.1 9.5 4.25 4.25 0 0 0 7 18z',
  moon: 'M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z',
  bell: 'M6 16v-5a6 6 0 1 1 12 0v5l1.5 2h-15zM10 20.5a2 2 0 0 0 4 0',
  note: 'M5 4h14v11l-5 5H5zM14 20v-5h5',
  calc: 'M6 3h12v18H6zM9 7h6M9 12h.01M12 12h.01M15 12h.01M9 16h.01M12 16h.01M15 16h.01',
  clip: 'M9 3.5h6v3H9zM8 5H6v16h12V5h-2',
  speaker: 'M4 9v6h4l5 4V5L8 9zM16.5 8.5a5 5 0 0 1 0 7M19 6a8.5 8.5 0 0 1 0 12',
  picker: 'M14 6l4 4M17 3l4 4-3 3-4-4zM14 6l-9 9v4h4l9-9',
  check: 'M5 12.5l4.5 4.5L19 7',
  file: 'M6 3h8l5 5v13H6zM14 3v5h5',
  chat: 'M4 5h16v11H9l-5 4z',
  sun: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
  timer: 'M12 9v4.5l3 1.8M9 2h6M12 22a8 8 0 1 0 0-16 8 8 0 0 0 0 16z',
  lock: 'M6 11h12v10H6zM8.5 11V8a3.5 3.5 0 0 1 7 0v3',
  usb: 'M9 3h6v6H9zM7 9h10v8a5 5 0 0 1-10 0z',
  camera: 'M4 8h3l2-3h6l2 3h3v11H4zM12 10.5a3 3 0 1 0 0 6 3 3 0 0 0 0-6z',
  phone:
    'M6 3h3l2 5-2.5 1.5a11 11 0 0 0 6 6L16 13l5 2v3a3 3 0 0 1-3 3A15 15 0 0 1 3 6a3 3 0 0 1 3-3z',
  headphones: 'M4 15v-3a8 8 0 0 1 16 0v3M4 15h3v5H4zM17 15h3v5h-3z',
  frame: 'M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5',
  users:
    'M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM2.5 20a6.5 6.5 0 0 1 13 0M16 4.5a3.5 3.5 0 0 1 0 6.5M18 14a6 6 0 0 1 3.5 6',
};
const fills = {
  play: 'M8 5v14l11-7z',
  pause: 'M7 5h3.5v14H7zM13.5 5H17v14h-3.5z',
  prev: 'M18 6v12l-8.5-6zM6 6h2.2v12H6z',
  next: 'M6 6v12l8.5-6zM15.8 6H18v12h-2.2z',
};
const ic = (name, size = 24, cls = '') =>
  fills[name]
    ? `<svg class="ic ${cls}" viewBox="0 0 24 24" width="${size}" height="${size}" fill="currentColor" aria-hidden="true"><path d="${fills[name]}"/></svg>`
    : `<svg class="ic ${cls}" viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${paths[name]}"/></svg>`;

const chapters = [
  [
    'At rest',
    'Quiet, until it matters.',
    'The time and the weather in a slim island at the top of your screen. Nothing more, until something happens.',
  ],
  [
    'Music',
    'Your soundtrack, always in sight.',
    'Spotify, YouTube, VLC and every Windows media session. The cover glows in the notch and the bars move with the music.',
  ],
  [
    'Hover',
    'It opens when you need it.',
    'Cover, seek bar, shuffle and repeat, washed in the colours of the album. Move away and it glides back.',
  ],
  [
    'Messages',
    'Messages, without the interruption.',
    'WhatsApp, Discord, Telegram, Signal, Slack and Outlook pop out for a moment and step aside. Quiet hours keep your evening yours.',
  ],
  [
    'New in 1.11',
    'A sky for every hour.',
    'With nothing playing, the open notch glows like the sky outside: dawn, day, dusk, or a few stars at night. And it says hello.',
  ],
  [
    'Tools',
    'Little tools. One click away.',
    'Notes, reminders in plain words, a calculator, your clipboard, a mixer and a colour picker, in one toolbar.',
  ],
  [
    'Beam',
    'Drop it on the notch.',
    'Drag a file to the top of your screen and pick a friend. Beam sends it straight to their notch, nearby or across the internet.',
  ],
  [
    'Friends',
    'One code. Friends for good.',
    'Share a short invite code. Whoever types it is your friend at once, with chat, waves and screen sharing.',
  ],
];

const tiles = [
  ['sun', 'Weather', 'The temperature when closed. The sky and the next hour’s rain when open.'],
  [
    'bell',
    'Reminders in plain words',
    '“20 min laundry”. “14:30 dentist”. It rings right on time.',
  ],
  ['calc', 'Calculator', 'Type a sum. The answer is already there. Click to copy.'],
  [
    'clip',
    'Clipboard history',
    'Everything you copied, one click away. Copied passwords can clear themselves.',
  ],
  ['speaker', 'Volume per app', 'Spotify a little louder. The meeting a little quieter.'],
  ['picker', 'Colour picker', 'Any pixel on your screen, copied as a colour code.'],
  ['camera', 'Screenshots', 'A fresh screenshot pops out, ready to copy its text or send.'],
  ['phone', 'Calls', 'See the call, mute your microphone and let the music pause.'],
  ['headphones', 'Batteries', 'Your laptop, AirPods and Bluetooth devices at a glance.'],
  ['lock', 'VPN', 'A small lock while a VPN keeps you covered.'],
  ['usb', 'USB drives', 'Open or eject a stick right from the notch.'],
  ['frame', 'Out of the way', 'Steps aside for fullscreen games and videos.'],
];

const story =
  'Holm puts your music, your messages, your friends and the little tools you reach for every day in one place at the top of your screen. It opens when you need it and steps aside when you don’t.';

export function homePage({ link, win, download, version, store, repo, esc }) {
  const words = story
    .split(' ')
    .map((word) => `<span class="w">${esc(word)}</span>`)
    .join(' ');
  const eq = '<span class="eq"><i></i><i></i><i></i><i></i></span>';
  const stage = /* HTML */ `<div class="stage" data-stage data-step="0" aria-hidden="true">
    <div class="screen">
      <div class="wall"></div>
      <div class="win w1"></div>
      <div class="win w2"></div>
      <div class="fly">${ic('file', 34)}<span>Mockups.pdf</span></div>
      <div class="isl">
        <div class="wash"></div>
        <div class="sky"><i></i><i></i><i></i><i></i><i></i><i></i><i></i></div>
        <div class="layer l0">
          <b class="t">9:41</b><span class="sp"></span>${ic('cloud', 20)}<b class="t">13°</b>
        </div>
        <div class="layer l1"><span class="art sm"></span><span class="sp"></span>${eq}</div>
        <div class="layer l2 col">
          <div class="row">
            <span class="art"><i></i></span>
            <div class="grow">
              <b class="t1">Night Swim</b><span class="t2">The Lighthouse</span
              ><span class="t3">Spotify</span>
            </div>
            ${eq}
          </div>
          <div class="row prog">
            <span class="t3">1:24</span><span class="bar"><i></i></span
            ><span class="t3">-2:07</span>
          </div>
          <div class="row ctl">${ic('prev', 26)}${ic('pause', 34)}${ic('next', 26)}</div>
        </div>
        <div class="layer l3">
          <span class="app wa">${ic('chat', 22)}</span>
          <div class="grow">
            <b class="t1">Lena</b><span class="t2">Are we still on for 3?</span>
          </div>
          <span class="t3">now</span>
        </div>
        <div class="layer l4 col">
          <div class="row">
            <div class="grow">
              <span class="hello">Good evening, Alex</span><b class="clock">21:30</b
              ><span class="t2">Thursday, November 12</span>
            </div>
            <div class="wx">${ic('moon', 30)}<b>9°</b><span class="t3">Clear · Zurich</span></div>
          </div>
          <div class="row cards">
            <span class="card"
              >${ic('bell', 20, 'amber')}<span
                ><b>Dentist</b><span class="t3">tomorrow at 9:00</span></span
              ></span
            ><span class="card"
              >${ic('note', 20, 'yellow')}<span
                ><b>Notes</b><span class="t3">Buy oat milk</span></span
              ></span
            >
          </div>
        </div>
        <div class="layer l5 col">
          <div class="row tools">
            <span class="tool">${ic('note', 22)}</span><span class="tool">${ic('bell', 22)}</span
            ><span class="tool on">${ic('calc', 22)}</span
            ><span class="tool">${ic('clip', 22)}</span
            ><span class="tool">${ic('speaker', 22)}</span
            ><span class="tool">${ic('picker', 22)}</span>
          </div>
          <div class="calc"><span class="sum">50 + 15%</span><b class="result">57.5</b></div>
        </div>
        <div class="layer l6 col">
          <span class="t3 up">Send Mockups.pdf to</span>
          <div class="row friends">
            <span class="av a1">L</span><span class="av a2">S</span><span class="av a3">M</span
            ><span class="av a4">J</span>
          </div>
          <div class="row sent">
            <span class="bar"><i></i></span><span class="ok">${ic('check', 18)}</span>
          </div>
        </div>
        <div class="layer l7 col">
          <span class="t3 up">Your invite code</span><b class="code">HOLM-7KQ2X-9XF4M</b>
          <span class="joined"><span class="av a1">L</span>Lena is now your friend</span>
        </div>
      </div>
    </div>
  </div>`;

  return /* HTML */ `<div class="home">
    <section class="h-hero" data-scrub="hero">
      <div class="hero-pin">
        <div class="pill" aria-hidden="true">
          <b>9:41</b><span class="sp"></span><span class="eq"><i></i><i></i><i></i><i></i></span>
        </div>
        <p class="kicker">Holm for Windows</p>
        <h1 class="title">Holm<span>.</span></h1>
        <p class="h-lede">The notch your Windows was missing.</p>
        <div class="h-actions">
          <a class="cta" href="${download}" data-download>${win} Download for Windows</a
          ><a class="more" href="#story">See it move <span aria-hidden="true">↓</span></a>
        </div>
        <p class="h-fine">
          Free &amp; open source · Windows 10 &amp; 11 · <span data-version>${esc(version)}</span>
        </p>
      </div>
    </section>

    <section class="story" id="story" aria-label="What the notch does">
      ${stage}
      <div class="chapters">
        ${chapters
          .map(
            ([kicker, title, text], i) =>
              `<article class="chapter" data-step="${i}"><p class="kicker">${kicker}</p><h2>${title}</h2><p>${text}</p></article>`,
          )
          .join('')}
      </div>
    </section>

    <section class="words" data-scrub="words" aria-label="Holm in a sentence">
      <p>${words}</p>
    </section>

    <section class="beam-big" data-scrub="beam">
      <p class="kicker">Beam</p>
      <h2 class="beam-word">From your notch<br />to theirs.</h2>
      <p class="beam-copy">
        Nearby over your network, or across the internet through a relay that can’t read what you
        send. No account, no cloud folder, no cable.
      </p>
    </section>

    <section class="little" aria-labelledby="little-title">
      <p class="kicker reveal-up">And all the little things</p>
      <h2 id="little-title" class="reveal-up">
        Small. Thoughtful.<br /><span>Everywhere you look.</span>
      </h2>
      <div class="tiles">
        ${tiles
          .map(
            ([icon, title, text]) =>
              `<article class="tile reveal-up">${ic(icon, 28)}<h3>${title}</h3><p>${text}</p></article>`,
          )
          .join('')}
      </div>
      <a class="more" href="${link('features/')}"
        >Explore every feature <span aria-hidden="true">→</span></a
      >
    </section>

    <section class="light" aria-labelledby="light-title">
      <h2 id="light-title" class="reveal-up">Light as air.</h2>
      <div class="facts">
        <div class="fact reveal-up">
          <b>⅓ s</b><span>of CPU a minute while it waits, measured on our test laptop.</span>
        </div>
        <div class="fact reveal-up">
          <b>0</b><span>pop-ups over your fullscreen games and videos.</span>
        </div>
        <div class="fact reveal-up">
          <b>MIT</b><span>Free and open source. Read every line on GitHub.</span>
        </div>
      </div>
    </section>

    <section class="get" id="get" aria-labelledby="get-title">
      <p class="kicker reveal-up">Get Holm</p>
      <h2 id="get-title" class="reveal-up">Make yourself at home.</h2>
      <div class="ways">
        <article class="way reveal-up">
          <p class="kicker">GitHub</p>
          <h3>Newest first.</h3>
          <p>
            Every release lands here first, with notes on what changed. Holm keeps itself up to
            date.
          </p>
          <a class="cta" href="${download}" data-download
            >${win} Download <span data-version>${esc(version)}</span></a
          >
          <span class="h-fine">Windows x64 · self-contained · no .NET needed</span>
        </article>
        <article class="way reveal-up">
          <p class="kicker">Microsoft Store</p>
          <h3>At the Store’s pace.</h3>
          <p>
            Less frequent, extra checks before each release, and updates from the Store. It may be a
            little behind.
          </p>
          ${
            store.url
              ? `<a class="cta ghost" href="${store.url}">Get it from Microsoft Store <span aria-hidden="true">↗</span></a>`
              : '<span class="cta ghost off">Microsoft Store · coming soon</span>'
          }
          <span class="h-fine">Signed by the Store · Store-managed updates</span>
        </article>
      </div>
      <p class="h-fine center reveal-up">
        The GitHub build is not code-signed yet: SmartScreen may ask before it opens, and Smart App
        Control may block it. The Store version is signed.
        <a href="${link('docs/installation/')}">Installation help</a> ·
        <a href="${repo}">Source on GitHub <span aria-hidden="true">↗</span></a>
      </p>
    </section>
  </div>`;
}
