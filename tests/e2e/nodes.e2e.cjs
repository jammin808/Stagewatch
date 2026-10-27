// Show apps on /ws are peers, not screens (Show Bus §7.1, §11), against the rig with two node keys: a show app that
// proves its node key hears its verdict before any frame, is never listed in Screens and never hears a screen command;
// one node key speaks for one node id on /ws and on the context route; a socket that only claims an id stays a screen,
// flagged and out of "all"; an unknown key is refused once, keeps the public frame and locks no address out; and a
// restarted rig keeps what each key speaks for. The rig's `nodes` line is its own record of each node link: what speaks
// for its id, by which key's id, and its words for one verified none. These are the bytes Patterns, PlayMap and AGMessage
// dial against.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const { Rig, until, PIN } = require('./rig.cjs');

let rig, browser, ctx, viewer, key1, key2;
const links = [];
before(async () => {
  rig = await Rig.create({ nodeKeys: 2 });
  [key1, key2] = rig.keys;
  browser = await chromium.launch();
  ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
  viewer = await ctx.newPage();
  await viewer.goto(rig.base + '/viewer');
  await until(async () => (await rig.screens()).length === 1, 8000, 'the viewer listed in Screens');
});
after(async () => {
  for (const l of links) await l.close();
  if (browser) await browser.close();
  if (rig) await rig.stop();
});

/** A show app's link, closed when the file ends. */
async function dial(query, credential) {
  const l = await rig.link(query, credential);
  links.push(l);
  return l;
}

/** What a socket heard from `from` on that only a screen hears. */
const SCREEN_ONLY = ['identify', 'redirect', 'reload', 'disconnect'];
const screenCommands = (link, from) => link.messages.slice(from).filter((m) => m.json && SCREEN_ONLY.includes(m.json.type));
const pause = (ms) => new Promise((r) => setTimeout(r, ms));

/** A Show Bus §6 context, as a show app posts its own. */
const context = (kind, id, name, light, headline) => ({ v: 1, node: { t: kind, id, name }, light, headline, items: [], recent: [], commands: [] });

/** The rig's own record of the node links naming these ids (its `nodes` line), as the fields a verdict is read by. */
async function nodeLinks(...ids) {
  const said = await rig.control('nodes', (j) => j.rig === 'nodes');
  return said.nodes.filter((n) => ids.includes(n.id)).map(({ t, id, verified, keyId, links, words }) => ({ t, id, verified, keyId, links, words }));
}

/** The context's peers row for an id, once the rig has joined it (once a second). */
const peerRow = (id, pred = () => true) =>
  until(async () => (await rig.context()).peers.find((p) => p.id === id && pred(p)), 5000, `the context's peers row for ${id}`);

test('a show app with its node key hears its verdict first, then frames, and is never a screen', async () => {
  assert.equal(rig.ready.nodeKeys.length, 2, 'the ready line names each key made in this run, with its secret');
  const a = await dial('node=pat00001&t=patterns', key1.secret);
  await a.next(() => true, 5000, 'the first message');
  assert.deepEqual(a.messages[0].json, { type: 'auth', ok: true, scope: 'node', node: { t: 'patterns', id: 'pat00001', verified: 'key' } });
  await a.next((j) => j.type === 'state', 5000, 'a frame after the verdict', 1);

  const screens = await rig.screens();
  assert.equal(screens.length, 1, 'only the browser is a screen: ' + JSON.stringify(screens));
  assert.ok(!screens.some((s) => s.id.includes('pat00001') || s.claim.includes('pat00001')));

  const row = await peerRow('pat00001');
  assert.equal(row.link, 'ws');
  assert.equal(row.verified, 'key');
  assert.equal(row.t, 'patterns');
  assert.equal(row.headline, 'follows this Stagewatch over /ws — no context yet');

  // The first proven use binds the key, and the rig keeps the pair (names and ids, never a secret).
  const saved = await until(() => rig.savedState().keys.find((k) => k.id === key1.id && k.node === 'pat00001'), 5000, 'the binding saved');
  assert.equal(saved.nodeKind, 'patterns');
  assert.ok(!JSON.stringify(rig.savedState()).includes(key1.secret), 'no secret in the state file');
});

test('screen commands reach browsers only: "all" skips the show app, and naming it is refused as a show app', async () => {
  const a = links[0];
  const from = a.messages.length;
  const all = await rig.run('screen identify all');
  assert.equal(all.ok, true, JSON.stringify(all));
  assert.equal(all.stage, 'applied');
  assert.equal(all.effect, 'Identifying 1 screen');
  await viewer.waitForSelector('#sw-identify', { state: 'visible', timeout: 5000 });

  const named = await rig.run('screen identify pat00001');
  assert.equal(named.ok, false);
  assert.match(named.error, /pat00001 is a show app .*not a screen/);
  const go = await rig.run('screen go /clock pat00001');
  assert.equal(go.ok, false);
  assert.match(go.error, /is a show app/);

  await pause(600);
  assert.deepEqual(screenCommands(a, from), [], 'the show app heard no screen command');
  assert.ok(a.open);
});

test('one key speaks for one node: under another id it is verified none and keeps its frames; its context is refused', async () => {
  const b = await dial('node=pat00002&t=patterns', key1.secret);
  await b.next(() => true, 5000, 'the first message');
  assert.deepEqual(b.messages[0].json, {
    type: 'auth', ok: true, scope: 'node', node: { t: 'patterns', id: 'pat00002', verified: 'none' },
    words: 'this key speaks for patterns pat00001 — make a key for patterns pat00002',
  });
  await b.next((j) => j.type === 'state', 5000, 'frames still flow', 1);

  const forged = await rig.request('POST', '/api/peers/context', key1.secret, context('patterns', 'pat00002', 'Forged', 'red', 'not me'));
  assert.equal(forged.status, 403, forged.text);
  assert.equal(forged.json.ok, false);
  assert.match(forged.json.error, /^this key speaks for patterns pat00001/);

  const own = await rig.request('POST', '/api/peers/context', key1.secret, context('patterns', 'pat00001', 'Patterns FOH', 'amber', 'Playlist paused'));
  assert.equal(own.status, 200, own.text);
  assert.deepEqual(own.json, { ok: true, verified: 'key' });

  // An id that has a key is spoken for with that key alone: another key, or the operator's PIN, is refused.
  for (const credential of [key2.secret, PIN]) {
    const other = await rig.request('POST', '/api/peers/context', credential, context('patterns', 'pat00001', 'Imposter', 'green', 'all fine'));
    assert.equal(other.status, 403, other.text);
    assert.equal(other.json.error, 'patterns pat00001 speaks with its own key');
  }

  const row = await peerRow('pat00001', (p) => p.name === 'Patterns FOH');
  assert.equal(row.light, 'amber');
  assert.equal(row.headline, 'Playlist paused');
  assert.equal(row.link, 'ws');
  assert.equal(row.verified, 'key');
  const ctxNow = await rig.context();
  assert.ok(!ctxNow.peers.some((p) => p.id === 'pat00002'), 'a link nothing proves adds no row');
  // The rig's own record of both links: the key's own id proved by it, the other id verified none in the words it was told.
  assert.deepEqual(await nodeLinks('pat00001', 'pat00002'), [
    { t: 'patterns', id: 'pat00001', verified: 'key', keyId: key1.id, links: 1, words: null },
    { t: 'patterns', id: 'pat00002', verified: 'none', keyId: key1.id, links: 1, words: 'this key speaks for patterns pat00001 — make a key for patterns pat00002' },
  ]);
  const listed = await rig.control('nodes', (j) => j.rig === 'nodes');
  assert.ok(!JSON.stringify(listed).includes(key1.secret) && !JSON.stringify(listed).includes(key2.secret), 'no key\'s secret in the nodes line');
  assert.equal((await rig.screens()).length, 1, 'a show app verified none is still not a screen');
  assert.equal(rig.savedState().keys.find((k) => k.id === key2.id).node, null, 'a refused request binds no key');

  // Its name, as its context told it, is refused as a show app too.
  const byName = await until(async () => { const r = await rig.run('screen identify Patterns FOH'); return !r.ok && /show app/.test(r.error) && r; }, 5000, 'the name refused');
  assert.match(byName.error, /Patterns FOH is a show app \(patterns pat00001\)/);
});

test('a socket that only claims an id stays a screen: flagged, out of "all", reached by its own id', async () => {
  const c = await dial('node=pat00003&t=patterns');
  await c.next(() => true, 5000, 'the first message');
  assert.deepEqual(c.messages[0].json, {
    type: 'auth', ok: true, scope: 'none', node: { t: 'patterns', id: 'pat00003', verified: 'none' }, words: 'no key — the id is only claimed',
  });
  const claim = await until(async () => (await rig.screens()).find((s) => s.claim === 'patterns pat00003'), 5000, 'the claim in Screens');
  assert.match(claim.id, /^claim-[0-9a-f]{12}$/);
  assert.deepEqual(await nodeLinks('pat00003'), [], 'a claim is not a node link');

  const from = c.messages.length;
  const all = await rig.run('screen identify all');
  assert.equal(all.effect, 'Identifying 1 screen', 'the claim is not one of "all"');
  await pause(600);
  assert.deepEqual(screenCommands(c, from), []);

  const byId = await rig.run(`screen identify ${claim.id}`);
  assert.equal(byId.ok, true, JSON.stringify(byId));
  const told = await c.next((j) => j.type === 'identify', 5000, 'the claim identified by its id', from);
  assert.equal(told.json.id, claim.id);
});

test('the operator\'s PIN on a node link proves the operator: verified operator, and still never a screen', async () => {
  const d = await dial('node=agm00001&t=messenger', PIN);
  await d.next(() => true, 5000, 'the first message');
  assert.deepEqual(d.messages[0].json, { type: 'auth', ok: true, scope: 'pin', node: { t: 'messenger', id: 'agm00001', verified: 'operator' } });
  const row = await peerRow('agm00001');
  assert.equal(row.link, 'ws');
  assert.equal(row.verified, 'operator');
  assert.deepEqual(await nodeLinks('agm00001'), [{ t: 'messenger', id: 'agm00001', verified: 'operator', keyId: null, links: 1, words: null }]);
  assert.ok(!(await rig.screens()).some((s) => s.id.includes('agm00001')));
});

test('screen disconnect all: the browser is told to stay off; the show apps stay open and keep hearing frames', async () => {
  const [a] = links;
  const from = a.messages.length;
  const r = await rig.run('screen disconnect all');
  assert.equal(r.ok, true, JSON.stringify(r));
  assert.equal(r.effect, 'Disconnected 1 screen');
  await viewer.waitForSelector('#sw-off', { state: 'visible', timeout: 5000 });
  await a.next((j) => j.type === 'state' || j.type === 'beat', 4000, 'the show app still hears the show', from);
  await pause(600);
  assert.ok(links.every((l) => l.open), 'no show app and no claim was disconnected');
  assert.deepEqual(screenCommands(a, from), []);
});

test('an unknown key is refused once and keeps the public frame; unknown keys lock no address out', async () => {
  const unknown = 'swk_abcdefghijkmnpqrstuvwxyz23456789';
  const e = await dial('node=pat00004&t=patterns', unknown);
  await e.next(() => true, 5000, 'the first message');
  assert.deepEqual(e.messages[0].json, { type: 'auth', ok: false, error: 'unknown or revoked API key', retry: 60 });
  await e.next((j) => j.type === 'state', 5000, 'the public frame', 1);
  assert.ok(e.open, 'a key reason closes no socket');

  for (let i = 0; i < 10; i++) {
    const again = await rig.link('node=pat00004&t=patterns', unknown);
    const first = await again.next(() => true, 5000, 'a refusal');
    assert.equal(first.json.ok, false);
    await again.close();
  }
  for (let i = 0; i < 6; i++) {
    const post = await rig.request('POST', '/api/peers/context', unknown, context('patterns', 'pat00004', 'Old key', 'green', 'polling'));
    assert.equal(post.status, 401, post.text);
  }

  const note = await rig.run('note the operator is still in');
  assert.equal(note.ok, true, JSON.stringify(note));
  assert.equal(note.stage, 'applied');
  const control = await dial('role=control', PIN);
  const first = await control.next(() => true, 5000, 'the control page\'s auth');
  assert.deepEqual(first.json, { type: 'auth', ok: true }, 'the control page still takes the PIN');
});

test('a restarted rig keeps what each key speaks for', async () => {
  while (links.length) await links.pop().close();
  await rig.stop();
  const ready = await rig.start();
  assert.deepEqual(ready.nodeKeys, [], 'no key is made, and no secret printed, again');

  const elsewhere = await dial('node=pat00002&t=patterns', key1.secret);
  const refused = await elsewhere.next(() => true, 5000, 'the verdict');
  assert.equal(refused.json.node.verified, 'none');
  assert.equal(refused.json.words, 'this key speaks for patterns pat00001 — make a key for patterns pat00002');

  const home = await dial('node=pat00001&t=patterns', key1.secret);
  const verdict = await home.next(() => true, 5000, 'the verdict');
  assert.equal(verdict.json.node.verified, 'key');

  // Key 2 was refused earlier and bound to nothing: its first proven use binds it now.
  const fresh = await dial('node=pat00009&t=patterns', key2.secret);
  assert.equal((await fresh.next(() => true, 5000, 'the verdict')).json.node.verified, 'key');
});
