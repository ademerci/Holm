// What the relay takes: Holm inboxes and Holm notes. Kept apart from index.js so tests can run it in Node.

const INBOX = /^wnf-[a-z2-7]{26}$/;
const INVITE = /^holm-inv-[a-z2-7]{20}$/;
const NOTE = /^wnf1 [A-Za-z0-9_-]{40,200} [A-Za-z0-9_-]{30,}$/;
const CARD = /^hinv1 [A-Za-z0-9_-]{30,}$/;
export const MAX_NOTE = 4096; // ntfy's limit, which Holm's notes are made to fit
export const INBOX_TTL = 7 * 24 * 3600;
export const INVITE_TTL = 12 * 3600;

export const isTopic = (t) => INBOX.test(t) || INVITE.test(t);
export const isInvite = (t) => INVITE.test(t);
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
