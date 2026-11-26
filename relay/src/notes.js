// What the relay takes: Holm inboxes and Holm notes. Kept apart from index.js so tests can run it in Node.

const INBOX = /^wnf-[a-z2-7]{26}$/;
const INVITE = /^holm-inv-[a-z2-7]{20}$/;
const NOTE = /^wnf1 [A-Za-z0-9_-]{40,200} [A-Za-z0-9_-]{30,}$/;
const CARD = /^hinv1 [A-Za-z0-9_-]{30,}$/;
const READ_TOKEN = /^[A-Za-z0-9_-]{32,128}$/;
export const MAX_NOTE = 4096; // ntfy's limit, which Holm's notes are made to fit
export const MAX_BODY_BYTES = MAX_NOTE * 2; // hard streaming byte cap for POST/PUT bodies
export const INBOX_TTL = 7 * 24 * 3600;
export const INVITE_TTL = 12 * 3600;

export const isTopic = (t) => INBOX.test(t) || INVITE.test(t);
export const isInvite = (t) => INVITE.test(t);
export const isReadToken = (t) => typeof t === "string" && READ_TOKEN.test(t);
export const now = () => Math.floor(Date.now() / 1000);

/** ntfy's "since": Unix seconds, a duration like 12h, or "all". Anything else means "only new notes". */
export function parseSince(value, t = now()) {
  if (value == null || value === "") return null;
  if (value === "all") return 0;
  if (/^\d{1,12}$/.test(value)) return Number(value);
  const d = /^(\d{1,6})([smhd])$/.exec(value);
  if (d) return t - Number(d[1]) * { s: 1, m: 60, h: 3600, d: 86400 }[d[2]];
  return null;
}

/** Whether the text is a note this inbox takes: a friend note for an inbox, a sealed card for an invite code. */
export function isNoteFor(topic, text) {
  if (typeof text !== "string" || text.length > MAX_NOTE) return false;
  return isInvite(topic) ? CARD.test(text) : NOTE.test(text);
}

/**
 * Extracts an optional inbox read token from X-Holm-Read-Token (or Authorization: Bearer ...).
 * Returns { present: false, token: null } when omitted, { present: true, token } when valid,
 * or { present: true, token: null } when present but malformed.
 */
export function extractReadToken(request) {
  const direct = request.headers.get("x-holm-read-token");
  if (direct != null) {
    const trimmed = direct.trim();
    return { present: true, token: isReadToken(trimmed) ? trimmed : null };
  }
  const auth = request.headers.get("authorization");
  if (auth != null) {
    const token = auth.startsWith("Bearer ") ? auth.slice(7).trim() : "";
    return { present: true, token: isReadToken(token) ? token : null };
  }
  return { present: false, token: null };
}

/**
 * Streams a request body as UTF-8 text up to maxBytes bytes, rejecting oversized payloads even
 * when Content-Length is omitted (chunked), spoofed, or non-numeric. Returns null on overflow.
 */
export async function readTextCapped(request, maxBytes = MAX_BODY_BYTES) {
  const rawLen = request.headers.get("content-length");
  if (rawLen != null) {
    const trimmed = rawLen.trim();
    if (/^\d+$/.test(trimmed) && Number(trimmed) > maxBytes) return null;
  }
  if (!request.body) return "";
  const reader = request.body.getReader();
  const chunks = [];
  let total = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      if (!value || value.byteLength === 0) continue;
      total += value.byteLength;
      if (total > maxBytes) {
        await reader.cancel().catch(() => {});
        return null;
      }
      chunks.push(value);
    }
  } finally {
    try { reader.releaseLock(); } catch { /* already released */ }
  }
  const combined = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    combined.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new TextDecoder("utf-8").decode(combined);
}
