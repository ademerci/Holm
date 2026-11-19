// Motion for the home page (home.mjs, home.css). The page reads fine without it; with reduced motion, nothing
// moves. Work happens only while a moving part is on screen: no timers, one animation frame per scroll at most.
const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
const watch = 'IntersectionObserver' in window;
document.documentElement.classList.add('h-js');
const clamp = (v) => Math.min(1, Math.max(0, v));

// The pinned screen shows the chapter in the middle of the window, and is sized to fit its column.
const stage = document.querySelector('[data-stage]');
const chapters = [...document.querySelectorAll('.chapter')];
function fit() {
  const scale = Math.min(stage.clientWidth / 960, stage.clientHeight / 600);
  stage.style.setProperty('--fit', String(Math.min(1.1, Math.max(0.25, scale))));
}
function show(step) {
  stage.dataset.step = String(step);
  chapters.forEach((c) => c.classList.toggle('on', c.dataset.step === String(step)));
}
if (stage) {
  fit();
  addEventListener('resize', fit, { passive: true });
  show(0);
  if (watch) {
    const reading = new IntersectionObserver(
      (entries) => entries.forEach((e) => e.isIntersecting && show(Number(e.target.dataset.step))),
      { rootMargin: '-45% 0px -45% 0px' },
    );
    chapters.forEach((c) => reading.observe(c));
  }
}

// Scroll-linked parts: the hero recedes, the big Beam line grows in, the sentence lights up word by word.
const scrubbed = [...document.querySelectorAll('[data-scrub]')];
const onScreen = new Set();
let frame = 0;
function progress(el) {
  const r = el.getBoundingClientRect();
  const vh = innerHeight;
  if (el.dataset.scrub === 'hero') return clamp(-r.top / Math.max(1, r.height - vh));
  if (el.dataset.scrub === 'words') {
    // From when the sentence's top reaches 70 % of the window until its end passes 40 %.
    const text = el.firstElementChild.getBoundingClientRect();
    return clamp((vh * 0.7 - text.top) / (vh * 0.3 + text.height));
  }
  return clamp((vh - r.top) / (vh * 0.85));
}
function light(el, p) {
  el.words ??= [...el.querySelectorAll('.w')];
  const lit = Math.round(p * el.words.length);
  if (lit === el.lit) return;
  el.lit = lit;
  el.words.forEach((w, i) => w.classList.toggle('lit', i < lit));
}
function update() {
  frame = 0;
  for (const el of onScreen) {
    const p = progress(el);
    if (el.dataset.scrub === 'words') light(el, p);
    else el.style.setProperty('--p', p.toFixed(3));
  }
}
const schedule = () => {
  if (!frame && onScreen.size) frame = requestAnimationFrame(update);
};
if (!still && watch) {
  const near = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (e.isIntersecting) onScreen.add(e.target);
      else onScreen.delete(e.target);
      e.target.classList.toggle('off', !e.isIntersecting);
    }
    schedule();
  });
  scrubbed.forEach((el) => near.observe(el));
  if (stage)
    new IntersectionObserver(([e]) => stage.classList.toggle('off', !e.isIntersecting)).observe(
      stage,
    );
  addEventListener('scroll', schedule, { passive: true });
  addEventListener('resize', schedule, { passive: true });
} else scrubbed.forEach((el) => el.dataset.scrub === 'words' && light(el, 1));

// Sections below the fold rise in as they arrive.
if (!still && watch) {
  const arriving = new IntersectionObserver(
    (entries) => {
      for (const e of entries) {
        if (!e.isIntersecting) continue;
        e.target.classList.remove('pending');
        arriving.unobserve(e.target);
      }
    },
    { rootMargin: '0px 0px -8% 0px' },
  );
  for (const el of document.querySelectorAll('.reveal-up')) {
    if (el.getBoundingClientRect().top < innerHeight) continue;
    el.classList.add('pending');
    arriving.observe(el);
  }
  addEventListener('beforeprint', () =>
    document.querySelectorAll('.pending').forEach((el) => el.classList.remove('pending')),
  );
}
