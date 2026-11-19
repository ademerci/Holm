// Anonymous usage counts for the admin page: one Analytics Engine data point per request, holding what kind of
// request it was and a short hash of the inbox, never an address, a note or the inbox name itself. Kept 3 months.
//
// GET /admin        the page (asks for the admin key, keeps it in the browser)
// GET /admin/stats  the numbers as JSON; needs "Authorization: Bearer <ADMIN_KEY>"
// Secrets: ADMIN_KEY (the page's key) and CF_API_TOKEN (Account Analytics: Read, to query the counts).

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

const PAGE = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>Holm relay</title>
<style>
  :root { --bg: #f4f6f8; --card: #fff; --text: #14181d; --muted: #66707a; --line: #e2e6ea; --accent: #2b7de9; --warn: #d9822b; --bad: #d64545; --live: #9cc3f5; }
  @media (prefers-color-scheme: dark) { :root { --bg: #111417; --card: #1a1e23; --text: #e9edf1; --muted: #8f99a3; --line: #2a3037; --accent: #5aa0ff; --live: #2f5b91; } }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--text); font: 15px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif; }
  main { max-width: 880px; margin: 0 auto; padding: 32px 16px 48px; }
  h1 { font-size: 22px; margin: 0 0 4px; }
  .sub { color: var(--muted); margin: 0 0 24px; }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin-bottom: 24px; }
  .card { background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 14px 16px; }
  .label { color: var(--muted); font-size: 13px; }
  .big { font-size: 28px; font-weight: 600; font-variant-numeric: tabular-nums; }
  h2 { font-size: 15px; margin: 0 0 10px; }
  .meter { display: grid; grid-template-columns: 150px 1fr 120px; gap: 10px; align-items: center; margin: 6px 0; font-size: 14px; }
  .bar { height: 8px; background: var(--line); border-radius: 4px; overflow: hidden; }
  .bar i { display: block; height: 100%; background: var(--accent); }
  .num { text-align: right; font-variant-numeric: tabular-nums; color: var(--muted); }
  .chart { display: flex; align-items: flex-end; gap: 4px; height: 140px; margin-top: 8px; }
  .day { flex: 1; display: flex; flex-direction: column-reverse; min-width: 0; }
  .day i { display: block; }
  .days { display: flex; gap: 4px; color: var(--muted); font-size: 11px; }
  .days span { flex: 1; text-align: center; }
  .legend { color: var(--muted); font-size: 13px; margin-top: 8px; }
  .legend b { display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin: 0 4px 0 12px; vertical-align: -1px; }
  form { display: flex; gap: 8px; flex-wrap: wrap; }
  input { flex: 1; min-width: 200px; padding: 9px 11px; border: 1px solid var(--line); border-radius: 8px; background: var(--card); color: var(--text); font: inherit; }
  button { padding: 9px 14px; border: 0; border-radius: 8px; background: var(--accent); color: #fff; font: inherit; cursor: pointer; }
  .error { color: var(--bad); margin-top: 10px; }
  .note { color: var(--muted); font-size: 13px; margin-top: 24px; }
  @media (max-width: 520px) { .meter { grid-template-columns: 1fr 90px; } .meter .bar { grid-column: 1 / -1; grid-row: 2; } }
</style>
</head>
<body>
<main>
  <h1>Holm relay</h1>
  <p class="sub" id="sub">Anonymous counts: inboxes are short hashes, no names, addresses or notes.</p>
  <section id="login" class="card" hidden>
    <form id="form"><input id="key" type="password" placeholder="Admin key" autocomplete="current-password" required><button>Show</button></form>
    <div class="error" id="error"></div>
  </section>
  <section id="stats" hidden>
    <div class="grid">
      <div class="card"><div class="label">Online now (about)</div><div class="big" id="online"></div></div>
      <div class="card"><div class="label">Active today</div><div class="big" id="day"></div></div>
      <div class="card"><div class="label">Active this week</div><div class="big" id="week"></div></div>
      <div class="card"><div class="label">Active this month</div><div class="big" id="month"></div></div>
    </div>
    <div class="card" style="margin-bottom:24px">
      <h2>Free plan, last 24 hours</h2>
      <div id="quota"></div>
    </div>
    <div class="card" style="margin-bottom:24px">
      <h2>Last 24 hours</h2>
      <div class="grid" id="kinds" style="margin:0"></div>
    </div>
    <div class="card">
      <h2>Notes per day</h2>
      <div class="chart" id="chart"></div>
      <div class="days" id="days"></div>
      <div class="legend"><b style="background:var(--accent);margin-left:0"></b>chat and other kept notes<b style="background:var(--live)"></b>online checks</div>
    </div>
    <p class="note">Online now counts inboxes whose WebSocket is open; one Holm account on two PCs counts once. Active means connected at least once. Quotas reset at 00:00 UTC; these are estimates from the counts, the Cloudflare dashboard has the exact ones. <a href="#" id="logout">Forget key</a></p>
  </section>
</main>
<script>
const $ = (id) => document.getElementById(id);
const fmt = (v) => Number(v || 0).toLocaleString("en");
let key = "";
try { key = localStorage.getItem("holm-admin-key") || ""; } catch {}
const labels = { note: "Kept notes (chat, requests…)", live: "Online checks", invite: "Invite codes", poll: "Code lookups", open: "Connections", refused: "Refused" };

async function load() {
  if (!key) { $("login").hidden = false; return; }
  const response = await fetch("/admin/stats", { headers: { authorization: "Bearer " + key } });
  const body = await response.json().catch(() => ({ error: "no answer" }));
  if (!response.ok) {
    if (response.status === 401) { key = ""; try { localStorage.removeItem("holm-admin-key"); } catch {} }
    $("login").hidden = false; $("stats").hidden = true; $("error").textContent = body.error || response.status;
    return;
  }
  $("login").hidden = true; $("stats").hidden = false;
  $("online").textContent = fmt(body.online);
  $("day").textContent = fmt(body.active.day);
  $("week").textContent = fmt(body.active.week);
  $("month").textContent = fmt(body.active.month);
  const names = { workerRequests: "Worker requests", objectRequests: "Durable Object requests", rowsWritten: "Rows written" };
  $("quota").innerHTML = Object.entries(body.quota).map(([k, q]) => {
    const pct = Math.min(100, q.used / q.free * 100);
    const color = pct > 90 ? "var(--bad)" : pct > 70 ? "var(--warn)" : "var(--accent)";
    return '<div class="meter"><span>' + names[k] + '</span><div class="bar"><i style="width:' + pct.toFixed(1) + '%;background:' + color + '"></i></div><span class="num">' + fmt(q.used) + " / " + fmt(q.free) + "</span></div>";
  }).join("");
  $("kinds").innerHTML = Object.entries(labels).map(([k, l]) => '<div><div class="label">' + l + '</div><div class="big" style="font-size:22px">' + fmt(body.last24h[k]) + "</div></div>").join("");
  const max = Math.max(1, ...body.days.map((d) => (d.note || 0) + (d.invite || 0) + (d.live || 0)));
  $("chart").innerHTML = body.days.map((d) => {
    const kept = (d.note || 0) + (d.invite || 0), live = d.live || 0;
    return '<div class="day" title="' + new Date(d.day * 1000).toISOString().slice(0, 10) + ": " + fmt(kept) + " kept, " + fmt(live) + ' online checks"><i style="height:' + (kept / max * 140) + 'px;background:var(--accent)"></i><i style="height:' + (live / max * 140) + 'px;background:var(--live)"></i></div>';
  }).join("");
  $("days").innerHTML = body.days.map((d) => "<span>" + new Date(d.day * 1000).getUTCDate() + "</span>").join("");
  $("sub").textContent = "Updated " + new Date().toLocaleTimeString() + ". Anonymous counts: inboxes are short hashes, no names, addresses or notes.";
}

$("form").addEventListener("submit", (e) => {
  e.preventDefault();
  key = $("key").value.trim();
  try { localStorage.setItem("holm-admin-key", key); } catch {}
  $("error").textContent = "";
  load();
});
$("logout").addEventListener("click", (e) => { e.preventDefault(); key = ""; try { localStorage.removeItem("holm-admin-key"); } catch {} location.reload(); });
load();
setInterval(() => { if (key && !document.hidden) load(); }, 60000);
</script>
</body>
</html>
`;
