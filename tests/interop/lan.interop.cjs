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
// given none (its keyed rows are another unit's). The rows run in order, each a node:test test that writes its matrix row;
// S0.1 starts the apps (the rig first, which the others link to) and every later row reads the apps it started; the rows
// that take an app down come last but one, and S7.1 (no secret anywhere) last, as it stops the apps to sweep what they left
// (the rig's certificate's password among the secrets it looks for). After every row both of the rig's lockout
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
const { Run, hex } = require('./lib/run.cjs');
const { Wire } = require('./lib/wire.cjs');
const { Lan } = require('./lib/lan.cjs');
const { Matrix } = require('./lib/matrix.cjs');
const { until, fatal } = require('./lib/inbox.cjs');
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

test('S7.3 unknown words are refused everywhere', row({
  id: 'S7.3',
  title: 'unknown words are refused by Patterns, PlayMap and the rig',
  apps: ['patterns', 'playmap', 'stagewatch'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.facts = await S.unknownWordsRefused(apps.patterns, apps.playmap, apps.stagewatch);
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
  const back = await until(async () => { const l = await stagewatchLinks(); return allLive(l) ? l : null; }, GONE_MS, 'every link to the restarted rig live');
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
