// Reads Focumon's logo-timer DOM into Holm's status shape.
// Focus/break sessions count up; break is "chillin'" / time-limit meta.
//
// Logo timer status is often "FOCUS!" (not the monster name). Every trainer
// on the floor has a focumon slot; yours is the one with the blue
// "Productive Me!" bubble above it. /focumons/… links also appear in side
// lists, so we must NOT pick "nearest link" — we probe on-screen above each
// floor slot for that bubble.
(() => {
  const CHILLIN = "chillin'";
  const TIME = /^(\d+):(\d{2})(?::(\d{2}))?$/;
  const MINI = /\/assets\/focumon\/mini\/[^"'?\s]+\.(png|webp|gif)/i;
  const BATTLE = /\/assets\/focumon\/battle\/[^"'?\s]+\.(png|webp|gif)/i;
  const FOCUMON_SLOT = '[data-auto-scroll-target="focumon"]';
  const FOCUMON_LINK = 'a[href*="/focumons/"]';
  const TRAINER_LINK = 'a[href*="/trainers/"]';
  const ME_LABEL = /productive\s*me!?/i;
  const OTHER_LABEL = /\b(focusing\.{0,3}|taking a break)\b/i;
  const FOCUMON_HREF = /\/focumons\/(\d+)-([a-z0-9_-]+)/i;
  const NOT_A_MONSTER = /^(focus!?|break!?|chillin'?|idle|focusing|taking a break)$/i;
  const BLUE_CLASS = /\b(bg-)?(blue|sky|indigo|cyan|azure)(-\d+)?\b/i;

  function meta(id) {
    const el = document.getElementById(id);
    return el && "content" in el ? String(el.content || "") : "";
  }

  function parseClock(text) {
    const m = TIME.exec(String(text || "").trim());
    if (!m) return null;
    const hours = Number(m[1]);
    const minutes = Number(m[2]);
    const seconds = m[3] != null ? Number(m[3]) : 0;
    if (![hours, minutes, seconds].every(Number.isFinite)) return null;
    return hours * 3600 + minutes * 60 + seconds;
  }

  function timerBody() {
    return document.querySelector('[data-logo-timer-target="body"]');
  }

  function liveSeconds() {
    const body = timerBody();
    if (body) {
      const fromBody = parseClock(body.textContent);
      if (fromBody != null) return fromBody;
    }
    const counted = Number.parseInt(meta("logo-timer-seconds-counted"), 10);
    return Number.isFinite(counted) && counted >= 0 ? counted : 0;
  }

  function monsterName(statusText, isBreak) {
    let name = (statusText || "").trim();
    if (name.includes("|")) name = name.split("|")[0].trim();
    if (!name || name.toLowerCase() === CHILLIN || NOT_A_MONSTER.test(name)) return "";
    if (isBreak && /break|chill/i.test(name)) return "";
    return name.slice(0, 40);
  }

  function titleCaseSlug(slug) {
    if (!slug) return "";
    return slug
      .split("-")
      .filter(Boolean)
      .map((w) => w.charAt(0).toUpperCase() + w.slice(1))
      .join(" ")
      .slice(0, 40);
  }

  function absoluteMonsterUrl(src) {
    if (!src) return null;
    try {
      const url = new URL(src, location.href);
      if (url.protocol !== "https:") return null;
      if (url.hostname !== "focumon.com" && !url.hostname.endsWith(".focumon.com")) return null;
      if (!MINI.test(url.pathname) && !BATTLE.test(url.pathname)) return null;
      return { href: url.href, mini: MINI.test(url.pathname) };
    } catch {
      return null;
    }
  }

  function urlsFromNode(root) {
    const found = [];
    const push = (src) => {
      const hit = absoluteMonsterUrl(src);
      if (hit && !found.some((f) => f.href === hit.href)) found.push(hit);
    };
    if (!root) return found;

    if (root.tagName === "IMG") push(root.currentSrc || root.getAttribute("src") || "");
    for (const img of root.querySelectorAll?.("img[src]") || []) {
      push(img.currentSrc || img.getAttribute("src") || "");
    }

    const styled = [root, ...(root.querySelectorAll?.("[style]") || [])];
    for (const el of styled) {
      const style = el.getAttribute?.("style") || "";
      for (const m of style.matchAll(/url\(["']?([^"')]+)["']?\)/gi)) push(m[1]);
      if (typeof getComputedStyle === "function" && el.nodeType === 1) {
        const cs = getComputedStyle(el);
        for (const m of `${cs.backgroundImage || ""} ${cs.maskImage || ""} ${cs.webkitMaskImage || ""}`.matchAll(
          /url\(["']?([^"')]+)["']?\)/gi
        )) {
          push(m[1]);
        }
      }
    }
    return found;
  }

  function pickUrl(candidates) {
    if (!candidates.length) return null;
    return (candidates.find((c) => c.mini) || candidates[0]).href;
  }

  function linkSlug(href) {
    const m = FOCUMON_HREF.exec(String(href || ""));
    return m ? m[2].toLowerCase() : "";
  }

  function monsterHref(link) {
    const href = link?.getAttribute?.("href") || link?.href || "";
    return FOCUMON_HREF.test(href) ? href : "";
  }

  function isVisible(el) {
    if (!el || typeof el.getBoundingClientRect !== "function") return false;
    const r = el.getBoundingClientRect();
    if (r.width < 4 || r.height < 4) return false;
    if (r.bottom < -20 || r.right < -20) return false;
    if (r.top > (window.innerHeight || 0) + 40 || r.left > (window.innerWidth || 0) + 40) return false;
    if (typeof getComputedStyle !== "function") return true;
    try {
      const cs = getComputedStyle(el);
      if (cs.display === "none" || cs.visibility === "hidden") return false;
      if (Number.parseFloat(cs.opacity || "1") < 0.05) return false;
      // Skip sr-only / clipped duplicates that steal "Productive Me" matches.
      if (cs.position === "absolute" && (r.width <= 1 || r.height <= 1)) return false;
      if (/(^|\s)(sr-only|visually-hidden|screen-reader)(\s|$)/i.test(String(el.className || ""))) return false;
    } catch {
      return false;
    }
    return true;
  }

  function centerOf(el) {
    if (!isVisible(el)) return null;
    const r = el.getBoundingClientRect();
    return { el, cx: r.left + r.width / 2, cy: r.top + r.height / 2, r, area: r.width * r.height };
  }

  function parseRgb(color) {
    if (!color || color === "transparent" || color === "none") return null;
    const m = String(color).match(/rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)/i);
    if (!m) return null;
    return { r: Number(m[1]), g: Number(m[2]), b: Number(m[3]) };
  }

  function isBlueish(rgb) {
    if (!rgb) return false;
    return rgb.b >= 120 && rgb.b > rgb.r + 10 && rgb.b >= rgb.g - 5;
  }

  function elementLooksBlue(el) {
    const cls = typeof el.className === "string" ? el.className : el.className?.baseVal || "";
    if (BLUE_CLASS.test(cls)) return true;
    if (typeof getComputedStyle !== "function") return false;
    try {
      const cs = getComputedStyle(el);
      if (isBlueish(parseRgb(cs.backgroundColor))) return true;
      if (/blue|sky|indigo|cyan/i.test(cs.backgroundImage || "")) return true;
    } catch {
      return false;
    }
    return false;
  }

  function relatedLink(slot) {
    const own = slot.closest?.(FOCUMON_LINK);
    if (own && monsterHref(own)) return own;
    let el = slot.parentElement;
    for (let i = 0; i < 8 && el && el !== document.body; i++, el = el.parentElement) {
      const links = [...(el.querySelectorAll?.(FOCUMON_LINK) || [])].filter((a) => monsterHref(a));
      if (links.length === 1) return links[0];
      if (links.length > 1) break;
    }
    return null;
  }

  function relatedTrainer(slot) {
    let el = slot.parentElement;
    for (let i = 0; i < 8 && el && el !== document.body; i++, el = el.parentElement) {
      const links = [...(el.querySelectorAll?.(TRAINER_LINK) || [])];
      if (links.length === 1) return links[0];
      if (links.length > 1) break;
    }
    return null;
  }

  // Sample what's painted above/around a floor sprite (speech bubbles sit above).
  function probeSlot(slot) {
    const c = centerOf(slot) || centerOf(slot.querySelector?.("img"));
    if (!c) return { me: false, other: false, blue: false, text: "", points: 0 };

    const texts = [];
    let me = false;
    let other = false;
    let blue = false;
    const seen = new Set();

    const consider = (el) => {
      if (!el || el.nodeType !== 1 || seen.has(el)) return;
      seen.add(el);
      // Own text only — parent floors include everyone's bubbles and false-positive "me".
      let raw = "";
      for (const n of el.childNodes || []) {
        if (n.nodeType === 3) raw += n.nodeValue || "";
      }
      raw = raw.replace(/\s+/g, " ").trim();
      if (!raw) {
        raw = (el.innerText || el.textContent || "").replace(/\s+/g, " ").trim();
        // Huge containers (the whole floor) are useless as labels.
        if (raw.length > 220) {
          if (elementLooksBlue(el)) blue = true;
          return;
        }
      }
      if (raw) texts.push(raw);
      if (ME_LABEL.test(raw)) me = true;
      if (OTHER_LABEL.test(raw)) other = true;
      if (elementLooksBlue(el)) blue = true;
    };

    if (typeof document.elementsFromPoint === "function") {
      const xs = [c.cx - 30, c.cx, c.cx + 30];
      for (let dy = 12; dy <= 220; dy += 18) {
        for (const x of xs) {
          const y = c.cy - dy;
          if (y < 0) continue;
          let stack;
          try {
            stack = document.elementsFromPoint(x, y);
          } catch {
            continue;
          }
          for (const el of stack.slice(0, 8)) consider(el);
        }
      }
      // Also a bit to the side (bubble sometimes offset).
      for (const x of [c.cx - 70, c.cx + 70]) {
        for (const y of [c.cy - 40, c.cy - 90, c.cy - 140]) {
          if (y < 0) continue;
          try {
            for (const el of document.elementsFromPoint(x, y).slice(0, 6)) consider(el);
          } catch {
            /* ignore */
          }
        }
      }
    }

    // Rect-near fallback when elementsFromPoint is empty (covered by canvas, etc.).
    if (!me && !other) {
      for (const el of document.querySelectorAll("div, span, p, button")) {
        const tc = centerOf(el);
        if (!tc || tc.area > 80000) continue;
        const d = Math.hypot(tc.cx - c.cx, tc.cy - c.cy);
        if (d > 180) continue;
        if (tc.cy > c.cy + 30) continue; // bubble should be above / level
        consider(el);
      }
    }

    return { me, other, blue, text: texts.join(" | ").slice(0, 200), points: seen.size };
  }

  function currentTrainerHints() {
    // Only Stimulus currentTrainerId — scraping every /trainers/ link matches strangers.
    const hints = [];
    const seen = new Set();
    const add = (type, value) => {
      const key = type + ":" + value;
      if (!value || seen.has(key)) return;
      seen.add(key);
      hints.push({ type, value });
    };
    for (const el of document.querySelectorAll("*")) {
      for (const attr of el.attributes || []) {
        if (/current-trainer-id-value$/i.test(attr.name) && attr.value) add("id", attr.value);
      }
    }
    return hints;
  }

  function pickMine() {
    const slots = [...document.querySelectorAll(FOCUMON_SLOT)];
    const hints = currentTrainerHints();
    const scored = [];

    for (const slot of slots) {
      const url = pickUrl(urlsFromNode(slot));
      if (!url) continue;
      const c = centerOf(slot) || centerOf(slot.querySelector?.("img"));
      if (!c) continue;

      const probe = probeSlot(slot);
      const link = relatedLink(slot);
      const trainer = relatedTrainer(slot);
      const href = monsterHref(link) || "";
      const trainerHref = trainer?.getAttribute?.("href") || "";
      const blob = probe.text || "";

      let score = 0;
      const reason = [];

      // Strongest: the short bubble text actually sampled above this sprite.
      if (ME_LABEL.test(blob)) {
        score += 3000;
        reason.push("me-text");
      } else if (probe.me) {
        score += 400;
        reason.push("me");
      }

      if (probe.blue && ME_LABEL.test(blob)) {
        score += 300;
        reason.push("blue-me");
      } else if (probe.blue && !OTHER_LABEL.test(blob)) {
        score += 80;
        reason.push("blue");
      }

      if (OTHER_LABEL.test(blob) && !ME_LABEL.test(blob)) {
        score -= 2000;
        reason.push("other-text");
      } else if (probe.other && !probe.me && !ME_LABEL.test(blob)) {
        score -= 800;
        reason.push("other");
      }

      scored.push({
        url,
        href,
        slug: linkSlug(href),
        trainerHref,
        score,
        reason: reason.join(","),
        probe: { me: probe.me, other: probe.other, blue: probe.blue, text: probe.text },
        cx: Math.round(c.cx),
        cy: Math.round(c.cy),
      });
    }

    scored.sort((a, b) => b.score - a.score || a.cy - b.cy);
    const best = scored.find((s) => s.score > 0) || null;

    if (best) {
      console.info("Focumon for Holm: mine →", best.url, best.href, best.reason, best.probe);
    } else {
      console.info("Focumon for Holm: no Productive Me slot found", {
        slots: slots.length,
        hints,
        top: scored.slice(0, 5),
      });
    }

    return { best, scored: scored.slice(0, 8), hints, slots: slots.length };
  }

  function read() {
    const activated = meta("logo-timer-activated") === "true";
    if (!activated) return { v: 1, phase: "gone" };

    const statusText = meta("logo-timer-status-text") || "";
    const isBreak =
      document.title.trim().toLowerCase() === CHILLIN ||
      meta("logo-timer-time-limit-seconds") !== "" ||
      statusText.trim().toLowerCase() === CHILLIN;

    const picked = pickMine();
    const monster = monsterName(statusText, isBreak) || titleCaseSlug(picked.best?.slug);

    return {
      v: 1,
      phase: isBreak ? "break" : "focus",
      down: false,
      seconds: liveSeconds(),
      monster,
      image: picked.best?.url || null,
    };
  }

  function debug() {
    const picked = pickMine();
    const status = read();
    const report = {
      status,
      best: picked.best,
      top: picked.scored,
      hints: picked.hints,
      slots: picked.slots,
      breeze: picked.scored.filter((s) => /breeze/i.test(s.href || s.slug || "")),
      version: document.documentElement?.dataset?.holmFocumon || "",
    };
    console.info("Focumon for Holm debug", report);
    return report;
  }

  globalThis.FocumonReaders = { read, debug };
})();
