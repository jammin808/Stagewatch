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
// The desk's readers: the operator's frame ends with desk, the config with peers, each peers row of the context with collects; and
// the producer page draws the frame's desk in a browser — the next proposal, approved or rejected by the id it shows, a high one by
// a second press that sends the word high, Approve greyed with its reason while the proposal's app is not collecting. The page
// takes no press on what it has just drawn (a double-click neither confirms a high proposal nor decides the one behind), names
// both ends as the desk knows their ids, and reads a line longer than the frame carries whole before Approve is live.
// One report: what a verified app's own journal told the desk (its context's recent lines) reaches /api/report/network.csv and
// the report's show network section once, with its node, its own time and its road; another app's key posting under its id
// adds nothing; the report's AI section holds the desk's rows, a rejection included; and none of it is the rig's own recent.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const WebSocket = require('ws');
const { chromium } = require('playwright');
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
  assert.equal(sent.k, 'Desk', 'in the desk\'s own kind');
  assert.equal(sent.n, 'pm-a1b2c3', 'with the node it was sent to, by its id');
});

test('one report: a verified app\'s lines reach network.csv once, with their node, own time and road; a forged id\'s never; /report holds the rejection', async () => {
  const headers = { 'X-Stagewatch-Pin': PIN };
  const csv = async () => (await fetch(rig.base + '/api/report/network.csv', { headers })).text();
  assert.equal((await fetch(rig.base + '/api/report/network.csv')).status, 401, 'network.csv needs a credential');
  const at = Date.now();
  playmap.recent = [{ at, text: 'PLAYLIST GO show — Intro, "take 2"' }];
  await playmap.post();
  const text = await until(async () => { const t = await csv(); return t.includes('PLAYLIST GO show') ? t : null; }, 5000, 'PlayMap\'s line in network.csv');
  const lines = text.split('\r\n');
  assert.equal(lines[0], '# peer lines are a sample; each node\'s own journal is the record');
  assert.equal(lines[1], 'Time,Node,Kind,Node id,Node time,Via,Text');
  assert.ok(lines.some((l) => /^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d,PlayMap Hall,playmap,pm-a1b2c3,,node key '[^']+',PlayMap Hall joins the report; its earlier lines are in its own journal$/.test(l)),
    'the app joined the report when the desk first heard it, with no backlog: ' + lines.slice(0, 6).join(' | '));
  const stamp = new Date(at).toISOString().slice(0, 19).replace('T', ' ');
  const row = lines.find((l) => l.includes('PLAYLIST GO show'));
  assert.match(row, new RegExp(`^\\d{4}-\\d\\d-\\d\\d \\d\\d:\\d\\d:\\d\\d,PlayMap Hall,playmap,pm-a1b2c3,${stamp},node key '[^']+',"PLAYLIST GO show — Intro, ""take 2"""$`),
    'its line with its node, its own time on its own clock and the road its context came by');
  // The same lines again, and the same context held: journalled once.
  await playmap.post();
  await pause(1500);
  assert.equal((await csv()).split('PLAYLIST GO show').length - 1, 1, 'a line is in the report once');
  // Another app's key posting as PlayMap: refused, and nothing it said is in the report.
  const forged = await agmessage.call('POST', '/api/peers/context', { ...playmap.context(), recent: [{ at: at + 1, text: 'FORGED LINE' }] });
  assert.equal(forged.status, 403, 'a node key cannot speak for another node: ' + forged.text);
  await pause(1500);
  assert.ok(!(await csv()).includes('FORGED LINE'), 'no row of a context that was refused');
  // The journal file keeps the row: the peer kind, the node, the line's own time and the road, apart from its words.
  const kept = fs.readFileSync(rig.journal, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l)).find((r) => r.k === 'Peer' && r.x.startsWith('PLAYLIST GO show'));
  assert.ok(kept && kept.n === 'pm-a1b2c3' && kept.pt === at && /^node key '/.test(kept.via) && kept.nn === 'PlayMap Hall' && kept.nk === 'playmap', JSON.stringify(kept));
  // The page: the desk's rows, the rejection included, in the AI section; the app's line in the show network, never in the rig's own recent.
  const html = await (await fetch(rig.base + '/report', { headers })).text();
  assert.match(html, /Rejected p\d+ \(wire\): [^<]* — not during the keynote/, 'the rejection is reported, with who (by role) and why');
  assert.match(html, /<h2>The show network<\/h2>.*Peer lines are a sample; each node's own journal is the record\..*PLAYLIST GO show — Intro/s, 'the show network section says it is a sample and lists the line');
  assert.ok(!JSON.stringify((await rig.context()).recent).includes('PLAYLIST GO show'), 'a peer\'s line is never said again as the rig\'s own');
  playmap.recent = [];
});

test('the readers: the operator frame ends with desk, the config with peers, each peers row of the context with collects', async () => {
  const frame = await (await fetch(`${rig.base}/api/state?detail=1`, { headers: { 'X-Stagewatch-Pin': rig.pin } })).text();
  const state = JSON.parse(frame);
  assert.equal(Object.keys(state).at(-1), 'desk', 'desk is the operator frame\'s last field');
  assert.deepEqual(Object.keys(state.desk), ['pending', 'next', 'apps']);
  assert.deepEqual(state.desk.apps.map((a) => a.id).sort(), [...APPS].sort());
  assert.deepEqual(state.desk.apps.find((a) => a.id === 'pm-a1b2c3'), { id: 'pm-a1b2c3', name: 'PlayMap Hall', kind: 'playmap', collects: true, verified: true });
  for (const query of ['', '?detail=1']) {
    const open = await (await fetch(`${rig.base}/api/state${query}`)).text();
    assert.ok(!open.includes('"desk"') && !open.includes('PlayMap Hall'), `no frame without the PIN carries the desk (${query || 'plain'})`);
  }
  // The config's list of apps changes no oftener than the desk's hold (5 s): three apps heard one after another are all in it by then.
  let config = null;
  await until(async () => {
    config = await (await fetch(`${rig.base}/api/config`)).json();
    return (config.peers || []).length === 3;
  }, 9000, 'the three apps in the config\'s peers');
  assert.equal(Object.keys(config).at(-1), 'peers', 'peers is the config\'s last field');
  assert.deepEqual([...config.peers].sort((a, b) => a.id.localeCompare(b.id)), [
    { id: 'ag-778899', name: 'AGMessage', kind: 'messenger' },
    { id: 'pat-d4e5f6', name: 'Patterns FOH', kind: 'patterns' },
    { id: 'pm-a1b2c3', name: 'PlayMap Hall', kind: 'playmap' },
  ]);
  const rows = (await rig.context()).peers.filter((p) => APPS.includes(p.id));
  assert.equal(rows.length, 3);
  for (const row of rows) {
    assert.equal(Object.keys(row).at(-1), 'collects', 'collects is a peers row\'s last field');
    assert.equal(row.collects, true, `${row.name} asks for its approved lines every 200 ms`);
  }
});

// What the producer page says while no proposal from another app waits.
const IDLE = 'Nothing from other apps waits for approval.';

test('the producer page draws the desk and decides by the id it shows; a high proposal takes a second press', async () => {
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto(`${rig.base}/producer?pin=${encodeURIComponent(rig.pin)}`);
    const shows = (id) => page.waitForFunction((x) => (document.getElementById('deskNext') || { dataset: {} }).dataset.id === x, id, { timeout: 8000 });
    const idle = () => page.waitForFunction((words) => document.getElementById('desk').textContent === words, IDLE, { timeout: 8000 });
    await idle();
    assert.equal(await page.isVisible('#deskBox'), true, 'the panel is drawn from the frame\'s desk');
    assert.match(await page.textContent('#deskApps'), /PlayMap Hall ✓ collecting/);

    const undo = (await propose(agmessage, playmap, 'UNDO')).json;
    const panic = (await propose(agmessage, playmap, 'PANIC')).json;
    await shows(undo.id);
    assert.equal(await page.textContent('#deskCount'), '· 2 waiting');
    assert.match(await page.textContent('#deskNext'), /^REVIEWAGMessage → PlayMap Hall · p\d+UNDO/);
    assert.equal(await page.textContent('#deskApprove'), 'Approve');
    await page.click('#deskApprove');
    await until(() => playmap.ran.some((r) => r.id === undo.id), 5000, 'PlayMap running the UNDO the page approved');
    assert.equal(playmap.ran.find((r) => r.id === undo.id).decidedBy, 'wire', 'a page decides as the wire');
    await page.waitForFunction(() => /^approved p\d+ \(AGMessage → PlayMap Hall: UNDO\)/.test(document.getElementById('deskMsg').textContent), null, { timeout: 8000 });

    // The high one next: one press arms it and approves nothing; the second sends its id and the word high.
    await shows(panic.id);
    assert.match(await page.textContent('#deskNext'), /^HIGH/);
    await page.click('#deskApprove');
    await page.waitForFunction(() => document.getElementById('deskApprove').textContent === 'Approve HIGH — press again', null, { timeout: 8000 });
    await pause(600);
    assert.equal((await proposals()).find((p) => p.id === panic.id).state, 'pending', 'one press approves nothing high');
    await page.click('#deskApprove');
    await until(() => playmap.ran.some((r) => r.id === panic.id), 5000, 'PlayMap running the PANIC the page approved with the word high');
    await idle();

    // Reject by the id shown; and Approve is greyed, with why, while the proposal's app is not collecting.
    const again = (await propose(agmessage, playmap, 'UNDO')).json;
    await shows(again.id);
    await page.click('#deskReject');
    await until(async () => (await proposals()).find((p) => p.id === again.id).state === 'rejected', 5000, 'the page\'s rejection');
    await idle();
    patterns.silent();
    const quiet = (await propose(agmessage, patterns, 'EYE FOCUS stage')).json;
    await shows(quiet.id);
    // Past --desk-fresh 2 the app no longer collects: Approve says why, and stays greyed after the dwell every new proposal has.
    await page.waitForFunction(() => (document.getElementById('deskApprove') || {}).title === 'Patterns FOH is not asking this desk for its approved lines: it would not collect this',
      null, { timeout: 8000 });
    await pause(1200);
    assert.equal(await page.isDisabled('#deskApprove'), true);
    assert.equal(await page.textContent('#deskWhy'), 'Patterns FOH is not collecting — approve once it asks again');
    assert.match(await page.textContent('#deskApps'), /Patterns FOH — not collecting/);
    await until(async () => (await rig.context()).peers.find((p) => p.id === 'pat-d4e5f6').collects === false, 5000, 'the context saying Patterns no longer collects');
    await page.click('#deskReject');
    await idle();
    assert.equal((await proposals()).find((p) => p.id === quiet.id).state, 'rejected');
    assert.equal(playmap.ran.filter((r) => [undo.id, panic.id, again.id].includes(r.id)).length, 2, 'PlayMap ran what the page approved, each once, and not what it rejected');
    assert.deepEqual(errors, []);
  } finally {
    patterns.asking = true;
    await browser.close();
  }
});

test('the producer page takes no press on what it has just drawn, names both ends by their ids, and reads a long line whole', async () => {
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto(`${rig.base}/producer?pin=${encodeURIComponent(rig.pin)}`);
    const shows = (id) => page.waitForFunction((x) => (document.getElementById('deskNext') || { dataset: {} }).dataset.id === x, id, { timeout: 8000 });
    const idle = () => page.waitForFunction((words) => document.getElementById('desk').textContent === words, IDLE, { timeout: 8000 });
    const approveReads = (words, timeout = 8000) => page.waitForFunction((x) => (document.getElementById('deskApprove') || {}).textContent === x, words, { timeout });
    const stateOf = async (id) => (await proposals()).find((p) => p.id === id).state;
    await idle();

    // A double-click on a high proposal arms it and approves nothing: the armed Approve takes no press while it is new.
    const panic = (await propose(agmessage, playmap, 'PANIC')).json;
    await shows(panic.id);
    await page.dblclick('#deskApprove');
    await approveReads('Approve HIGH — press again');
    await pause(1500);   // past the dwell, and long enough for a decision to have landed
    assert.equal(await stateOf(panic.id), 'pending', 'a double-click approves nothing high');
    assert.equal(playmap.ran.some((r) => r.id === panic.id), false);
    // Left alone, the arm falls back: Approve reads as it did, and nothing was approved.
    await approveReads('Approve', 9000);
    assert.equal(await stateOf(panic.id), 'pending', 'an arm that ran out approved nothing');

    // An arm is the proposal's own: when another proposal takes its place, that one is not armed.
    const blackout = (await propose(agmessage, playmap, 'BLACKOUT ON')).json;
    await page.click('#deskApprove');
    await approveReads('Approve HIGH — press again');
    assert.equal((await api(['desk', 'reject', panic.id])).json.ok, true);
    await shows(blackout.id);
    assert.equal(await page.textContent('#deskApprove'), 'Approve', 'the next high proposal is not armed by the press on the one before');
    await pause(1200);
    assert.equal(await stateOf(blackout.id), 'pending');
    assert.equal((await api(['desk', 'reject', blackout.id])).json.ok, true);
    await idle();

    // A double-click with two proposals waiting approves the one shown, and not the one drawn in its place.
    const first = (await propose(agmessage, playmap, 'UNDO')).json;
    const second = (await propose(agmessage, playmap, 'SHOW SET REMOTE-VIEW-ONLY ON')).json;
    await shows(first.id);
    assert.equal(await page.textContent('#deskCount'), '· 2 waiting');
    await page.dblclick('#deskApprove');
    await until(() => playmap.ran.some((r) => r.id === first.id), 5000, 'PlayMap running the proposal the double-click approved');
    await shows(second.id);
    await pause(1500);
    assert.equal(await stateOf(second.id), 'pending', 'the proposal behind was not approved by the same double-click');
    assert.equal(playmap.ran.filter((r) => r.id === first.id).length, 1);
    assert.equal(await page.textContent('#err'), '', 'the second click sent nothing: no refusal came back');
    await page.click('#deskReject');
    await idle();

    // Both ends are named as the desk knows their ids, never as the proposal names them; the frame gives the ids beside the names.
    const odd = await agmessage.call('POST', '/api/proposals', {
      from: { t: agmessage.kind, id: agmessage.id, name: 'Operator at FOH' }, target: { t: playmap.kind, id: playmap.id, name: 'AGMessage' }, line: 'UNDO',
    });
    assert.equal(odd.status, 201, odd.text);
    await shows(odd.json.id);
    assert.match(await page.textContent('#deskNext'), /^REVIEWAGMessage → PlayMap Hall · p\d+UNDO/);
    assert.equal(await page.getAttribute('#deskNext .who', 'title'), 'ag-778899 → pm-a1b2c3');
    const frame = await (await fetch(`${rig.base}/api/state?detail=1`, { headers: { 'X-Stagewatch-Pin': rig.pin } })).json();
    assert.deepEqual(frame.desk.next, {
      id: odd.json.id, from: 'AGMessage', target: 'PlayMap Hall', line: 'UNDO', risk: 'review', collects: true, cut: false, fromId: 'ag-778899', targetId: 'pm-a1b2c3',
    });
    assert.ok(!JSON.stringify(frame).includes('Operator at FOH'), 'the name a proposal gives itself is in no frame');
    await page.click('#deskReject');
    await idle();
    const journalRows = () => fs.readFileSync(rig.journal, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l).x);
    const oddTag = odd.json.id.split('-')[0];
    await until(() => journalRows().includes(`Rejected ${oddTag} (wire): AGMessage → PlayMap Hall: UNDO`), 5000, 'the journal row of the rejection');
    assert.ok(journalRows().includes(`Proposed ${oddTag}: AGMessage → PlayMap Hall: UNDO`), 'the journal names both ends as the frame does');
    assert.ok(!fs.readFileSync(rig.journal, 'utf8').includes('Operator at FOH'), 'the name a proposal gives itself is in no journal row');

    // A line longer than the frame carries: the frame says it is cut, and the page reads the whole of it before Approve is live.
    const long = 'SET out1 name ' + 'a'.repeat(150) + ' AND THEN THE PART NOBODY SAW';
    const whole = (await propose(agmessage, playmap, long)).json;
    await shows(whole.id);
    const cut = (await (await fetch(`${rig.base}/api/state?detail=1`, { headers: { 'X-Stagewatch-Pin': rig.pin } })).json()).desk.next;
    assert.equal(cut.cut, true);
    assert.equal(cut.line, long.slice(0, 120));
    await page.waitForFunction((x) => document.getElementById('deskLine').textContent === x, long, { timeout: 8000 });
    await page.click('#deskApprove');
    await until(() => playmap.ran.some((r) => r.id === whole.id), 5000, 'PlayMap running the long line the page showed whole');
    assert.equal(playmap.ran.find((r) => r.id === whole.id).line, long);
    await idle();

    // When the whole line can't be read, Approve stays greyed and says why; the part shown ends with its mark; Reject still works.
    await page.route('**/api/proposals/**', (route) => route.abort());
    const unread = (await propose(agmessage, playmap, long)).json;
    await shows(unread.id);
    await page.waitForFunction(() => (document.getElementById('deskWhy') || {}).textContent === 'This line is longer than shown and could not be read here — read it at the desk',
      null, { timeout: 8000 });
    assert.equal(await page.textContent('#deskLine'), long.slice(0, 120) + '…');
    assert.equal(await page.isDisabled('#deskApprove'), true);
    await page.click('#deskReject');
    await idle();
    assert.equal(await stateOf(unread.id), 'rejected');
    assert.equal(playmap.ran.some((r) => r.id === unread.id), false);
    await page.unroute('**/api/proposals/**');
    assert.deepEqual(errors, []);
  } finally {
    await browser.close();
  }
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
