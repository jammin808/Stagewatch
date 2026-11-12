// The interop lane on its LAN topology: four real apps, each in a network namespace of its own on one bridge, at its own
// address (Stagewatch's rig 10.99.0.2, Patterns' bench 10.99.0.3, PlayMap's node 10.99.0.4, AGMessage 10.99.0.5) with its
// own loopback and every app on its default ports; the driver is the switch (10.99.0.1) and never listens on the Show Bus
// port (it says hellos only in S1.6's control: an id of its own, from a port of its own, to PlayMap alone). The rig is on the
// show network as the app runs on a show laptop: on the Show Bus port, serving its certificate beside plain HTTP, and making
// three node keys: one for Patterns and one for PlayMap, each handed over in that app's own settings, and one handed to
// nobody, which row S2.7 tries against an id that has a key of its own and row S2.1b gives PlayMap (LINK STAGEWATCH, said
// as its operator says it at PlayMap's own machine: on its wire at 127.0.0.1, from inside its namespace, so no key crosses
// this LAN) before giving it its own key again. The rows read PlayMap's link with the rig's certificate pinned and Patterns'
// link carried over TLS with its key; that neither app ever sends its key in clear off its own machine is held by that app's
// own tests (Patterns' Round 89.5, PlayMap's PM-2), not read here; the rig's verdict on a node link is read from both ends,
// the app's link and the rig's own record of it (its `nodes` line). AGMessage is
// given none (its keyed rows are another unit's). The rig is the room's approval desk and its one verdict, as the app is: the
// desk rows send a line across it both ways (the bench's assistant proposing a look that the operator approves there, the
// rig's own line run at Patterns), deciding over HTTP with the operator's PIN; the doors rows read the verdict on its route,
// the operator frame's `network` and the rig's Companion door, judged by the Companion module's own rule (lib/companion.cjs),
// with PlayMap configured by the node key bound to it and AGMessage only seen. The rows run in order, each a node:test test
// that writes its matrix row; S0.1 starts the apps (the rig first, which the others link to) and every later row reads the
// apps it started; the rows that stall the rig or take an app down (and back) come after the others, S7.4 (no address ever
// counted) last but one, and S7.1 (no secret anywhere) last, as it stops the apps to sweep what they left (the rig's
// certificate's password among the secrets it looks for). After every row both of the rig's lockout
// tables are read (wrong PINs; wrong view passes: window addresses, viewing codes, crew links), and no address of this LAN is
// counted in either: Patterns and PlayMap speak with the node keys the rig made for them, AGMessage with none (a request with
// none is not a guess), and a node key refused for another node's id is not a guess either. Each table's reader is shown to
// see this LAN's addresses by a control from the switch: the PIN's in S0.1 (a wrong PIN counted, the right one clearing it),
// the view passes' in S7.1, after the last read (a wrong window address counted; only the end of its minute clears it). When
// the rows end the apps are stopped, the files each keeps a secret in by design are removed, and the matrix is written,
// whatever happened.
//
// Each deadline is the contract's number × 1.5 + 2 s (Show Bus §2 and §4: a hello every 2 s → 5 s; stale after 6 s → 11 s;
// gone after 12 s → 20 s; Stagewatch's show thread stuck after 3 s plus a beat every 2 s → 9.5 s; a frame again within a
// beat after it turns over → 5 s), counted from the event as the app that caused it says it happened where it says so (the
// rig's own times for a stall), else from the driver's act; a hello from the moment the last app said it was ready, and a row
// that only gets to read after its deadline has passed fails rather than claim it.
'use strict';
const { test, before, after } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const { Run, hex } = require('./lib/run.cjs');
const { Wire } = require('./lib/wire.cjs');
const { Lan } = require('./lib/lan.cjs');
const { Matrix } = require('./lib/matrix.cjs');
const { until, fatal, sleep } = require('./lib/inbox.cjs');
const { Stagewatch, NODE_ID, BUS_PORT } = require('./apps/stagewatch.cjs');
const { Patterns } = require('./apps/patterns.cjs');
const { PlayMap } = require('./apps/playmap.cjs');
const { AGMessage } = require('./apps/agmessage.cjs');
const S = require('./lib/scenarios.cjs');

/** The hosts in address order: .2, .3, .4, .5. */
const HOSTS = ['stagewatch', 'patterns', 'playmap', 'agmessage'];
/** S0.1's deadline: each app is ready within 60 s of its start, a cold .NET start on a CI runner included. */
const READY_MS = 60000;
/** A hello every 2 s: heard within 5 s. A link's step (a clip, a blackout) answers within the same. */
const HELLO_MS = 5000;
/** Stale after 6 s of silence: said within 11 s. */
const STALE_MS = 11000;
/** Gone after 12 s, and back after a restart or a cable: within 20 s. */
const GONE_MS = 20000;
/** Stuck 3 s after the show thread's last turn, said on the next 2 s beat: within 9.5 s of the rig's own stall time. */
const STUCK_MS = 9500;
/** Frames again within a beat (2 s) of the show thread turning over: within 5 s of the rig's own time. */
const RESUME_MS = 5000;
/** A context posted or read every 5 s: in the other app's reader within 9.5 s of the change. */
const CONTEXT_MS = 9500;
/** S1.6 watches each reader this long: a hello every 2 s, ten of them. */
const ONE_PEER_MS = 20000;
/** The rig builds its context once a second (every fourth 250 ms tick): one built after a moment is read within this. */
const CONTEXT_BUILT_MS = 3000;
/** S2.1b watches PlayMap's link carry a key the rig refuses this long: past its next context post (one every 5 s), with 2 s more. */
const REFUSED_WATCH_MS = 7000;
/** §14: a context is 64 KiB at most, as the wire carries it. */
const CONTEXT_BYTES = 64 * 1024;
/** The rig's node keys: Patterns', PlayMap's, and one handed to nobody (S2.7; S2.1b gives it to PlayMap and takes it back). */
const RIG_KEYS = 3;
/** A stall long enough that the stuck beat is certain before it ends: StuckAfter + 2 × HeartbeatEvery + 5 s. */
const STALL_MS = 12000;
/** How far PlayMap's time may be from the rig's own frame read just after it: a frame's age and the reads between them. */
const TIME_SLACK_S = 2;
/**
 * A line across the desk (rows S3.3, S3.4): an app collects every 2 s and the desk's receipt waits 3 s for what the app made of
 * it, beside the proposal's own round trip; the row's whole deadline, from the driver's act to the rig's desk saying done.
 */
const DESK_MS = 25000;
/** A verdict older than 3 s is never served (the stuck answer is): stuck said within 3 s × 1.5 + 2 s of the rig's own stall time. */
const DOORS_STUCK_MS = 6500;
/** The doors' verdict follows a node's lights once a second: a change read within a hello's deadline of the event behind it. */
const DOORS_MS = HELLO_MS;
/**
 * Before a doors row acts, the room is ready again: a key refused for a node's id (S2.7's forgery) is kept 30 s
 * (PeerContexts.KeepFor) and lights that node's key row red while nothing of its own is taken, so a row that starts within
 * that time of it waits this long for the room it starts from. A precondition, not a deadline: nothing is measured by it.
 */
const ROOM_READY_MS = 30000 + DOORS_MS;

let run;
let lan;
let matrix;
const apps = {};

before(async () => {
  run = new Run('lan');
  matrix = new Matrix(run, 'lan');
  lan = await Lan.start(run, 'lan', HOSTS);
});

after(async () => {
  const exits = {};
  const removed = [];
  const failed = [];
  // An app whose stop or secret files' removal throws is recorded in exits and said below; the other apps still stop, and
  // each app's temporary folder still goes and the matrix is still written, whatever any of them did.
  for (const [name, app] of Object.entries(apps).reverse()) {
    try {
      exits[name] = await app.stop();
      removed.push(...run.removeSecretFiles(app.secretFiles()));
    } catch (e) {
      exits[name] = { ...exits[name], failed: e.code || e.message };
      failed.push(`${name}: ${e.code || e.message}`);
    }
  }
  try {
    const tempLeft = run ? run.removeTempFolders() : [];
    if (matrix) {
      matrix.measure('exits', exits);
      matrix.measure('tempFoldersLeft', tempLeft);
      matrix.removed(removed);
      matrix.write();
    }
  } finally {
    if (lan) await lan.close();
  }
  if (failed.length) throw new Error(`an app's end failed (${failed.join('; ')}); the matrix's exits name it`);
});

/** A row: the matrix records it when it runs (after `before`), and the test fails when the row does. */
const row = (spec, fn) => () => matrix.row(spec, fn)();

/** Every address of this LAN: the switch's and each app's. None is ever counted in the rig's lockout tables. */
const lanAddresses = () => [lan.map.switch, ...HOSTS.map((n) => lan.host(n).addr)];

/** The rig's two lockout tables read after a row: no address of this LAN counted in either, written in the row. */
const lockoutAfter = (r) => S.lockoutAfter(apps.stagewatch, r, lanAddresses());

/** Each app's id as it says it on the show network: the bench's from its ready line, PlayMap's from its settings,
 *  AGMessage's from its own context. */
const ids = {};

/** When the last app to start (AGMessage) said it was ready: from then every app has been saying its hello. */
let readyAt = 0;

/** Every app's view of its Stagewatch link (lib/scenarios.cjs). */
const stagewatchLinks = () => S.stagewatchLinks(apps);
const { allLive } = S;

/** The rig's three node keys as the lane hands them out: Patterns', PlayMap's, and the free one, handed to nobody. */
const rigKeys = () => ({ patterns: apps.stagewatch.keyFor('patterns'), playmap: apps.stagewatch.keyFor('playmap'), free: apps.stagewatch.keyFor('nobody') });

/**
 * What the rig's --state holds of each of its keys now (the node each speaks for and that node's kind, ids and names, never
 * a secret), which must be as the lane handed them out: Patterns' and PlayMap's each bound to its own node, the free key to
 * none. Returns what was read; `when` says when, in the words of a failure.
 */
function boundAsHanded(when) {
  const read = Object.fromEntries(Object.entries(rigKeys()).map(([who, k]) => {
    const saved = apps.stagewatch.savedKeys().find((x) => x.id === k.id);
    return [who, saved ? { node: saved.node, nodeKind: saved.nodeKind } : null];
  }));
  const handed = { patterns: { node: ids.patterns, nodeKind: 'patterns' }, playmap: { node: ids.playmap, nodeKind: 'playmap' }, free: { node: null, nodeKind: null } };
  if (JSON.stringify(read) !== JSON.stringify(handed)) throw new Error(`the keys' bindings ${when} are ${JSON.stringify(read)}, not ${JSON.stringify(handed)}`);
  return read;
}

test('S0.1 each app is ready in its own namespace', row({
  id: 'S0.1',
  title: 'each app is ready in its own namespace',
  apps: HOSTS,
  deadlineMs: READY_MS,
}, async (r) => {
  r.evidence.push(...HOSTS.flatMap((n) => [run.log(`${n}.out`), run.log(`${n}.err`)]), run.log('lan.out'));
  const sw = new Stagewatch(run, lan.host('stagewatch'), { keys: RIG_KEYS });
  apps.stagewatch = sw;
  await sw.start(READY_MS);
  sw.checkReady();
  await sw.reachable();
  matrix.said('stagewatch', `rig ${sw.ready.node}`);

  const pat = new Patterns(run, lan.host('patterns'));
  apps.patterns = pat;
  await pat.start(sw.address, READY_MS, sw.keyFor('patterns'));
  pat.checkReady();
  await pat.reachable();
  matrix.said('patterns', `bench ${pat.ready.build}, node ${pat.ready.node}`);
  ids.patterns = pat.ready.node;

  const pm = new PlayMap(run, lan.host('playmap'));
  apps.playmap = pm;
  r.evidence.push(pm.engineLog);
  await pm.start({ addr: pat.wire.addr, port: pat.wire.port, token: pat.token }, sw.address, READY_MS, sw.keyFor('playmap'));
  pm.checkReady();
  await pm.reachable();
  matrix.said('playmap', `node ${pm.nodeId}, renderer ${pm.up.renderer}`);
  ids.playmap = pm.nodeId;

  const agm = new AGMessage(run, lan.host('agmessage'));
  apps.agmessage = agm;
  await agm.start(sw.address, READY_MS);
  readyAt = Date.now();
  agm.checkReady();
  matrix.said('agmessage', `build ${agm.health.build}`);

  // Each app's own process is in its host's namespace, and no two of the four, nor the switch, share one.
  const namespaces = {};
  for (const name of HOSTS) {
    const own = apps[name].proc.namespace();
    const host = fs.readlinkSync(lan.host(name).ns);
    if (own !== host) throw new Error(`${name} runs in ${own}, not its host's ${host}`);
    namespaces[name] = own;
  }
  const all = new Set([...Object.values(namespaces), fs.readlinkSync('/proc/self/ns/net')]);
  if (all.size !== HOSTS.length + 1) throw new Error(`the apps and the switch share a namespace: ${JSON.stringify(namespaces)}`);

  // Ready means still running: none has ended since it said so, and each said so inside its deadline.
  const readyMs = {};
  for (const name of HOSTS) {
    const app = apps[name];
    if (!app.proc.running) throw new Error(`${name} ended after it was ready: ${JSON.stringify(app.proc.exit)}`);
    if (app.readyMs > READY_MS) throw new Error(`${name} was ready after ${app.readyMs} ms, past ${READY_MS}`);
    readyMs[name] = app.readyMs;
  }
  r.facts = { readyMs, addresses: Object.fromEntries(HOSTS.map((n) => [n, lan.host(n).addr])), namespaces };
  matrix.measure('readyMs', readyMs);
  // AGMessage's id on the show network, as its own context says it, and its context token, learned for the sweep.
  ids.agmessage = (await agm.context()).node.id;
  r.facts.agmessageContextToken = await agm.learnContextToken();
  if (r.facts.agmessageContextToken.node !== ids.agmessage) throw new Error(`AGMessage's context read with its token is ${r.facts.agmessageContextToken.node}'s`);
  await lockoutAfter(r);
  // The control that keeps that reading honest: the PIN's reader sees a failure from this LAN's switch, and the right PIN
  // clears it.
  r.facts.lockoutReader = await sw.proveLockoutReader(lan.map.switch);
}));

test('S1.1 Patterns hears PlayMap and AGMessage', row({
  id: 'S1.1',
  title: 'Patterns hears PlayMap and AGMessage, each from its own address, within 5 s of the last app ready',
  apps: ['patterns', 'playmap', 'agmessage'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('patterns.wire'));
  const read = await S.patternsHears(apps.patterns, [
    { kind: 'playmap', id: ids.playmap, addr: lan.host('playmap').addr },
    { kind: 'messenger', id: ids.agmessage, addr: lan.host('agmessage').addr },
  ], S.left(readyAt, HELLO_MS));
  r.facts = { sinceReadyMs: S.inTime(readyAt, HELLO_MS, 'Patterns hearing PlayMap and AGMessage'), ...read };
  await lockoutAfter(r);
}));

test('S1.2 PlayMap hears Patterns and AGMessage', row({
  id: 'S1.2',
  title: 'PlayMap hears Patterns and AGMessage, each from its own address where its NODES says one, within 5 s of the last app ready',
  apps: ['playmap', 'patterns', 'agmessage'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('playmap.wire'));
  const read = await S.playmapHears(apps.playmap, [
    { t: 'patterns', id: ids.patterns, addr: lan.host('patterns').addr, followed: true },
    { t: 'messenger', id: ids.agmessage, addr: lan.host('agmessage').addr },
  ], S.left(readyAt, HELLO_MS));
  r.facts = { sinceReadyMs: S.inTime(readyAt, HELLO_MS, 'PlayMap hearing Patterns and AGMessage'), ...read };
  await lockoutAfter(r);
}));

test('S1.3 AGMessage hears Patterns', row({
  id: 'S1.3',
  title: 'AGMessage hears Patterns from its own address, within 5 s of the last app ready',
  apps: ['agmessage', 'patterns'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('agmessage-admin.out'));
  const read = await S.agmessageHears(apps.agmessage, [{ t: 'patterns', id: ids.patterns, addr: lan.host('patterns').addr }], S.left(readyAt, HELLO_MS));
  r.facts = { sinceReadyMs: S.inTime(readyAt, HELLO_MS, 'AGMessage hearing Patterns'), ...read };
  await lockoutAfter(r);
}));

test('S1.4 the rig hears all three', row({
  id: 'S1.4',
  title: "the rig hears Patterns, PlayMap and AGMessage on the Show Bus: its hello table (hellos alone) lists each from its own address, live and unmarked, and its context's peers each live (AGMessage's row its hello's alone), within 5 s of the last app ready",
  apps: ['stagewatch', 'patterns', 'playmap', 'agmessage'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('stagewatch.err'));
  const read = await S.rigHears(apps.stagewatch, [
    { t: 'patterns', id: ids.patterns, addr: lan.host('patterns').addr },
    { t: 'playmap', id: ids.playmap, addr: lan.host('playmap').addr },
    { t: 'messenger', id: ids.agmessage, addr: lan.host('agmessage').addr, helloOnly: true },
  ], S.left(readyAt, HELLO_MS));
  r.facts = { sinceReadyMs: S.inTime(readyAt, HELLO_MS, 'the rig hearing all three'), ...read };
  await lockoutAfter(r);
}));

test('S1.5 every app hears the rig', row({
  id: 'S1.5',
  title: "every app hears the rig: kind main, its --node-id, from the rig's own address where the reader says one, within 5 s of the last app ready",
  apps: HOSTS,
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('patterns.wire'), run.log('playmap.wire'), run.log('agmessage-admin.out'));
  const addr = lan.host('stagewatch').addr;
  const patterns = await S.patternsHears(apps.patterns, [{ kind: 'main', id: NODE_ID, addr }], S.left(readyAt, HELLO_MS));
  const playmap = await S.playmapHears(apps.playmap, [{ t: 'main', id: NODE_ID, addr }], S.left(readyAt, HELLO_MS));
  const agmessage = await S.agmessageHears(apps.agmessage, [{ t: 'main', id: NODE_ID, addr }], S.left(readyAt, HELLO_MS));
  r.facts = { sinceReadyMs: S.inTime(readyAt, HELLO_MS, 'every app hearing the rig'), patterns, playmap, agmessage };
  await lockoutAfter(r);
}));

test('S1.6 one id is one peer', row({
  id: 'S1.6',
  title: "one id is one peer: read every 500 ms for 20 s, the rig (its hello table and its context's peers), Patterns and PlayMap (its NODES, its STATE's peers and its context's) each list every show app they hear once, by its id, and never twice; PlayMap reads the rig's 1.3 hello as live and unmarked at the rig's one address at every read, and marks an id two senders say in turn (the control)",
  apps: ['stagewatch', 'patterns', 'playmap', 'agmessage'],
  // A window watched, not a deadline: its length is a fact of the row.
  deadlineMs: null,
}, async (r) => {
  r.evidence.push(run.log('patterns.wire'), run.log('playmap.wire'));
  const rig = { id: NODE_ID, addr: lan.host('stagewatch').addr };
  const readers = {
    'the rig (its hello table)': {
      read: async () => (await apps.stagewatch.heard()).peers.map((p) => p.id),
      expect: [ids.patterns, ids.playmap, ids.agmessage],
    },
    'the rig (its context peers)': {
      read: async () => ((await apps.stagewatch.context()).peers || []).map((p) => p.id),
      expect: [ids.patterns, ids.playmap, ids.agmessage],
    },
    "Patterns (STATE's bus.apps)": {
      read: async () => (((await apps.patterns.state()).bus || {}).apps || []).map((a) => a.id),
      expect: [NODE_ID, ids.playmap, ids.agmessage],
    },
    'PlayMap (NODES)': {
      read: async () => (await apps.playmap.nodes()).map((n) => n.id),
      expect: [NODE_ID, ids.patterns, ids.agmessage],
    },
    // Its reader of Show Bus 1.3's hello: the rig's row live and unmarked, at the one address it says its hello from, at every read.
    "PlayMap (STATE's peers)": {
      read: async () => {
        const peers = (await apps.playmap.state()).peers || [];
        const wrong = S.playmapUnmarked(peers, rig);
        if (wrong) throw wrong;
        return peers.map((p) => p.id);
      },
      expect: [NODE_ID, ids.patterns, ids.agmessage],
    },
    // Its context lists no id it marks in conflict (§6), so the rig listed there is the rig unmarked as well.
    "PlayMap (its context's peers)": {
      read: async () => ((await apps.playmap.context()).json.peers || []).map((p) => p.id),
      expect: [NODE_ID, ids.patterns, ids.agmessage],
    },
  };
  r.facts = { watchMs: ONE_PEER_MS, ...(await S.oneIdOnePeer(readers, ONE_PEER_MS)) };
  r.facts.playmapRig = (await apps.playmap.state()).peers.find((p) => p.id === NODE_ID);
  // The control's id stays in PlayMap's hello table until it is forgotten (60 s after its last hello), past the rows that
  // follow; its kind is messenger, AGMessage's, so a later row picks PlayMap's peers by id, never by kind.
  r.facts.conflictControl = await S.playmapMarksConflict(apps.playmap,
    { at: lan.host('playmap').addr, port: BUS_PORT, from: lan.map.switch, rig }, HELLO_MS);
  await lockoutAfter(r);
}));

test('S4.5 PlayMap follows Patterns, paired by proof', row({
  id: 'S4.5',
  title: "PlayMap follows Patterns' clip and blackout, paired by proof",
  apps: ['playmap', 'patterns'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('playmap.wire'), run.log('patterns.wire'), apps.playmap.engineLog);
  r.facts = await S.playmapFollowsPatterns(apps.playmap, apps.patterns, `${lan.host('patterns').addr}:${apps.patterns.wire.port}`, HELLO_MS);
  await lockoutAfter(r);
}));

/**
 * A proposal on the rig's desk, read as the operator reads it, once it is `state` (null: any); waits within ms. Its fields are
 * the desk's own: id, from, target, line, risk, state, decidedBy.
 */
async function deskHas(pred, ms, what) {
  return until(async () => (await apps.stagewatch.proposals()).find(pred) || null, ms, what);
}

/** The rig's desk says done for proposal `id`, in its own record (GET /api/proposals/<id>), within ms. */
function deskDone(id, ms, what) {
  return until(async () => {
    const p = await apps.stagewatch.proposal(id);
    if (['failed', 'rejected', 'undelivered', 'expired'].includes(p.state)) throw fatal(`the rig's desk says ${p.state} for ${id}: ${JSON.stringify(p.result || p.words || '')}`);
    return p.state === 'done' ? p : null;
  }, ms, what);
}

test('S3.4 the rig → Patterns', row({
  id: 'S3.4',
  title: "the rig's desk sends Patterns VIDEO STOP with a synthetic clip on its air (peer patterns, with the operator's PIN): the clip is held at its top, the rig rates it high by Patterns' table, and the rig's desk says done in Patterns' words (ran VIDEO STOP: OK)",
  apps: ['stagewatch', 'patterns'],
  deadlineMs: DESK_MS,
}, async (r) => {
  r.evidence.push(run.log('patterns.out'), run.log('patterns.wire'), run.log('stagewatch.out'));
  const pat = apps.patterns;
  const sw = apps.stagewatch;
  const clip = await pat.clip('stop.mp4', 60);
  const playing = await until(async () => {
    const v = (await pat.state()).video;
    return v && v.file === 'stop.mp4' && v.playing === true && v.paused === false && v.positionMs > 0 ? v : null;
  }, HELLO_MS, "Patterns' clip playing on its air");
  const sentAt = Date.now();
  const sent = await sw.api(['peer', 'patterns', 'VIDEO STOP'], 'interop-s3-4');
  if (sent.status !== 200 || !sent.json || sent.json.ok !== true) throw new Error(`peer patterns VIDEO STOP answered ${sent.status} ${sent.text.slice(0, 200)}`);
  const id = sent.json.refs && sent.json.refs.sharedProposal;
  if (!id) throw new Error(`the receipt names no proposal: ${sent.text.slice(0, 200)}`);
  // Patterns' own STATE: the clip on air held at its top (VIDEO STOP is the clip's stop and nothing else).
  const stopped = await until(async () => {
    const v = (await pat.state()).video;
    if (!v || v.file !== 'stop.mp4') throw new Error(`Patterns' air has ${JSON.stringify(v && v.file)}, not stop.mp4`);
    return v.paused === true && v.playing === false && v.positionMs === 0 ? v : null;
  }, S.left(sentAt, DESK_MS), "Patterns' clip held at its top");
  const stoppedMs = S.since(sentAt);
  const done = await deskDone(id, S.left(sentAt, DESK_MS), "the rig's desk saying done for VIDEO STOP");
  const doneMs = S.inTime(sentAt, DESK_MS, "the rig's desk saying done for VIDEO STOP");
  if (done.line !== 'VIDEO STOP' || done.target.id !== ids.patterns) throw new Error(`the done proposal is ${JSON.stringify({ line: done.line, target: done.target })}`);
  if (done.from.t !== 'main' || done.from.id !== NODE_ID) throw new Error(`the proposal is from ${JSON.stringify(done.from)}, not the rig's own desk`);
  // Patterns' own words and its own table: its VIDEO STOP row is high, and the receipt carries what Patterns answered.
  if (done.risk !== 'high') throw new Error(`the rig rates VIDEO STOP ${done.risk}, not high as Patterns' table says`);
  if (!/ran VIDEO STOP: OK/.test(sent.json.effect || '')) throw new Error(`the receipt's effect is "${sent.json.effect}", not Patterns' OK to VIDEO STOP`);
  r.facts = {
    clip: { file: path.basename(clip.key), lengthMs: clip.lengthMs, playingAtMs: playing.positionMs },
    receipt: { status: sent.status, stage: sent.json.stage, effect: sent.json.effect, proposal: id },
    stopped: { paused: stopped.paused, playing: stopped.playing, positionMs: stopped.positionMs, ms: stoppedMs },
    desk: { state: done.state, risk: done.risk, form: done.form, decidedBy: done.decidedBy, ms: doneMs },
  };
  await lockoutAfter(r);
}));

test('S3.3 Patterns → the rig → Patterns', row({
  id: 'S3.3',
  title: "Patterns' assistant proposes a look to the rig's desk (the bench's propose look): pending there at review as the bench's own APPLY <id> naming the look, approved by the operator (desk approve <id>, with the PIN), Patterns applies it once, opening EDIT SAFE with the air's look unchanged, its chip says applied once, and the rig's desk says done",
  apps: ['patterns', 'stagewatch'],
  deadlineMs: DESK_MS,
}, async (r) => {
  r.evidence.push(run.log('patterns.out'), run.log('patterns.wire'), run.log('stagewatch.out'));
  const pat = apps.patterns;
  const sw = apps.stagewatch;
  const before = await pat.state();
  const title = `Interop look ${hex(3)}`;
  if ((before.looks || []).some((l) => l.name === title)) throw new Error(`Patterns already has a look called ${title}`);
  // EDIT SAFE closed before the proposal, so an open one after it is the apply's.
  if (before.editSafe !== false) throw new Error(`Patterns' EDIT SAFE is ${before.editSafe} before the proposal, not closed`);
  const from = pat.proc.out.items.length;
  const proposedAt = Date.now();
  const said = await pat.control(`propose look ${title}`, (j) => j.control === 'propose');
  if (!said.ok) throw new Error(`the bench refused the proposal: ${said.error}`);
  // Pending on the rig's desk, from Patterns' own id, rated by Patterns' own verified table.
  // The bench's own proposal, by the id and title in the line Patterns sends (APPLY <local id>: Look "<title>").
  const pending = await deskHas((p) => p.from && p.from.id === ids.patterns && p.state === 'pending'
    && typeof p.line === 'string' && p.line.startsWith(`APPLY ${said.id}:`), S.left(proposedAt, DESK_MS),
    `Patterns' proposal APPLY ${said.id} pending on the rig's desk`);
  if (!pending.line.includes(`"${title}"`)) throw new Error(`Patterns' proposal "${pending.line}" does not name the look ${title}`);
  if (pending.risk !== 'review') throw new Error(`the rig rates Patterns' proposal ${pending.risk} ("${pending.line}"), not review`);
  if (pending.target.id !== ids.patterns) throw new Error(`Patterns' proposal is for ${JSON.stringify(pending.target)}, not Patterns itself`);
  const decided = await sw.api(['desk', 'approve', pending.id], 'interop-s3-3');
  if (decided.status !== 200 || !decided.json || decided.json.ok !== true) throw new Error(`desk approve ${pending.id} answered ${decided.status} ${decided.text.slice(0, 200)}`);
  // Patterns' own STATE: the look is there, in the preview of an open EDIT SAFE; the air's look is the one it was.
  const applied = await until(async () => {
    const st = await pat.state();
    return (st.looks || []).some((l) => l.name === title) ? st : null;
  }, S.left(proposedAt, DESK_MS), `Patterns' look "${title}"`);
  if (applied.editSafe !== true) throw new Error(`Patterns applied the look with EDIT SAFE ${applied.editSafe}: it reached the air`);
  if (applied.airLook !== before.airLook) throw new Error(`the air's look moved from "${before.airLook}" to "${applied.airLook}"`);
  // The assistant's chip says what the desk made of it, as the bench hears it.
  const parsed = (l) => { try { return JSON.parse(l.line); } catch { return null; } };
  const chip = await pat.proc.out.wait((l) => { const j = parsed(l); return j && j.id === said.id && /^applied/.test(j.said || ''); },
    S.left(proposedAt, DESK_MS), "the assistant's chip saying applied", from);
  const done = await deskDone(pending.id, S.left(proposedAt, DESK_MS), "the rig's desk saying done for Patterns' look");
  const ms = S.inTime(proposedAt, DESK_MS, "Patterns' look proposed, approved, applied and done");
  // Applied once: one look by the title, one chip line saying applied for the bench's id.
  const after = await pat.state();
  const named = (after.looks || []).filter((l) => l.name === title).length;
  if (named !== 1) throw new Error(`Patterns holds ${named} looks called ${title}, not one`);
  const appliedChips = pat.proc.out.items.slice(from).filter((l) => { const j = parsed(l); return j && j.id === said.id && /^applied/.test(j.said || ''); }).length;
  if (appliedChips !== 1) throw new Error(`the assistant's chip said applied ${appliedChips} times for ${said.id}, not once`);
  r.facts = {
    proposal: { id: pending.id, line: pending.line, risk: pending.risk, from: pending.from.id },
    decided: { status: decided.status, stage: decided.json.stage, effect: decided.json.effect },
    patterns: {
      look: title, editSafe: applied.editSafe, previewLook: applied.previewLook,
      airLook: { before: before.airLook, after: applied.airLook }, editSafeBefore: before.editSafe, looksNamed: named,
    },
    chip: parsed(chip).said, benchId: said.id, appliedChips,
    desk: { state: done.state, decidedBy: done.decidedBy },
    ms,
  };
  await lockoutAfter(r);
}));

test('S7.3 unknown words are refused everywhere', row({
  id: 'S7.3',
  title: 'unknown words are refused by Patterns, PlayMap and the rig',
  apps: ['patterns', 'playmap', 'stagewatch'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.facts = await S.unknownWordsRefused(apps.patterns, apps.playmap, apps.stagewatch);
  // The desk's word: a line an app's verified verbs do not list is refused there with the words, and becomes no proposal, so
  // nothing reaches the app (AGMessage's leg waits for its keyed rows).
  const desk = {};
  for (const app of ['patterns', 'playmap']) {
    const sent = await apps.stagewatch.api(['peer', app, 'FROBNICATE'], `interop-s7-3-${app}`);
    if (sent.status !== 409 || !sent.json || !/does not list 'FROBNICATE'/.test(sent.json.error || '')) {
      throw new Error(`peer ${app} FROBNICATE answered ${sent.status} ${sent.text.slice(0, 200)}`);
    }
    desk[app] = `${sent.status} ${sent.json.error}`;
  }
  const queued = (await apps.stagewatch.proposals()).filter((p) => /FROBNICATE/i.test(p.line || ''));
  if (queued.length) throw new Error(`the rig's desk holds ${queued.length} proposal(s) of FROBNICATE`);
  r.facts.desk = desk;
  await lockoutAfter(r);
}));

/**
 * A change of an app's own context reaches the rig's context, there and back (rows S2.1, S2.3): before the change the rig's row
 * for `id` says the app's own light and headline, proved by its own key on its node link; `on()` changes the app's own context,
 * and from the moment the app's own context says something new, the rig's row says it within 9.5 s; `off()` puts it back, and
 * the rig follows within the same. `off()` runs whatever happened once `on()` has, so the rows after find the room as it was.
 * Resolves with what was read and how long each took.
 */
async function contextReachesRig(id, own, on, off, what) {
  const sw = apps.stagewatch;
  const before = await S.rigShowsContext(sw, id, own, CONTEXT_MS, what);
  let undone = true;
  const step = async (act, words) => {
    const from = (await own()).headline;
    await act();
    const changed = await until(async () => { const c = await own(); return c.headline !== from ? c : null; }, HELLO_MS, `${what}'s own context ${words}`);
    const at = Date.now();
    const shown = await S.rigShowsContext(sw, id, own, S.left(at, CONTEXT_MS), what);
    if (shown.rig.headline === from) throw new Error(`the rig still says "${from}"`);
    const ms = S.since(at);
    if (ms > CONTEXT_MS) throw new Error(`the rig said ${what}'s new context ${ms} ms after ${what} did, past ${CONTEXT_MS}`);
    return { own: { light: changed.light, headline: changed.headline }, rig: shown.rig, ms };
  };
  try {
    undone = false;
    const changed = await step(on, 'changed');
    undone = true;
    const back = await step(off, 'back');
    return { before, changed, back };
  } finally {
    // Said, never thrown over the row's own failure.
    if (!undone) await off().catch((e) => console.error(`putting ${what}'s context back: ${e.message}`));
  }
}

/** A certificate's fingerprint as the apps show it to a person: its first sixteen hex digits in fours. */
const shortFingerprint = (fp) => fp.slice('sha256:'.length, 'sha256:'.length + 16).match(/.{4}/g).join(' ');

/** Patterns' blackout, on or off, said on its wire: it answers OK. */
const blackout = (state) => async () => {
  const said = await apps.patterns.say(`BLACKOUT ${state}`);
  if (said !== 'OK') throw new Error(`Patterns answered BLACKOUT ${state} with ${JSON.stringify(said)}`);
};

/**
 * A deck that pairs with Patterns and leaves (S2.3's change of Patterns' own context): a wire connection of its own from the
 * switch, paired by proof under a name of its own, is a deck Patterns lists among its own things; closing it takes it away.
 */
function deck() {
  let conn = null;
  return {
    on: async () => {
      conn = await Wire.connect(lan.host('patterns').addr, apps.patterns.wire.port, { capture: run.log('patterns.deck.wire') });
      await conn.pair('InteropDeck', apps.patterns.token);
    },
    off: async () => { if (conn) conn.close(); conn = null; },
  };
}

test("S2.1 PlayMap's context reaches the rig", row({
  id: 'S2.1',
  title: "PlayMap's context reaches the rig: PlayMap's link to it live and paired, the rig's certificate pinned; the rig's row for PlayMap, proved by PlayMap's own key on its node link, says PlayMap's own light and headline, and a change of them (Patterns' blackout, which PlayMap follows) within 9.5 s, there and back",
  apps: ['playmap', 'stagewatch', 'patterns'],
  deadlineMs: [CONTEXT_MS, CONTEXT_MS],
}, async (r) => {
  r.evidence.push(run.log('playmap.wire'), apps.playmap.engineLog);
  const pm = apps.playmap;
  // Its key goes to the rig only over TLS (PlayMap sends none in clear off its machine), the certificate it pinned is the one
  // the rig serves, and the rig's answer for its key pairs the link.
  const pinned = `certificate is the one pinned (${shortFingerprint(apps.stagewatch.tls)})`;
  const link = await until(async () => {
    const l = (await pm.state()).links.stagewatch;
    if (l.state !== 'live' || l.paired !== true) throw new Error(`PlayMap's link to the rig is ${l.state}, paired ${l.paired}: ${l.detail}`);
    if (!(l.detail || '').includes(pinned)) throw new Error(`PlayMap's link to the rig does not say the rig's certificate is pinned: ${l.detail}`);
    return l;
  }, HELLO_MS, "PlayMap's link to the rig live and paired, the rig's certificate pinned");
  const own = async () => { const c = (await pm.context()).json; return { light: c.light, headline: c.headline }; };
  r.facts = { link: { state: link.state, host: link.host, paired: link.paired, detail: link.detail },
    ...(await contextReachesRig(ids.playmap, own, blackout('ON'), blackout('OFF'), 'PlayMap')) };
  await lockoutAfter(r);
}));

test("S2.3 Patterns' context reaches the rig", row({
  id: 'S2.3',
  title: "Patterns' context reaches the rig: Patterns' link to it live over TLS with its key; the rig's row for Patterns, proved by Patterns' own key on its node link, says Patterns' own CONTEXT light and headline, and a change of them (a deck pairing with Patterns, then leaving) within 9.5 s, there and back",
  apps: ['patterns', 'stagewatch'],
  deadlineMs: [CONTEXT_MS, CONTEXT_MS],
}, async (r) => {
  r.evidence.push(run.log('patterns.wire'));
  const pat = apps.patterns;
  const link = await until(async () => {
    const l = (await pat.state()).bus.stagewatch;
    if (l.state !== 'live') throw new Error(`Patterns' link to the rig is ${l.state}: ${l.words}`);
    if (l.carrier !== 'tls' || l.key !== 'key') throw new Error(`Patterns' link to the rig is carried ${l.carrier}, its key "${l.key}": ${l.words}`);
    return l;
  }, HELLO_MS, "Patterns' link to the rig live over TLS with its key");
  r.evidence.push(run.log('patterns.deck.wire'));
  const own = async () => { const c = (await pat.context()).json; return { light: c.light, headline: c.headline }; };
  const d = deck();
  r.facts = { link: { state: link.state, carrier: link.carrier, key: link.key, words: link.words },
    ...(await contextReachesRig(ids.patterns, own, d.on, d.off, 'Patterns')) };
  await lockoutAfter(r);
}));

test("S2.4 Patterns reads the rig's context", row({
  id: 'S2.4',
  title: "Patterns reads the rig's context: its STATE's bus.stagewatch headline is the rig's own, and a change of it (the running order's next item) within 9.5 s, there and back",
  apps: ['patterns', 'stagewatch'],
  deadlineMs: [CONTEXT_MS, CONTEXT_MS],
}, async (r) => {
  r.evidence.push(run.log('patterns.wire'), run.log('stagewatch.err'));
  const sw = apps.stagewatch;
  const pat = apps.patterns;
  const agree = (ms) => until(async () => {
    const rig = (await sw.context()).headline;
    const read = (await pat.state()).bus.stagewatch.headline;
    if (read !== rig) throw new Error(`Patterns says "${read}", the rig "${rig}"`);
    return rig;
  }, ms, "Patterns' bus.stagewatch headline the rig's own");
  const before = await agree(CONTEXT_MS);
  const step = async (command) => {
    const from = (await sw.context()).headline;
    await sw.command(command);
    const changed = await until(async () => { const h = (await sw.context()).headline; return h !== from ? h : null; }, HELLO_MS, `the rig's headline after ${command}`);
    const at = Date.now();
    const read = await agree(S.left(at, CONTEXT_MS));
    if (read === from) throw new Error(`Patterns still says "${from}"`);
    const ms = S.since(at);
    if (ms > CONTEXT_MS) throw new Error(`Patterns said the rig's new headline ${ms} ms after the rig did, past ${CONTEXT_MS}`);
    return { rig: changed, patterns: read, ms };
  };
  r.facts = { before, next: await step('next'), previous: await step('previous') };
  await lockoutAfter(r);
}));

test("S2.6 PlayMap's context fits", row({
  id: 'S2.6',
  title: "PlayMap's context fits: under 64 KiB as its wire carries it, and so do Patterns' and the rig's own, each size recorded",
  apps: ['playmap', 'patterns', 'stagewatch'],
  deadlineMs: null,
}, async (r) => {
  const sizes = {
    playmap: (await apps.playmap.context()).bytes,
    patterns: (await apps.patterns.context()).bytes,
    stagewatch: (await apps.stagewatch.contextRead()).bytes,
  };
  const over = Object.entries(sizes).filter(([, b]) => b >= CONTEXT_BYTES);
  if (over.length) throw new Error(`a context of 64 KiB or more: ${over.map(([a, b]) => `${a} ${b} bytes`).join(', ')}`);
  r.facts = { bytes: sizes, limit: CONTEXT_BYTES };
  matrix.measure('contextBytes', sizes);
  await lockoutAfter(r);
}));

/** A row of the room's verdict by its id (node:<id>:<check>, or one of the rig's own), or undefined. */
const doorRow = (p, id, check) => (p.rows || []).find((x) => x.id === `node:${id}:${check}`);

/** The verdict as a fact: its words, its counts and the rows that are not green, each as its light and words. */
const verdictFacts = (p) => ({
  ready: p.ready, stuck: p.stuck, red: p.red, amber: p.amber, words: p.words,
  notGreen: (p.rows || []).filter((x) => x.light !== 'green').map((x) => `${x.id} ${x.light}: ${x.words}${x.configured ? ` (${x.configured})` : ''}`),
});

/**
 * The items of an app's own context that carry a light, as { id: light }; an id that names a file by its path (Patterns'
 * `source:vid:<path>`) is kept by the file's name, so the runner's folders stay on the runner.
 */
const ownLights = (ctx) => Object.fromEntries((ctx.items || []).filter((i) => i.light)
  .map((i) => [String(i.id).replace(/^(source:vid:).*[\\/]([^\\/]+)$/, '$1$2'), i.light]));

test('S8.1 ready with everything up', row({
  id: 'S8.1',
  title: "ready for doors with everything up: the rig's one verdict (GET /api/preflight) is ready, Patterns' and PlayMap's heard and context rows green and AGMessage's heard row green (its context row recorded: it posts none without a key), no app's own context holds a red item (S8.0, measured), and every door says the same: the operator frame's network and the Companion module's network_ready",
  apps: HOSTS,
  deadlineMs: DOORS_MS,
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log(`stagewatch.companion.${apps.stagewatch.starts}`));
  const sw = apps.stagewatch;
  // S8.0: what each app's own context lights, read from the app itself; a red one there would close the doors.
  const own = {
    patterns: ownLights((await apps.patterns.context()).json),
    playmap: ownLights((await apps.playmap.context()).json),
    agmessage: ownLights(await apps.agmessage.context()),
  };
  matrix.measure('ownItems', own);
  const red = Object.entries(own).flatMap(([app, items]) => Object.entries(items).filter(([, l]) => l === 'red').map(([id]) => `${app} ${id}`));
  if (red.length) throw new Error(`an app's own context lights red: ${red.join(', ')}`);
  const t = Date.now();
  const verdict = await until(async () => {
    const p = await sw.preflight();
    if (!p.ready || p.stuck) throw new Error(`not ready: ${p.words}`);
    for (const [who, id] of [['Patterns', ids.patterns], ['PlayMap', ids.playmap]]) {
      for (const check of ['heard', 'context']) {
        const x = doorRow(p, id, check);
        if (!x || x.light !== 'green') throw new Error(`${who}'s ${check} row is ${x ? `${x.light}: ${x.words}` : 'missing'}`);
      }
    }
    // AGMessage is heard; its context row is recorded, not held: it posts no context until it holds a node key (SW-INT-AG).
    const ag = doorRow(p, ids.agmessage, 'heard');
    if (!ag || ag.light !== 'green') throw new Error(`AGMessage's heard row is ${ag ? `${ag.light}: ${ag.words}` : 'missing'}`);
    return p;
  }, DOORS_MS, 'the doors ready, Patterns and PlayMap heard and their contexts green, AGMessage heard');
  const agContext = doorRow(verdict, ids.agmessage, 'context');
  const frame = await until(async () => { const n = (await sw.operatorFrame()).network; return n && n.ready === true ? n : null; }, DOORS_MS,
    "the operator frame's network ready");
  const deck = await sw.companion();
  const companion = await deck.networkReady();
  if (companion !== 'yes') throw new Error(`the Companion module's network_ready reads ${companion}, not yes: ${JSON.stringify(deck.snapshot && deck.snapshot.network)}`);
  r.facts = {
    ownItems: own, verdict: verdictFacts(verdict), readMs: S.since(t), frame, companion,
    agmessageContext: agContext ? `${agContext.light}: ${agContext.words}` : 'no row',
  };
  await lockoutAfter(r);
}));

test('S2.7 a node key cannot speak for another node', row({
  id: 'S2.7',
  title: "a node key cannot speak for another node: with Patterns' and PlayMap's keys each bound to its own node and the third bound to none, Patterns' key posting PlayMap's id and AGMessage's, PlayMap's posting Patterns' and the free key posting PlayMap's are each refused 403 with Stagewatch's words; no forged word is in the rig's context read at once, in the first it built after the posts (AGMessage's row still its hello's alone) or in any read after, its rows for PlayMap and Patterns stay each app's own, proved by its key on its node link, every key stays bound as it was, and no address is counted",
  apps: ['stagewatch', 'patterns', 'playmap', 'agmessage'],
  deadlineMs: null,
}, async (r) => {
  r.evidence.push(run.log('stagewatch.err'));
  const sw = apps.stagewatch;
  // Words no app says, as the forged context's name and headline: found in a read of the rig, they are the forgery applied.
  const forgedWords = `interop forged ${hex(4)}`;
  const forged = (t, id) => ({ v: 1, node: { t, id, name: forgedWords }, light: 'green', headline: forgedWords, items: [], recent: [], commands: [] });
  const keys = rigKeys();
  // AGMessage's id is one no app posts a context for here (it is heard by its hello alone), so a forgery the rig applied for it
  // would stay in every context the rig builds after it; one for PlayMap's or Patterns' id only until that app's next post.
  const tries = [
    { who: "Patterns' key", key: keys.patterns, t: 'playmap', id: ids.playmap,
      words: `this key speaks for patterns ${ids.patterns} — make a key for playmap ${ids.playmap}` },
    { who: "Patterns' key", key: keys.patterns, t: 'messenger', id: ids.agmessage,
      words: `this key speaks for patterns ${ids.patterns} — make a key for messenger ${ids.agmessage}` },
    { who: "PlayMap's key", key: keys.playmap, t: 'patterns', id: ids.patterns,
      words: `this key speaks for playmap ${ids.playmap} — make a key for patterns ${ids.patterns}` },
    { who: 'the free key', key: keys.free, t: 'playmap', id: ids.playmap,
      words: `playmap ${ids.playmap} speaks with its own key` },
  ];
  // Patterns' and PlayMap's keys each bound to its own node (the binding the rig saves, which is what a refused post could
  // change, and what shows the reader sees bindings at all), the free key bound to none.
  const before = boundAsHanded('before the posts');

  const refused = [];
  for (const t of tries) {
    const res = await sw.postContext(t.key, forged(t.t, t.id));
    if (res.status !== 403 || !res.json || res.json.ok !== false || res.json.error !== t.words) {
      throw new Error(`${t.who} posting ${t.t} ${t.id}'s context answered ${res.status} ${res.text.slice(0, 200)}`);
    }
    refused.push({ who: t.who, as: `${t.t} ${t.id}`, status: res.status, error: res.json.error });
  }
  const answeredAt = Date.now();

  // No forged word anywhere in a context of the rig's, PlayMap's and Patterns' rows each its key's on its node link, and
  // AGMessage's row still its hello's alone; what each row said.
  const unforged = (ctx, when) => {
    if (JSON.stringify(ctx).includes(forgedWords)) throw fatal(`the rig's context ${when} shows the forged context: ${JSON.stringify(ctx.peers)}`);
    const rows = {};
    for (const [name, id, own] of [['playmap', ids.playmap, true], ['patterns', ids.patterns, true], ['agmessage', ids.agmessage, false]]) {
      const row = (ctx.peers || []).find((p) => p.id === id);
      const fine = row && (own ? row.verified === 'key' && row.link === 'ws' : row.headline === S.HELLO_ONLY && row.link === '' && row.verified === '');
      if (!fine) throw fatal(`the rig's row for ${name} ${when} is ${JSON.stringify(row)}, not ${own ? "its key's on its node link" : "its hello's alone"}`);
      rows[name] = { light: row.light, headline: row.headline, verified: row.verified, link: row.link };
    }
    return rows;
  };
  // At once, with no wait; then the first context the rig built after every post had its answer (it builds one a second, so
  // a read at once may still be one built before).
  const atOnce = unforged(await sw.context(), 'right after the posts');
  const built = await until(async () => { const c = await sw.context(); return c.at > answeredAt ? c : null; }, CONTEXT_BUILT_MS, 'a context the rig built after the posts');
  const builtAfter = unforged(built, 'built after the posts');
  // Then each row agrees with its app's own context, and every read of the rig's on the way is free of the forgery.
  const kept = {};
  for (const [name, app, id] of [['playmap', apps.playmap, ids.playmap], ['patterns', apps.patterns, ids.patterns]]) {
    const own = async () => { const c = (await app.context()).json; return { light: c.light, headline: c.headline }; };
    kept[name] = await S.rigShowsContext(sw, id, own, CONTEXT_MS, name, (ctx) => unforged(ctx, 'on a later read'));
  }
  r.facts = { refused, atOnce, builtAfter: { ms: built.at - answeredAt, rows: builtAfter }, kept, bindings: { before, after: boundAsHanded('after the refused posts') } };
  await lockoutAfter(r);
}));

/** PlayMap's own context's item for its Stagewatch link ({ light, state }), as its CONTEXT says it now; null when it has none. */
async function playmapLinkItem() {
  const item = ((await apps.playmap.context()).json.items || []).find((i) => i.id === 'link:stagewatch');
  return item ? { light: item.light, state: item.state } : null;
}

test("S2.1b PlayMap reads the rig's verdict on its node link", row({
  id: 'S2.1b',
  title: "PlayMap reads the rig's verdict on its node link: its link taken off until the rig holds no node link for it, then given the free key, which speaks for no node, by its operator at its own machine, it dials the rig and is refused in the rig's own words within 5 s, the rig's own record of the link verified none in those words; from the answer on its link is live and never paired, watched for 7 s past its next context post: amber in its context, the rig's row for it with no proven link at every read and no proven node link in the rig's record; given its own key again, which speaks for it, it is paired again within 5 s with the rig's certificate still pinned, the rig's record its key's alone, verified key; every key stays bound as it was, and no address is counted",
  apps: ['playmap', 'stagewatch'],
  deadlineMs: [HELLO_MS, HELLO_MS],
}, async (r) => {
  r.evidence.push(run.log('playmap.wire'), run.log('playmap-operator.wire'), run.log('playmap-operator.out'), apps.playmap.engineLog,
    run.log('stagewatch.err'));
  const pm = apps.playmap;
  const sw = apps.stagewatch;
  const keys = rigKeys();
  // Kept in the row as each step reads it, so a row that fails keeps what it read up to the failure.
  const facts = {};
  r.facts = facts;
  facts.bindings = { before: boundAsHanded('before the row') };
  const pinned = `certificate is the one pinned (${shortFingerprint(sw.tls)})`;
  // The rig's verdict for a node key on a link naming an id another key speaks for: verified none, in these words (§7.1).
  const words = `playmap ${ids.playmap} speaks with its own key`;
  // The rig's own record of the node links naming PlayMap's id (its `nodes` line), as the fields a verdict is read by.
  const rigLinks = async () => (await sw.nodes()).filter((n) => n.id === ids.playmap)
    .map(({ t, verified, keyId, links, words: w }) => ({ t, verified, keyId, links, words: w }));
  // The rig's record holds the free key's link refused in its words, or no link at all; never a proven one, never another key's.
  const refusedOnly = (rows, when) => {
    const other = rows.filter((n) => n.t !== 'playmap' || n.verified !== 'none' || n.keyId !== keys.free.id || n.words !== words);
    if (other.length) throw fatal(`the rig's record of PlayMap's node link ${when}, with the free key, holds ${JSON.stringify(other)}`);
    return rows;
  };
  // The rig's context row for PlayMap, which must be there, with no proven link (link is "ws" only for a node link its key proves).
  const rigRowUnproven = async (when) => {
    const row = ((await sw.context()).peers || []).find((p) => p.id === ids.playmap);
    if (!row) throw fatal(`the rig's context has no row for ${ids.playmap} ${when}`);
    if (row.link !== '') throw fatal(`the rig's row for PlayMap says link "${row.link}" ${when}, with the free key: ${JSON.stringify(row)}`);
    return row;
  };
  // Paired by the rig's verdict for the key that speaks for PlayMap's id as a playmap: a link that named another id or kind
  // would be refused in the rig's words ("this key speaks for playmap …"), so paired is the link dialled as this node, t=playmap.
  const pairedWithOwnKey = async () => {
    const l = await pm.stagewatchLink();
    if (l.state !== 'live' || l.paired !== true) throw new Error(`PlayMap's link to the rig is ${l.state}, paired ${l.paired}: ${l.detail}`);
    if (!(l.detail || '').includes(pinned)) throw new Error(`PlayMap's link to the rig does not say the rig's certificate is pinned: ${l.detail}`);
    if ((l.detail || '').includes(words)) throw new Error(`PlayMap's paired link still says the rig's refusal: ${l.detail}`);
    return l;
  };
  // With the free key, every read from LINK STAGEWATCH's answer on: off (the link not started yet: PlayMap followed no
  // Stagewatch before it) or not paired, whatever the link's state.
  const notPaired = (l, when) => {
    if (l.state !== 'off' && l.paired !== false) throw fatal(`PlayMap's link says paired ${l.paired} ${when}, with the free key: ${l.state} "${l.detail}"`);
    return l;
  };
  // The rig's record once PlayMap's own key speaks again: its key's link alone, verified key, and the free key's gone.
  const ownKeyOnly = async () => {
    const rows = await rigLinks();
    const own = [{ t: 'playmap', verified: 'key', keyId: keys.playmap.id, links: 1, words: null }];
    if (JSON.stringify(rows) !== JSON.stringify(own)) throw new Error(`the rig's record of PlayMap's node link is ${JSON.stringify(rows)}, not ${JSON.stringify(own)}`);
    return rows;
  };
  const own = await until(pairedWithOwnKey, HELLO_MS, "PlayMap's link to the rig live and paired with its own key");
  const ownItem = await playmapLinkItem();
  if (!ownItem || ownItem.light !== 'green') throw new Error(`PlayMap's context says its paired Stagewatch link is ${JSON.stringify(ownItem)}, not green`);
  facts.ownKey = { state: own.state, paired: own.paired, detail: own.detail, item: ownItem, rig: await until(ownKeyOnly, HELLO_MS, "the rig's record of PlayMap's link its own key's") };

  // Set before each line is said: a line whose answer fails may still have been taken. `restored` once PlayMap's own key is
  // given again and taken; `done` once the row has read all it reads.
  let changed = false;
  let restored = false;
  let done = false;
  try {
    // First no link at all (LINK STAGEWATCH OFF, until STATE says off and the rig holds no node link for PlayMap), so every
    // read after the free key's line is of the link that key dials, never of the one PlayMap's own key had paired.
    changed = true;
    facts.offReceipt = await pm.unlinkStagewatch();
    const off = await until(async () => { const l = await pm.stagewatchLink(); return l.state === 'off' ? l : null; }, HELLO_MS,
      "PlayMap's link to the rig off");
    const offRig = await until(async () => ((await rigLinks()).length === 0 ? [] : null), HELLO_MS, "the rig holding no node link for PlayMap");
    facts.off = { state: off.state, item: await playmapLinkItem(), rig: offRig };
    facts.freeReceipt = await pm.linkStagewatch(sw.address, keys.free);
    const givenAt = Date.now();
    const refused = await until(async () => {
      const l = notPaired(await pm.stagewatchLink(), "before the rig's verdict was read");
      return l.state === 'live' && (l.detail || '').includes(words) ? l : null;
    }, HELLO_MS, "PlayMap's link to the rig live and refused in the rig's words");
    const refusedMs = S.inTime(givenAt, HELLO_MS, "PlayMap's link refused in the rig's words");
    const refusedAt = Date.now();
    facts.freeKey = { refusedMs, state: refused.state, paired: refused.paired, detail: refused.detail };
    // The rig's own record of the same verdict: the free key's link to PlayMap's id, verified none in its words.
    facts.freeKey.rigRecord = await until(async () => {
      const rows = refusedOnly(await rigLinks(), 'once PlayMap was refused');
      return rows.length ? rows : null;
    }, HELLO_MS, "the rig's record of PlayMap's link: the free key's, verified none in the rig's words");
    facts.freeKey.item = await until(async () => {
      notPaired(await pm.stagewatchLink(), 'while its context caught up');
      const i = await playmapLinkItem();
      return i && i.light === 'amber' && i.state.includes(words) ? i : null;
    }, HELLO_MS, "PlayMap's context saying its Stagewatch link amber in the rig's words");
    // The rig's context built after the refusal: its row for PlayMap there, with no proven link.
    facts.freeKey.rig = await until(async () => {
      const c = await sw.context();
      if (!(c.at > refusedAt)) return null;
      const row = await rigRowUnproven('after the refusal');
      return { builtMs: c.at - refusedAt, light: row.light, verified: row.verified, link: row.link };
    }, CONTEXT_BUILT_MS, "a context the rig built after the refusal");
    // Then for the watch, at every read: refused in the rig's words (live, or held once a refused post holds the key and the
    // connection is down), never paired, never green in PlayMap's context; the rig's row for PlayMap there with no proven link;
    // the rig's record the free key's link refused in its words, or none while PlayMap's connection is down.
    const watch = { ms: REFUSED_WATCH_MS, reads: 0, states: {}, items: {}, rigRecord: { refused: 0, none: 0 } };
    facts.freeKey.watch = watch;
    const end = Date.now() + REFUSED_WATCH_MS;
    while (Date.now() < end) {
      const l = notPaired(await pm.stagewatchLink(), 'during the watch');
      if (!['live', 'held'].includes(l.state) || !(l.detail || '').includes(words)) {
        throw new Error(`PlayMap's link with the free key says ${l.state} "${l.detail}", not refused in the rig's words`);
      }
      const i = await playmapLinkItem();
      if (!i || i.light === 'green') throw new Error(`PlayMap's context says its Stagewatch link is ${JSON.stringify(i)} with the free key`);
      await rigRowUnproven('during the watch');
      const rows = refusedOnly(await rigLinks(), 'during the watch');
      watch.reads++;
      watch.states[l.state] = (watch.states[l.state] || 0) + 1;
      watch.items[i.light] = (watch.items[i.light] || 0) + 1;
      watch.rigRecord[rows.length ? 'refused' : 'none']++;
      await new Promise((res) => setTimeout(res, 250));
    }

    facts.ownReceipt = await pm.linkStagewatch(sw.address, keys.playmap);
    restored = true;
    const againAt = Date.now();
    const again = await until(pairedWithOwnKey, HELLO_MS, "PlayMap's link to the rig paired again with its own key");
    const againMs = S.inTime(againAt, HELLO_MS, "PlayMap's link paired again with its own key");
    facts.ownKeyAgain = { pairedMs: againMs, state: again.state, paired: again.paired, detail: again.detail };
    facts.ownKeyAgain.rigRecord = await until(ownKeyOnly, HELLO_MS, "the rig's record of PlayMap's link its own key's alone again");
    facts.ownKeyAgain.item = await until(async () => { const i = await playmapLinkItem(); return i && i.light === 'green' ? i : null; }, HELLO_MS,
      "PlayMap's context saying its Stagewatch link green again");
    const ownContext = async () => { const c = (await pm.context()).json; return { light: c.light, headline: c.headline }; };
    facts.ownKeyAgain.rig = (await S.rigShowsContext(sw, ids.playmap, ownContext, CONTEXT_MS, 'PlayMap')).rig;
    done = true;
  } finally {
    // The rows after this one find PlayMap paired with its own key, whatever happened here; what the cleanup did is said on
    // standard error, never thrown over the row's own failure.
    if (changed && !done) {
      const said = (m) => console.error(`S2.1b's cleanup: ${run.secrets.scrub(m)}`);
      if (!restored) await pm.linkStagewatch(sw.address, keys.playmap).then(() => said('PlayMap given its own key again'), (e) => said(`giving PlayMap its own key again: ${e.message}`));
      await until(pairedWithOwnKey, HELLO_MS, "PlayMap's link paired again with its own key")
        .then(() => said('PlayMap paired again with its own key'), (e) => said(`PlayMap not paired again with its own key: ${e.message}`));
    }
  }
  facts.bindings.after = boundAsHanded('after the row');
  await lockoutAfter(r);
}));

test('S6.2a a stuck Stagewatch: PlayMap shows no time', row({
  id: 'S6.2a',
  title: "a stuck Stagewatch: PlayMap's link stays live and shows no time because Stagewatch says it is stuck, and the rig's running time again once the show turns over",
  apps: ['stagewatch', 'playmap'],
  deadlineMs: [STUCK_MS, RESUME_MS],
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log('playmap.wire'));
  const pm = apps.playmap;
  const sw = apps.stagewatch;
  const link = async () => (await pm.state()).links.stagewatch;
  const stuckWords = /stuck/i;
  // The rig's countdown runs, so a time PlayMap shows after the stall can be told from the one it held before it.
  await sw.command('start');
  const first = await until(async () => {
    const l = await link();
    return l.state === 'live' && l.timer.trusted === true && l.timer.state === 'running' && typeof l.timer.seconds === 'number' ? l : null;
  }, HELLO_MS, "PlayMap's running Stagewatch time before the stall");
  const stall = await sw.stall(STALL_MS);
  // From the rig's own stall time: no time within 9.5 s, said as "?" and not trusted, while the link stays live (the stuck
  // beat keeps coming: not the silence a stale link would show) and with Stagewatch's own reason.
  const blank = await until(async () => {
    const l = await link();
    if (l.timer.text !== '?' || l.timer.trusted !== false) return null;
    if (l.state !== 'live') throw new Error(`PlayMap shows no time, but its link is ${l.state}: "${l.timer.why}"`);
    if (!stuckWords.test(l.timer.why || '')) throw new Error(`PlayMap shows no time, its link live, for another reason: "${l.timer.why}"`);
    return l;
  }, S.left(stall.stalledAt, STUCK_MS), 'PlayMap showing no time, its link live, because Stagewatch says it is stuck');
  const noTimeMs = S.since(stall.stalledAt);
  if (noTimeMs > STUCK_MS) throw new Error(`PlayMap showed no time ${noTimeMs} ms after the stall, past ${STUCK_MS}`);
  // Until the rig turns over, PlayMap never shows a time it trusts again (a screen never shows a confident stale time), and
  // its link stays live with the stuck reason. A time read back is held against the rig's own turn-over time, which its
  // output may say a moment after the frames went.
  let samples = 0;
  let runningAt = null;
  const running = stall.running(STALL_MS + 10000).then((at) => { runningAt = at; });
  const tick = () => Promise.race([running, new Promise((res) => setTimeout(res, 100))]);
  while (runningAt === null) {
    const l = await link();
    const readAt = Date.now();
    if (l.timer.trusted !== false || l.timer.text !== '?') {
      await Promise.race([running, new Promise((res) => setTimeout(res, 2000))]);
      if (runningAt === null || readAt < runningAt) {
        throw new Error(`PlayMap showed "${l.timer.text}" (trusted ${l.timer.trusted}) while the rig's show was stuck`);
      }
      break;
    }
    if (runningAt === null && (l.state !== 'live' || !stuckWords.test(l.timer.why || ''))) {
      throw new Error(`while the rig's show was stuck PlayMap's link said ${l.state}, "${l.timer.why}"`);
    }
    samples++;
    await tick();
  }
  await running;
  const back = await until(async () => {
    const l = await link();
    return l.state === 'live' && l.timer.trusted === true && l.timer.text !== '?' ? l : null;
  }, S.left(runningAt, RESUME_MS), "PlayMap's time again");
  const resumeMs = S.since(runningAt);
  if (resumeMs > RESUME_MS) throw new Error(`PlayMap's time came back ${resumeMs} ms after the rig turned over, past ${RESUME_MS}`);
  // The time again is the rig's running one, not the one held before the stall: within a frame of the rig's own, and moved
  // on by the stall's length.
  const rig = (await sw.frame()).seconds;
  const after = (await link()).timer;
  if (Math.abs(after.seconds - rig) > TIME_SLACK_S) throw new Error(`PlayMap's time is ${after.text} (${after.seconds} s), the rig's own ${rig} s`);
  const moved = first.timer.seconds - after.seconds;
  if (moved < STALL_MS / 1000 - TIME_SLACK_S) throw new Error(`PlayMap's time moved ${moved} s across a ${STALL_MS} ms stall: it is the time held before it`);
  // The rig's show as the rows after this one found it: the countdown back at its start.
  await sw.command('reset');
  r.facts = {
    before: { text: first.timer.text, seconds: first.timer.seconds },
    stall: { ms: STALL_MS, rigSaid: runningAt - stall.stalledAt },
    noTimeMs,
    blank: { state: blank.state, why: blank.timer.why },
    stuckSamples: samples,
    resumeMs,
    after: { text: back.timer.text, seconds: after.seconds, rigSeconds: rig, movedS: Math.round(moved * 10) / 10 },
  };
  await lockoutAfter(r);
}));

test('S6.2b a stuck Stagewatch: Patterns shows no time', row({
  id: 'S6.2b',
  title: "a stuck Stagewatch: Patterns' link stays live and says stuck with no time from it, and its time comes back once the rig's show turns over",
  apps: ['stagewatch', 'patterns'],
  deadlineMs: [STUCK_MS, RESUME_MS],
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log('patterns.wire'));
  const pat = apps.patterns;
  const sw = apps.stagewatch;
  const link = async () => (await pat.state()).bus.stagewatch;
  const first = await until(async () => { const l = await link(); return l.state === 'live' && l.stuck === false && l.text !== '' ? l : null; }, HELLO_MS,
    "Patterns' Stagewatch time before the stall");
  const stall = await sw.stall(STALL_MS);
  // From the rig's own stall time: stuck within 9.5 s, the link still live (the stuck beat keeps coming) and no time shown.
  const stuck = await until(async () => {
    const l = await link();
    if (l.stuck !== true) return null;
    if (l.state !== 'live') throw new Error(`Patterns says stuck, but its link is ${l.state}`);
    if (l.text !== '') throw new Error(`Patterns says stuck and still shows "${l.text}"`);
    return l;
  }, S.left(stall.stalledAt, STUCK_MS), 'Patterns saying the rig is stuck, its link live, with no time');
  const stuckMs = S.since(stall.stalledAt);
  if (stuckMs > STUCK_MS) throw new Error(`Patterns said stuck ${stuckMs} ms after the stall, past ${STUCK_MS}`);
  // Until the rig turns over Patterns never shows a time from it; a time read back is held against the rig's own turn-over time.
  let samples = 0;
  let runningAt = null;
  const running = stall.running(STALL_MS + 10000).then((at) => { runningAt = at; });
  while (runningAt === null) {
    const l = await link();
    const readAt = Date.now();
    if (l.text !== '' || l.stuck !== true) {
      await Promise.race([running, sleep(2000)]);
      if (runningAt === null || readAt < runningAt) throw new Error(`Patterns showed "${l.text}" (stuck ${l.stuck}) while the rig's show was stuck`);
      break;
    }
    samples++;
    await Promise.race([running, sleep(100)]);
  }
  await running;
  const back = await until(async () => { const l = await link(); return l.state === 'live' && l.stuck === false && l.text !== '' ? l : null; },
    S.left(runningAt, RESUME_MS), "Patterns' Stagewatch time again");
  const resumeMs = S.since(runningAt);
  if (resumeMs > RESUME_MS) throw new Error(`Patterns' time came back ${resumeMs} ms after the rig turned over, past ${RESUME_MS}`);
  r.facts = {
    before: { text: first.text, light: first.light },
    stall: { ms: STALL_MS, rigSaid: runningAt - stall.stalledAt },
    stuckMs,
    stuck: { state: stuck.state, light: stuck.light, headline: stuck.headline },
    stuckSamples: samples,
    resumeMs,
    after: { text: back.text, light: back.light },
  };
  await lockoutAfter(r);
}));

test('S8.3 stuck is never ready', row({
  id: 'S8.3',
  title: "a stuck show is never ready for doors: GET /api/preflight answers not ready and stuck, with one red row, within 6.5 s of the rig's own stall time, and the Companion module's network_ready reads unknown; once the show turns over the room is ready again on both",
  apps: ['stagewatch'],
  deadlineMs: [DOORS_STUCK_MS, RESUME_MS],
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log(`stagewatch.companion.${apps.stagewatch.starts}`));
  const sw = apps.stagewatch;
  const deck = await sw.companion();
  const ready = await until(async () => ((await sw.preflight()).ready ? true : null), ROOM_READY_MS, 'the doors ready before the stall');
  const stall = await sw.stall(STALL_MS);
  const stuck = await until(async () => {
    const p = await sw.preflight();
    if (!p.stuck) return null;
    if (p.ready !== false || p.red !== 1 || (p.rows || []).length !== 1 || p.rows[0].light !== 'red') throw new Error(`stuck, but the verdict is ${JSON.stringify(verdictFacts(p))}`);
    return p;
  }, S.left(stall.stalledAt, DOORS_STUCK_MS), 'the verdict stuck');
  const stuckMs = S.inTime(stall.stalledAt, DOORS_STUCK_MS, 'the verdict stuck');
  const unknown = await until(async () => ((await deck.networkReady()) === 'unknown' ? 'unknown' : null), S.left(stall.stalledAt, DOORS_STUCK_MS),
    "the Companion module's network_ready unknown");
  const unknownMs = S.inTime(stall.stalledAt, DOORS_STUCK_MS, "the Companion module's network_ready unknown");
  const runningAt = await stall.running(STALL_MS + 10000);
  const again = await until(async () => { const p = await sw.preflight(); return p.ready && !p.stuck ? p : null; }, S.left(runningAt, RESUME_MS), 'the doors ready again');
  const yes = await until(async () => ((await deck.networkReady()) === 'yes' ? 'yes' : null), S.left(runningAt, RESUME_MS), "the Companion module's network_ready yes again");
  const againMs = S.inTime(runningAt, RESUME_MS, 'the doors and the Companion module ready again');
  r.facts = { readyBefore: ready, stall: { ms: STALL_MS, rigSaid: runningAt - stall.stalledAt }, stuck: verdictFacts(stuck), stuckMs, companion: { stuck: unknown, unknownMs, again: yes }, again: verdictFacts(again), againMs };
  await lockoutAfter(r);
}));

/** What the doors read while PlayMap was gone (row S6.3), for row S8.2, which asserts them. */
let doorsWhileGone = null;

test('S6.3 PlayMap killed, the doors say why', row({
  id: 'S6.3',
  title: "PlayMap killed: the rig's context row for it goes stale (within 11 s) then gone (within 20 s); the doors are not ready, PlayMap's heard row red and first, naming the node key that configures it (within 20 s); restarted (the same id, home, address and ports), its links to the rig and to Patterns pair again and the doors are ready again, each within 20 s",
  apps: ['playmap', 'stagewatch', 'patterns'],
  deadlineMs: [STALE_MS, GONE_MS, GONE_MS],
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log('playmap.wire'), run.log('playmap.2.out'), apps.playmap.engineLog);
  const sw = apps.stagewatch;
  const pm = apps.playmap;
  const deck = await sw.companion();
  const rigRow = async () => ((await sw.context()).peers || []).find((x) => x.id === ids.playmap) || null;
  const before = await until(async () => {
    const p = await sw.preflight();
    const heard = doorRow(p, ids.playmap, 'heard');
    return p.ready && heard && heard.light === 'green' ? heard : null;
  }, ROOM_READY_MS, "the doors ready with PlayMap's heard row green");
  if (!/^node key /.test(before.configured || '')) throw new Error(`PlayMap's heard row is configured by "${before.configured}", not its node key`);
  await pm.kill();
  const killedAt = Date.now();
  const stale = await until(async () => { const x = await rigRow(); return x && x.heard === 'stale' ? x : null; }, STALE_MS, "the rig's context row for PlayMap stale");
  const staleMs = S.since(killedAt);
  const gone = await until(async () => { const x = await rigRow(); return !x || x.heard === 'gone' ? (x || { heard: 'not listed' }) : null; },
    S.left(killedAt, GONE_MS), "the rig's context row for PlayMap gone");
  const goneMs = S.since(killedAt);
  const red = await until(async () => {
    const p = await sw.preflight();
    const heard = doorRow(p, ids.playmap, 'heard');
    return !p.ready && heard && heard.light === 'red' ? p : null;
  }, S.left(killedAt, GONE_MS), "the doors not ready, PlayMap's heard row red");
  const redMs = S.inTime(killedAt, GONE_MS, "the doors not ready, PlayMap's heard row red");
  // Every door says the same while it is gone: the operator frame and the Companion module (row S8.2 asserts them).
  doorsWhileGone = {
    verdict: red,
    heard: doorRow(red, ids.playmap, 'heard'),
    frame: await until(async () => { const n = (await sw.operatorFrame()).network; return n && n.ready === false ? n : null; }, DOORS_MS, "the operator frame's network not ready"),
    companion: await until(async () => { const v = await deck.networkReady(); return v === 'no' ? v : null; }, DOORS_MS, "the Companion module's network_ready no"),
  };
  await pm.launch(READY_MS);
  pm.checkReady();
  const restartedAt = Date.now();
  const links = await until(async () => {
    const st = await pm.state();
    const l = st.links;
    return l.stagewatch.state === 'live' && l.stagewatch.paired === true && l.stagewatch.timer.trusted === true && l.patterns.state === 'live' && l.patterns.paired === true ? l : null;
  }, GONE_MS, "PlayMap's links to the rig and to Patterns live and paired again");
  const linksMs = S.since(restartedAt);
  const again = await until(async () => {
    const p = await sw.preflight();
    const heard = doorRow(p, ids.playmap, 'heard');
    const context = doorRow(p, ids.playmap, 'context');
    return p.ready && heard && heard.light === 'green' && context && context.light === 'green' ? p : null;
  }, S.left(restartedAt, GONE_MS), "the doors ready again with PlayMap's heard and context rows green");
  const againMs = S.inTime(restartedAt, GONE_MS, 'the doors ready again');
  r.facts = {
    before: { light: before.light, configured: before.configured },
    stale: { heard: stale.heard, light: stale.light, ms: staleMs },
    gone: { heard: gone.heard, ms: goneMs },
    red: verdictFacts(red), redMs,
    restart: { readyMs: pm.readyMs, linksMs, stagewatch: { state: links.stagewatch.state, paired: links.stagewatch.paired }, patterns: { state: links.patterns.state, paired: links.patterns.paired } },
    again: verdictFacts(again), againMs,
  };
  await lockoutAfter(r);
}));

test('S8.2 not ready, saying why', row({
  id: 'S8.2',
  title: "not ready, saying why (read inside S6.3, while PlayMap was gone): the verdict not ready with one red row, first, PlayMap's heard, naming the node key that configures it and how to fix it, its words beginning with PlayMap's name; the operator frame's network not ready with that red count, and the Companion module's network_ready no",
  apps: ['stagewatch', 'playmap'],
  deadlineMs: null,
}, async (r) => {
  if (!doorsWhileGone) throw new Error('S6.3 read no verdict while PlayMap was gone');
  const { verdict, heard, frame, companion } = doorsWhileGone;
  const wrong = [];
  if (verdict.ready !== false || verdict.stuck !== false) wrong.push(`ready ${verdict.ready}, stuck ${verdict.stuck}`);
  if (verdict.red < 1 || verdict.rows[0].id !== heard.id) wrong.push(`red ${verdict.red}, first row ${verdict.rows[0] && verdict.rows[0].id}`);
  if (!/^not heard (for \d+ s|since \d\d:\d\d)$/.test(heard.words || '')) wrong.push(`PlayMap's row says "${heard.words}"`);
  if (!/^node key /.test(heard.configured || '')) wrong.push(`PlayMap's row is configured by "${heard.configured}"`);
  if (!heard.fix) wrong.push("PlayMap's row says no fix");
  if (!/^not ready: Interop PlayMap /.test(verdict.words || '')) wrong.push(`the verdict says "${verdict.words}"`);
  if (frame.ready !== false || frame.red !== verdict.red) wrong.push(`the frame's network is ${JSON.stringify(frame)}`);
  if (companion !== 'no') wrong.push(`the Companion module reads ${companion}`);
  r.facts = { verdict: verdictFacts(verdict), heard: { light: heard.light, words: heard.words, configured: heard.configured, fix: heard.fix }, frame, companion };
  if (wrong.length) throw new Error(`while PlayMap was gone: ${wrong.join('; ')}`);
  await lockoutAfter(r);
}));

test('S6.1 the rig killed and restarted', row({
  id: 'S6.1',
  title: 'the rig killed and restarted: every link lets go (none live within 11 s, every one gone within 20 s), then every link is live again',
  apps: HOSTS,
  deadlineMs: [STALE_MS, GONE_MS, GONE_MS],
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log('stagewatch.2.out'), run.log('playmap.wire'), run.log('patterns.wire'));
  const sw = apps.stagewatch;
  const before = await until(async () => { const l = await stagewatchLinks(); return allLive(l) ? l : null; }, HELLO_MS, 'every link to the rig live');
  // The killed rig's lockout tables go with it: they are read before the kill, and the restarted rig's after the row.
  const kept = await S.readLockout(sw, lanAddresses());
  if (kept.counted.length) throw new Error(`before the kill the rig counted failures against ${kept.counted.join(', ')}`);
  await sw.kill();
  const killedAt = Date.now();
  // Silence or a reset: within 11 s no app says its link is live, and PlayMap shows no time.
  const letGo = await until(async () => {
    const l = await stagewatchLinks();
    const live = [l.playmap.state === 'live' && 'PlayMap', l.agmessage.state === 'connected' && 'AGMessage', l.patterns.state === 'live' && 'Patterns'].filter(Boolean);
    if (live.length) throw new Error(`${live.join(', ')} still say live: ${JSON.stringify(l)}`);
    if (l.playmap.trusted !== false || l.playmap.text !== '?') throw new Error(`PlayMap still shows "${l.playmap.text}"`);
    return l;
  }, STALE_MS, 'every link letting go of the killed rig');
  const letGoMs = S.since(killedAt);
  // Then gone within 20 s of the kill: none says live, connected or stale (a reset may take a link straight there).
  const gone = await until(async () => {
    const l = await stagewatchLinks();
    const still = S.stillHeld(l);
    if (still.length) throw new Error(`still ${still.map(([n, st]) => `${n} ${st}`).join(', ')}`);
    if (l.playmap.trusted !== false || l.playmap.text !== '?') throw new Error(`PlayMap shows "${l.playmap.text}"`);
    return l;
  }, S.left(killedAt, GONE_MS), 'every link to the killed rig gone');
  const goneMs = S.since(killedAt);
  // The same rig again: the same id, --state, journal, PIN, address and ports.
  await sw.start(READY_MS);
  sw.checkReady();
  const restartedAt = Date.now();
  // Live again as before: Patterns following the rig by the name its hello gives, not only at the address it dials, so the
  // rows after find its link settled.
  const back = await until(async () => {
    const l = await stagewatchLinks();
    return allLive(l) && l.patterns.name === before.patterns.name ? l : null;
  }, GONE_MS, 'every link to the restarted rig live, Patterns following it by its name again');
  r.facts = { before, lockoutBeforeKill: kept.read, letGo, letGoMs, gone, goneMs, restartReadyMs: sw.readyMs, back, backMs: S.since(restartedAt) };
  await lockoutAfter(r);
}));

test("S6.1b the rig's cable pulled", row({
  id: 'S6.1b',
  title: "the rig's cable pulled: every link goes stale, then gone, through silence; put back, every link is live again",
  apps: HOSTS,
  deadlineMs: [STALE_MS, GONE_MS, GONE_MS],
}, async (r) => {
  r.evidence.push(run.log('lan.out'), run.log('playmap.wire'), run.log('patterns.wire'));
  const before = await until(async () => { const l = await stagewatchLinks(); return allLive(l) ? l : null; }, HELLO_MS, 'every link to the rig live');
  await lan.cable('stagewatch', 'down');
  const downAt = Date.now();
  // Silence, not a reset: each link says stale within 11 s, with no time on PlayMap.
  const stale = await until(async () => {
    const l = await stagewatchLinks();
    const not = [l.playmap.state !== 'stale' && `PlayMap ${l.playmap.state}`, l.agmessage.state !== 'stale' && `AGMessage ${l.agmessage.state}`,
      l.patterns.state !== 'stale' && `Patterns ${l.patterns.state}`].filter(Boolean);
    if (not.length) throw new Error(`not stale yet: ${not.join(', ')}`);
    if (l.playmap.trusted !== false || l.playmap.text !== '?') throw new Error(`PlayMap still shows "${l.playmap.text}"`);
    return l;
  }, STALE_MS, 'every link to the silent rig stale');
  const staleMs = S.since(downAt);
  // Then gone within 20 s: none says live or stale.
  const gone = await until(async () => {
    const l = await stagewatchLinks();
    const still = S.stillHeld(l);
    if (still.length) throw new Error(`still ${still.map(([n, st]) => `${n} ${st}`).join(', ')}`);
    return l;
  }, S.left(downAt, GONE_MS), 'every link to the silent rig gone');
  const goneMs = S.since(downAt);
  await lan.cable('stagewatch', 'up');
  const upAt = Date.now();
  const back = await until(async () => { const l = await stagewatchLinks(); return allLive(l) ? l : null; }, GONE_MS, 'every link to the rig live again');
  r.facts = { before, stale, staleMs, gone, goneMs, back, backMs: S.since(upAt) };
  await lockoutAfter(r);
}));

test('S8.4 a seen-only node leaving does not block', row({
  id: 'S8.4',
  title: "AGMessage, which no node key or link configures (only seen), killed: its heard row turns amber within 11 s and the doors stay ready at every read through the gone bound; restarted, its link to the rig is connected again and its row green within 20 s",
  apps: ['agmessage', 'stagewatch'],
  deadlineMs: [STALE_MS, GONE_MS, GONE_MS],
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log('agmessage.2.out'));
  const sw = apps.stagewatch;
  const agm = apps.agmessage;
  const before = await until(async () => {
    const p = await sw.preflight();
    const heard = doorRow(p, ids.agmessage, 'heard');
    return p.ready && heard && heard.light === 'green' ? heard : null;
  }, ROOM_READY_MS, "the doors ready with AGMessage's heard row green");
  if (before.configured) throw new Error(`AGMessage's heard row is configured by "${before.configured}": it is not only seen`);
  await agm.kill();
  const killedAt = Date.now();
  // At every read until the gone bound has passed: ready, and AGMessage's row never red.
  const watch = { reads: 0, lights: {} };
  let amberMs = null;
  let amber = null;
  while (S.since(killedAt) < GONE_MS + 2000) {
    const p = await sw.preflight();
    const heard = doorRow(p, ids.agmessage, 'heard');
    if (!p.ready) throw new Error(`AGMessage gone ${S.since(killedAt)} ms closed the doors: ${JSON.stringify(verdictFacts(p))}`);
    if (heard && heard.light === 'red') throw new Error(`AGMessage's heard row turned red: ${heard.words}`);
    const light = heard ? heard.light : 'not listed';
    watch.reads++;
    watch.lights[light] = (watch.lights[light] || 0) + 1;
    if (amberMs === null && light === 'amber') { amberMs = S.since(killedAt); amber = heard; }
    await sleep(250);
  }
  if (amberMs === null || amberMs > STALE_MS) throw new Error(`AGMessage's heard row was amber ${amberMs === null ? 'never' : `${amberMs} ms after the kill`}, not within ${STALE_MS}: ${JSON.stringify(watch.lights)}`);
  await agm.start(sw.address, READY_MS);
  agm.checkReady();
  const restartedAt = Date.now();
  const linked = await until(async () => { const h = (await agm.hostHealth()).stage; return h === 'connected' ? h : null; }, GONE_MS, "AGMessage's link to the rig connected again");
  const green = await until(async () => {
    const p = await sw.preflight();
    const heard = doorRow(p, ids.agmessage, 'heard');
    return p.ready && heard && heard.light === 'green' ? heard : null;
  }, S.left(restartedAt, GONE_MS), "AGMessage's heard row green again");
  r.facts = {
    before: { light: before.light, configured: before.configured },
    amber: { ms: amberMs, words: amber.words },
    watch,
    restart: { readyMs: agm.readyMs, stage: linked, greenMs: S.inTime(restartedAt, GONE_MS, "AGMessage's heard row green again"), words: green.words },
  };
  await lockoutAfter(r);
}));

test('S7.4 no app\'s address is ever counted', row({
  id: 'S7.4',
  title: "no address is ever counted: every row before this one read both of the rig's lockout tables after it with every address of this LAN at 0, and now neither table counts any address at all",
  apps: HOSTS,
  deadlineMs: null,
}, async (r) => {
  r.facts = await S.nobodyCounted(apps.stagewatch, matrix, 'S7.4');
  await lockoutAfter(r);
}));

test('S7.1 no secret anywhere', row({
  id: 'S7.1',
  title: "no secret of the register (the PIN, the apps' tokens, AGMessage's admin and context tokens) in any log, frame, STATE, context, report or capture the run kept",
  apps: HOSTS,
  deadlineMs: null,
}, async (r) => {
  await lockoutAfter(r);
  // The control that keeps every row's view-pass reading honest, after the last one: the view passes' reader sees a wrong
  // window address from this LAN's switch (only its minute clears it, so no read after this one asserts the switch).
  const viewPassReader = await apps.stagewatch.proveViewPassReader(lan.map.switch);
  r.facts = { viewPassReader, ...(await S.noSecretAnywhere({ run, matrix, apps })) };
  matrix.removed(r.facts.removed);
  matrix.measure('exits', r.facts.exits);
}));
