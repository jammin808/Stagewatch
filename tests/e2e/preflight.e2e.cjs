// One preflight for the room ('ready for doors'), against the rig, which judges it as the app does (NetworkPreflight, once a
// second) and serves it on the app's doors. Two scripted show apps post their contexts once a second: a PlayMap node with a
// node key — the key comes to speak for its id, so that node is configured — and AGMessage with the operator's credential,
// which configures nothing: it is only seen. With both live the room is ready, each row green but the one that says AGMessage
// posts with the operator's credential, amber, which never blocks. Both stop at one moment. At 6 s each is amber (stale); at
// 12 s the configured one is red — not ready, its row naming the key that configures it — and the one only seen is amber, gone
// at a time of day. The configured one posts again and the room is ready again, the seen one still amber: nothing a person did
// not configure closes the doors by leaving. Every door says the same verdict: GET /api/preflight (read scope, no query), the
// operator frame's network (after desk), MCP's get_preflight, and the Showcaller page's chip, with the reasons under it while
// the room is not ready. A stalled show thread is never ready: the route answers stuck, then the verdict comes back. The
// verdict is never an item of the rig's own context.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const { Rig, until } = require('./rig.cjs');

const LIVE_MS = 6000;    // Show Bus liveness: live to 6 s
const STALE_MS = 12000;  // stale to 12 s, gone after
const MAX_AGE_MS = 3000; // a verdict older than this is never served as the verdict
// A deadline is the contract's number × 1.5 + 2 s; nothing is retried.
const deadline = (ms) => ms * 1.5 + 2000;
// The rig judges once a second, and a post is stamped here just after the rig took it.
const SLACK_MS = 500;

const PLAYMAP = { t: 'playmap', id: 'pm-a1b2c3', name: 'Stage left' };
const AGM = { t: 'messenger', id: 'ag-778899', name: 'AGM' };

let rig, browser;
const posters = [];

before(async () => {
  rig = await Rig.create({ nodeKeys: 1, nodeId: 'doorsrig01' });
  browser = await chromium.launch();
});
after(async () => {
  try {
    for (const p of posters) p.stop();
    if (browser) await browser.close();
  } finally {
    if (rig) await rig.dispose();
  }
});

const pause = (ms) => new Promise((r) => setTimeout(r, ms));

/** A show app that posts its context once a second with `credential`, until stopped: `last` is when the rig last took one. */
function poster(node, credential) {
  const p = { last: 0, timer: null, taken: null };
  const post = async () => {
    const r = await rig.request('POST', '/api/peers/context', credential, {
      v: 1, node, light: 'green', headline: 'all good', items: [{ id: 'out1', kind: 'output', label: 'Output 1', light: 'green', state: 'on air' }],
    });
    p.taken = r;
    if (r.status === 200) p.last = Date.now();
    return r;
  };
  p.start = async () => { const first = await post(); p.timer = setInterval(() => post().catch(() => {}), 1000); return first; };
  p.stop = () => { clearInterval(p.timer); p.timer = null; };
  posters.push(p);
  return p;
}

async function preflight(credential = rig.pin) {
  const r = await rig.request('GET', '/api/preflight', credential);
  assert.equal(r.status, 200, r.text);
  return r.json;
}
const row = (p, id) => p.rows.find((r) => r.id === id);
const heard = (p, node) => row(p, `node:${node.id}:heard`);

async function frame() {
  const res = await fetch(rig.base + '/api/state?detail=1', { headers: { 'X-Stagewatch-Pin': rig.pin } });
  return res.json();
}

/** The operator frame once its network passes `test`: a frame carries the verdict from the first one built after it was judged. */
function frameWhen(test, what) {
  return until(async () => { const f = await frame(); return f.network && test(f.network) && f; }, 3000, what);
}

async function mcpPreflight() {
  const res = await fetch(rig.base + '/mcp', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Stagewatch-Pin': rig.pin },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name: 'get_preflight', arguments: {} } }),
  });
  const result = (await res.json()).result;
  return { isError: result.isError, json: JSON.parse(result.content[0].text) };
}

test('the route: read scope, no query, and a room with nothing in it is ready', async () => {
  const first = await until(async () => {
    const r = await rig.request('GET', '/api/preflight', rig.pin);
    return r.status === 200 && r;
  }, 5000, 'a first verdict');
  assert.deepEqual(Object.keys(first.json), ['v', 'at', 'ready', 'stuck', 'red', 'amber', 'green', 'words', 'rows']);
  assert.equal(first.json.v, 1);
  assert.equal(first.json.ready, true);
  assert.equal(first.json.stuck, false);
  assert.equal(first.json.red, 0);
  assert.equal(first.json.words, 'ready for doors');
  assert.ok(Math.abs(Date.now() - first.json.at) < MAX_AGE_MS, 'built within the time a verdict is served for');
  assert.equal((await rig.request('GET', '/api/preflight')).status, 401, 'no credential');
  assert.equal((await rig.request('GET', '/api/preflight', rig.keys[0].secret)).status, 200, 'any key reads it');
  const query = await rig.request('GET', '/api/preflight?rows=red', rig.pin);
  assert.equal(query.status, 400);
  assert.deepEqual(query.json, { ok: false, error: '/api/preflight takes no query' });
  const f = await frameWhen((n) => n.ready, 'the frame ready');
  assert.deepEqual(f.network, { ready: true, red: 0, amber: 0, stuck: false });
  const keys = Object.keys(f);
  assert.deepEqual(keys.slice(-2), ['desk', 'network'], 'the operator frame ends desk, then network');
  const open = await (await fetch(rig.base + '/api/state?detail=1')).json();
  assert.ok(!('network' in open) && !('desk' in open), 'the public frame has neither');
});

test('a configured node gone is red and the room not ready; one only seen is amber and never blocks; every door says so', async () => {
  const key = rig.keys[0];
  const playmap = poster(PLAYMAP, key.secret);
  const agm = poster(AGM, rig.pin);
  const took = [await playmap.start(), await agm.start()];
  assert.deepEqual(took.map((r) => [r.status, r.json.verified]), [[200, 'key'], [200, 'operator']]);

  // Both live: ready. The PlayMap node is configured by the key that came to speak for it; AGMessage by nothing.
  const live = await until(async () => {
    const p = await preflight();
    return heard(p, PLAYMAP) && heard(p, AGM) && row(p, `node:${PLAYMAP.id}:key`) && row(p, `node:${AGM.id}:key`) && p;
  }, 5000, 'both apps in the preflight');
  assert.equal(live.ready, true);
  assert.equal(live.red, 0);
  assert.deepEqual([heard(live, PLAYMAP).light, heard(live, PLAYMAP).words, heard(live, PLAYMAP).kind, heard(live, PLAYMAP).name], ['green', 'live', 'playmap', 'Stage left']);
  assert.equal(heard(live, PLAYMAP).configured, "node key 'Rig node 1'");
  assert.equal(heard(live, AGM).configured, '', 'the operator\'s credential configures nothing');
  assert.deepEqual(['context', 'own', 'key'].map((c) => row(live, `node:${PLAYMAP.id}:${c}`).light), ['green', 'green', 'green']);
  assert.equal(row(live, `node:${PLAYMAP.id}:key`).words, 'posts with its own node key');
  assert.equal(row(live, `node:${AGM.id}:key`).light, 'amber', 'posted with the operator\'s credential: said, and never blocking');
  assert.equal(live.amber, 1);
  assert.equal(live.words, 'ready · 1 to look at');
  assert.deepEqual((await frameWhen((n) => n.amber === 1, 'the frame with its amber row')).network, { ready: true, red: 0, amber: 1, stuck: false });

  // The Showcaller page's chip reads the frame.
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(rig.base + '/producer?pin=' + encodeURIComponent(rig.pin));
  await page.waitForFunction(() => document.getElementById('netReady').textContent === '△ Ready for doors · 1 to look at', null, { timeout: 8000 });
  assert.equal(await page.evaluate(() => document.getElementById('netBox').classList.contains('hidden')), true, 'a ready room shows no rows until asked');

  // Both stop at one moment.
  playmap.stop();
  agm.stop();
  await pause(50);
  const quiet = Math.max(playmap.last, agm.last);
  const lights = [];
  let amberAt = null, redAt = null, red = null, seenAtRed = null;
  while (Date.now() - quiet < deadline(STALE_MS) && redAt === null) {
    const p = await preflight();
    const at = Date.now() - quiet;
    const mine = heard(p, PLAYMAP);
    lights.push([at, mine.light, heard(p, AGM).light, p.ready]);
    if (mine.light === 'amber' && amberAt === null) {
      amberAt = at;
      assert.equal(p.ready, true, 'a stale node never blocks');
      assert.match(mine.words, /^not heard for \d+ s$/);
    }
    if (mine.light === 'red') { redAt = at; red = p; seenAtRed = heard(p, AGM); }
    await pause(200);
  }
  const trace = JSON.stringify(lights.filter((l, i) => i === 0 || l[1] !== lights[i - 1][1] || l[2] !== lights[i - 1][2]));
  assert.ok(amberAt !== null && amberAt >= LIVE_MS - SLACK_MS && amberAt <= deadline(LIVE_MS), `amber at 6 s (${amberAt} ms; ${trace})`);
  assert.ok(redAt !== null && redAt >= STALE_MS - SLACK_MS && redAt <= deadline(STALE_MS), `red at 12 s (${redAt} ms; ${trace})`);
  assert.equal(red.ready, false);
  assert.equal(red.stuck, false);
  assert.equal(red.red, 1);
  const gone = heard(red, PLAYMAP);
  assert.equal(red.rows[0].id, gone.id, 'the red row is first');
  assert.match(gone.words, /^not heard for 1\d s$/);
  assert.equal(gone.configured, "node key 'Rig node 1'", 'the row names what configures it');
  // Expected only because a node key speaks for it: its red says the other way out too (a node retired, or reinstalled with a new id).
  assert.equal(gone.fix, 'is PlayMap running and on this network? If it was retired or reinstalled: revoke its key, or let that key speak for the new node (Settings → Remote)');
  assert.ok(red.words.startsWith('not ready: Stage left not heard for 1'), red.words);
  assert.ok(!red.rows.some((r) => r.node === PLAYMAP.id && r.check !== 'heard'), 'a node that is gone says one thing');
  // The one only seen: amber, gone at a time of day, never red.
  assert.equal(seenAtRed.light, 'amber');
  assert.match(seenAtRed.words, /^(went away at \d\d:\d\d|not heard for \d+ s)$/);
  assert.ok(lights.every((l) => l[2] !== 'red'), `a node only seen is never red (${trace})`);
  const seenAmberAt = (lights.find((l) => l[2] === 'amber') || [null])[0];
  assert.ok(seenAmberAt !== null && seenAmberAt >= LIVE_MS - SLACK_MS && seenAmberAt <= deadline(LIVE_MS), `the seen node amber at 6 s too (${seenAmberAt} ms; ${trace})`);

  // Every door says the same.
  const { network } = await frameWhen((n) => n.ready === false, 'the frame not ready');
  assert.deepEqual([network.red, network.stuck], [1, false]);
  const mcp = await mcpPreflight();
  assert.equal(mcp.isError, false);
  assert.equal(mcp.json.ready, false);
  assert.equal(heard(mcp.json, PLAYMAP).light, 'red');
  await page.waitForFunction(() => document.getElementById('netReady').textContent.startsWith('✕ Not ready for doors · 1 red'), null, { timeout: 5000 });
  await page.waitForFunction(() => /✕ Stage left — not heard (for \d+ s|since \d\d:\d\d) \(node key 'Rig node 1'\)/.test(document.getElementById('netRows').textContent), null, { timeout: 8000 });
  assert.equal(await page.evaluate(() => document.getElementById('netBox').classList.contains('hidden')), false, 'a room that is not ready says why without a press');
  assert.equal(await page.evaluate(() => document.getElementById('netRows').textContent.includes('AGM')), false, 'only the red rows stand there unasked');
  // Never an item of the rig's own context: its light is its own parts'.
  const context = await rig.context();
  assert.ok(!context.items.some((i) => i.id.startsWith('node:') || i.kind === 'preflight'), JSON.stringify(context.items.map((i) => i.id)));
  assert.ok(!('network' in context) && !/not ready|ready for doors/.test(JSON.stringify(context)), 'no verdict in the context');
  assert.notEqual(context.light, 'red');

  // The seen one, well gone: still amber, by the time it went.
  const wentAway = await until(async () => {
    const p = await preflight();
    return /^went away at \d\d:\d\d$/.test(heard(p, AGM).words) && p;
  }, Math.max(3000, deadline(STALE_MS) - (Date.now() - quiet) + 2000), 'the seen node gone');
  assert.equal(heard(wentAway, AGM).light, 'amber');
  assert.equal(heard(wentAway, AGM).fix, '');

  // The configured one comes back: ready again, the seen one still gone and still not blocking.
  await playmap.start();
  const back = await until(async () => {
    const p = await preflight();
    return p.ready && heard(p, PLAYMAP).light === 'green' && p;
  }, 5000, 'ready again');
  assert.equal(back.red, 0);
  assert.equal(heard(back, AGM).light, 'amber');
  assert.match(heard(back, AGM).words, /^went away at \d\d:\d\d$/);
  await page.waitForFunction(() => document.getElementById('netReady').textContent.startsWith('△ Ready for doors · '), null, { timeout: 5000 });
  await page.click('#netReady');
  await page.waitForFunction(() => /△ AGM — went away at \d\d:\d\d/.test(document.getElementById('netRows').textContent), null, { timeout: 8000 });
  assert.deepEqual(errors, []);
  await ctx.close();
  playmap.stop();
});

test('a stalled show thread is never ready: the route answers stuck, and the verdict comes back when the show turns over', async () => {
  const before = await preflight();
  assert.equal(before.stuck, false);
  const stall = await rig.stall(8000);
  let stuckAt = null, stuck = null;
  while (Date.now() - stall.receivedAt < deadline(MAX_AGE_MS) && stuckAt === null) {
    const p = await preflight();
    if (p.stuck) { stuckAt = Date.now() - stall.receivedAt; stuck = p; }
    else assert.ok(Date.now() - p.at <= MAX_AGE_MS + SLACK_MS, `a verdict served is at most 3 s old (${Date.now() - p.at} ms)`);
    await pause(100);
  }
  // The verdict it had was built within the second before the stall: stale, and so stuck, from about 2 s in.
  assert.ok(stuckAt !== null && stuckAt >= MAX_AGE_MS - 1000 - SLACK_MS && stuckAt <= deadline(MAX_AGE_MS), `stuck within the deadline (${stuckAt} ms)`);
  assert.equal(stuck.ready, false);
  assert.equal(stuck.red, 1);
  assert.deepEqual(stuck.rows.map((r) => [r.id, r.check, r.light]), [['stagewatch:show-thread', 'stagewatch', 'red']]);
  assert.equal(stuck.rows[0].words, "Stagewatch's show thread is stuck — no verdict until it turns over");
  const mcp = await mcpPreflight();
  assert.equal(mcp.json.stuck, true, 'MCP is told the same');
  await stall.running;
  const again = await until(async () => {
    const p = await preflight();
    return !p.stuck && p;
  }, 4000, 'the verdict back');
  assert.ok(again.rows.every((r) => r.id !== 'stagewatch:show-thread'));
});
