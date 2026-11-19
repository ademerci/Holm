// Runs the relay locally (wrangler dev: the real workerd runtime with local Durable Objects and SQLite) and checks it
// the way Holm uses it. `npm test` in relay/.
import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { isNoteFor, parseSince } from "../src/notes.js";

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
const poll = async (t, since = "all") => (await (await fetch(`${BASE}/${t}/json?poll=1&since=${since}`)).text())
  .split("\n").filter(Boolean).map((l) => JSON.parse(l));

before(async () => {
  dev = spawn(`npx wrangler dev --port ${PORT} --ip 127.0.0.1 --show-interactive-dev-session=false`,
              { cwd: fileURLToPath(new URL("..", import.meta.url)), shell: true, stdio: ["ignore", "pipe", "pipe"] });
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("wrangler dev didn't start")), 60_000);
    const look = (d) => { if (/Ready on/.test(String(d))) { clearTimeout(timer); resolve(); } };
    dev.stdout.on("data", look);
    dev.stderr.on("data", look);
  });
});

after(() => {
  if (process.platform === "win32") spawn("taskkill", ["/pid", String(dev.pid), "/t", "/f"]);
  else dev.kill();
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
  const ws = new WebSocket(`ws://127.0.0.1:${PORT}/${t}/ws${since != null ? `?since=${since}` : ""}`);
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

test("an inbox takes 60 notes a minute", async () => {
  const t = inbox();
  const statuses = [];
  for (let i = 0; i < 61; i++) statuses.push((await post(t, note(String(i).padStart(40, "x")))).status);
  assert.equal(statuses.filter((s) => s === 200).length, 60, statuses.join(","));
  assert.equal(statuses.at(-1), 429);
});

test("only a WebSocket follows", async () => {
  assert.equal((await fetch(`${BASE}/${inbox()}/ws`)).status, 426);
  assert.equal((await fetch(`${BASE}/${inbox()}/other`)).status, 405);
});
