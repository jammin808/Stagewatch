// Running-order cues and timed GO against the rig (--playout, --cue): a fake PlayMap node and a fake Patterns desk on their line
// wires (fakepeers.cjs), each listing what a cue may name (the node's INDEX, the desk's CUE LIST). The rig's running order holds
// cues as the app's does and runs them through the app's planner (PlayoutDirector): an item loaded with its standby off sends
// nothing; one that starts sends each cue's GO — a PlayMap playlist's item, a Patterns cue by CUE GO alone, never STANDBY beside
// it; one loaded with standby on stands by at the end of the turn that loaded it (PlayMap's CUE on an idle playlist, Patterns'
// CUE STANDBY) and GOes when it starts, and one that loads and starts in one turn never stands by; cues marked together GO at one
// instant — the node with AT on its own clock (sent with it, then scheduled once it took it), the desk held here until its moment;
// a cue naming a link this PC does not have is skipped with a row. The show log (the context's recent
// lines) has a row for each timing and skip. Neither peer ever carries the token.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { Rig, until } = require('./rig.cjs');
const { fakePlayMap, fakePatterns, legacyKey, ok, err } = require('./fakepeers.cjs');

const TOKEN = 'e2e-cu3s-t1m3-9a1b';
const PLAYMAP_ID = 'pm6f7a8b';

// The node's playlist stays idle: a CUE is only ever sent to an idle one, and these tests look at what is sent, not what plays.
const pmState = () => JSON.stringify({
  v: 1, seq: 4, id: PLAYMAP_ID, t: 'playmap', at: Date.now(),
  video: { file: 'intro.mov', role: 'playlist', positionMs: 0, lengthMs: 600000, playing: false, seq: 2 },
  playlists: [{ id: 'pl1', name: 'Main show', state: 'idle', index: 0, item: { id: 'it1', label: 'Intro' }, positionMs: 0, syncGroup: '', seq: 2, next: { id: 'it2', label: 'Walk-in' } }],
  sync: { role: 'solo', leader: '' },
  project: { index: 'sig1' },
});
const pmIndex = JSON.stringify({
  v: 1, revision: 1, sig: 'sig1',
  playlists: [{ id: 'pl1', name: 'Main show', loop: false, syncGroup: null, items: [{ id: 'it1', label: 'Intro' }, { id: 'it2', label: 'Walk-in' }] }],
});
const pmContext = JSON.stringify({ v: 1, node: { t: 'playmap', id: PLAYMAP_ID, name: 'Stage left' }, light: 'green', headline: 'ok', items: [], commands: [] });
const patState = () => JSON.stringify({ video: null, cuestack: { armed: true, hold: false, listRev: 3, standby: { id: 'c1', number: '12', name: 'Walk-in', requireConfirm: false } } });
const patCues = JSON.stringify({ name: 'Main', listRev: 3, cues: [{ id: 'c1', number: '12', name: 'Walk-in', enabled: true }] });
const patContext = JSON.stringify({ v: 1, node: { t: 'patterns', id: '8e7d6c5b', name: 'Patterns FOH' }, light: 'green', headline: 'ok', items: [], commands: [] });

let rig, pm, pat;

before(async () => {
  pm = await fakePlayMap({
    token: TOKEN, state: pmState, context: pmContext, whoami: '{"paired":true,"role":"remote","armed":false,"may":"*"}',
    commands: (body, tag) => (body === 'INDEX' ? [ok(tag, pmIndex)] : body.startsWith('PLAYLIST ') ? [ok(tag)] : [err(tag, 'not in this fake: ' + body)]),
  });
  pat = await fakePatterns({
    token: TOKEN, state: patState, context: patContext,
    commands: (body, tag) => (body === 'CUE LIST' ? [ok(tag, patCues)] : body.startsWith('CUE ') ? [ok(tag)] : [err(tag, 'not in this fake: ' + body)]),
  });
  rig = await Rig.create({
    wire: true, journal: '-', env: { STAGEWATCH_RIG_PLAYOUT_TOKEN: TOKEN },
    args: [
      '--playout', `stage-left=127.0.0.1:${pm.port},playmap`, '--playout', `patterns=127.0.0.1:${pat.port}`,
      // Item 2: standby off (the default). Item 3: standby on. Item 4: together, and standby on — it loads and starts in one turn,
      // so it must never stand by. Item 5: a link this PC does not have.
      '--cue', '2=stage-left:pl1:it1', '--cue', '2=patterns::c1',
      '--cue', '3=stage-left:pl1:it2:standby', '--cue', '3=patterns::c1:standby',
      '--cue', '4=stage-left:pl1:it1:together:standby', '--cue', '4=patterns::c1:standby:together',
      '--cue', '5=nope:pl1:it1',
    ],
  });
});

after(async () => {
  if (rig) await rig.dispose();
  if (pm) await pm.close();
  if (pat) await pat.close();
});

const recent = async () => ((await rig.context()).recent || []).map((r) => r.text);
const settle = (ms) => new Promise((r) => setTimeout(r, ms));
const playmapHeard = () => pm.commandsHeard();
const patternsHeard = () => pat.commandsHeard();

test('the links pair, and each peer is asked for what a cue may name: the node its INDEX, the desk its CUE LIST', async () => {
  await until(() => pm.heard.some((l) => / INDEX$/.test(l)) && pat.heard.some((l) => / CUE LIST$/.test(l)), 15000, 'INDEX and CUE LIST asked');
  await until(async () => {
    const c = await rig.context();
    const verbs = (c.commands || []).map((x) => x.verb);
    return verbs.includes('playout stage-left go <playlist> [<item>] [together]') && verbs.includes('playout patterns go <cue>');
  }, 15000, 'both kinds heard: the context offers a GO together on the node, a cue GO on the desk');
  assert.ok(!pat.heard.some((l) => / INDEX$/.test(l)) && !pm.heard.some((l) => / CUE LIST$/.test(l)), 'each is asked only its own kind\'s list');
  assert.deepEqual(playmapHeard().concat(patternsHeard()), [], 'and no command went before any item started');
});

test('an item loaded with standby off sends nothing; when it starts, each cue GOes — Patterns by CUE GO alone', async () => {
  const loaded = await rig.run('next');
  assert.equal(loaded.ok, true, JSON.stringify(loaded));
  await settle(600);
  assert.deepEqual(playmapHeard().concat(patternsHeard()), [], 'a load with standby off sends nothing');
  await rig.run('start');
  await until(() => playmapHeard().includes('PLAYLIST GO pl1 it1') && patternsHeard().includes('CUE GO c1'), 8000, 'the start\'s GOs');
  await settle(300);
  assert.ok(!playmapHeard().some((c) => c.startsWith('PLAYLIST CUE')) && !patternsHeard().includes('CUE STANDBY c1'),
    'no CUE and no STANDBY: standby is off unless set');
});

test('standby on: the load stands each cue by at the end of its turn — CUE on the idle playlist, STANDBY on the desk — and the start GOes', async () => {
  await rig.run('pause');
  await rig.run('next');
  await until(() => playmapHeard().includes('PLAYLIST CUE pl1 it2') && patternsHeard().includes('CUE STANDBY c1'), 8000, 'the standby cues');
  assert.ok(!playmapHeard().includes('PLAYLIST GO pl1 it2'), 'a load that does not start sends no GO');
  const gos = patternsHeard().filter((c) => c === 'CUE GO c1').length;
  await rig.run('start');
  await until(() => playmapHeard().includes('PLAYLIST GO pl1 it2') && patternsHeard().filter((c) => c === 'CUE GO c1').length === gos + 1, 8000, 'the start\'s GOs');
});

test('together: an item that loads and starts in one turn GOes at one instant — AT on the node\'s clock, the desk held here — and never stands by', async () => {
  const before = patternsHeard().filter((c) => c === 'CUE GO c1').length;
  const standbys = patternsHeard().filter((c) => c === 'CUE STANDBY c1').length;
  const sentAt = Date.now();
  await rig.run('next');   // the timer runs: the item loads and starts in one turn
  await until(() => playmapHeard().some((c) => /^PLAYLIST GO pl1 it1 AT \d+$/.test(c)) && patternsHeard().filter((c) => c === 'CUE GO c1').length === before + 1,
    8000, 'the timed GO and the held one');
  const at = Number(playmapHeard().find((c) => /^PLAYLIST GO pl1 it1 AT \d+$/.test(c)).split(' ').pop());
  assert.ok(at >= sentAt + 100 && at <= Date.now() + 2000, `the AT is the lead ahead on the node's clock (${at - sentAt} ms after the line went)`);
  const rows = await until(async () => {
    const r = await recent();
    return r.some((t) => /Playout: playout cue pl1 it1 on Stage left: play Main show · it1 sent with AT for \d\d:\d\d:\d\d\.\d{3} \(± \d+ ms\)$/.test(t))
      && r.some((t) => /Playout: playout cue pl1 it1 on Stage left: play Main show · it1 scheduled for \d\d:\d\d:\d\d\.\d{3} \(± \d+ ms\)$/.test(t))
      && r.some((t) => /Playout: playout cue c1 on Patterns: GO cue c1 held here, to be sent at .*: Patterns takes no AT$/.test(t)) && r;
  }, 8000, 'the rows that say when each went: the node\'s sent with AT, then scheduled once it took it; the desk\'s held');
  assert.ok(!rows.some((t) => t.includes('not together')), 'both clocks known: together');
  await settle(300);
  assert.ok(!playmapHeard().includes('PLAYLIST CUE pl1 it1') && patternsHeard().filter((c) => c === 'CUE STANDBY c1').length === standbys,
    'a load that starts in its turn stands nothing by, though its cues are set to stand by');
});

test('a cue naming a link this PC does not have is skipped at the item\'s start, with a row, and nothing is sent for it', async () => {
  const heard = playmapHeard().length + patternsHeard().length;
  await rig.run('next');
  await until(async () => (await recent()).some((t) => t.endsWith('Playout: playout cue pl1 it1 on Nope: skipped — no playout link called nope on this PC')), 8000, 'the skip row');
  await settle(300);
  assert.equal(playmapHeard().length + patternsHeard().length, heard, 'nothing sent for it');
});

test('the token reaches neither peer, nor any line the rig says', () => {
  const key = legacyKey(TOKEN);
  for (const [who, heard] of [['PlayMap', pm.heard], ['Patterns', pat.heard]]) {
    assert.ok(heard.some((l) => / AUTH-PROOF [0-9a-f]{64}$/.test(l)), `${who} was paired by proof`);
    assert.ok(!heard.some((l) => l.includes(TOKEN) || l.toUpperCase().includes(key)), `${who} never heard the token`);
  }
  assert.ok(!rig.log.includes(TOKEN) && !rig.log.toUpperCase().includes(key), 'the rig\'s log never says it');
});
