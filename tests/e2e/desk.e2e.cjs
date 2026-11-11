// One desk for every app (Show Bus §11), against the rig's approval desk — the app's own service — and three show apps on its
// node routes (a PlayMap node, a Patterns desk, AGMessage), each with its node key, posting its verbs and asking for its approved
// lines every 200 ms; the rig keeps an app collecting for 2 s after its last ask (--desk-fresh 2). An app's proposal is rated by the
// most specific whole form of its target's verified table. `peer <app> <line>` sends one listed line, born approved by the role
// wire, and its receipt is what the app made of it: applied, in the app's words, the line run once. An app that stopped asking is
// refused, and nothing is queued. `desk approve next` approves a review proposal, never a high one, which takes its id and the
// word; an approved line aimed at the rig itself runs here, and the decision's receipt is that run's, the proposal named either
// way. A line its app takes longer over than the receipt's deadline is accepted, then answered once more, final. A key or a filled
// secret placeholder rides in no line — nor a key where the app is named, or in a rejection's reason — and is in no log, journal,
// context or proposal after. A restarted desk makes ids of a new run, which every app runs, never skipping one as an id it ran
// before. With no PIN set, peer and desk pass only from this PC. These are the bytes PlayMap, Patterns and AGMessage collect against.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const WebSocket = require('ws');
const { Rig, until, PIN } = require('./rig.cjs');
const { FakeApp } = require('./fakepeers.cjs');

// How long PlayMap takes over STATUS: past the receipt's 3 s deadline, well inside its follow-up's.
const SLOW_MS = 4000;
const RIG = { kind: 'main', id: 'deskrig01', name: 'Rig Hall' };
const KEY_IN_LINE = 'swk_abcdefghijkmnpqrstuvwxyz234567';
const TOKEN_IN_LINE = 'tok-9f8e7d6c5b4a';

const PLAYMAP_VERBS = [
  ['UNDO', 'review', 'Undoes the last edit'],
  ['PANIC', 'high', 'Blacks every output'],
  ['STATUS', 'low', 'Says its state'],
  ['SHOW SET <setting> ON|OFF', 'review', 'Sets a show setting'],
  ['BIND ADD <source> <value> TO <target line>', 'review', 'Binds a control to a line'],
  ['BLACKOUT ON|OFF', 'high', 'Blacks every output'],
  ['PLAYLIST CUE <playlist> <item>|#<n>', 'review', 'Cues an item'],
  ['SET <output> name <name>', 'low', 'Names an output'],
  ['LINK STAGEWATCH <host[:port]>|""|OFF [<key>]', 'review', 'Links to a Stagewatch'],
  ['LINK PATTERNS <host[:port]> <token>', 'review', 'Links to a Patterns desk'],
];
const PATTERNS_VERBS = [['APPLY <id>', 'review', "Applies one of this desk's own proposals"], ['EYE FOCUS <thing>', 'low', 'Points the Eye']];
const AGMESSAGE_VERBS = [['notice <text>', 'review', 'A notice to every meeting']];

let rig, playmap, patterns, agmessage;
const apps = () => [playmap, patterns, agmessage];

before(async () => {
  rig = await Rig.create({ nodeKeys: 3, nodeId: 'deskrig01', journal: true, args: ['--desk-fresh', '2'] });
  const [k1, k2, k3] = rig.keys;
  const desk = () => rig.base;
  playmap = await new FakeApp({
    desk, key: k1.secret, kind: 'playmap', id: 'pm-a1b2c3', name: 'PlayMap Hall', verbs: PLAYMAP_VERBS,
    run: async (line) => {
      if (line === 'STATUS') await pause(SLOW_MS);   // longer than a receipt waits: accepted first, then its one follow-up
      return line.startsWith('PLAYLIST CUE show #9') ? { ok: false, words: 'Show has no item #9' } : { ok: true, words: line.startsWith('PLAYLIST CUE') ? 'cued Intro' : '' };
    },
  }).start();
  patterns = await new FakeApp({ desk, key: k2.secret, kind: 'patterns', id: 'pat-d4e5f6', name: 'Patterns FOH', verbs: PATTERNS_VERBS }).start();
  agmessage = await new FakeApp({ desk, key: k3.secret, kind: 'messenger', id: 'ag-778899', name: 'AGMessage', verbs: AGMESSAGE_VERBS }).start();
  // The rig joins its show network once a second: each app is one a line can go to once its context is in the rig's own.
  await until(() => heard(rig.base, rig.pin, APPS), 8000, 'the three apps in the context');
});
after(async () => {
  try { for (const a of apps()) if (a) a.close(); } finally { if (rig) await rig.dispose(); }
});

/** A command with a receipt, each argument one path segment, under a credential (the PIN unless given). */
async function api(segments, { pin = rig.pin, id = 'e' + Math.floor(performance.now()) } = {}) {
  const headers = { 'X-Request-Id': id };
  if (pin) headers['X-Stagewatch-Pin'] = pin;
  const res = await fetch(`${rig.base}/api/${segments.map(encodeURIComponent).join('/')}`, { headers });
  return { status: res.status, json: await res.json() };
}

/** POST /api/peer: the app and the line as a JSON body. */
async function peer(app, line, id = 'p' + Math.floor(performance.now())) {
  const res = await fetch(`${rig.base}/api/peer`, {
    method: 'POST', headers: { 'X-Stagewatch-Pin': rig.pin, 'X-Request-Id': id, 'Content-Type': 'application/json' }, body: JSON.stringify({ app, line }),
  });
  return { status: res.status, json: await res.json() };
}

/** An app's proposal for another app, under the proposer's own node key. */
const propose = (from, target, line, reason = '') => from.call('POST', '/api/proposals', {
  from: { t: from.kind, id: from.id, name: from.name }, target: { t: target.kind, id: target.id, name: target.name }, line, reason,
});

const proposals = async () => (await rig.request('GET', '/api/proposals', rig.pin)).json.proposals;
const pause = (ms) => new Promise((r) => setTimeout(r, ms));

/** Whether the rig's own context lists each of these apps: it joins its show network once a second, and has none in its first. */
const heard = async (base, pin, ids) => {
  const context = await (await fetch(`${base}/api/context`, { headers: pin ? { 'X-Stagewatch-Pin': pin } : {} })).json();
  return (context.peers || []).filter((p) => ids.includes(p.id)).length === ids.length;
};
const APPS = ['pm-a1b2c3', 'pat-d4e5f6', 'ag-778899'];

test('an app\'s proposal is rated by the most specific whole form of its target\'s verified table', async () => {
  const risks = {};
  for (const line of ['UNDO', 'PANIC', 'SHOW SET REMOTE-VIEW-ONLY ON', 'BIND ADD note 60 TO BLACKOUT ON', 'OUTPUTS OFF']) {
    const r = await propose(agmessage, playmap, line);
    assert.equal(r.status, 201, r.text);
    assert.match(r.json.id, /^p\d+-[a-z2-9]{4}$/, 'ids are p<n>-<run>');
    risks[line] = r.json.risk;
  }
  assert.deepEqual(risks, {
    UNDO: 'review', PANIC: 'high', 'SHOW SET REMOTE-VIEW-ONLY ON': 'review', 'BIND ADD note 60 TO BLACKOUT ON': 'high', 'OUTPUTS OFF': 'high',
  }, 'a line that carries a high line is high; a line no form takes is high');
  assert.equal(playmap.ran.length, 0, 'nothing pending reached PlayMap');
});

test('desk approve next approves a review proposal; a high one takes its id and the word high', async () => {
  const waiting = (await proposals()).filter((p) => p.state === 'pending');
  assert.deepEqual(waiting.map((p) => p.line), ['UNDO', 'PANIC', 'SHOW SET REMOTE-VIEW-ONLY ON', 'BIND ADD note 60 TO BLACKOUT ON', 'OUTPUTS OFF']);
  const [undo, panic] = waiting;
  const first = await api(['desk', 'approve', 'next'], { id: 'd1' });
  assert.equal(first.status, 200, JSON.stringify(first.json));
  assert.equal(first.json.stage, 'applied');
  assert.match(first.json.effect, /^approved p\d+ \(AGMessage → PlayMap Hall: UNDO\) — PlayMap Hall runs it and reports back$/);
  assert.equal(first.json.refs.sharedProposal, undo.id, 'the receipt names the proposal by its whole id, a string');
  await until(() => playmap.ran.some((r) => r.id === undo.id), 5000, 'PlayMap running the approved UNDO');
  assert.equal(playmap.ran.find((r) => r.id === undo.id).decidedBy, 'wire');

  const second = await api(['desk', 'approve', 'next'], { id: 'd2' });
  assert.equal(second.status, 409, 'next never approves a high proposal');
  assert.equal(second.json.error, `${panic.id.split('-')[0]} is high — approve it by its id: desk approve ${panic.id} high`);
  const noWord = await api(['desk', 'approve', panic.id], { id: 'd3' });
  assert.equal(noWord.status, 409, 'its id without the word does not either');
  assert.equal((await proposals()).find((p) => p.id === panic.id).state, 'pending');
  const withWord = await api(['desk', 'approve', panic.id.split('-')[0], 'high'], { id: 'd4' });
  assert.equal(withWord.status, 200, JSON.stringify(withWord.json));
  await until(() => playmap.ran.some((r) => r.id === panic.id), 5000, 'PlayMap running the approved PANIC');

  const rejected = await api(['desk', 'reject', 'next', 'not during the keynote'], { id: 'd5' });
  assert.equal(rejected.status, 200, JSON.stringify(rejected.json));
  const after = await proposals();
  assert.equal(after.find((p) => p.line === 'SHOW SET REMOTE-VIEW-ONLY ON').state, 'rejected');
  for (const p of after.filter((x) => x.state === 'pending')) await api(['desk', 'reject', p.id]);
  assert.equal(playmap.ran.length, 2, 'PlayMap ran what was approved, each once, and nothing rejected');
});

test('peer sends one listed line: the receipt is what the app made of it, and the app ran it once', async () => {
  const sent = await api(['peer', 'playmap', 'PLAYLIST CUE show #3'], { id: 'a1' });
  assert.equal(sent.status, 200, JSON.stringify(sent.json));
  assert.equal(sent.json.type, 'receipt');
  assert.equal(sent.json.id, 'a1');
  assert.equal(sent.json.stage, 'applied', 'exactly applied: PlayMap ran it and said so within the deadline');
  assert.match(sent.json.effect, /^PlayMap Hall ran PLAYLIST CUE show #3: cued Intro \(p\d+\)$/);
  const id = sent.json.refs.sharedProposal;
  assert.match(id, /^p\d+-[a-z2-9]{4}$/);
  assert.deepEqual(playmap.ran.filter((r) => r.line === 'PLAYLIST CUE show #3').map((r) => r.id), [id], 'PlayMap saw the line once');
  const kept = (await rig.request('GET', `/api/proposals/${id}`, rig.pin)).json;
  assert.equal(kept.state, 'done');
  assert.equal(kept.decidedBy, 'wire', 'decided by a role word, never a key\'s name or an address');
  assert.deepEqual(kept.from, { t: 'main', id: 'deskrig01', name: 'Rig Hall' });
  assert.equal(kept.form, 'PLAYLIST CUE <playlist> <item>|#<n>');

  const refused = await api(['peer', 'PlayMap Hall', 'PLAYLIST CUE show #9'], { id: 'a2' });
  assert.equal(refused.status, 409);
  assert.match(refused.json.error, /^PlayMap Hall refused PLAYLIST CUE show #9: Show has no item #9 \(p\d+\)$/);

  const quoted = 'SET out1 name "Left wall"';
  const byBody = await peer('pm-a1b2c3', quoted, 'a3');
  assert.equal(byBody.status, 200, JSON.stringify(byBody.json));
  const bySegment = await api(['peer', 'pm-a1b2c3', quoted], { id: 'a4' });
  assert.equal(bySegment.status, 200, JSON.stringify(bySegment.json));
  assert.equal(playmap.ran.filter((r) => r.line === quoted).length, 2, 'a line with its own quote marks reaches PlayMap exactly, by the body and by one path segment');

  const unlisted = await api(['peer', 'playmap', 'OUTPUTS OFF'], { id: 'a5' });
  assert.equal(unlisted.status, 409);
  assert.equal(unlisted.json.error, "PlayMap Hall does not list 'OUTPUTS OFF' — see its verbs");
  const nobody = await api(['peer', 'pm', 'UNDO'], { id: 'a6' });
  assert.equal(nobody.status, 409);
  assert.match(nobody.json.error, /^no app called 'pm' on the show network — heard: /);
  const twoWords = await api(['peer', 'playmap', 'UNDO', 'now'], { id: 'a7' });
  assert.equal(twoWords.status, 400, 'more than one argument after the app is refused: quote the line as one');
});

test('peer agmessage notice is applied, decided by the role wire; a WebSocket sends one too, with its receipt', async () => {
  const sent = await api(['peer', 'agmessage', 'notice Q&A in 2 minutes'], { id: 'n1' });
  assert.equal(sent.status, 200, JSON.stringify(sent.json));
  assert.equal(sent.json.stage, 'applied');
  const ran = agmessage.ran.filter((r) => r.line === 'notice Q&A in 2 minutes');
  assert.equal(ran.length, 1);
  assert.equal(ran[0].decidedBy, 'wire');
  assert.equal(ran[0].from.t, 'main', 'born approved from the desk itself');

  const ws = new WebSocket(`${rig.wsBase}/ws?role=control&pin=${rig.pin}`);
  const receipts = [];
  ws.on('message', (d) => { const j = JSON.parse(d.toString('utf8')); if (j.type === 'receipt') receipts.push(j); });
  await new Promise((resolve, reject) => { ws.once('open', resolve); ws.once('error', reject); });
  try {
    ws.send(JSON.stringify({ cmd: 'peer', args: ['patterns', 'EYE FOCUS stage'], id: 'w1' }));
    const receipt = await until(() => receipts.find((r) => r.id === 'w1'), 6000, 'the WebSocket receipt');
    assert.equal(receipt.ok, true);
    assert.equal(receipt.stage, 'applied');
    assert.match(receipt.effect, /^Patterns FOH ran EYE FOCUS stage \(p\d+\)$/);
    assert.deepEqual(patterns.ran.map((r) => r.line), ['EYE FOCUS stage']);
  } finally {
    ws.close();
  }
});

test('an approved line aimed at the desk itself runs here: the decision\'s receipt is that run\'s, and names the proposal either way', async () => {
  for (const line of ['peer playmap UNDO', 'desk approve next']) {
    const person = await propose(agmessage, RIG, line);
    assert.equal(person.status, 400, `${line}: a person's line is never a proposal`);
    assert.match(person.json.error, /^(peer|desk) is a person's at this desk — never a proposal$/);
  }
  const message = await propose(agmessage, RIG, 'message "Two minutes"');
  assert.equal(message.status, 201, message.text);
  assert.equal(message.json.risk, 'review', 'rated by the desk\'s own rules');
  const ran = await api(['desk', 'approve', 'next'], { id: 'h1' });
  assert.equal(ran.status, 200, JSON.stringify(ran.json));
  assert.equal(ran.json.stage, 'applied');
  assert.match(ran.json.effect, /^approved p\d+ — ran here: message on$/);
  assert.equal(ran.json.refs.sharedProposal, message.json.id, 'the outer receipt keeps its proposal, whatever the line run inside it noted');
  const done = (await rig.request('GET', `/api/proposals/${message.json.id}`, rig.pin)).json;
  assert.equal(done.state, 'done');
  assert.equal(done.decidedBy, 'wire');
  assert.equal((await (await fetch(`${rig.base}/api/state`, { headers: { 'X-Stagewatch-Pin': rig.pin } })).json()).message, 'Two minutes', 'and the line ran, once, on the rig');

  // A line the rig refuses when it runs: the decision is refused in the run's own words, the proposal still named.
  const aux = await propose(agmessage, RIG, 'aux 1 start');
  assert.equal(aux.status, 201, aux.text);
  assert.equal(aux.json.risk, 'review');
  const refused = await api(['desk', 'approve', 'next'], { id: 'h2' });
  assert.equal(refused.status, 409, JSON.stringify(refused.json));
  assert.equal(refused.json.error, "the rig doesn't simulate aux (the app does)");
  assert.equal(refused.json.refs.sharedProposal, aux.json.id, 'a refused receipt keeps the ref too');
  const failed = (await rig.request('GET', `/api/proposals/${aux.json.id}`, rig.pin)).json;
  assert.equal(failed.state, 'failed');
  assert.equal(failed.words, "the rig doesn't simulate aux (the app does)");
  await api(['clearmessage']);
});

test('a line its app is slow over is accepted at the deadline, then answered once more, final', async () => {
  const ws = new WebSocket(`${rig.wsBase}/ws?role=control&pin=${rig.pin}`);
  const receipts = [];
  ws.on('message', (d) => { const j = JSON.parse(d.toString('utf8')); if (j.type === 'receipt' && j.id === 'slow1') receipts.push({ ...j, at: performance.now() }); });
  await new Promise((resolve, reject) => { ws.once('open', resolve); ws.once('error', reject); });
  try {
    const sentAt = performance.now();
    ws.send(JSON.stringify({ cmd: 'peer', args: ['playmap', 'STATUS'], id: 'slow1' }));
    const first = await until(() => receipts[0], 8000, 'the receipt at the deadline');
    assert.equal(first.ok, true);
    assert.equal(first.stage, 'accepted', 'not applied: PlayMap has it and has not answered');
    assert.match(first.effect, /^PlayMap Hall has p\d+ — waiting for its answer$/);
    assert.ok(!first.final, 'the first receipt is not the last');
    assert.ok(first.at - sentAt >= 2500 && first.at - sentAt < SLOW_MS, `it waited its deadline, not for the app (${Math.round(first.at - sentAt)} ms)`);
    const last = await until(() => receipts[1], 8000, 'the follow-up');
    assert.equal(last.final, true);
    assert.equal(last.ok, true);
    assert.equal(last.stage, 'applied');
    assert.match(last.effect, /^PlayMap Hall ran STATUS \(p\d+\)$/);
    assert.equal(last.refs.sharedProposal, first.refs.sharedProposal, 'both name the one proposal');
    await pause(600);
    assert.equal(receipts.length, 2, 'one follow-up, and no more');
    assert.equal(playmap.ran.filter((r) => r.line === 'STATUS').length, 1, 'PlayMap ran it once');
  } finally {
    ws.close();
  }
  // PlayMap asked for nothing while it ran the line: the next test finds it collecting again.
  const asked = playmap.collects;
  await until(() => playmap.collects > asked, 3000, 'PlayMap asking again');
});

test('a key or a filled secret placeholder rides in no line, and is nowhere after', async () => {
  const before = (await proposals()).length;
  const keyed = await peer('playmap', `LINK STAGEWATCH 10.0.0.5 ${KEY_IN_LINE}`, 's1');
  assert.equal(keyed.status, 409);
  assert.equal(keyed.json.error, 'a key rides in no line');
  const filled = await peer('playmap', `LINK PATTERNS 10.0.0.6 ${TOKEN_IN_LINE}`, 's2');
  assert.equal(filled.status, 409);
  assert.equal(filled.json.error, 'a secret rides in no line — type it on PlayMap Hall itself');
  const proposed = await propose(agmessage, playmap, `LINK STAGEWATCH 10.0.0.5 ${KEY_IN_LINE}`);
  assert.equal(proposed.status, 400, 'a proposal is held to the same fence');
  // The arguments the wrong way round: the key where the app is named. Its refusal names no app, and never quotes the word.
  for (const swapped of [await api(['peer', `LINK STAGEWATCH 10.0.0.5 ${KEY_IN_LINE}`, 'playmap'], { id: 's3' }), await peer(`LINK STAGEWATCH 10.0.0.5 ${KEY_IN_LINE}`, 'playmap', 's4')]) {
    assert.equal(swapped.status, 409);
    assert.equal(swapped.json.error, 'a key rides in no line');
  }
  // A rejection's reason is journalled: one that holds a key rejects nothing, and the proposal still waits.
  const waiting = await propose(agmessage, playmap, 'UNDO');
  assert.equal(waiting.status, 201, waiting.text);
  const because = await api(['desk', 'reject', waiting.json.id, `because ${KEY_IN_LINE} said so`], { id: 's5' });
  assert.equal(because.status, 409, JSON.stringify(because.json));
  assert.equal(because.json.error, 'a key rides in no line');
  assert.equal((await proposals()).find((p) => p.id === waiting.json.id).state, 'pending');
  assert.equal((await api(['desk', 'reject', waiting.json.id], { id: 's6' })).status, 200);
  await pause(1200);   // the rig's next context, and its journal's writer
  assert.equal((await proposals()).length, before + 1, 'nothing was queued but the proposal rejected here');
  const everywhere = [
    rig.log, fs.readFileSync(rig.journal, 'utf8'), JSON.stringify(await rig.context()), JSON.stringify(await proposals()),
    await (await fetch(`${rig.base}/api/state?detail=1`, { headers: { 'X-Stagewatch-Pin': rig.pin } })).text(),
  ].join('\n');
  assert.ok(!everywhere.includes(KEY_IN_LINE) && !everywhere.includes(TOKEN_IN_LINE), 'neither is in the log, the journal, the context, the queue or a frame');
  assert.ok(!everywhere.includes('swk_') && !everywhere.includes('said so'), 'no part of the key, and nothing of the refused reason');
  const rows = fs.readFileSync(rig.journal, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l).x);
  assert.ok(rows.some((x) => /^Not rejected p\d+ \(wire\): AGMessage → PlayMap Hall: UNDO — a key rides in no line$/.test(x)),
    'the refused rejection is a row that says why, without the reason: ' + rows.slice(-6).join(' | '));
  assert.ok(!playmap.ran.some((r) => r.line.startsWith('LINK ')), 'and neither reached PlayMap');
});

test('the journal keeps each desk row, who and from where apart from its words', async () => {
  await pause(300);
  const rows = fs.readFileSync(rig.journal, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l));
  const sent = rows.find((r) => /^Sent p\d+ to PlayMap Hall \(wire\): PLAYLIST CUE show #3$/.test(r.x));
  assert.ok(sent, 'the sent line is a desk row: ' + rows.map((r) => r.x).slice(-12).join(' | '));
  assert.equal(sent.via, 'PIN, 127.0.0.1', 'who sent it and from where is the row\'s via, never its words');
  assert.ok(rows.some((r) => /^Outcome p\d+: PlayMap Hall: PLAYLIST CUE show #3 — done \(cued Intro\)$/.test(r.x)), 'and what came of it');
  assert.ok(rows.some((r) => /^Refused to send to PlayMap Hall \(wire\): PlayMap Hall does not list that line — see its verbs$/.test(r.x)), 'a refusal never quotes an unlisted line');
  assert.ok(rows.some((r) => / — not during the keynote$/.test(r.x)), 'a rejection keeps its reason');
});

test('an app that stopped asking is refused, and nothing is queued for it', async () => {
  patterns.silent();
  await pause(2400);   // past --desk-fresh 2
  const before = (await proposals()).length;
  const sent = await api(['peer', 'patterns', 'EYE FOCUS stage'], { id: 'q1' });
  assert.equal(sent.status, 409, JSON.stringify(sent.json));
  assert.match(sent.json.error, /^Patterns FOH has not asked this desk for approved lines in \d+ s — it would not collect this$/);
  const asked = await propose(agmessage, patterns, 'EYE FOCUS stage');
  assert.equal(asked.status, 201);
  const approve = await api(['desk', 'approve', asked.json.id], { id: 'q2' });
  assert.equal(approve.status, 409, 'an approval for a silent app is refused too');
  assert.equal((await proposals()).find((p) => p.id === asked.json.id).state, 'pending', 'and the proposal still waits');
  assert.equal((await proposals()).length, before + 1, 'the refused send queued nothing');
  await api(['desk', 'reject', asked.json.id]);
  assert.equal(patterns.ran.length, 1);
  patterns.asking = true;
});

test('a restarted desk makes ids of a new run, which every app runs — never skipping one as an id it ran before', async () => {
  const runA = (await proposals())[0].id.split('-')[1];
  await rig.stop();
  await rig.start();
  await until(() => heard(rig.base, rig.pin, APPS), 12000, 'the three apps heard again');
  const lines = [[playmap, 'playmap', 'UNDO'], [patterns, 'patterns', 'EYE FOCUS stage'], [agmessage, 'agmessage', 'notice doors in 5']];
  for (const [app, word, line] of lines) {
    const ranBefore = app.ran.length;
    const sent = await until(async () => {
      const r = await api(['peer', word, line]);
      return r.status === 200 ? r : null;   // an app is collecting again once it has asked the new run
    }, 8000, `a line for ${word} after the restart`);
    assert.equal(sent.json.stage, 'applied', JSON.stringify(sent.json));
    const id = sent.json.refs.sharedProposal;
    assert.match(id, /^p[1-3]-[a-z2-9]{4}$/, 'the new run counts from p1 again');
    assert.notEqual(id.split('-')[1], runA, 'under a run tag of its own');
    assert.equal(app.ran.length, ranBefore + 1, `${word} ran the new run's line`);
    assert.equal(app.ran[app.ran.length - 1].id, id);
  }
  const old = await rig.request('GET', `/api/proposals/p1-${runA}`, rig.pin);
  assert.equal(old.status, 404);
  assert.match(old.json.error, /this desk has restarted since/);
  for (const a of apps()) assert.deepEqual(a.errors, [], `${a.name} met no refusal on its node routes`);
});

test('with no PIN set, peer and desk pass only from this PC', async () => {
  const open = await Rig.create({ nodeKeys: 1, pinFile: true, pin: '', args: ['--desk-fresh', '2'] });
  const app = await new FakeApp({ desk: () => open.base, key: open.keys[0].secret, kind: 'playmap', id: 'pm-open01', name: 'PlayMap Open', verbs: PLAYMAP_VERBS }).start();
  try {
    await until(() => heard(open.base, '', ['pm-open01']), 8000, 'the app in the context');
    // A request that came in over loopback under another machine's name (a rebound name, a forwarder) is another machine's.
    const elsewhere = await new Promise((resolve, reject) => {
      const req = http.request({ host: '127.0.0.1', port: open.port, path: '/api/peer/playmap/UNDO', headers: { Host: `stage.example:${open.port}`, 'X-Request-Id': 'o1' } }, (res) => {
        let body = '';
        res.on('data', (d) => { body += d; });
        res.on('end', () => resolve({ status: res.statusCode, json: JSON.parse(body) }));
      });
      req.on('error', reject);
      req.end();
    });
    assert.equal(elsewhere.status, 403);
    assert.equal(elsewhere.json.ok, false);
    assert.match(elsewhere.json.error, /^peer acts on another show app with a person's authority — from another machine over HTTP it needs a PIN set and given, a control key, or its address in Settings → Remote → May drive other show apps$/);
    assert.equal(app.ran.length, 0, 'nothing refused at the door reached the app');
    const local = await fetch(`${open.base}/api/peer/playmap/UNDO`, { headers: { 'X-Request-Id': 'o2' } });
    const receipt = await local.json();
    assert.equal(local.status, 200, JSON.stringify(receipt));
    assert.equal(receipt.stage, 'applied');
    assert.deepEqual(app.ran.map((r) => r.line), ['UNDO']);
  } finally {
    app.close();
    await open.dispose();
  }
});
