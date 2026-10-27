// Playout links against the rig (--playout): a fake PlayMap node and a fake Patterns desk on their line wires (fakepeers.cjs), each
// paired by proof with the token the rig takes from its environment. A playout line over HTTP, a WebSocket and Companion reaches the
// link it names, and its receipt is the peer's own answer — applied, refused in the peer's words, or accepted and then its one
// follow-up with "final" — with the peer's request in refs.peerRequest; a pause Patterns' own facts refuse is refused here and never
// sent. The context carries a link:<name> item per link in its record's light (amber at level 0), the frame's playout row names the
// link and node feeding the VT, the config names the links, and the show log's rows are the peers' outcomes and what their STATE
// showed. Neither peer, nor the rig's own lines, ever carries the token.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const net = require('node:net');
const { Rig, until } = require('./rig.cjs');
const { fakePlayMap, fakePatterns, legacyKey, ok, err } = require('./fakepeers.cjs');

const TOKEN = 'e2e-pl4y-0ut-7f3a';
const PLAYMAP_ID = 'pm5e6f7a';

// The node's one playlist as its STATE says it: idle until a CUE or a GO moves it; its clip on air plays on.
const show = { state: 'idle', item: { id: 'it1', label: 'Intro' }, next: { id: 'it2', label: 'Walk-in' } };
const started = Date.now();
const playing = () => 1000 + (Date.now() - started);
const pmState = () => JSON.stringify({
  v: 1, seq: 4, id: PLAYMAP_ID, t: 'playmap',
  video: { file: 'intro.mov', role: 'playlist', positionMs: playing(), lengthMs: 600000, playing: true, seq: 2 },
  playlists: [{ id: 'pl1', name: 'Main show', state: show.state, index: 0, item: show.item, positionMs: playing(), syncGroup: '', seq: 2, next: show.next }],
  sync: { role: 'solo', leader: '' },
});
const pmContext = JSON.stringify({
  v: 1, node: { t: 'playmap', id: PLAYMAP_ID, name: 'Stage left' }, light: 'green', headline: 'Out 3 · 60 fps', items: [],
  commands: [{ verb: 'PLAYLIST GO <playlist> [<item>]', risk: 'high' }],
});
const patState = () => JSON.stringify({ video: { file: 'opener.mp4', role: 'playlist', key: 'pl:3', positionMs: playing(), lengthMs: 600000, playing: true, seq: 1 } });
const patContext = JSON.stringify({
  v: 1, node: { t: 'patterns', id: '9f8e7d6c', name: 'Patterns FOH' }, light: 'green', headline: '3 screens green', items: [],
  commands: [{ verb: 'VIDEO PAUSE', risk: 'high' }],
});

let rig, pm, pat, pusher;

/** The node's answers: a GO to an item it has not, refused in its words; a CUE and a GO taken at once and run a moment later. */
function playmapCommands(body, tag, later) {
  if (body === 'PLAYLIST GO pl1 it9') return [err(tag, 'Main show has no item it9')];
  if (body === 'PLAYLIST CUE pl1 it2') {
    later([ok(tag, 'final {"stage":"applied","effect":"Walk-in cued"}')], 400);
    setTimeout(() => { show.state = 'cued'; show.item = { id: 'it2', label: 'Walk-in' }; }, 400);
    return [ok(tag, '{"stage":"accepted","effect":"loading Walk-in"}')];
  }
  if (body === 'PLAYLIST GO pl1 it1') {
    later([ok(tag, 'final {"stage":"applied","effect":"playing Intro"}')], 400);
    setTimeout(() => { show.state = 'playing'; show.item = { id: 'it1', label: 'Intro' }; show.next = { id: 'it2', label: 'Walk-in' }; }, 400);
    return [ok(tag, '{"stage":"accepted","effect":"starting Intro"}')];
  }
  return [err(tag, 'not in this fake: ' + body)];
}

before(async () => {
  pm = await fakePlayMap({ token: TOKEN, state: pmState, context: pmContext, whoami: '{"paired":true,"role":"remote","armed":false,"may":"*"}', commands: playmapCommands });
  pat = await fakePatterns({
    token: TOKEN, state: patState, context: patContext,
    commands: (body, tag) => (body === 'VIDEO END 10' ? [ok(tag, 'jumped to the last 10 s')] : [err(tag, 'not in this fake: ' + body)]),
  });
  // Both push their STATE while their clips play, as the real ones do: the clip's countdown and the playlist's state stay fresh.
  pusher = setInterval(() => { pm.push('STATE ' + pmState()); pat.push('STATE ' + patState()); }, 300);
  rig = await Rig.create({
    wire: true, journal: '-', env: { STAGEWATCH_RIG_PLAYOUT_TOKEN: TOKEN },
    args: ['--playout', `stage-left=127.0.0.1:${pm.port},playmap`, '--playout', `patterns=127.0.0.1:${pat.port}`],
  });
});

after(async () => {
  clearInterval(pusher);
  if (rig) await rig.dispose();
  if (pm) await pm.close();
  if (pat) await pat.close();
});

/** One request through the HTTP API with a request id: its status and receipt. */
async function http(line, id) {
  const parts = line.trim().split(/\s+/).map(encodeURIComponent);
  const res = await fetch(`${rig.base}/api/${parts.join('/')}`, { headers: { 'X-Stagewatch-Pin': rig.pin, 'X-Request-Id': id } });
  return { status: res.status, receipt: await res.json() };
}

/** Companion lines for `id` on one connection: resolves once `count` lines for it came (OK or ERROR @id …), in order. */
function companion(line, id, count, ms = 10000) {
  return new Promise((resolve, reject) => {
    const sock = net.connect(rig.companion, '127.0.0.1');
    const said = [];
    let buf = '';
    const timer = setTimeout(() => { sock.destroy(); reject(new Error(`Companion said ${JSON.stringify(said)} for @${id}`)); }, ms);
    sock.on('data', (d) => {
      buf += d;
      let nl;
      while ((nl = buf.indexOf('\n')) >= 0) {
        const l = buf.slice(0, nl).replace(/\r$/, '');
        buf = buf.slice(nl + 1);
        if (l.startsWith('READY')) sock.write(line + '\n');
        if (l.startsWith(`OK @${id} `) || l.startsWith(`ERROR @${id} `)) said.push(l);
        if (said.length === count) { clearTimeout(timer); sock.destroy(); resolve(said); return; }
      }
    });
    sock.on('error', (e) => { clearTimeout(timer); reject(e); });
  });
}

const recent = async () => ((await rig.context()).recent || []).map((r) => r.text);

test('the links pair by proof and every reader takes them: ready line, context items in the record\'s light, the peers\' contexts, config and frame', async () => {
  assert.deepEqual(rig.ready.playout.map((l) => `${l.name} ${l.kind} ${l.address}`),
    [`stage-left playmap 127.0.0.1:${pm.port}`, `patterns auto 127.0.0.1:${pat.port}`]);
  // Paired (the record says level 0 from the pairing on) is not yet heard: each link's go and cue rows wait for its kind, which its
  // peer's STATE says — so the wait is for both, never the one taken for the other.
  const items = await until(async () => {
    const c = await rig.context();
    const mine = (c.items || []).filter((i) => i.id === 'link:stage-left' || i.id === 'link:patterns');
    const verbs = (c.commands || []).map((x) => x.verb);
    return mine.length === 2 && mine.every((i) => i.state.includes('proved at level 0'))
      && verbs.includes('playout stage-left go <playlist> [<item>] [together]') && verbs.includes('playout patterns go <cue>') && c;
  }, 15000, 'both links paired and their kinds heard, in the context');
  const byId = Object.fromEntries(items.items.map((i) => [i.id, i]));
  assert.deepEqual([byId['link:stage-left'].kind, byId['link:stage-left'].label, byId['link:stage-left'].light], ['playout', 'PlayMap Stage left', 'amber'],
    'a level-0 link is amber in the context: its record is legacy, whatever its own line says');
  assert.deepEqual([byId['link:patterns'].kind, byId['link:patterns'].label, byId['link:patterns'].light], ['playout', 'Patterns', 'amber']);
  const verbs = items.commands.map((c) => c.verb);
  assert.ok(verbs.includes('playout stage-left go <playlist> [<item>] [together]') && verbs.includes('playout stage-left cue <playlist> <item>'),
    'the context offers a PlayMap node its playlist verbs, a GO together among them: ' + verbs.join(' | '));
  assert.ok(verbs.includes('playout patterns go <cue>') && !verbs.some((v) => v.startsWith('playout patterns go <playlist>')),
    'and a Patterns desk its cue verbs, never a playlist\'s');
  assert.deepEqual(verbs.filter((v) => v.includes('together')), ['playout stage-left go <playlist> [<item>] [together]'],
    'together only on the node\'s GO: a Patterns desk takes no AT');
  await until(async () => JSON.stringify((await rig.context()).peers || []).includes(PLAYMAP_ID), 10000, 'the node\'s own context pulled onto the network');

  const config = await until(async () => {
    const c = await (await fetch(rig.base + '/api/config', { headers: { 'X-Stagewatch-Pin': rig.pin } })).json();
    const kinds = (c.playoutLinks || []).map((l) => l.kind);
    return kinds.includes('playmap') && kinds.includes('patterns') && c;
  }, 10000, 'the config names both links by the kind each peer said');
  assert.deepEqual(config.playoutLinks, [
    { name: 'stage-left', kind: 'playmap', label: 'PlayMap Stage left' },
    { name: 'patterns', kind: 'patterns', label: 'Patterns' },
  ], 'the config names each link, its kind as heard and its peer');

  const frame = await until(async () => {
    const f = await (await fetch(rig.base + '/api/state?detail=1', { headers: { 'X-Stagewatch-Pin': rig.pin } })).json();
    return f.playout && f.playout.link === 'stage-left' && f;
  }, 10000, 'the frame\'s playout row');
  assert.deepEqual([frame.playout.source, frame.playout.link, frame.playout.node, frame.playout.running], ['PlayMap', 'stage-left', PLAYMAP_ID, true],
    'the clip feeding the VT names its peer, its link and its node');
  await until(() => pm.heard.some((l) => / WHOAMI$/.test(l)), 10000, 'a PlayMap node is asked WHOAMI once its kind is heard');
  assert.ok(!pat.heard.some((l) => / WHOAMI$/.test(l)), 'a Patterns desk is never asked WHOAMI');
});

test('HTTP: a GO the node refuses is refused in its words, 409, with its request named; a link it does not have is refused here', async () => {
  const { status, receipt } = await http('playout stage-left go pl1 it9', 'h1');
  assert.equal(status, 409, JSON.stringify(receipt));
  assert.deepEqual([receipt.id, receipt.ok, receipt.stage, receipt.error], ['h1', false, 'applied', 'PlayMap Stage left refused PLAYLIST GO pl1 it9: Main show has no item it9']);
  assert.ok(Number.isInteger(receipt.refs.peerRequest) && receipt.refs.peerRequest > 0, 'refs.peerRequest is the number of the request the node answered: ' + JSON.stringify(receipt.refs));
  assert.ok(pm.commandsHeard().includes('PLAYLIST GO pl1 it9'));
  assert.equal(receipt.final, undefined, 'a receipt that is not a follow-up carries no final');

  const before = pm.commandsHeard().length + pat.commandsHeard().length;
  const nope = await http('playout nope go pl1 it1', 'h2');
  assert.deepEqual([nope.status, nope.receipt.ok, nope.receipt.error], [409, false, 'no playout link called nope — links: stage-left, patterns']);
  assert.equal(pm.commandsHeard().length + pat.commandsHeard().length, before, 'a refusal here sends nothing to either peer');
});

test('WebSocket: a CUE the node takes at once and runs later answers accepted, then its one follow-up with final, the same id', async () => {
  const link = await rig.link('role=control', rig.pin);
  try {
    const from = link.messages.length;
    link.send({ cmd: 'playout', args: ['stage-left', 'cue', 'pl1', 'it2'], id: 'w1' });
    const first = await link.next((j) => j.type === 'receipt' && j.id === 'w1', 6000, 'the receipt', from);
    assert.deepEqual([first.json.ok, first.json.stage, first.json.effect], [true, 'accepted', 'PlayMap Stage left: loading Walk-in'], first.text);
    assert.equal(first.json.final, undefined);
    const final = await link.next((j) => j.type === 'receipt' && j.id === 'w1' && j.final === true, 6000, 'the follow-up', from);
    assert.deepEqual([final.json.ok, final.json.stage, final.json.effect], [true, 'applied', 'PlayMap Stage left took PLAYLIST CUE pl1 it2 (Walk-in cued)'], final.text);
    assert.equal(final.json.refs.peerRequest, first.json.refs.peerRequest, 'the follow-up names the same request');
    assert.match(final.text, /,"final":true\}$/, 'final is the receipt\'s last field');
    const receipts = link.messages.slice(from).filter((m) => m.json && m.json.type === 'receipt' && m.json.id === 'w1');
    assert.equal(receipts.length, 2, 'one receipt and one follow-up, no more');
  } finally {
    await link.close();
  }
});

test('Companion: a GO is accepted, then final applied, and the show log has its first outcome and what the node\'s STATE showed', async () => {
  const [first, final] = await companion('@c1 PLAYOUT stage-left go pl1 it1', 'c1', 2);
  assert.equal(first, 'OK @c1 accepted PlayMap Stage left: starting Intro');
  assert.equal(final, 'OK @c1 final applied PlayMap Stage left took PLAYLIST GO pl1 it1 (playing Intro)');
  const rows = await until(async () => {
    const r = await recent();
    return r.some((t) => t.endsWith('Playout: seen: Main show plays it1 on PlayMap Stage left')) && r;
  }, 10000, 'the effect seen in the node\'s STATE in the show log');
  // One row when its outcome first settles (13.12.10): taken and not run yet; its move to applied is the receipt's and the status
  // line's, and what the room got is the row its STATE earned.
  assert.ok(rows.some((t) => t.endsWith('Playout: PlayMap Stage left took PLAYLIST GO pl1 it1 and has not run it yet (starting Intro)')),
    'the outcome\'s row: ' + JSON.stringify(rows.slice(-8)));
  assert.ok(!rows.some((t) => t.endsWith('Playout: PlayMap Stage left did it: PLAYLIST GO pl1 it1 (playing Intro)')), 'and no second row for its move');
});

test('G48: Patterns\' pause of a playlist item is refused here in Patterns\' words and never sent; its end goes, in the desk\'s words', async () => {
  await until(() => pat.heard.some((l) => / STATUS$/.test(l)), 10000, 'the desk\'s STATE asked for');
  const pause = await http('patterns pause', 'g1');
  assert.equal(pause.status, 409, JSON.stringify(pause.receipt));
  assert.match(pause.receipt.error, /^Patterns: 'opener\.mp4' is a playlist item — its own clock keeps running, so it is not paused/);
  assert.ok(!pat.heard.some((l) => l.includes('VIDEO PAUSE')), 'the desk heard no pause');
  const end = await http('patterns end', 'g2');
  assert.equal(end.status, 200, JSON.stringify(end.receipt));
  assert.deepEqual([end.receipt.ok, end.receipt.stage, end.receipt.effect], [true, 'applied', 'Patterns took VIDEO END 10 (jumped to the last 10 s)']);
});

test('the token reaches neither peer as a word, nor any line the rig says', () => {
  const key = legacyKey(TOKEN);
  for (const [who, heard] of [['PlayMap', pm.heard], ['Patterns', pat.heard]]) {
    assert.ok(heard.some((l) => / AUTH-PROOF [0-9a-f]{64}$/.test(l)), `${who} was paired by proof`);
    assert.ok(!heard.some((l) => l.includes(TOKEN) || l.toUpperCase().includes(key)), `${who} never heard the token`);
  }
  assert.ok(!rig.log.includes(TOKEN) && !rig.log.toUpperCase().includes(key), 'the rig\'s log never says it');
  assert.ok(!rig.out.items.some((l) => l.line.includes(TOKEN)), 'nor its standard output');
});
