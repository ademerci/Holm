// Runs the relay locally (wrangler dev: the real workerd runtime with local Durable Objects and SQLite) and checks it
// the way Holm uses it. `npm test` in relay/.
import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import http from "node:http";
import { fileURLToPath } from "node:url";
import { isNoteFor, parseSince, readTextCapped, inboxFor, MAX_BODY_BYTES } from "../src/notes.js";

const PORT = 8799;
const BASE = `http://127.0.0.1:${PORT}`;
let dev;

const letters = "abcdefghijklmnopqrstuvwxyz234567";
const topic = (prefix, n) => prefix + Array.from(crypto.getRandomValues(new Uint8Array(n)), (b) => letters[b & 31]).join("");
const inbox = () => topic("wnf-", 26);
const invite = () => topic("holm-inv-", 20);
const note = (body = "B".repeat(120)) => `wnf1 ${"A".repeat(91)} ${body}`;
const card = () => `hinv1 ${"C".repeat(80)}`;
// Each run is its own address to the per-address limit, so runs right after one another don't share a minute.
const ip = `10.${[0, 0, 0].map(() => Math.floor(Math.random() * 250)).join(".")}`;
const post = (t, text, headers = {}) => fetch(`${BASE}/${t}`, { method: "POST", body: text, headers: { "cf-connecting-ip": ip, ...headers } });
const TOKEN = "t".repeat(43); // every test inbox is read with this token unless a test says otherwise
const poll = async (t, since = "all", headers = {}) => (await (await fetch(`${BASE}/${t}/json?poll=1&since=${since}`, { headers: { "x-holm-read-token": TOKEN, ...headers } })).text())
  .split("\n").filter(Boolean).map((l) => JSON.parse(l));

before(async () => {
  dev = spawn(`npx wrangler dev --port ${PORT} --ip 127.0.0.1 --show-interactive-dev-session=false --var ADMIN_KEY:test-admin-key`,
              { cwd: fileURLToPath(new URL("..", import.meta.url)), shell: true, stdio: ["ignore", "pipe", "pipe"], detached: process.platform !== "win32" });
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("wrangler dev didn't start")), 60_000);
    const look = (d) => { if (/Ready on/.test(String(d))) { clearTimeout(timer); resolve(); } };
    dev.stdout.on("data", look);
    dev.stderr.on("data", look);
  });
});

after(() => {
  if (process.platform === "win32") spawn("taskkill", ["/pid", String(dev.pid), "/t", "/f"]);
  else process.kill(-dev.pid, "SIGTERM"); // the whole group: dev.kill() stops only the shell and wrangler keeps the job alive
});

test("only Holm notes and Holm inboxes", async () => {
  assert.ok(isNoteFor(inbox(), note()));
  assert.ok(!isNoteFor(inbox(), card()));
  assert.ok(!isNoteFor(invite(), note()));
  assert.ok(!isNoteFor(inbox(), note("B".repeat(5000))));
  assert.equal((await post(inbox(), "hello")).status, 400);
  assert.equal((await post("my-topic", note())).status, 404);
  assert.equal((await post(inbox(), note("B".repeat(5000)))).status, 400);
  assert.equal((await post(inbox(), note("B".repeat(9000)))).status, 413);
});

test("since as ntfy reads it", () => {
  assert.equal(parseSince("all"), 0);
  assert.equal(parseSince("1700000000"), 1700000000);
  assert.equal(parseSince("12h", 100000), 100000 - 43200);
  assert.equal(parseSince(""), null);
  assert.equal(parseSince("abc"), null);
});

test("a note is kept and found by polling, as ntfy answers", async () => {
  const t = inbox();
  const sent = await post(t, note());
  assert.equal(sent.status, 200);
  const body = await sent.json();
  assert.equal(body.event, "message");
  const kept = await poll(t);
  assert.equal(kept.length, 1);
  assert.equal(kept[0].message, note());
  assert.equal(kept[0].event, "message");
  assert.equal(kept[0].id, body.id);
  assert.deepEqual(await poll(t, String(body.time + 1)), []);
  assert.equal((await fetch(`${BASE}/${t}/json`)).status, 400, "no ntfy streaming: Holm follows with /ws");
});

test("an invite card waits at its code's inbox", async () => {
  const t = invite();
  assert.equal((await post(t, card())).status, 200);
  assert.equal((await poll(t, "12h"))[0].message, card());
  assert.deepEqual(await poll(invite(), "12h"), []);
});

test("Cache: no is delivered live but not kept", async () => {
  const t = inbox();
  assert.equal((await post(t, note("P".repeat(60)), { "X-Cache": "no" })).status, 200);
  assert.deepEqual(await poll(t), []);
});

function follow(t, since) {
  const ws = new WebSocket(`ws://127.0.0.1:${PORT}/${t}/ws${since != null ? `?since=${since}` : ""}`, { headers: { "x-holm-read-token": TOKEN } });
  const events = [];
  const waiters = [];
  ws.addEventListener("message", (e) => { events.push(JSON.parse(e.data)); waiters.splice(0).forEach((w) => w()); });
  const until = async (n) => {
    const deadline = Date.now() + 10_000;
    while (events.length < n) {
      if (Date.now() > deadline) throw new Error(`only ${events.length} of ${n} events`);
      await new Promise((r) => { waiters.push(r); setTimeout(r, 200); });
    }
    return events;
  };
  return { ws, events, until, opened: new Promise((r) => ws.addEventListener("open", r)) };
}

test("a follower gets the open event, what was kept, then new notes live", async () => {
  const t = inbox();
  await post(t, note("1".repeat(40)));
  const f = follow(t, "all");
  await f.opened;
  const [open, first] = await f.until(2);
  assert.equal(open.event, "open");
  assert.ok(open.time > 1_700_000_000);
  assert.equal(first.message, note("1".repeat(40)));
  await post(t, note("2".repeat(40)));
  await post(t, note("3".repeat(40)), { Cache: "no" }); // presence: live only
  const events = await f.until(4);
  assert.deepEqual(events.slice(2).map((e) => e.message), [note("2".repeat(40)), note("3".repeat(40))]);
  f.ws.close();
});

test("without since a follower gets only new notes", async () => {
  const t = inbox();
  await post(t, note("o".repeat(40)));
  const f = follow(t);
  await f.opened;
  await f.until(1);
  await post(t, note("n".repeat(40)));
  const events = await f.until(2);
  assert.equal(events[1].message, note("n".repeat(40)));
  assert.equal(events.length, 2);
  f.ws.close();
});

test("an inbox enforces per-IP and per-topic minute quotas", async () => {
  const t = inbox();
  const ip1 = "10.201.1.1";
  const singleIp = [];
  for (let i = 0; i < 21; i++) {
    singleIp.push((await post(t, note(String(i).padStart(40, "x")), { "cf-connecting-ip": ip1 })).status);
  }
  assert.equal(singleIp.filter((s) => s === 200).length, 20, singleIp.join(","));
  assert.equal(singleIp.at(-1), 429);

  // Additional distinct IPs can fill up to the 60/min topic cap, then the 61st is rejected.
  const more = [];
  for (const addr of ["10.201.1.2", "10.201.1.3"]) {
    for (let i = 0; i < 20; i++) {
      more.push((await post(t, note(String(i).padStart(40, "y")), { "cf-connecting-ip": addr })).status);
    }
  }
  assert.equal(more.filter((s) => s === 200).length, 40, more.join(","));
  assert.equal((await post(t, note("z".repeat(40)), { "cf-connecting-ip": "10.201.1.4" })).status, 429);
});

test("streaming body cap rejects oversized chunked and non-numeric Content-Length payloads", async () => {
  // Unit-check readTextCapped without Content-Length and with non-numeric Content-Length.
  let chunksRead = 0;
  const stream = new ReadableStream({
    pull(controller) {
      chunksRead++;
      controller.enqueue(new Uint8Array(2048).fill(65));
      if (chunksRead >= 20) controller.close();
    },
  });
  const fakeReq = { headers: new Headers({ "content-length": "not-a-number" }), body: stream };
  assert.equal(await readTextCapped(fakeReq, MAX_BODY_BYTES), null);
  assert.ok(chunksRead <= 6, `stopped streaming early after ${chunksRead} chunks`);

  // Live worker check with chunked body (no Content-Length header).
  const t = inbox();
  const oversized = new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode("wnf1 " + "A".repeat(91) + " "));
      for (let i = 0; i < 10; i++) controller.enqueue(new TextEncoder().encode("B".repeat(1024)));
      controller.close();
    },
  });
  const res = await fetch(`${BASE}/${t}`, {
    method: "POST",
    body: oversized,
    duplex: "half",
    headers: { "cf-connecting-ip": "10.202.2.2" },
  });
  assert.equal(res.status, 413);
});

test("GET /json is rate-limited per IP", async () => {
  const t = inbox();
  const readIp = "10.203.3.3";
  let lastStatus = 0;
  let okCount = 0;
  for (let i = 0; i < 61; i++) {
    const r = await fetch(`${BASE}/${t}/json?poll=1&since=all`, { headers: { "x-holm-read-token": TOKEN, "cf-connecting-ip": readIp } });
    lastStatus = r.status;
    if (r.status === 200) okCount++;
  }
  assert.equal(okCount, 60);
  assert.equal(lastStatus, 429);
});

function wsHandshake(path, headers = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({
      host: "127.0.0.1",
      port: PORT,
      path,
      method: "GET",
      headers: {
        connection: "Upgrade",
        upgrade: "websocket",
        "sec-websocket-version": "13",
        "sec-websocket-key": Buffer.from(crypto.getRandomValues(new Uint8Array(16))).toString("base64"),
        ...headers,
      },
    });
    req.on("upgrade", (res, socket) => {
      socket.destroy();
      resolve(res.statusCode ?? 101);
    });
    req.on("response", (res) => {
      res.resume();
      res.on("end", () => resolve(res.statusCode));
    });
    req.on("error", reject);
    req.end();
  });
}

test("excess WebSocket followers are rejected with 429 instead of evicting existing ones", async () => {
  const t = inbox();
  const followers = [];
  try {
    for (let i = 0; i < 8; i++) {
      const f = follow(t);
      await f.opened;
      await f.until(1);
      followers.push(f);
    }
    const ninthStatus = await wsHandshake(`/${t}/ws`, { "x-holm-read-token": TOKEN, "cf-connecting-ip": "10.204.4.4" });
    assert.equal(ninthStatus, 429);

    // First follower was NOT evicted and still receives new notes.
    await post(t, note("S".repeat(40)), { "cf-connecting-ip": "10.204.4.5" });
    const events = await followers[0].until(2);
    assert.equal(events[1].message, note("S".repeat(40)));
  } finally {
    for (const f of followers) { try { f.ws.close(); } catch {} }
  }
});

test("wnf-* inbox binds X-Holm-Read-Token and rejects unauthorized reads", async () => {
  const t = inbox();
  const tokenA = "a".repeat(43);
  const tokenB = "b".repeat(43);
  await post(t, note("R".repeat(40)), { "cf-connecting-ip": "10.205.5.5" });

  // Malformed token is rejected even before binding.
  const badFmt = await fetch(`${BASE}/${t}/json?poll=1&since=all`, {
    headers: { "x-holm-read-token": "too-short", "cf-connecting-ip": "10.205.5.6" },
  });
  assert.equal(badFmt.status, 401);

  // First valid token binds read_hash and returns kept notes.
  const first = await poll(t, "all", { "x-holm-read-token": tokenA, "cf-connecting-ip": "10.205.5.6" });
  assert.equal(first.length, 1);
  assert.equal(first[0].message, note("R".repeat(40)));

  // Subsequent unauthenticated or wrong-token reads on /json and /ws are rejected with 401.
  const unauthJson = await fetch(`${BASE}/${t}/json?poll=1&since=all`, { headers: { "cf-connecting-ip": "10.205.5.7" } });
  assert.equal(unauthJson.status, 401);
  const wrongJson = await fetch(`${BASE}/${t}/json?poll=1&since=all`, {
    headers: { "x-holm-read-token": tokenB, "cf-connecting-ip": "10.205.5.7" },
  });
  assert.equal(wrongJson.status, 401);
  assert.equal(await wsHandshake(`/${t}/ws`, { "cf-connecting-ip": "10.205.5.7" }), 401);
  assert.equal(await wsHandshake(`/${t}/ws`, { "x-holm-read-token": tokenB, "cf-connecting-ip": "10.205.5.7" }), 401);

  // Matching token succeeds on both /json and /ws.
  const again = await poll(t, "all", { "x-holm-read-token": tokenA, "cf-connecting-ip": "10.205.5.6" });
  assert.equal(again.length, 1);
  assert.equal(await wsHandshake(`/${t}/ws`, { "x-holm-read-token": tokenA, "cf-connecting-ip": "10.205.5.6" }), 101);
});

test("a read without a token is refused, and an inbox named after its token can't be claimed first", async () => {
  // No token, even on an inbox nobody has read yet (Holm older than 2.4 sent none).
  const fresh = inbox();
  assert.equal((await fetch(`${BASE}/${fresh}/json?poll=1&since=all`, { headers: { "cf-connecting-ip": "10.207.7.1" } })).status, 401);
  assert.equal(await wsHandshake(`/${fresh}/ws`, { "cf-connecting-ip": "10.207.7.1" }), 401);

  // Holm 2.4.2+ names a new inbox after its token: a squatter who binds first is replaced by the owner.
  const owner = "o".repeat(43), squatter = "s".repeat(43);
  const t = await inboxFor(owner);
  await post(t, note("Q".repeat(40)), { "cf-connecting-ip": "10.207.7.2" });
  assert.equal((await poll(t, "all", { "x-holm-read-token": squatter, "cf-connecting-ip": "10.207.7.3" })).length, 1);
  assert.equal((await poll(t, "all", { "x-holm-read-token": owner, "cf-connecting-ip": "10.207.7.4" })).length, 1);
  const locked = await fetch(`${BASE}/${t}/json?poll=1&since=all`, { headers: { "x-holm-read-token": squatter, "cf-connecting-ip": "10.207.7.3" } });
  assert.equal(locked.status, 401);
});

test("only a WebSocket follows", async () => {
  assert.equal((await fetch(`${BASE}/${inbox()}/ws`)).status, 426);
  assert.equal((await fetch(`${BASE}/${inbox()}/other`)).status, 405);
});

test("the admin page has strict security headers and /admin/stats is key-checked and rate-limited", async () => {
  const page = await fetch(`${BASE}/admin`);
  assert.equal(page.status, 200);
  assert.equal(page.headers.get("x-frame-options"), "DENY");
  assert.equal(page.headers.get("x-content-type-options"), "nosniff");
  assert.equal(page.headers.get("referrer-policy"), "no-referrer");
  const csp = page.headers.get("content-security-policy") ?? "";
  assert.match(csp, /default-src 'none'/);
  assert.match(csp, /frame-ancestors 'none'/);
  const nonceMatch = /script-src 'nonce-([A-Za-z0-9_-]+)'/.exec(csp);
  assert.ok(nonceMatch, "CSP must carry a script nonce");
  const html = await page.text();
  assert.match(html, /<title>Holm relay<\/title>/);
  assert.ok(!html.includes("fonts.googleapis.com"), "no external font origin");
  assert.ok(html.includes(`<script nonce="${nonceMatch[1]}">`), "inline script uses CSP nonce");

  const adminIp = "10.206.6.6";
  assert.equal((await fetch(`${BASE}/admin/stats`, { headers: { "cf-connecting-ip": adminIp } })).status, 401);
  assert.equal((await fetch(`${BASE}/admin/stats`, { headers: { authorization: "Bearer wrong", "cf-connecting-ip": adminIp } })).status, 401);
  // The right key gets past the door; locally there is no API token to read the counts with, and no internal names leak.
  const stats = await fetch(`${BASE}/admin/stats`, { headers: { authorization: "Bearer test-admin-key", "cf-connecting-ip": adminIp } });
  assert.equal(stats.status, 503);
  assert.equal((await stats.json()).error, "analytics not configured");
  assert.equal((await fetch(`${BASE}/admin/other`)).status, 404);

  // Per-IP rate limit on /admin/stats (10/min): 3 requests already used above, next 7 return 401, 11th returns 429.
  const rateIp = "10.206.6.7";
  const statuses = [];
  for (let i = 0; i < 11; i++) {
    statuses.push((await fetch(`${BASE}/admin/stats`, { headers: { authorization: "Bearer wrong", "cf-connecting-ip": rateIp } })).status);
  }
  assert.equal(statuses.slice(0, 10).every((s) => s === 401), true, statuses.join(","));
  assert.equal(statuses[10], 429);
});
