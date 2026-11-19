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
import { isInvite, isNoteFor, isTopic, parseSince, now, INBOX_TTL, INVITE_TTL, MAX_NOTE } from "./notes.js";
import { admin, track } from "./stats.js";

const INBOX_KEEP = 300, INVITE_KEEP = 20; // notes kept per inbox: the oldest go first
const TOPIC_PER_MINUTE = 60; // notes one inbox takes per minute
const SOCKETS = 8; // one identity can follow its inbox from a few PCs (Holm account)

function json(status, body) {
  return new Response(JSON.stringify(body) + "\n", { status, headers: { "content-type": "application/json" } });
}
const fail = (status, error) => json(status, { code: status, error });

function randomId() {
  const alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
  const bytes = crypto.getRandomValues(new Uint8Array(12));
  return Array.from(bytes, (b) => alphabet[b % 62]).join("");
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
  const stub = env.INBOX.get(env.INBOX.idFromName(topic));
  const action = parts[1] ?? "";

  if ((request.method === "POST" || request.method === "PUT") && action === "") {
    if (Number(request.headers.get("content-length") ?? 0) > MAX_NOTE * 2) return fail(413, "note too large");
    // Abuse: Cloudflare's rate limiter per address (approximate, per location), then the inbox's own limit.
    const ip = request.headers.get("cf-connecting-ip") ?? "local";
    if (env.POST_LIMIT && !(await env.POST_LIMIT.limit({ key: ip })).success) return fail(429, "too many notes, wait a minute");
    const text = await request.text();
    if (!isNoteFor(topic, text)) return fail(400, "not a Holm note");
    const keep = !/^no$/i.test(request.headers.get("cache") ?? request.headers.get("x-cache") ?? "");
    return stub.fetch(`https://inbox/${topic}`, { method: "POST", body: text, headers: { "x-keep": keep ? "1" : "0" } });
  }
  if (request.method === "GET" && action === "json") {
    if (!["1", "true", "yes"].includes(url.searchParams.get("poll") ?? "")) return fail(400, "only polling: use /ws to follow");
    return stub.fetch(`https://inbox/${topic}/json?since=${encodeURIComponent(url.searchParams.get("since") ?? "")}`);
  }
  if (request.method === "GET" && action === "ws") {
    if (request.headers.get("upgrade")?.toLowerCase() !== "websocket") return fail(426, "a WebSocket only");
    return stub.fetch(request);
  }
  return fail(405, "not here");
}

/** One inbox: its notes in SQLite, and the WebSockets following it. */
export class Inbox extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    this.ready = false;
    this.posts = []; // times of the last minute's notes; starts empty after the object slept, which is fine
  }

  table() {
    if (this.ready) return;
    this.sql.exec("CREATE TABLE IF NOT EXISTS notes (seq INTEGER PRIMARY KEY, id TEXT NOT NULL, time INTEGER NOT NULL, expires INTEGER NOT NULL, message TEXT NOT NULL)");
    this.ready = true;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const [topic, action] = url.pathname.split("/").filter(Boolean);
    if (request.method === "POST") return this.post(topic, await request.text(), request.headers.get("x-keep") === "1");
    const since = parseSince(url.searchParams.get("since"));
    if (action === "json") return new Response(this.kept(since).map((n) => JSON.stringify({ ...n, event: "message", topic }) + "\n").join(""),
                                               { headers: { "content-type": "application/x-ndjson" } });
    return this.follow(topic, since);
  }

  async post(topic, text, keep) {
    const t = now();
    this.posts = this.posts.filter((p) => p > t - 60);
    if (this.posts.length >= TOPIC_PER_MINUTE) return fail(429, "this inbox takes no more notes this minute");
    this.posts.push(t);
    const note = { id: randomId(), time: t, event: "message", topic, message: text };
    const line = JSON.stringify(note);
    for (const ws of this.ctx.getWebSockets()) {
      try { ws.send(line); } catch { /* closing */ }
    }
    if (keep) {
      this.table();
      const ttl = isInvite(topic) ? INVITE_TTL : INBOX_TTL;
      const { seq } = this.sql.exec("INSERT INTO notes (id, time, expires, message) VALUES (?, ?, ?, ?) RETURNING seq",
                                    note.id, t, t + ttl, text).one();
      const keepCount = isInvite(topic) ? INVITE_KEEP : INBOX_KEEP;
      if (seq > keepCount) this.sql.exec("DELETE FROM notes WHERE seq <= ?", seq - keepCount); // by primary key: reads only what goes
      if (!this.alarmSet) {
        this.alarmSet = (await this.ctx.storage.getAlarm()) != null;
        if (!this.alarmSet) { await this.ctx.storage.setAlarm((t + ttl + 60) * 1000); this.alarmSet = true; }
      }
    }
    return json(200, note);
  }

  /** The kept notes since a time, oldest first; none for "only new ones". */
  kept(since) {
    if (since == null) return [];
    this.table();
    return this.sql.exec("SELECT id, time, message FROM notes WHERE time >= ? AND expires > ? ORDER BY seq", since, now()).toArray();
  }

  follow(topic, since) {
    const open = this.ctx.getWebSockets();
    if (open.length >= SOCKETS) { try { open[0].close(1013, "too many followers"); } catch { /* gone */ } }
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

  /** Expired notes go; an inbox with none left is deleted entirely, so it takes no storage. */
  async alarm() {
    this.alarmSet = false;
    this.table();
    this.sql.exec("DELETE FROM notes WHERE expires <= ?", now());
    const next = this.sql.exec("SELECT MIN(expires) AS e FROM notes").one().e;
    if (next != null) {
      await this.ctx.storage.setAlarm((next + 60) * 1000);
      this.alarmSet = true;
    } else {
      await this.ctx.storage.deleteAll();
      this.ready = false;
    }
  }
}
