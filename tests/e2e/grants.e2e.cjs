// Windows granted to show apps (Show Bus §12), against the rig with two node keys: a Patterns desk that holds a window as a page and
// a PlayMap node that draws one, each on its own key-verified node link (/ws?node=…&t=…&can=…) with a context it posts once a second.
// A grant made over HTTP with the PIN finds its app among the show apps the rig hears; the app's link hears `grants` (the page's
// address only over this machine's loopback), says it saw them, and the page opened on that address shows the window's page, green.
// The holder's own @-link places the window ("Patterns FOH says: Screen 2 — LED wall") in the context and the operator frame; a
// non-holder's identical link places nothing. --state keeps the grant across a restart. A draw grant reaches PlayMap's link as
// window-assign with no address, shown once it says so, behind once the window changes. Revoke and withdraw end the grant's page in
// plain black with no words; the app hears its new address (revoke) or none (withdraw), and a real window then loads from the same
// browser — no lockout, no tombstone. No grant's address is in the rig's standard output or error, a frame, the context or
// /api/windows, in any run's. A --state whose windows or grants need repair is repaired as it loads, each repair said once on
// standard error with no address, and written back. A grant from a deck (Companion TCP) is asked of the operator, never made: it waits
// on the desk, the deck that asked cannot approve it, and a person with the PIN makes it, the deck hearing the outcome as its final.
// These are the bytes Patterns and PlayMap dial against.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const net = require('node:net');
const path = require('node:path');
const { chromium } = require('playwright');
const { Rig, until, freePort } = require('./rig.cjs');
const { tempDir, removeDir } = require('./temp.cjs');

const RIG = 'grantsrig01';
const PAT = { t: 'patterns', id: 'patfoh01', name: 'Patterns FOH' };
const PM = { t: 'playmap', id: 'pmstage01', name: 'Stage left' };
const CLAIM = 'Patterns FOH says: Screen 2 — LED wall';

let rig, browser, ctx, patKey, pmKey;
const links = [];
const posters = [];
/** Every grant address any app was given, to look for where it must never be. */
const addresses = new Set();
/** The standard output of the rig's runs before the last: each run's lines are its own (rig.cjs), the fence reads them all. */
const earlierOut = [];

before(async () => {
  rig = await Rig.create({ nodeKeys: 2, nodeId: RIG, journal: '-', wire: true });
  [patKey, pmKey] = rig.keys;
  browser = await chromium.launch();
  ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
});
after(async () => {
  // The rig goes whatever a close above threw: a rig left running would outlive its folder's removal at this file's end.
  try {
    for (const p of posters) p.stop();
    for (const l of links) await l.close();
    if (browser) await browser.close();
  } finally {
    if (rig) await rig.dispose();
  }
});

const pause = (ms) => new Promise((r) => setTimeout(r, ms));

/** A show app's node link, closed when the file ends; every grant address it hears is kept for the fence. */
async function dial(node, can, key) {
  const l = await rig.link(`node=${node.id}&t=${node.t}&can=${can}`, key);
  links.push(l);
  l.ws.on('message', (data) => {
    const m = JSON.parse(data.toString('utf8'));
    if (m.type === 'grants') for (const g of m.grants) if (g.path) addresses.add(g.path);
  });
  return l;
}

/** The newest `grants` a link heard from index `from` on that passes pred. */
const grants = (link, pred, what, from = 0) => heard(link, (j) => j.type === 'grants' && pred(j), what, from, 8000);
/** The JSON of the first message a link heard from index `from` on that passes pred. */
const heard = async (link, pred, what, from = 0, ms = 5000) => (await link.next(pred, ms, what, from)).json;

/** A show app's context, posted with its key once a second until stopped (Show Bus §6). */
function poster(node, key, items, edges) {
  const p = { timer: null };
  const post = () => rig.request('POST', '/api/peers/context', key, { v: 1, node, light: 'green', headline: 'all good', items, links: edges });
  p.start = async () => { const first = await post(); p.timer = setInterval(() => post().catch(() => {}), 1000); return first; };
  p.stop = () => { clearInterval(p.timer); p.timer = null; };
  posters.push(p);
  return p;
}
const item = (id, kind, label) => ({ id, kind, label, light: 'green', state: '' });
const edge = (from, to, kind) => ({ from, to, kind, light: 'green' });
/** A context that places this rig's lobby on its screen 2 (two hops: the window feeds a source, the source shows on a screen). */
const placing = (screenLabel) => ({
  items: [item('source:web:1', 'source', 'Lobby page'), item('screen:2', 'screen', screenLabel)],
  edges: [edge(`@${RIG}/window:lobby`, 'source:web:1', 'feeds'), edge('source:web:1', 'screen:2', 'shows')],
});

async function frame(detail) {
  const res = await fetch(`${rig.base}/api/state${detail ? '?detail=1' : ''}`, { headers: { 'X-Stagewatch-Pin': rig.pin } });
  return res.text();
}
const operatorWindows = async () => JSON.parse(await frame(true)).windows || [];
const contextItem = async (id) => (await rig.context()).items.find((i) => i.id === id);
const grantOf = async (name, node) => ((await rig.window(name)).grants || []).find((g) => g.node === node);
const waitAttr = (page, a, v, ms = 8000) => page.waitForFunction(([n, x]) => document.documentElement.getAttribute(n) === x, [a, v], { timeout: ms });
const attr = (page, a) => page.evaluate((n) => document.documentElement.getAttribute(n), a);

/**
 * A grant made over HTTP with the PIN, once the rig hears its app by its name and its node link (it joins the show network once a
 * second): the grant takes the app's name and how it holds the window from what the rig hears at that moment.
 */
async function grant(window, node) {
  await until(async () => (await rig.context()).peers.some((p) => p.id === node.id && p.name === node.name && p.link === 'ws'), 8000,
    `${node.name} heard by its name and its node link`);
  return until(async () => { const r = await rig.run(`window grant ${window} ${node.id}`); return r.ok && r; }, 8000, `${window} granted to ${node.name}`);
}

/** The page a black end leaves: plain black, no words, no holding page, gone and not trying again. */
async function blackEnd(page) {
  await waitAttr(page, 'data-link', 'gone');
  assert.equal(await page.locator('#black').isVisible(), true, 'plain black');
  assert.equal(await page.locator('#gone').isVisible(), false, 'no words');
  assert.equal(await page.locator('#hold').isVisible(), false, 'no holding page');
}

let pat, pm, patPoster, lobbyPage;

test('a page grant: granted over HTTP, its address on the app\'s own link only, acknowledged, opened, shown and green', async () => {
  const place = placing('Screen 2 — LED wall');
  patPoster = poster(PAT, patKey.secret, place.items, place.edges);
  assert.equal((await patPoster.start()).status, 200, 'Patterns posts its context with its key');
  pat = await dial(PAT, 'pages', patKey.secret);
  await pat.next((j) => j.type === 'auth', 5000, 'the verdict');
  assert.deepEqual(pat.messages[0].json.node, { t: 'patterns', id: PAT.id, verified: 'key' });
  // 12.6: the grants (none yet) after the verdict and before any frame.
  await pat.next(() => true, 5000, 'the message after the verdict', 1);
  assert.deepEqual(pat.messages[1].json, { type: 'grants', from: RIG, rev: 1, link: 'local', grants: [] }, 'grants, empty, before any frame');
  await pat.next((j) => j.type === 'state', 5000, 'a frame after the grants', 2);

  assert.equal((await rig.run('window new lobby /viewer')).ok, true);
  const made = await grant('lobby', PAT);
  assert.match(made.effect, /lobby/);
  const lobby = await rig.window('lobby');
  const sent = await grants(pat, (j) => j.grants.length === 1, 'the grant');
  assert.equal(sent.link, 'local', 'this machine\'s loopback carries a page address');
  const [row] = sent.grants;
  assert.deepEqual({ ...row, path: undefined }, { window: lobby.id, name: 'lobby', how: 'page', path: undefined, cannot: '' });
  assert.match(row.path, /^\/w\/[A-Za-z0-9_-]+$/);
  assert.notEqual(row.path, lobby.path, 'a page grant has an address of its own');
  assert.equal((await grantOf('lobby', PAT.id)).state, 'sent', 'sent, not yet acknowledged');

  pat.send({ type: 'grants-seen', rev: sent.rev, cannot: '' });
  const held = await until(async () => { const g = await grantOf('lobby', PAT.id); return g && g.state === 'held' && g; }, 5000, 'the grant acknowledged');
  assert.equal(held.how, 'page');
  assert.equal(held.name, 'Patterns FOH');
  // 12.10: a holder with no viewer on its grant places nothing; a grant never opened adds nothing to the light.
  const before = await until(async () => { const i = await contextItem('window:lobby'); return i && /granted to Patterns FOH/.test(i.state) && i; }, 4000, 'the grant in the item');
  assert.ok(!before.state.includes(CLAIM), 'no claim while nobody has the grant open: ' + before.state);
  assert.equal(before.light, 'grey', 'a window none of whose viewers was opened is grey');

  lobbyPage = await ctx.newPage();
  await lobbyPage.goto(rig.base + row.path);
  await waitAttr(lobbyPage, 'data-showing', '/viewer');
  const shown = await until(async () => { const g = await grantOf('lobby', PAT.id); return g && g.state === 'shown' && g; }, 5000, 'the grant shown');
  assert.equal(shown.open, 1);
  const lit = await until(async () => { const i = await contextItem('window:lobby'); return i && i.light === 'green' && i; }, 4000, 'lobby green');
  assert.match(lit.state, /granted to Patterns FOH \(shows it\)/);
  const [w] = await until(async () => { const ws = await operatorWindows(); return ws.length === 1 && ws[0].light === 'green' && ws; }, 4000, 'the operator frame\'s windows');
  assert.equal(w.name, 'lobby');
  assert.equal(w.inStep, true);
  assert.equal(w.showing, '/viewer');
  assert.equal(w.granted, 'Patterns FOH', 'the apps\' names alone');
  assert.equal(JSON.parse(await frame(false)).windows, undefined, 'the public frame has no windows');
  // The show log says who took the grant, once in this run (a second of ticks later still once), never its address.
  await until(() => tookLines() === 1, 4000, 'the took line');
  await pause(2500);
  assert.equal(tookLines(), 1, 'said once, not every second');
});

/** How many times standard error said Patterns took lobby's address, over every run so far. */
const tookLines = () => rig.log.split("window: Patterns FOH took lobby's address").length - 1;

test('placement: the holder\'s own @-link places the window on its screen; a non-holder\'s identical link places nothing', async () => {
  const placed = await until(async () => { const i = await contextItem('window:lobby'); return i && i.state.includes(CLAIM) && i; }, 5000, 'the claim in the context');
  assert.match(placed.state, /^.*granted to Patterns FOH \(shows it\) · Patterns FOH says: Screen 2 — LED wall$/);
  await until(async () => (await operatorWindows())[0]?.on === CLAIM, 4000, 'the claim in the operator frame');

  const fake = placing('Screen 9 — not here');
  const other = poster(PM, pmKey.secret, fake.items, fake.edges);
  assert.equal((await other.start()).status, 200, 'PlayMap posts the same link with its own key');
  await until(async () => (await rig.context()).peers.some((p) => p.id === PM.id && p.name === PM.name), 5000, 'PlayMap heard');
  await pause(2500);   // two more joins of the network
  assert.equal((await operatorWindows())[0].on, CLAIM, 'a non-holder\'s link changes nothing');
  assert.ok(!(await contextItem('window:lobby')).state.includes('Screen 9'), 'nor the context');
});

test('--state keeps the grant: a restarted rig gives the app the same address', async () => {
  const before = [...addresses];
  const saved = rig.savedState().windows.find((x) => x.name === 'lobby');
  assert.deepEqual(saved.grants.map((g) => [g.node, g.t, g.name, g.how]), [[PAT.id, 'patterns', 'Patterns FOH', 'page']]);
  earlierOut.push(rig.out.items.map((l) => l.line).join('\n'), JSON.stringify(rig.ready));
  await rig.stop();
  await rig.start();
  assert.ok(!JSON.stringify(rig.ready).includes(before[0]), 'the ready line names no grant address');
  pat = await dial(PAT, 'pages', patKey.secret);
  const again = await grants(pat, (j) => j.grants.length === 1, 'the grant after the restart');
  assert.equal(again.rev, 1, 'rev is per link');
  assert.deepEqual(again.grants.map((g) => g.path), before, 'the same address');
  pat.send({ type: 'grants-seen', rev: again.rev, cannot: '' });
  // The page on the grant's address comes back with the rig and shows the window again.
  await waitAttr(lobbyPage, 'data-link', 'ok', 15000);
  await waitAttr(lobbyPage, 'data-showing', '/viewer');
  await until(async () => (await grantOf('lobby', PAT.id))?.state === 'shown', 8000, 'shown again');
  // A new run says who took the grant once more, and once.
  await until(() => tookLines() === 2, 4000, 'the took line in the new run');
  await pause(2500);
  assert.equal(tookLines(), 2, 'once per run');
});

test('a draw grant: PlayMap\'s link hears window-assign and no address; shown once it says so, behind once the window changes', async () => {
  pm = await dial(PM, 'window-draw', pmKey.secret);
  await pm.next((j) => j.type === 'grants', 5000, 'its grants');
  assert.equal((await rig.run('window new stage /clock')).ok, true);
  await grant('stage', PM);
  const stage = await rig.window('stage');
  const sent = await grants(pm, (j) => j.grants.length === 1, 'the draw grant');
  assert.deepEqual(sent.grants, [{ window: stage.id, name: 'stage', how: 'draw', path: '', cannot: '' }]);
  const assign = await heard(pm, (j) => j.type === 'window-assign' && j.window === stage.id, 'window-assign');
  assert.equal(assign.content, '/clock');
  assert.equal(assign.name, 'stage');
  pm.send({ type: 'grants-seen', rev: sent.rev, cannot: '' });
  pm.send({ type: 'window-shown', window: stage.id, rev: assign.rev, content: '/clock', hold: false, ms: 12, error: '' });
  await until(async () => (await grantOf('stage', PM.id))?.state === 'shown', 5000, 'stage shown by PlayMap');
  assert.equal((await rig.window('stage')).drawn, 1);
  await until(async () => (await contextItem('window:stage'))?.light === 'green', 4000, 'stage green');

  // A page grant's window is not this link's to say it draws.
  const from = pm.messages.length;
  const lobby = await rig.window('lobby');
  pm.send({ type: 'window-shown', window: lobby.id, rev: lobby.rev, content: '/viewer', hold: false, ms: 1, error: '' });
  const refused = await heard(pm, (j) => j.type === 'error', 'the refusal', from);
  assert.equal(refused.error, 'only a show app a window is granted to draw may say what it draws');

  const at = pm.messages.length;
  assert.equal((await rig.run('window identify stage')).ok, true);
  await pm.next((j) => j.type === 'window-identify' && j.window === stage.id && j.name === 'stage', 5000, 'window-identify', at);
  const reload = await rig.run('window reload stage');
  assert.equal(reload.ok, false, 'a window only apps draw has no page to reload');
  assert.match(reload.error, /drawn by Stage left, which has no page to reload/);

  assert.equal((await rig.run('window show stage /timeline')).ok, true);
  const next = await heard(pm, (j) => j.type === 'window-assign' && j.content === '/timeline', 'the change', at);
  assert.ok(next.rev > assign.rev);
  await until(async () => (await grantOf('stage', PM.id))?.state === 'stale', 5000, 'behind: it shows an older change');
  assert.deepEqual(pm.messages.filter((m) => m.json && ['identify', 'reload', 'redirect'].includes(m.json.type)), [], 'never a screen\'s identify or reload');
  assert.ok(!pm.messages.some((m) => m.text.includes('/w/')), 'no address on a draw link');
});

test('revoke: the grant\'s page goes plain black, the app hears its new address, and a real window then loads from the same browser', async () => {
  const old = [...addresses].pop();
  const from = pat.messages.length;
  assert.equal((await rig.run('window revoke lobby')).ok, true);
  await blackEnd(lobbyPage);
  const fresh = await grants(pat, (j) => j.grants.length === 1 && j.grants[0].path !== old, 'the new address', from);
  assert.notEqual(fresh.grants[0].path, old);
  pat.send({ type: 'grants-seen', rev: fresh.rev, cannot: '' });

  // The old address reloaded: no window by it (no tombstone), and no lockout for the browser that held it.
  await lobbyPage.reload();
  assert.equal((await fetch(rig.base + old)).status, 404);
  const real = await ctx.newPage();
  await real.goto(rig.base + (await rig.window('lobby')).path);
  await waitAttr(real, 'data-showing', '/viewer');
  await real.close();
  lobbyPage = await ctx.newPage();
  await lobbyPage.goto(rig.base + fresh.grants[0].path);
  await waitAttr(lobbyPage, 'data-showing', '/viewer');
});

test('withdraw: the grant\'s page goes plain black, the app holds no grant, and nothing is locked out', async () => {
  const from = pat.messages.length;
  assert.equal((await rig.run(`window withdraw lobby ${PAT.id}`)).ok, true);
  await blackEnd(lobbyPage);
  await grants(pat, (j) => j.grants.length === 0, 'no grant', from);
  assert.deepEqual(rig.savedState().windows.find((x) => x.name === 'lobby').grants, [], '--state drops it');
  await until(async () => !(await contextItem('window:lobby')).state.includes('Patterns FOH'), 4000, 'nobody named in the item');
  const real = await ctx.newPage();
  await real.goto(rig.base + (await rig.window('lobby')).path);
  await waitAttr(real, 'data-showing', '/viewer');
  assert.equal(await attr(real, 'data-link'), 'ok');
  await real.close();
});

/** One connection on the rig's Companion door from this PC: what it sends once READY, and every line it hears. */
async function deck() {
  const sock = net.connect(rig.companion, '127.0.0.1');
  const lines = [];
  let buf = '';
  let ready;
  const isReady = new Promise((r) => { ready = r; });
  sock.on('data', (d) => {
    buf += d;
    let nl;
    while ((nl = buf.indexOf('\n')) >= 0) {
      const l = buf.slice(0, nl).replace(/\r$/, '');
      buf = buf.slice(nl + 1);
      if (l.startsWith('READY')) ready();
      lines.push(l);
    }
  });
  sock.on('error', (e) => lines.push('socket error: ' + e.message));
  let timer;
  const late = new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(`no READY on the Companion door (heard ${JSON.stringify(lines)})`)), 5000); });
  try { await Promise.race([isReady, late]); } finally { clearTimeout(timer); }
  return {
    send: (line) => sock.write(line + '\n'),
    /** The first line heard that passes pred, waited for. */
    said: (pred, what, ms = 8000) => until(() => lines.find(pred), ms, `${what} (heard ${JSON.stringify(lines)})`),
    close: () => sock.destroy(),
  };
}

test('a deck asks: a grant over Companion TCP waits on the desk; the deck cannot approve it; a person with the PIN makes it, and the deck hears so', async () => {
  const from = pat.messages.length;
  const d = await deck();
  try {
    const answer = (id) => (l) => l.startsWith(`OK @${id} `) || l.startsWith(`ERROR @${id} `);
    d.send(`@g1 WINDOW GRANT lobby ${PAT.id}`);
    const accepted = await d.said(answer('g1'), 'the ask answered');
    const m = /^OK @g1 accepted (p\d+) waits for a person's approval$/.exec(accepted);
    assert.ok(m, 'asked, waiting for a person: ' + accepted);
    const short = m[1];
    assert.equal(await grantOf('lobby', PAT.id), undefined, 'asked, not granted');
    assert.deepEqual(rig.savedState().windows.find((x) => x.name === 'lobby').grants, [], 'nothing kept');

    // The deck that asked approves nothing it asked: a deck proves no person (Show Bus 12.4).
    d.send(`@g2 DESK APPROVE ${short} HIGH`);
    assert.match(await d.said(answer('g2'), 'the deck\'s approval answered'), /^ERROR @g2 .*a window grant is approved by a person who proved it/);
    await pause(300);
    assert.equal(await grantOf('lobby', PAT.id), undefined, 'still not granted');

    // A person with the PIN approves it over HTTP: the grant is made, the app's link hears it, the deck's ask hears its final.
    const approved = await rig.run(`desk approve ${short} high`);
    assert.equal(approved.ok, true, JSON.stringify(approved));
    await grants(pat, (j) => j.grants.length === 1, 'the approved grant', from);
    assert.equal((await grantOf('lobby', PAT.id)).node, PAT.id);
    const final = await d.said((l) => l.startsWith('OK @g1 final ') || l.startsWith('ERROR @g1 final '), 'the ask\'s final');
    assert.match(final, new RegExp(`^OK @g1 final applied this Stagewatch ran .*window grant lobby ${PAT.id}.*\\(${short}\\)$`));
  } finally {
    d.close();
  }
});

test('the fence: no grant address in the rig\'s output, a frame, the context or /api/windows', async () => {
  assert.ok(addresses.size >= 2, 'the apps were given addresses: ' + addresses.size);
  const nodes = await rig.control('nodes', (j) => j.rig === 'nodes');
  const places = {
    'standard output': earlierOut.join('\n') + rig.out.items.map((l) => l.line).join('\n') + JSON.stringify(rig.ready) + JSON.stringify(nodes),
    'standard error': rig.log,
    'the operator frame': await frame(true),
    'the public frame': await frame(false),
    'the context': JSON.stringify(await rig.context()),
    '/api/windows': JSON.stringify(await rig.windows()),
  };
  for (const a of addresses) {
    const token = a.slice('/w/'.length);
    for (const [where, text] of Object.entries(places)) assert.ok(!text.includes(token), `a grant's address in ${where}`);
  }
});

test('--state repaired as it loads: each repair said once on standard error with no address, and written back', async () => {
  // The folder is the test's: the rig given it loads the state written here, and is stopped before the folder goes.
  const dir = tempDir('sw-rig-repair-');
  let fixed = null;
  try {
    const a = 'abcdefghijkmnpqrstuvwxyz23', g = '23456789abcdefghijkmnpqrst', h = 'pqrstuvwxyz23456789abcdefg';
    const win = (id, name, token, grants) => ({ id, name, token, content: '/viewer', rev: 1, grants });
    const made = '2026-11-01T10:00:00Z';
    fs.writeFileSync(path.join(dir, 'windows.json'), JSON.stringify({
      windows: [
        win('wlobby1', 'lobby', a, [
          { node: PAT.id, t: 'patterns', name: 'Patterns FOH', how: 'page', token: g, made },
          { node: PM.id, t: 'playmap', name: 'Stage left', how: 'sideways', token: h, made },
        ]),
        win('wstage1', 'stage', a, []),
      ],
      keys: [],
    }));
    fixed = new Rig(await freePort(), dir, {});
    await fixed.start();
    const said = fixed.log.split('\n').filter((l) => / state: /.test(l));
    assert.equal(said.length, 2, 'two repairs said:\n' + said.join('\n'));
    assert.match(said.join('\n'), /Windows: the grant of lobby to Stage left was dropped — it says neither page nor draw; a person grants it again/);
    assert.match(said.join('\n'), /Windows: stage shared its address with another window and was given a new one/);
    for (const t of [a, g, h]) assert.ok(!fixed.log.includes(t), 'no address on standard error');
    const saved = fixed.savedState();
    const lobby = saved.windows.find((w) => w.name === 'lobby');
    const stage = saved.windows.find((w) => w.name === 'stage');
    assert.equal(lobby.token, a, 'the first window keeps its address');
    assert.notEqual(stage.token, a, 'written back: the second has its new address');
    assert.deepEqual(lobby.grants.map((x) => [x.node, x.how, x.token]), [[PAT.id, 'page', g]], 'written back: the bad grant gone, the good one kept');

    // Written back at once, so the next start has nothing to repair and says nothing.
    const from = fixed.log.length;
    await fixed.stop();
    await fixed.start();
    assert.ok(!/ state: /.test(fixed.log.slice(from)), 'no repair said again: ' + fixed.log.slice(from));
    assert.equal(fixed.savedState().windows.find((w) => w.name === 'stage').token, stage.token, 'the new address lasts');
  } finally {
    if (fixed) await fixed.stop();
    removeDir(dir);
  }
});
