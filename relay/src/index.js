// Holm relay: the inbox server for Holm's friend notes, on Cloudflare Workers with one Durable Object per inbox.
//
// It speaks the small part of the ntfy protocol Holm uses, so older Holm versions can leave notes here too:
//   POST /<topic>                    leave a note (text body); "Cache: no" delivers it live without keeping it
//   GET  /<topic>/json?poll=1&since= the notes kept since a time (Unix seconds, or 12h, 30m, all), one JSON per line
// and one thing ntfy doesn't have, which only Holm 1.18+ uses:
//   GET  /<topic>/ws?since=          a WebSocket: an "open" event, the kept notes since then, then each new note live
//
// The notes are sealed by Holm end to end (Friends.cs, FriendInvite.cs); this server only sees ciphertext and the inbox
// name. It takes nothing but Holm notes, keeps an inbox's last notes for a week (an invite card for 12 hours) and sleeps
// between notes: a waiting WebSocket costs nothing while its Durable Object hibernates.

import { DurableObject } from "cloudflare:workers";
import {
  isInvite,
  isNoteFor,
  isTopic,
  parseSince,
  now,
  extractReadToken,
  readTextCapped,
  INBOX_TTL,
  INVITE_TTL,
  MAX_BODY_BYTES,
} from "./notes.js";
import { admin, track } from "./stats.js";

const INBOX_KEEP = 300, INVITE_KEEP = 20; // notes kept per inbox: the oldest go first
const TOPIC_PER_MINUTE = 60; // notes one inbox takes per minute across all senders
const IP_TOPIC_PER_MINUTE = 20; // notes one address may send to one inbox per minute
const INBOX_IP_KEPT_PER_HOUR = 100; // kept notes one address may send to one inbox per hour (< INBOX_KEEP)
const INVITE_IP_KEPT_PER_HOUR = 6; // kept cards one address may send to one invite per hour (< INVITE_KEEP)
const READ_PER_MINUTE = 60; // fallback per-address read/ws cap when READ_LIMIT binding isn't present
const SOCKETS = 8; // one identity can follow its inbox from a few PCs (Holm account)
const AUTH_TTL = 30 * 24 * 3600; // rolling 30-day TTL for an inbox's registered read-token hash
const AUTH_REFRESH_THRESHOLD = 15 * 24 * 3600;
const MAX_TRACKED_IPS = 256;

const readHits = new Map();

function json(status, body) {
  return new Response(JSON.stringify(body) + "\n", { status, headers: { "content-type": "application/json" } });
}
const fail = (status, error) => json(status, { code: status, error });

function randomId() {
  const alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
  const bytes = crypto.getRandomValues(new Uint8Array(12));
  return Array.from(bytes, (b) => alphabet[b % 62]).join("");
}

async function sha256Bytes(text) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return new Uint8Array(digest);
}

function bytesToHex(bytes) {
  return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
}

function hexToBytes(hex) {
  const out = new Uint8Array(hex.length / 2);
  for (let i = 0; i < out.length; i++) out[i] = parseInt(hex.slice(i * 2, i * 2 + 2), 16);
  return out;
}

function allowReadLocal(ip) {
  const t = Date.now();
  const cutoff = t - 60_000;
  const recent = (readHits.get(ip) ?? []).filter((ts) => ts > cutoff);
  if (recent.length >= READ_PER_MINUTE) {
    readHits.set(ip, recent);
    return false;
  }
  recent.push(t);
  readHits.set(ip, recent);
  if (readHits.size > 2048) {
    for (const [k, v] of readHits) {
      if (v.length === 0 || v[v.length - 1] <= cutoff) readHits.delete(k);
    }
    if (readHits.size > 2048) readHits.delete(readHits.keys().next().value);
  }
  return true;
}

async function allowRead(env, ip) {
  const limiter = env.READ_LIMIT ?? env.POST_LIMIT;
  if (limiter && !(await limiter.limit({ key: `read:${ip}` })).success) return false;
  return allowReadLocal(ip);
}

/** What a request was, for the counts (stats.js); null for the plain home page. */
function kindOf(request, url, response, topic) {
  if (!topic) return null;
  if (response.status >= 400) return "refused";
  if (request.method === "POST" || request.method === "PUT") {
    if (isInvite(topic)) return "invite";
    return /^no$/i.test(request.headers.get("cache") ?? request.headers.get("x-cache") ?? "") ? "live" : "note";
  }
  return url.pathname.endsWith("/ws") ? "open" : "poll";
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/admin" || url.pathname.startsWith("/admin/")) return (await admin(request, env, url.pathname)) ?? fail(404, "no such page");
    const response = await route(request, env, url);
    const topic = url.pathname.split("/").filter(Boolean)[0];
    const kind = kindOf(request, url, response, topic);
    if (kind) await track(env, kind, topic);
    return response;
  },
};

async function route(request, env, url) {
  const parts = url.pathname.split("/").filter(Boolean);
  if (parts.length === 0) return new Response("Holm relay: inboxes for Holm's friend notes.\n");
  const topic = parts[0];
  if (!isTopic(topic) || parts.length > 2) return fail(404, "no such inbox");
  const action = parts[1] ?? "";
  const ip = request.headers.get("cf-connecting-ip") ?? "local";

  if ((request.method === "POST" || request.method === "PUT") && action === "") {
    // Abuse: Cloudflare's rate limiter per address (approximate, per location), then the inbox's own limits.
    if (env.POST_LIMIT && !(await env.POST_LIMIT.limit({ key: ip })).success) return fail(429, "too many notes, wait a minute");
    const text = await readTextCapped(request, MAX_BODY_BYTES);
    if (text === null) return fail(413, "note too large");
    if (!isNoteFor(topic, text)) return fail(400, "not a Holm note");
    const keep = !/^no$/i.test(request.headers.get("cache") ?? request.headers.get("x-cache") ?? "");
    const clientKey = bytesToHex((await sha256Bytes(ip)).slice(0, 8));
    const stub = env.INBOX.get(env.INBOX.idFromName(topic));
    return stub.fetch(`https://inbox/${topic}`, {
      method: "POST",
      body: text,
      headers: { "x-keep": keep ? "1" : "0", "x-client-key": clientKey },
    });
  }
  if (request.method === "GET" && action === "json") {
    if (!["1", "true", "yes"].includes(url.searchParams.get("poll") ?? "")) return fail(400, "only polling: use /ws to follow");
    if (!(await allowRead(env, ip))) return fail(429, "too many requests, wait a minute");
    const headers = {};
    if (!isInvite(topic)) {
      const { present, token } = extractReadToken(request);
      if (present && !token) return fail(401, "unauthorized");
      if (token) headers["x-holm-read-token"] = token;
    }
    const stub = env.INBOX.get(env.INBOX.idFromName(topic));
    return stub.fetch(`https://inbox/${topic}/json?since=${encodeURIComponent(url.searchParams.get("since") ?? "")}`, { headers });
  }
  if (request.method === "GET" && action === "ws") {
    if (request.headers.get("upgrade")?.toLowerCase() !== "websocket") return fail(426, "a WebSocket only");
    if (!(await allowRead(env, ip))) return fail(429, "too many requests, wait a minute");
    if (!isInvite(topic)) {
      const { present, token } = extractReadToken(request);
      if (present && !token) return fail(401, "unauthorized");
    }
    const stub = env.INBOX.get(env.INBOX.idFromName(topic));
    return stub.fetch(request);
  }
  return fail(405, "not here");
}

/** One inbox: its notes in SQLite, and the WebSockets following it. */
export class Inbox extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    this.hasNotes = null; // null = unchecked, false = absent, true = created
    this.hasMeta = null;
    this.posts = []; // times of the last minute's notes across all senders
    this.ipMinute = new Map(); // sender hash -> timestamps (s) in last 60s
    this.ipHourKept = new Map(); // sender hash -> timestamps (s) in last 3600s for kept notes
  }

  tableExists(name) {
    return this.sql.exec("SELECT 1 AS ok FROM sqlite_master WHERE type = 'table' AND name = ?", name).toArray().length > 0;
  }

  notesExist() {
    if (this.hasNotes === null) this.hasNotes = this.tableExists("notes");
    return this.hasNotes;
  }

  metaExist() {
    if (this.hasMeta === null) this.hasMeta = this.tableExists("meta");
    return this.hasMeta;
  }

  ensureNotesTable() {
    if (this.hasNotes === true) return;
    this.sql.exec("CREATE TABLE IF NOT EXISTS notes (seq INTEGER PRIMARY KEY, id TEXT NOT NULL, time INTEGER NOT NULL, expires INTEGER NOT NULL, message TEXT NOT NULL)");
    this.hasNotes = true;
  }

  ensureMetaTable() {
    if (this.hasMeta === true) return;
    this.sql.exec("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL, expires INTEGER NOT NULL)");
    this.hasMeta = true;
  }

  async ensureAlarm(targetSec) {
    const targetMs = targetSec * 1000;
    const current = await this.ctx.storage.getAlarm();
    if (current == null || targetMs < current) {
      await this.ctx.storage.setAlarm(targetMs);
    }
  }

  pruneIpBucket(map, sender, cutoff) {
    const list = (map.get(sender) ?? []).filter((ts) => ts > cutoff);
    if (map.size >= MAX_TRACKED_IPS && !map.has(sender)) {
      for (const [k, v] of map) {
        const kept = v.filter((ts) => ts > cutoff);
        if (kept.length === 0) map.delete(k);
        else map.set(k, kept);
      }
      if (map.size >= MAX_TRACKED_IPS) map.delete(map.keys().next().value);
    }
    return list;
  }

  /**
   * Read authentication for wnf-* personal inboxes (not holm-inv-* invite topics).
   * - Once a read_hash is bound, matching X-Holm-Read-Token is required (401 otherwise).
   * - First valid X-Holm-Read-Token binds read_hash with a rolling TTL and cleanup alarm.
   * - Unauthenticated reads on an unbound inbox remain allowed for legacy Holm (<2.4) without initializing storage.
   */
  async authorizeRead(topic, request) {
    if (isInvite(topic)) return true;
    const { present, token } = extractReadToken(request);
    if (present && !token) return false;

    const t = now();
    let stored = null;
    if (this.metaExist()) {
      const rows = this.sql.exec("SELECT value, expires FROM meta WHERE key = 'read_hash'").toArray();
      if (rows.length > 0 && rows[0].expires > t) stored = rows[0];
    }

    if (stored != null) {
      if (!token) return false;
      const supplied = await sha256Bytes(token);
      const expected = hexToBytes(stored.value);
      if (supplied.byteLength !== expected.byteLength || !crypto.subtle.timingSafeEqual(supplied, expected)) return false;
      if (stored.expires - t < AUTH_REFRESH_THRESHOLD) {
        const nextExp = t + AUTH_TTL;
        this.sql.exec("UPDATE meta SET expires = ? WHERE key = 'read_hash'", nextExp);
        await this.ensureAlarm(nextExp + 60);
      }
      return true;
    }

    if (token) {
      const hashHex = bytesToHex(await sha256Bytes(token));
      this.ensureMetaTable();
      const exp = t + AUTH_TTL;
      this.sql.exec("INSERT OR REPLACE INTO meta (key, value, expires) VALUES ('read_hash', ?, ?)", hashHex, exp);
      await this.ensureAlarm(exp + 60);
    }
    return true;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const [topic, action] = url.pathname.split("/").filter(Boolean);
    if (request.method === "POST") {
      return this.post(topic, await request.text(), request.headers.get("x-keep") === "1", request.headers.get("x-client-key") ?? "unknown");
    }
    if (!(await this.authorizeRead(topic, request))) return fail(401, "unauthorized");
    const since = parseSince(url.searchParams.get("since"));
    if (action === "json") {
      return new Response(this.kept(since).map((n) => JSON.stringify({ ...n, event: "message", topic }) + "\n").join(""),
                          { headers: { "content-type": "application/x-ndjson" } });
    }
    return this.follow(topic, since);
  }

  async post(topic, text, keep, clientKey) {
    const t = now();
    this.posts = this.posts.filter((p) => p > t - 60);
    if (this.posts.length >= TOPIC_PER_MINUTE) return fail(429, "this inbox takes no more notes this minute");

    const minList = this.pruneIpBucket(this.ipMinute, clientKey, t - 60);
    if (minList.length >= IP_TOPIC_PER_MINUTE) return fail(429, "too many notes from this address to this inbox");

    let hourList = null;
    if (keep) {
      const hourCap = isInvite(topic) ? INVITE_IP_KEPT_PER_HOUR : INBOX_IP_KEPT_PER_HOUR;
      hourList = this.pruneIpBucket(this.ipHourKept, clientKey, t - 3600);
      if (hourList.length >= hourCap) return fail(429, "too many kept notes from this address to this inbox");
    }

    this.posts.push(t);
    minList.push(t);
    this.ipMinute.set(clientKey, minList);
    if (keep && hourList) {
      hourList.push(t);
      this.ipHourKept.set(clientKey, hourList);
    }

    const note = { id: randomId(), time: t, event: "message", topic, message: text };
    const line = JSON.stringify(note);
    for (const ws of this.ctx.getWebSockets()) {
      try { ws.send(line); } catch { /* closing */ }
    }
    if (keep) {
      this.ensureNotesTable();
      const ttl = isInvite(topic) ? INVITE_TTL : INBOX_TTL;
      const { seq } = this.sql.exec("INSERT INTO notes (id, time, expires, message) VALUES (?, ?, ?, ?) RETURNING seq",
                                    note.id, t, t + ttl, text).one();
      const keepCount = isInvite(topic) ? INVITE_KEEP : INBOX_KEEP;
      if (seq > keepCount) this.sql.exec("DELETE FROM notes WHERE seq <= ?", seq - keepCount); // by primary key: reads only what goes
      await this.ensureAlarm(t + ttl + 60);
    }
    return json(200, note);
  }

  /** The kept notes since a time, oldest first; none for "only new ones" or an empty/uninitialized inbox. */
  kept(since) {
    if (since == null || !this.notesExist()) return [];
    return this.sql.exec("SELECT id, time, message FROM notes WHERE time >= ? AND expires > ? ORDER BY seq", since, now()).toArray();
  }

  follow(topic, since) {
    const open = this.ctx.getWebSockets();
    if (open.length >= SOCKETS) return fail(429, "too many followers");
    const [client, server] = Object.values(new WebSocketPair());
    this.ctx.acceptWebSocket(server); // hibernatable: no cost while nothing happens
    server.serializeAttachment({ topic });
    server.send(JSON.stringify({ id: randomId(), time: now(), event: "open", topic }));
    for (const n of this.kept(since)) server.send(JSON.stringify({ ...n, event: "message", topic }));
    return new Response(null, { status: 101, webSocket: client });
  }

  webSocketMessage() { /* followers only listen; keepalives are protocol pings, answered without waking us */ }

  async webSocketClose(ws, code) {
    try { ws.close(code === 1005 ? 1000 : code, "bye"); } catch { /* already closed */ }
    await track(this.env, "close", ws.deserializeAttachment()?.topic);
  }

  async webSocketError(ws) {
    await track(this.env, "close", ws.deserializeAttachment()?.topic);
  }

  /** Expired notes and stale auth bindings go; an empty inbox is deleted entirely so it takes no storage. */
  async alarm() {
    const t = now();
    let next = null;
    if (this.notesExist()) {
      this.sql.exec("DELETE FROM notes WHERE expires <= ?", t);
      const row = this.sql.exec("SELECT MIN(expires) AS e FROM notes").one();
      if (row.e != null) next = row.e;
    }
    if (this.metaExist()) {
      if (this.ctx.getWebSockets().length === 0) {
        this.sql.exec("DELETE FROM meta WHERE expires <= ?", t);
      }
      const row = this.sql.exec("SELECT MIN(expires) AS e FROM meta").one();
      if (row.e != null) {
        const metaNext = Math.max(row.e, t + 60);
        next = next == null ? metaNext : Math.min(next, metaNext);
      }
    }
    if (next != null) {
      await this.ctx.storage.setAlarm((next + 60) * 1000);
    } else {
      await this.ctx.storage.deleteAll();
      this.hasNotes = false;
      this.hasMeta = false;
    }
  }
}
