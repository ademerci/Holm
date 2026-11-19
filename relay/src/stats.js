// Anonymous usage counts for the admin page: one Analytics Engine data point per request, holding what kind of
// request it was and a short hash of the inbox, never an address, a note or the inbox name itself. Kept 3 months.
//
// GET /admin        the page (admin.html: asks for the admin key, keeps it in the browser; ?demo shows made-up numbers)
// GET /admin/stats  the numbers as JSON; needs "Authorization: Bearer <ADMIN_KEY>"
// Secrets: ADMIN_KEY (the page's key) and CF_API_TOKEN (Account Analytics: Read, to query the counts).

import PAGE from "./admin.html"; // wrangler bundles .html as text

const DATASET = "holm_relay";
const FREE = { workerRequests: 100_000, objectRequests: 100_000, rowsWritten: 100_000 };

/** An inbox as the counts know it: 12 hex characters of its SHA-256, so the counts can't be used to read it. */
export async function inboxHash(topic) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(topic));
  return Array.from(new Uint8Array(digest).slice(0, 6), (b) => b.toString(16).padStart(2, "0")).join("");
}

/**
 * Counts one request. Kinds: note (kept), live (presence, not kept), invite (a card), poll (an invite lookup),
 * open and close (an inbox's WebSocket), refused (anything turned away).
 */
export async function track(env, kind, topic) {
  if (!env.STATS) return;
  try {
    const inbox = topic && topic.startsWith("wnf-") ? await inboxHash(topic) : "";
    env.STATS.writeDataPoint({ blobs: [kind, inbox], indexes: [kind] });
  } catch { /* counting never breaks a note */ }
}

function sameKey(a, b) {
  const x = new TextEncoder().encode(a ?? ""), y = new TextEncoder().encode(b ?? "");
  let diff = x.length ^ y.length;
  for (let i = 0; i < Math.max(x.length, y.length); i++) diff |= (x[i] ?? 0) ^ (y[i] ?? 0);
  return diff === 0;
}

async function query(env, sql) {
  const response = await fetch(`https://api.cloudflare.com/client/v4/accounts/${env.ACCOUNT_ID}/analytics_engine/sql`, {
    method: "POST",
    headers: { authorization: `Bearer ${env.CF_API_TOKEN}` },
    body: `${sql} FORMAT JSON`,
  });
  if (!response.ok) throw new Error(`Analytics Engine answered ${response.status}: ${(await response.text()).slice(0, 200)}`);
  return (await response.json()).data ?? [];
}

const n = (v) => Number(v ?? 0);

/** The numbers the admin page shows. */
export async function stats(env) {
  const t = `FROM ${DATASET} WHERE timestamp > NOW() - INTERVAL`;
  const [connections, active1, active7, active30, today, days] = await Promise.all([
    // Online now: inboxes with more opened than closed WebSockets this week (a lost close makes it a little high).
    query(env, `SELECT blob2 AS inbox, sumIf(_sample_interval, blob1 = 'open') - sumIf(_sample_interval, blob1 = 'close') AS open ${t} '7' DAY AND blob1 IN ('open', 'close') GROUP BY inbox`),
    query(env, `SELECT count(DISTINCT blob2) AS n ${t} '1' DAY AND blob1 = 'open'`),
    query(env, `SELECT count(DISTINCT blob2) AS n ${t} '7' DAY AND blob1 = 'open'`),
    query(env, `SELECT count(DISTINCT blob2) AS n ${t} '30' DAY AND blob1 = 'open'`),
    query(env, `SELECT blob1 AS kind, SUM(_sample_interval) AS n ${t} '1' DAY GROUP BY kind`),
    query(env, `SELECT intDiv(toUInt32(timestamp), 86400) * 86400 AS day, blob1 AS kind, SUM(_sample_interval) AS n ${t} '14' DAY GROUP BY day, kind ORDER BY day`),
  ]);
  const last24h = Object.fromEntries(today.map((r) => [r.kind, n(r.n)]));
  const count = (...kinds) => kinds.reduce((sum, k) => sum + (last24h[k] ?? 0), 0);
  const workerRequests = count("note", "live", "invite", "poll", "open", "refused");
  const objectRequests = count("note", "live", "invite", "poll", "open");
  const rowsWritten = 2 * count("note", "invite");
  const perDay = {};
  for (const r of days) (perDay[n(r.day)] ??= {})[r.kind] = n(r.n);
  return {
    online: connections.filter((r) => n(r.open) > 0).length,
    active: { day: n(active1[0]?.n), week: n(active7[0]?.n), month: n(active30[0]?.n) },
    last24h,
    quota: {
      workerRequests: { used: workerRequests, free: FREE.workerRequests },
      objectRequests: { used: objectRequests, free: FREE.objectRequests },
      rowsWritten: { used: rowsWritten, free: FREE.rowsWritten },
    },
    days: Object.entries(perDay).map(([day, kinds]) => ({ day: Number(day), ...kinds })),
  };
}

/** /admin and /admin/stats, or null for anything else. */
export async function admin(request, env, path) {
  if (path === "/admin" || path === "/admin/") return new Response(PAGE, { headers: { "content-type": "text/html; charset=utf-8", "cache-control": "no-store" } });
  if (path !== "/admin/stats") return null;
  const json = (status, body) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json", "cache-control": "no-store" } });
  if (!env.ADMIN_KEY) return json(503, { error: "ADMIN_KEY isn't set: npx wrangler secret put ADMIN_KEY" });
  const key = (request.headers.get("authorization") ?? "").replace(/^Bearer /, "");
  if (!sameKey(key, env.ADMIN_KEY)) return json(401, { error: "wrong key" });
  if (!env.CF_API_TOKEN || !env.ACCOUNT_ID) return json(503, { error: "CF_API_TOKEN or ACCOUNT_ID isn't set" });
  try { return json(200, await stats(env)); }
  catch (e) { return json(502, { error: String(e.message ?? e) }); }
}
