// The interop lane on its LAN topology: four real apps, each in a network namespace of its own on one bridge, at its own
// address (Stagewatch's rig 10.99.0.2, Patterns' bench 10.99.0.3, PlayMap's node 10.99.0.4, AGMessage 10.99.0.5) with its
// own loopback and every app on its default ports; the driver is the switch (10.99.0.1) and never listens on the Show Bus
// port (it says hellos only in S1.6's control: an id of its own, from a port of its own, to PlayMap alone). The rig is on the
// show network as the app runs on a show laptop: on the Show Bus port, serving its certificate beside plain HTTP, and making
// three node keys: one for Patterns and one for PlayMap, each handed over in that app's own settings, and one handed to
// nobody, which row S2.7 tries against an id that has a key of its own and row S2.1b gives PlayMap (LINK STAGEWATCH, said
// as its operator says it at PlayMap's own machine: on its wire at 127.0.0.1, from inside its namespace, so no key crosses
// this LAN) before giving it its own key again. Patterns' link to the rig speaks LINK on /ws (its Round 96.1): each end proves
// itself to the other with the key, Patterns pins the certificate the proof was made over and keeps a floor, and no person's
// Trust is asked; S0.1 reads that between Patterns' start and PlayMap's, and S0.2 asserts it. The rows read PlayMap's link with
// the rig's certificate pinned and Patterns' link carried over TLS with its key; that neither app ever sends its key in clear off its own machine is held by that app's
// own tests (Patterns' Round 89.5, PlayMap's PM-2), not read here; the rig's verdict on a node link is read from both ends,
// the app's link and the rig's own record of it (its `nodes` line). AGMessage is
// given none (its keyed rows are another unit's). The rig is the room's approval desk and its one verdict, as the app is: the
// desk rows send a line across it both ways (the bench's assistant proposing a look that the operator approves there, the
// rig's own line run at Patterns), deciding over HTTP with the operator's PIN; the doors rows read the verdict on its route,
// the operator frame's `network` and the rig's Companion door, judged by the Companion module's own rule (lib/companion.cjs),
// with PlayMap configured by the node key bound to it and AGMessage only seen. The VT rows (S4.1–S4.4) run a second rig beside
// the room's, in its namespace on ports of its own and off the show network, with playout links to Patterns' and PlayMap's wires,
// each paired by proof with that app's own token (handed over in that link's own variable of the rig's environment), and stop it
// once they end, so the room's rig, its doors and its rows are as they were. The rows run in order, each a node:test test
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
const { PlayMap, DOORS: PlayMapDoors } = require('./apps/playmap.cjs');
const { AGMessage } = require('./apps/agmessage.cjs');
const S = require('./lib/scenarios.cjs');
const { dialLink, kidOf } = require('./lib/link.cjs');

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
/**
 * What the rig's own LINK verdict offered (S2.8's control reads it): its `can`, and its `stuckMs`, which 13.6.6 has it carry only
 * when it lists `stuck`. The plan's stuck rows add 4 s to that number; the lane adds none of its own.
 */
let rigVerdict = null;
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

/**
 * What Patterns' link to the rig said as the room was set up, between Patterns' start and PlayMap's (S0.1): { facts } once its
 * link proved itself with LINK, or { error } with the reason it did not; null until S0.1 reaches it. Row S0.2 asserts it.
 */
let proved = null;

/** S0.2's deadline: Patterns' link dials the rig at its start, so it proves itself within a hello's of its ready line. */
const PROVED_MS = HELLO_MS;

/** What Patterns' bench answers a `trust` line when nothing waits for a person's Trust (its desk's own words). */
const NOTHING_WAITS = 'nothing waits for Trust: the Nodes card draws no certificate beside TRUST';

/**
 * Patterns' link to the rig proving itself (Show Bus 1.3 §13.6.5, Patterns' Round 96.1): its STATE's record at level 1 with both
 * proofs (`mutual`), pinned by the proof at the certificate the rig serves (read from the rig's own ready line, never from
 * Patterns), its floor kept, under the kid its key makes; the rig's own record (its `nodes` line) holding Patterns' link proved by
 * that key and --state binding the key to Patterns' id. No person's Trust is asked: the bench's trust line finds nothing waiting.
 * Resolves with what was read; `from` is Patterns' ready time.
 */
async function linkProves(sw, pat, from) {
  const fp = sw.tls.slice('sha256:'.length);
  const key = sw.keyFor('patterns');
  const kid = kidOf(key.secret);
  const rigRows = async () => (await sw.nodes()).filter((n) => n.id === pat.ready.node)
    .map(({ t, verified, keyId, links, words }) => ({ t, verified, keyId, links, words }));
  const read = await until(async () => {
    const l = await pat.stagewatchLink();
    const rec = l.link || {};
    if (l.state !== 'live' || rec.level !== 1 || rec.proof !== 'mutual') throw new Error(`Patterns' link to the rig is ${l.state}, level ${rec.level}, proof ${rec.proof}: "${rec.words}"`);
    const rows = await rigRows();
    const row = rows.find((n) => n.verified === 'key' && n.keyId === key.id);
    if (!row) throw new Error(`the rig's nodes line holds no link of Patterns' proved by its key: ${JSON.stringify(rows)}`);
    return { link: l, rig: row };
  }, S.left(from, PROVED_MS), "Patterns' link to the rig proved with LINK");
  const provedMs = S.inTime(from, PROVED_MS, "Patterns' link to the rig proved with LINK");
  const rec = read.link.link;
  const wrong = [];
  if (read.link.carrier !== 'tls' || rec.carrier !== 'ws') wrong.push(`carrier ${read.link.carrier}/${rec.carrier}, not tls over ws`);
  if (rec.tls !== 'pinned' || rec.pin !== fp.slice(0, 16)) wrong.push(`tls ${rec.tls} at ${rec.pin}, not pinned at the rig's ${fp.slice(0, 16)}`);
  if (rec.kid !== kid) wrong.push(`kid ${rec.kid}, not ${kid}, the one its key makes`);
  if (!rec.floor || rec.floor.tls !== true || rec.floor.level !== 1 || rec.floor.proof !== 'mutual') wrong.push(`floor ${JSON.stringify(rec.floor)}`);
  if (read.link.key !== 'key') wrong.push(`key "${read.link.key}"`);
  if (rec.refusal) wrong.push(`refusal ${rec.refusal}`);
  if (rec.peer.t !== 'main' || rec.peer.id !== NODE_ID) wrong.push(`peer ${JSON.stringify(rec.peer)}`);
  if (wrong.length) throw new Error(`Patterns' proved link to the rig: ${wrong.join('; ')}`);
  const bound = sw.savedKeys().find((k) => k.id === key.id);
  if (!bound || bound.node !== pat.ready.node || bound.nodeKind !== 'patterns') throw new Error(`Patterns' key in --state is bound to ${JSON.stringify(bound)}, not Patterns`);
  // No person's Trust: the bench's own trust line finds nothing waiting, with the rig's own hex or any other.
  const trust = await pat.trust(fp.slice(0, 16));
  if (trust.ok !== false || trust.error !== NOTHING_WAITS) throw new Error(`the bench answered the rig's hex with ${JSON.stringify(trust)}`);
  const leaked = run.secrets.foundIn(JSON.stringify([read, trust]));
  if (leaked.length) throw new Error(`what was read holds ${leaked.join(', ')}`);
  return {
    provedMs,
    record: { level: rec.level, proof: rec.proof, tls: rec.tls, pin: rec.pin, kid: rec.kid, scope: rec.scope, flags: rec.flags, light: rec.light, floor: rec.floor, words: rec.words },
    rig: read.rig, bound: { node: bound.node, nodeKind: bound.nodeKind }, trust: trust.error,
  };
}

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
  title: "each app is ready in its own namespace, within 60 s of its start; between Patterns' start and PlayMap's the lane reads Patterns' link to the rig prove itself, as the room is set up (S0.2 asserts what it read)",
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
  // Patterns' link proves itself to the rig before the other apps start, as the room is set up; S0.2 asserts what was read, and
  // a link that did not prove itself fails that row, not this one.
  proved = await linkProves(sw, pat, pat.readyAt).then((facts) => ({ facts }), (error) => ({ error }));

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
  r.facts = { readyMs, addresses: Object.fromEntries(HOSTS.map((n) => [n, lan.host(n).addr])), namespaces, linkProved: proved.error ? 'not proved (S0.2 says why)' : 'proved (S0.2)' };
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
  title: "every app hears the rig: kind main, its --node-id, from the rig's own address where the reader says one, within 5 s of the last app ready (Patterns, whose link follows the rig, draws it no better than that link's record: amber only with the record live and amber, the key taken, and either at level 0 and legacy or at level 1 both ways proved and pinned with the one flag scope (the verdict's scope node, a finding), and the rig's own context, read from the rig, neither amber nor red with the headline Patterns draws)",
  apps: HOSTS,
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('patterns.wire'), run.log('playmap.wire'), run.log('agmessage-admin.out'));
  const addr = lan.host('stagewatch').addr;
  const own = async () => { const c = await apps.stagewatch.context(); return { light: c.light, headline: c.headline }; };
  const patterns = await S.patternsHears(apps.patterns, [{ kind: 'main', id: NODE_ID, addr, linked: true, own }], S.left(readyAt, HELLO_MS));
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

// S0.2 asserts what S0.1 read as Patterns' link proved itself, after the hello rows, whose deadlines count from the last app ready
// and take no row's time before them.
test('S0.2 Patterns and the rig prove themselves to each other over ws', row({
  id: 'S0.2',
  title: "Patterns links to the rig over ws, verified both ways, pinned, floor set (read as the room is set up, between Patterns' start and PlayMap's): within 5 s of Patterns' ready line its link's record is live at level 1 with both proofs, over TLS pinned by the proof at the certificate the rig serves (read from the rig), its floor {tls, level 1, mutual} kept, under the kid its key makes; the rig's own record holds Patterns' link proved by that key and --state binds the key to Patterns' id; no person's Trust is asked (the bench's trust line finds nothing waiting); and the link still so when the row reads it",
  apps: ['patterns', 'stagewatch'],
  deadlineMs: PROVED_MS,
}, async (r) => {
  r.evidence.push(run.log('patterns.out'), run.log('patterns.wire'), run.log('stagewatch.err'));
  if (!proved) throw new Error('S0.1 did not reach the read: Patterns never started');
  if (proved.error) throw proved.error;
  const now = await apps.patterns.stagewatchLink();
  const rec = now.link || {};
  if (now.state !== 'live' || rec.level !== 1 || rec.proof !== 'mutual' || rec.tls !== 'pinned' || now.key !== 'key') {
    throw new Error(`Patterns' link to the rig is now ${now.state}, level ${rec.level}, proof ${rec.proof}, tls ${rec.tls}, key "${now.key}": "${rec.words}"`);
  }
  r.facts = { ...proved.facts, now: { state: now.state, level: rec.level, proof: rec.proof, tls: rec.tls, since: rec.since } };
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

/** The VT rows' rig, beside the room's in its namespace, off the show network: its ports, its id. */
const VT_PORTS = { http: 19950, companion: 19951, osc: 19952 };
const VT_NODE = 'interop-sw-vt';
/** A VT a screen reads is the clip's own time left, within 1.5 s (the frame is built from the link's last STATE). */
const VT_SLACK_S = 1.5;
/** One kernel's clock seen over a link: the offset its matched pongs believe, within 50 ms. */
const CLOCK_AGREE_MS = 50;
/** S4.4's deadline: a believed sample (the link pings every 2 s, the belief takes the best of its window) within 10 s. */
const CLOCK_MS = 10000;

/**
 * Start a VT rows' rig (row `row`'s own: its name, folders, PIN and logs) with playout links `which` ('patterns', 'stage-left'),
 * each at its app's own wire address with that app's own pairing token, after stopping the one before; resolves once its ready
 * line is read and checked.
 */
async function vtStart(row, which) {
  await vtStop();
  const pat = `patterns=${lan.host('patterns').addr}:${apps.patterns.wire.port},patterns`;
  const pm = `stage-left=${lan.host('playmap').addr}:${PlayMapDoors.wire},playmap`;
  const vt = new Stagewatch(run, lan.host('stagewatch'), null, {
    name: `vt-${row}`, ports: VT_PORTS, nodeId: VT_NODE,
    playout: which.map((w) => (w === 'patterns' ? pat : pm)),
    tokens: Object.fromEntries(which.map((w) => [w, w === 'patterns' ? apps.patterns.token : apps.playmap.token])),
  });
  apps.vt = vt;
  await vt.start(READY_MS);
  vt.checkReady();
  return vt;
}

/**
 * Take PlayMap's VT setup away again (VIDEO STOP on its playlist, then PLAYLIST DELETE, on its wire: PlayMap deletes no playlist
 * that plays), so the rows after the VT rows find PlayMap's air and its own context as they were before them, nothing playing:
 * resolves with what PlayMap said, or null when no setup stands.
 */
async function vtTeardown() {
  if (!vtPlaylist) return null;
  const stop = await apps.playmap.say(`VIDEO STOP ON ${vtPlaylist}`);
  if (!/^OK( |$)/.test(stop)) throw new Error(`PlayMap answered VIDEO STOP with ${JSON.stringify(stop.slice(0, 200))}`);
  const said = await apps.playmap.say(`PLAYLIST DELETE ${vtPlaylist}`);
  if (!/^OK( |$)/.test(said)) throw new Error(`PlayMap answered PLAYLIST DELETE with ${JSON.stringify(said.slice(0, 200))}`);
  vtPlaylist = null;
  await until(async () => (await apps.playmap.state()).video === null, HELLO_MS, "PlayMap's air with no VT item");
  return { stop, delete: said };
}

/** Stop the VT rows' rig (if one runs) and remove what it keeps a secret in. */
async function vtStop() {
  const vt = apps.vt;
  if (!vt) return null;
  delete apps.vt;
  const how = await vt.stop();
  run.removeSecretFiles(vt.secretFiles());
  return how;
}

/** After a VT row: neither the room's rig nor the VT rig counts any address of this LAN, both written in the row. */
async function vtLockoutAfter(r) {
  await lockoutAfter(r);
  if (!apps.vt) return;
  const { read, counted } = await S.readLockout(apps.vt, lanAddresses());
  r.facts.vtAuth = read;
  if (counted.length) throw new Error(`the VT rig counted failures against ${counted.join(', ')}`);
}

/** The link called `name` as the VT rig's `links` line reads it, once `ok(link)` holds, within ms. */
function vtLink(name, ok, ms, what) {
  return until(async () => {
    const l = (await apps.vt.links()).find((x) => x.name === name);
    if (!l) throw fatal(`the VT rig runs no link called ${name}`);
    return ok(l) ? l : null;
  }, ms, what);
}

/**
 * Paired, the kind and node its peer said: the link as read, within ms of `from`. Neither app proves itself to a connector yet and
 * neither wire offers TLS, so the record says why it is amber (13.16.2): proved at level 0 (`legacy`) over plain TCP (`plain`).
 */
async function vtPaired(name, kind, node, from, what) {
  const l = await vtLink(name, (x) => x.pairing === 'paired' && x.kind === kind && x.node === node, S.left(from, HELLO_MS), what);
  const ms = S.inTime(from, HELLO_MS, what);
  const r = l.record;
  if (r.state !== 'live' || r.level !== 0 || r.light !== 'amber' || !r.flags.includes('legacy') || !r.flags.includes('plain')) {
    throw new Error(`${name}'s record is ${JSON.stringify({ state: r.state, level: r.level, light: r.light, flags: r.flags })}, not live at level 0, amber for plain and legacy`);
  }
  return { ms, kind: l.kind, node: l.node, level: r.level, light: r.light, flags: r.flags, state: r.state, words: r.words };
}

/**
 * The VT the VT rig's frame (any screen's, no PIN) reads, from link `link`, beside the clip's own time left as its app's STATE says
 * it (`own()`: { leftS, running }), read just before: within VT_SLACK_S of it, more the time between the two reads. Resolves with
 * both once they agree, within ms.
 */
function vtAgrees(link, source, node, own, ms, what) {
  return until(async () => {
    const t0 = Date.now();
    const clip = await own();
    const f = await apps.vt.frame();
    const p = f.playout;
    if (!p || p.link !== link) return null;
    if (p.source !== source || p.node !== node) throw new Error(`the VT names ${JSON.stringify({ source: p.source, node: p.node })}, not ${source} ${node}`);
    if (!p.running || !clip.running) return null;
    const gap = (Date.now() - t0) / 1000;
    const off = Math.abs(p.remaining - clip.leftS);
    return off <= VT_SLACK_S + gap ? { vt: { title: p.title, remaining: p.remaining, duration: p.duration, running: p.running }, own: clip, offS: Math.round(off * 1000) / 1000, readsS: gap } : null;
  }, ms, what);
}

/** Patterns' clip on air as its STATE says it: its time left and whether it runs. */
async function patternsClip() {
  const v = (await apps.patterns.state()).video;
  if (!v) return { leftS: null, running: false };
  return { file: v.file, role: v.role, leftS: (v.lengthMs - v.positionMs) / 1000, running: v.playing === true && v.paused === false, paused: v.paused, pausable: v.pausable };
}

/** PlayMap's clip on air (its focused playlist's item) as its STATE says it. */
async function playmapClip() {
  const v = (await apps.playmap.state()).video;
  if (!v) return { leftS: null, running: false };
  return { file: v.file, role: v.role, leftS: (v.lengthMs - v.positionMs) / 1000, running: v.playing === true && v.paused !== true, paused: v.paused };
}

/**
 * A playout line through the VT rig's HTTP door with the operator's PIN: its receipt, which must be the peer's own answer — applied
 * (`applied`) or refused — with the peer's request in refs.peerRequest.
 */
async function vtPlayout(words, id, applied) {
  const sent = await apps.vt.api(['playout', ...words], id);
  const j = sent.json;
  if (!j) throw new Error(`playout ${words.join(' ')} answered ${sent.status} ${sent.text.slice(0, 200)}`);
  if (applied && (sent.status !== 200 || j.ok !== true || j.stage !== 'applied')) throw new Error(`playout ${words.join(' ')} answered ${sent.status} ${sent.text.slice(0, 300)}`);
  if (!applied && (j.ok !== false || typeof j.error !== 'string')) throw new Error(`playout ${words.join(' ')} was not refused: ${sent.status} ${sent.text.slice(0, 300)}`);
  if (!j.refs || !j.refs.peerRequest) throw new Error(`playout ${words.join(' ')}'s receipt names no peer request: ${sent.text.slice(0, 300)}`);
  return { status: sent.status, ok: j.ok, stage: j.stage, effect: j.effect, error: j.error, peerRequest: j.refs.peerRequest };
}

/** The playlist PlayMap's VT setup made, while it stands: the VT rows' last takes it away again. */
let vtPlaylist = null;

/** PlayMap's VT setup (a 60 s colour item in a playlist called show, focused and playing), said on its wire: the ids it answered. */
async function playmapVtSetup() {
  // `also`: an ERR that says the line would change nothing (PlayMap refuses those), taken as done.
  const ok = async (line, what, also = null) => {
    const said = await apps.playmap.say(line);
    if (also && also.test(said)) return said;
    if (!/^OK( |$)/.test(said)) throw new Error(`PlayMap answered ${what} with ${JSON.stringify(said.slice(0, 200))}`);
    return said.slice(3).trim();
  };
  const list = await ok('PLAYLIST NEW show', 'PLAYLIST NEW');
  vtPlaylist = list;
  const item = await ok(`PLAYLIST ADD ${list} color:#202020 Intro`, 'PLAYLIST ADD');
  await ok(`SET ${item} durationNs 60000000000`, 'SET durationNs');
  // A node's only playlist is its focused one already.
  const focus = await ok(`PLAYLIST FOCUS ${list}`, 'PLAYLIST FOCUS', /^ERR already focused$/);
  await ok(`PLAYLIST GO ${list} #1`, 'PLAYLIST GO');
  return { list, item, focus };
}

test("S4.1 the rig's VT link to Patterns", row({
  id: 'S4.1',
  title: "a rig's playout link to Patterns' wire (--playout patterns=<Patterns>:9697,patterns, Patterns' own token): paired by proof as Patterns within 5 s of its ready line, its VT the clip's own time left within 1.5 s, and playout patterns pause applied in Patterns' words with Patterns' STATE paused and the VT stopped within 5 s",
  apps: ['stagewatch', 'patterns'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('vt-s4-1.out'), run.log('vt-s4-1.err'), run.log('patterns.wire'));
  const clip = await apps.patterns.clip('s4.mp4', 90);
  const vt = await vtStart('s4-1', ['patterns']);
  const readyAt = Date.now();
  r.facts = { clip: { file: path.basename(clip.key), lengthMs: clip.lengthMs } };
  r.facts.paired = await vtPaired('patterns', 'patterns', ids.patterns, readyAt, "the VT rig's link to Patterns paired");
  r.facts.vt = await vtAgrees('patterns', 'Patterns', ids.patterns, patternsClip, S.left(readyAt, HELLO_MS), "the VT rig's VT on Patterns' clip");
  r.facts.vtMs = S.inTime(readyAt, HELLO_MS, "the VT rig's VT on Patterns' clip");
  // The clip the VT follows is the one on Patterns' air: its STATE names it the programme's.
  if (r.facts.vt.own.role !== 'program') throw new Error(`Patterns' clip has role "${r.facts.vt.own.role}", not program`);
  const sentAt = Date.now();
  r.facts.pause = await vtPlayout(['patterns', 'pause'], 'interop-s4-1', true);
  if (!/VIDEO PAUSE/.test(r.facts.pause.effect || '')) throw new Error(`the receipt's effect is "${r.facts.pause.effect}", not Patterns' answer to VIDEO PAUSE`);
  const paused = await until(async () => { const c = await patternsClip(); return c.paused === true && !c.running ? c : null; }, S.left(sentAt, HELLO_MS), "Patterns' clip paused");
  const stopped = await until(async () => { const p = (await vt.frame()).playout; return p && p.link === 'patterns' && p.running === false ? p : null; }, S.left(sentAt, HELLO_MS), "the VT stopped");
  r.facts.paused = { patterns: paused, vt: { remaining: stopped.remaining, running: stopped.running }, ms: S.inTime(sentAt, HELLO_MS, "Patterns' clip paused and the VT stopped") };
  await vtLockoutAfter(r);
}));

test("S4.2 the rig's VT link to PlayMap", row({
  id: 'S4.2',
  title: "a rig's playout link to PlayMap's wire (--playout stage-left=<PlayMap>:9941,playmap, PlayMap's own token) with PlayMap's VT setup playing: paired by proof as PlayMap within 5 s, its VT the item's own time left within 1.5 s, playout stage-left pause applied with PlayMap's STATE paused, and playout stage-left go show #9 refused in PlayMap's own words",
  apps: ['stagewatch', 'playmap'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('vt-s4-2.out'), run.log('vt-s4-2.err'), run.log('playmap.wire'), apps.playmap.engineLog);
  r.facts = { setup: await playmapVtSetup() };
  await until(async () => (await playmapClip()).running, HELLO_MS, "PlayMap's VT setup playing");
  const vt = await vtStart('s4-2', ['stage-left']);
  const readyAt = Date.now();
  r.facts.paired = await vtPaired('stage-left', 'playmap', ids.playmap, readyAt, "the VT rig's link to PlayMap paired");
  r.facts.vt = await vtAgrees('stage-left', 'PlayMap', ids.playmap, playmapClip, S.left(readyAt, HELLO_MS), "the VT rig's VT on PlayMap's item");
  r.facts.vtMs = S.inTime(readyAt, HELLO_MS, "the VT rig's VT on PlayMap's item");
  let sentAt = Date.now();
  r.facts.pause = await vtPlayout(['stage-left', 'pause'], 'interop-s4-2-pause', true);
  const paused = await until(async () => { const c = await playmapClip(); return c.paused === true && !c.running ? c : null; }, S.left(sentAt, HELLO_MS), "PlayMap's item paused");
  r.facts.paused = { playmap: paused, ms: S.inTime(sentAt, HELLO_MS, "PlayMap's item paused") };
  // PlayMap's own words for a GO to an item its playlist has not, said on its own wire: the oracle the receipt is held to.
  const own = await apps.playmap.say('PLAYLIST GO show #9');
  if (!/^ERR /.test(own)) throw new Error(`PlayMap answered PLAYLIST GO show #9 with ${JSON.stringify(own)}`);
  sentAt = Date.now();
  r.facts.go = await vtPlayout(['stage-left', 'go', 'show', '#9'], 'interop-s4-2-go', false);
  r.facts.go.ms = S.inTime(sentAt, HELLO_MS, 'the refused GO answered');
  if (r.facts.go.status !== 409) throw new Error(`the refused GO answered ${r.facts.go.status}, not 409`);
  const words = own.slice(4);
  if (!r.facts.go.error.includes(words)) throw new Error(`the receipt says "${r.facts.go.error}", not PlayMap's own "${words}"`);
  r.facts.go.playmapSaid = own;
  await vtLockoutAfter(r);
}));

test('S4.3 both at once', row({
  id: 'S4.3',
  title: "one rig with both playout links (patterns, then stage-left), each with its own app's token: both paired within 5 s, the config names each by the kind its peer said, the context carries link:patterns and link:stage-left each in its own record's light, and the VT names Patterns' running clip first and, once that is paused through its link, PlayMap's running item",
  apps: ['stagewatch', 'patterns', 'playmap'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('vt-s4-3.out'), run.log('vt-s4-3.err'));
  await apps.patterns.clip('s4-3.mp4', 90);
  const vt = await vtStart('s4-3', ['patterns', 'stage-left']);
  const readyAt = Date.now();
  r.facts = {
    paired: {
      patterns: await vtPaired('patterns', 'patterns', ids.patterns, readyAt, "the VT rig's link to Patterns paired"),
      'stage-left': await vtPaired('stage-left', 'playmap', ids.playmap, readyAt, "the VT rig's link to PlayMap paired"),
    },
  };
  // The rig builds its context once a second: one built before both links were read paired may still say them connecting.
  const pairedAt = Date.now();
  // PlayMap's item, paused by S4.2, runs again through its link.
  let at = Date.now();
  r.facts.play = await vtPlayout(['stage-left', 'play'], 'interop-s4-3-play', true);
  await until(async () => (await playmapClip()).running, S.left(at, HELLO_MS), "PlayMap's item running again");
  const config = await until(async () => {
    const c = await vt.config();
    return JSON.stringify((c.playoutLinks || []).map((l) => [l.name, l.kind])) === JSON.stringify([['patterns', 'patterns'], ['stage-left', 'playmap']]) ? c : null;
  }, S.left(readyAt, HELLO_MS), 'the config naming both links by kind');
  r.facts.config = config.playoutLinks;
  const items = await until(async () => {
    const c = await vt.context();
    const mine = (c.items || []).filter((i) => i.id === 'link:patterns' || i.id === 'link:stage-left');
    return c.at > pairedAt && mine.length === 2 ? mine : null;
  }, S.left(readyAt, HELLO_MS), "the link items of a context the rig built after both links were read paired");
  const links = await vt.links();
  r.facts.items = items.map((i) => ({ id: i.id, label: i.label, light: i.light, state: i.state }));
  for (const i of items) {
    const l = links.find((x) => `link:${x.name}` === i.id);
    if (i.kind !== 'playout' || i.light !== l.record.light) throw new Error(`${i.id} is ${i.kind} ${i.light}, not its record's ${l.record.light}`);
    if (i.light === 'red' || l.pairing !== 'paired') throw new Error(`${i.id} is ${i.light} (${i.state}), its link ${l.pairing}`);
  }
  r.facts.vtFirst = await vtAgrees('patterns', 'Patterns', ids.patterns, patternsClip, S.left(readyAt, HELLO_MS), "the VT on Patterns' clip, the first link's");
  at = Date.now();
  r.facts.pause = await vtPlayout(['patterns', 'pause'], 'interop-s4-3-pause', true);
  r.facts.vtThen = await vtAgrees('stage-left', 'PlayMap', ids.playmap, playmapClip, S.left(at, HELLO_MS), "the VT on PlayMap's item once Patterns' clip is paused");
  r.facts.vtThenMs = S.inTime(at, HELLO_MS, "the VT moved to PlayMap's item");
  await vtLockoutAfter(r);
}));

test('S4.4 clocks agree on one kernel', row({
  id: 'S4.4',
  title: "on one kernel each of the VT rig's links believes its peer's clock within 50 ms of its own, within 10 s, and the context carries no clock: item",
  apps: ['stagewatch', 'patterns', 'playmap'],
  deadlineMs: CLOCK_MS,
}, async (r) => {
  r.evidence.push(run.log('vt-s4-3.out'), run.log('vt-s4-3.err'));
  try {
    if (!apps.vt) throw new Error("S4.3's rig is not running");
    const from = Date.now();
    r.facts = {};
    for (const name of ['patterns', 'stage-left']) {
      const l = await vtLink(name, (x) => x.clock !== null && Math.abs(x.clock.offsetMs) <= CLOCK_AGREE_MS, S.left(from, CLOCK_MS), `${name}'s clock within ${CLOCK_AGREE_MS} ms`);
      if (l.clock.apart) throw new Error(`${name}'s clock is said apart at ${l.clock.offsetMs} ms`);
      r.facts[name] = { ...l.clock, samples: l.record.samples };
    }
    r.facts.ms = S.inTime(from, CLOCK_MS, 'both clocks within 50 ms');
    const clocks = ((await apps.vt.context()).items || []).filter((i) => /^clock:/.test(i.id));
    if (clocks.length) throw new Error(`the context carries ${clocks.map((i) => `${i.id} (${i.state})`).join(', ')}`);
    await vtLockoutAfter(r);
  } finally {
    r.facts = r.facts || {};
    r.facts.vtStopped = await vtStop();
    r.facts.playmapTeardown = await vtTeardown();
  }
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

test("S2.1c PlayMap's context names the peers it follows", row({
  id: 'S2.1c',
  title: "PlayMap's context names the peers it follows (Show Bus 1.3 §6 P2): its link:patterns follows @<Patterns' id> and its link:stagewatch follows @<the rig's id>, each row in its link item's light, the item saying \"not proved\" (both links level 0, the id a hello's)",
  apps: ['playmap', 'patterns', 'stagewatch'],
  deadlineMs: null,
}, async (r) => {
  r.evidence.push(run.log('playmap.wire'));
  const ctx = (await apps.playmap.context()).json;
  const follows = {};
  for (const [item, id] of [['link:patterns', ids.patterns], ['link:stagewatch', NODE_ID]]) {
    const own = (ctx.items || []).find((i) => i.id === item);
    const rows = (ctx.links || []).filter((l) => l.from === item && l.kind === 'follows');
    if (!own || rows.length !== 1 || rows[0].to !== `@${id}` || rows[0].light !== own.light || !/ · not proved/.test(own.state || '')) {
      throw new Error(`PlayMap's context says ${item} ${JSON.stringify(own)} with follows rows ${JSON.stringify(rows)}, not one row to @${id} in its light`);
    }
    follows[item] = { to: rows[0].to, light: rows[0].light, item: own.state };
  }
  r.facts = { follows };
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
  const contexts = { patterns: (await apps.patterns.context()).json, playmap: (await apps.playmap.context()).json, agmessage: await apps.agmessage.context() };
  const own = Object.fromEntries(Object.entries(contexts).map(([app, c]) => [app, ownLights(c)]));
  matrix.measure('ownItems', own);
  // A red item is named with the app's own words for it (PlayMap's machine item says which of its health rows is red: the host's CPU,
  // memory or disk, read from the bench it shares with whatever else runs there).
  const wordsOf = (app, id) => { const it = ((contexts[app] && contexts[app].items) || []).find((x) => x.id === id); return it && it.state ? ` ("${String(it.state).slice(0, 160)}")` : ''; };
  const red = Object.entries(own).flatMap(([app, items]) => Object.entries(items).filter(([, l]) => l === 'red').map(([id]) => `${app} ${id}${wordsOf(app, id)}`));
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

/** The words the rig refuses a proof in (its own table, LinkAcceptor): the same for every wrong proof, so none tells which. */
const PROOF_REFUSED = 'the proof did not verify: a wrong key, or another machine between us';

test('S2.8 a wrong proof from another machine', row({
  id: 'S2.8',
  title: "a wrong proof for Patterns' key from another machine is refused in the rig's own words and counted against that machine and that key alone: from the switch, LINK under Patterns' id and kid with another key's proof hears refused (class refused, retry 60) and keeps the public view; the rig's proof table counts the switch once for that kid and Patterns' address never, and neither PIN nor view-pass table counts anyone; Patterns' own link stays live and proved, its record not moved; the control first: the same client with Patterns' own key hears an ok verdict whose server proof verifies with that key, so the refusal is the proof's alone",
  apps: ['stagewatch', 'patterns'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('stagewatch.err'), run.log('patterns.wire'));
  const sw = apps.stagewatch;
  const pat = apps.patterns;
  const key = sw.keyFor('patterns');
  const target = { host: lan.host('stagewatch').addr, port: sw.ports.http, pin: sw.tls, node: { t: 'patterns', id: ids.patterns } };
  const before = await pat.stagewatchLink();
  if (before.state !== 'live' || before.link.level !== 1 || before.link.proof !== 'mutual') throw new Error(`Patterns' link to the rig is ${before.state}, level ${before.link.level}, before the row`);
  const proofsBefore = (await sw.auth()).proofs;
  const dialledAt = Date.now();
  // The control: this client's transcript and proof are LINK's, so the acceptor proves itself back to it with the same key.
  const right = await dialLink({ ...target, secret: key.secret });
  right.link.close();
  if (!right.proved || right.verdict.node.verified !== 'key' || right.verdict.level !== 1) throw new Error(`the control's verdict did not prove the acceptor: ${JSON.stringify({ ...right.verdict, proof: undefined })}`);
  rigVerdict = { can: right.verdict.can, stuckMs: right.verdict.stuckMs };
  // The control spoke for Patterns' id with its own key from the switch: once it has closed, the rig's record holds Patterns' one
  // link again, at Patterns' address, and its hello table marks no conflict on the id.
  const patAddr = lan.host('patterns').addr;
  const oneLink = await until(async () => {
    const rows = (await sw.nodes()).filter((n) => n.id === ids.patterns);
    return rows.length === 1 && rows[0].verified === 'key' && rows[0].links === 1 && rows[0].address === patAddr ? rows[0] : null;
  }, S.left(dialledAt, HELLO_MS), "the rig's record holding Patterns' one link after the control closed");
  const heardRow = ((await sw.heard()).peers || []).find((p) => p.id === ids.patterns);
  if (!heardRow || heardRow.conflict) throw new Error(`the rig's hello table ${heardRow ? `marks Patterns' id in conflict: ${JSON.stringify(heardRow.conflict)}` : 'has no row for Patterns'}`);
  // The wrong proof: Patterns' kid, another key's proof (a key nobody holds, made here and used for nothing else).
  const sentAt = Date.now();
  const wrong = await dialLink({ ...target, secret: 'swk_' + hex(16), kid: right.kid });
  const said = wrong.verdict;
  if (said.ok !== false || said.class !== 'refused' || said.error !== PROOF_REFUSED || said.retry !== 60) {
    wrong.link.close();
    throw new Error(`the wrong proof was answered ${JSON.stringify(said)}`);
  }
  const view = await wrong.link.next((m) => m.type === 'state', S.left(sentAt, HELLO_MS), 'the public frame after the refusal', wrong.link.messages.indexOf(said) + 1);
  wrong.link.close();
  const tables = await sw.auth();
  const counted = (addr) => (tables.proofs[`${addr} ${right.kid}`] || 0) - (proofsBefore[`${addr} ${right.kid}`] || 0);
  if (counted(lan.map.switch) !== 1) throw new Error(`the rig counted ${counted(lan.map.switch)} wrong proofs against the switch for ${right.kid}, not 1: ${JSON.stringify(tables.proofs)}`);
  const atPatterns =Object.entries(tables.proofs).filter(([k]) => k.startsWith(patAddr + ' '));
  if (atPatterns.length) throw new Error(`the rig counts wrong proofs against Patterns' address: ${JSON.stringify(atPatterns)}`);
  const after = await pat.stagewatchLink();
  // Not moved: still live and proved, live since before the control was dialled (the record's `since` is its wall time of entering
  // the state, read again on each STATE, so it is held to that moment and not to the millisecond).
  if (after.state !== 'live' || after.link.level !== 1 || after.link.proof !== 'mutual' || after.link.since > dialledAt) {
    throw new Error(`Patterns' link moved: ${before.link.state}@${before.link.since} → ${after.link.state}@${after.link.since}, level ${after.link.level}, proof ${after.link.proof}`);
  }
  r.facts = {
    control: { ok: right.verdict.ok, proved: right.proved, verified: right.verdict.node.verified, level: right.verdict.level, scope: right.verdict.scope, can: right.verdict.can },
    wrong: { class: said.class, error: said.error, retry: said.retry, publicFrame: view.type },
    proofs: { switch: counted(lan.map.switch), patterns: 0, kid: right.kid },
    rigRecord: { links: oneLink.links, verified: oneLink.verified, keyId: oneLink.keyId, conflict: heardRow.conflict || null },
    patterns: { state: after.state, level: after.link.level, proof: after.link.proof, since: after.link.since },
  };
  await lockoutAfter(r);
}));

/** S5.1's window and where a Linux bench is with it: a page grant is opened by WebView2 alone (Patterns' WebEngine). */
const S5_WINDOW = 'lobby';
const NOT_OPEN = 'its page is not open on this desk yet';

test('S5.1 a window granted to Patterns, staged in the preview, gated, never said on air', row({
  id: 'S5.1',
  title: "a window granted from the rig to Patterns reaches Patterns' desk, is staged in the preview, gated and never said on air: window new lobby /clock and window grant lobby <Patterns' id> with the PIN; the rig holds the grant acknowledged (held) and says once that Patterns took its address; Patterns' STATE bus.stagewatch.windows names it by its held form and never an address, granted over TLS, not ready because its page cannot open on this bench (no web engine off Windows, the finding), shown nowhere; SCREEN 1 WINDOW lobby is refused not ready with nothing changed, PVW WINDOW lobby opens EDIT SAFE by itself and stages it on PGM, read in the preview and not on air, the take is refused by the ready gate with nothing taken, and STATE never says it on air; a withdraw ends the grant on both (that it is shown from the air once ready is S5.1w's, a Windows walk)",
  apps: ['stagewatch', 'patterns'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('stagewatch.err'), run.log('patterns.wire'));
  const sw = apps.stagewatch;
  const pat = apps.patterns;
  const ok = async (segments, id, what) => {
    const said = await sw.api(segments, id);
    if (said.status !== 200 || !said.json || said.json.ok !== true) throw new Error(`${what} answered ${said.status} ${said.text.slice(0, 200)}`);
    return said.json;
  };
  await ok(['window', 'new', S5_WINDOW, '/clock'], 'interop-s5-1-new', 'window new');
  const errFrom = sw.proc.err.items.length;
  const grantedAt = Date.now();
  const granted = await ok(['window', 'grant', S5_WINDOW, ids.patterns], 'interop-s5-1-grant', 'window grant');
  // --state now holds the window's and the grant's addresses: registered, so the sweep looks for them everywhere they may not be.
  const saved = sw.savedWindows().find((w) => w.name === S5_WINDOW);
  if (!saved || saved.grants.length !== 1 || saved.grants[0].node !== ids.patterns || saved.grants[0].how !== 'page') throw new Error(`--state keeps ${JSON.stringify(saved)}`);
  const held = await until(async () => {
    const w = (await sw.windows()).find((x) => x.name === S5_WINDOW);
    const g = w && (w.grants || []).find((x) => x.node === ids.patterns);
    return g && g.state === 'held' ? { w, g } : null;
  }, S.left(grantedAt, HELLO_MS), "the rig holding Patterns' grant acknowledged");
  const heldMs = S.inTime(grantedAt, HELLO_MS, "the rig holding Patterns' grant acknowledged");
  const took = await sw.proc.err.wait((l) => l.line.includes(`took ${S5_WINDOW}'s address`), S.left(grantedAt, HELLO_MS), "the rig's took line", errFrom);
  // Patterns' own STATE: the grant by its held form, its address kept off every reader.
  const seen = await until(async () => {
    const st = await pat.state();
    const w = (st.bus.stagewatch.windows || []).find((x) => x.name === S5_WINDOW);
    return w && w.granted === true ? { st, w } : null;
  }, S.left(grantedAt, HELLO_MS), "Patterns' STATE naming the granted window");
  const w = seen.w;
  const wrong = [];
  if (w.how !== 'page' || w.link !== 'tls' || w.cannot !== '') wrong.push(`how ${w.how}, link ${w.link}, cannot "${w.cannot}"`);
  if (w.window !== held.w.id) wrong.push(`window ${w.window}, not the rig's ${held.w.id}`);
  if (w.ready !== false || w.why !== NOT_OPEN) wrong.push(`ready ${w.ready} "${w.why}"`);
  if (w.shown !== '' || w.screens.length || w.staged.length) wrong.push(`shown "${w.shown}" on ${JSON.stringify(w.screens)}, staged ${JSON.stringify(w.staged)}`);
  if (/\/w\//.test(w.held) || run.secrets.foundIn(JSON.stringify(seen.st.bus.stagewatch)).length) wrong.push(`held "${w.held}" or the row carries an address`);
  if (wrong.length) throw new Error(`Patterns' window row: ${wrong.join('; ')}`);
  const before = { airLook: seen.st.airLook, screen: seen.st.screens[0], editSafe: seen.st.editSafe };
  // Live on a screen only when ready: refused, nothing changed.
  const live = await pat.say(`SCREEN 1 WINDOW ${S5_WINDOW}`);
  if (!/^ERR .*not ready on this desk — its page is not open on this desk yet; nothing changed/.test(live)) throw new Error(`SCREEN 1 WINDOW answered ${JSON.stringify(live.slice(0, 240))}`);
  // Staged in the programme's preview, then the take refused by the gate, nothing taken.
  const staged = await pat.say(`PVW WINDOW ${S5_WINDOW}`);
  if (!/^OK( |$)/.test(staged)) throw new Error(`PVW WINDOW answered ${JSON.stringify(staged.slice(0, 240))}`);
  const take = await pat.say('SCREEN 1 TAKE');
  if (!/^ERR .*PVW holds Stagewatch window 'lobby', not ready — its page is not open on this desk yet — nothing taken/.test(take)) throw new Error(`SCREEN 1 TAKE answered ${JSON.stringify(take.slice(0, 240))}`);
  const st = await pat.state();
  const row2 = (st.bus.stagewatch.windows || []).find((x) => x.name === S5_WINDOW);
  // The preview stages it and the air never has it: never on air nor outputs closed. PVW WINDOW opens EDIT SAFE by itself (Patterns'
  // REMOTE.md), so the staged row is held here, whatever an earlier row left open. The take is refused by the ready gate (the page
  // cannot open on this bench), so this row proves staging and the gate, not that a ready window is shown from the air.
  if (!row2 || row2.screens.length || row2.shown === 'on air' || row2.shown === 'outputs closed') throw new Error(`after the refused take Patterns says the window is "${row2 && row2.shown}" on ${JSON.stringify(row2 && row2.screens)}`);
  if (st.editSafe !== true) throw new Error(`after PVW WINDOW Patterns' EDIT SAFE is ${st.editSafe}, not open`);
  if (row2.shown !== 'in the preview — not on air' || !row2.staged.includes('PGM')) throw new Error(`the staged window reads "${row2.shown}", staged ${JSON.stringify(row2.staged)}`);
  if (st.airLook !== before.airLook || JSON.stringify(st.screens[0]) !== JSON.stringify(before.screen)) throw new Error(`the air moved: look ${before.airLook} → ${st.airLook}, screen ${JSON.stringify(st.screens[0])}`);
  // What the preview had again, then the window withdrawn: the grant ends on both.
  const back = await pat.say('PVW PROGRAM');
  const goneAt = Date.now();
  await ok(['window', 'delete', S5_WINDOW], 'interop-s5-1-delete', 'window delete');
  await until(async () => (((await pat.state()).bus.stagewatch.windows || []).some((x) => x.name === S5_WINDOW) ? null : true), S.left(goneAt, HELLO_MS), "Patterns' grant ended");
  const goneMs = S.inTime(goneAt, HELLO_MS, "Patterns' grant ended");
  if (sw.savedWindows().some((x) => x.name === S5_WINDOW)) throw new Error('--state still keeps the deleted window');
  r.facts = {
    grant: { effect: granted.effect, rigState: held.g.state, heldMs, took: took.line.replace(/^\S+ /, '') },
    patterns: { how: w.how, link: w.link, held: w.held, ready: w.ready, why: w.why, shown: w.shown, editSafe: { before: before.editSafe, afterStage: st.editSafe } },
    gate: { live: live.slice(0, 200), staged: staged.slice(0, 120), take: take.slice(0, 200), afterTake: { shown: row2.shown, screens: row2.screens, staged: row2.staged } },
    restored: back.slice(0, 120),
    withdrawn: { ms: goneMs },
  };
  await lockoutAfter(r);
}));

/** A line an AI agent proposes to PlayMap through the rig's MCP endpoint (run_command with a target): its reply text. */
async function mcpPropose(line, reason) {
  const said = await apps.stagewatch.mcp('tools/call', { name: 'run_command', arguments: { command: line, target: ids.playmap, reason } });
  const text = said && said.result && said.result.content && said.result.content[0] && said.result.content[0].text;
  if (typeof text !== 'string') throw new Error(`MCP run_command answered ${JSON.stringify(said).slice(0, 300)}`);
  const id = /Queued for the operator's approval as (p\d+-[a-z0-9]+)/.exec(text);
  if (said.result.isError || !id) throw new Error(`MCP run_command did not queue "${line}": ${text.slice(0, 300)}`);
  return { text, id: id[1] };
}

/** PlayMap's journal rows (its own file in its home, one JSON line each) whose line is `line`. */
function playmapJournalRows(line) {
  const file = path.join(apps.playmap.folders.home, 'playmap.journal.jsonl');
  const rows = fs.existsSync(file) ? fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map((l) => { try { return JSON.parse(l); } catch { return null; } }) : [];
  return rows.filter((x) => x && x.line === line);
}

/** S3.5's playlist on PlayMap, made for the MCP rows and taken away by S3.7. */
let mcpList = null;

/** PlayMap's word on its own wire for a setup line: its answer after OK. */
async function playmapOk(line, what) {
  const said = await apps.playmap.say(line);
  if (!/^OK( |$)/.test(said)) throw new Error(`PlayMap answered ${what} with ${JSON.stringify(said.slice(0, 200))}`);
  return said.slice(3).trim();
}

test('S3.5 an MCP agent → PlayMap', row({
  id: 'S3.5',
  title: "an AI agent on the rig's MCP endpoint proposes a line to PlayMap (run_command with PlayMap's id as target): it is only ever a proposal, pending on the rig's desk for PlayMap, rated by PlayMap's own verified table; the operator approves it (desk approve with the PIN); PlayMap collects it and runs it once (its journal holds the line once, ok), its STATE says the playlist cued, and the rig's desk says done",
  apps: ['stagewatch', 'playmap'],
  deadlineMs: DESK_MS,
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log('playmap.wire'), apps.playmap.engineLog);
  const sw = apps.stagewatch;
  const pm = apps.playmap;
  mcpList = await playmapOk('PLAYLIST NEW mcp', 'PLAYLIST NEW');
  await playmapOk(`PLAYLIST ADD ${mcpList} color:#203020 Cue`, 'PLAYLIST ADD');
  const line = `PLAYLIST CUE ${mcpList} #1`;
  const table = ((await pm.context()).json.commands || []).find((c) => c.verb.startsWith('PLAYLIST CUE '));
  if (!table) throw new Error("PlayMap's context lists no PLAYLIST CUE row");
  const proposedAt = Date.now();
  const asked = await mcpPropose(line, 'the next cue');
  const pending = await deskHas((p) => p.id === asked.id && p.state === 'pending', S.left(proposedAt, DESK_MS), 'the MCP proposal pending on the rig\'s desk');
  if (pending.target.id !== ids.playmap || pending.line !== line) throw new Error(`the proposal is ${JSON.stringify({ target: pending.target, line: pending.line })}`);
  if (pending.risk !== table.risk) throw new Error(`the rig rates "${line}" ${pending.risk}, not ${table.risk} as PlayMap's own table says`);
  if (playmapJournalRows(line).length) throw new Error('PlayMap ran the line before anyone approved it');
  const decided = await sw.api(['desk', 'approve', asked.id], 'interop-s3-5');
  if (decided.status !== 200 || !decided.json || decided.json.ok !== true) throw new Error(`desk approve ${asked.id} answered ${decided.status} ${decided.text.slice(0, 200)}`);
  const done = await deskDone(asked.id, S.left(proposedAt, DESK_MS), "the rig's desk saying done for the MCP proposal");
  const doneMs = S.inTime(proposedAt, DESK_MS, "the rig's desk saying done for the MCP proposal");
  const cued = await until(async () => {
    const pl = ((await pm.state()).playlists || []).find((x) => x.id === mcpList);
    return pl && pl.state === 'cued' ? pl : null;
  }, S.left(proposedAt, DESK_MS), "PlayMap's playlist cued");
  const ran = playmapJournalRows(line);
  if (ran.length !== 1 || ran[0].ok !== true) throw new Error(`PlayMap's journal holds ${JSON.stringify(ran)} for "${line}", not one ok row`);
  r.facts = {
    mcp: asked.text.slice(0, 200),
    proposal: { id: asked.id, from: pending.from, risk: pending.risk, tableRisk: table.risk },
    decided: { status: decided.status, stage: decided.json.stage },
    desk: { state: done.state, decidedBy: done.decidedBy, ms: doneMs },
    playmap: { state: cued.state, item: cued.item, journal: { who: ran[0].who, text: ran[0].text } },
  };
  await lockoutAfter(r);
}));

test('S3.7 a rejection never runs, and is reported', row({
  id: 'S3.7',
  title: "a second MCP proposal to PlayMap (PLAYLIST GO), rejected by the operator with words (desk reject with the PIN): the rig's desk says rejected and never collected; a third, approved after the rejection (PLAYLIST LOOP ON), is collected and run by PlayMap, so PlayMap read its queue after the rejection; the rejected line has no row in PlayMap's journal, its playlist neither moved nor played, S3.5's cue still one row, and the rig's show report holds the Desk row \"Rejected … — <words>\" (the words reach the report alone: the proposal's own record carries none, a finding)",
  apps: ['stagewatch', 'playmap'],
  deadlineMs: DESK_MS,
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), apps.playmap.engineLog);
  const sw = apps.stagewatch;
  const pm = apps.playmap;
  if (!mcpList) throw new Error('S3.5 made no playlist');
  const line = `PLAYLIST GO ${mcpList} #1`;
  const cue = `PLAYLIST CUE ${mcpList} #1`;
  const words = 'not before the speaker is off';
  const proposedAt = Date.now();
  const asked = await mcpPropose(line, 'the cue after');
  await deskHas((p) => p.id === asked.id && p.state === 'pending', S.left(proposedAt, DESK_MS), 'the second MCP proposal pending');
  const before = ((await pm.state()).playlists || []).find((x) => x.id === mcpList);
  const decided = await sw.api(['desk', 'reject', asked.id, ...words.split(' ')], 'interop-s3-7');
  if (decided.status !== 200 || !decided.json || decided.json.ok !== true) throw new Error(`desk reject ${asked.id} answered ${decided.status} ${decided.text.slice(0, 200)}`);
  const rejected = await until(async () => { const p = await sw.proposal(asked.id); return p.state === 'rejected' ? p : null; }, S.left(proposedAt, DESK_MS), 'the proposal rejected');
  // The anchor: a line approved after the rejection is collected and run, so PlayMap read its queue after the rejection; a
  // rejected one is never handed to it (its record never says collected).
  const sentinelAt = Date.now();
  const sentinelLine = `PLAYLIST LOOP ${mcpList} ON`;
  const sentinel = await mcpPropose(sentinelLine, 'wrap the list');
  await deskHas((p) => p.id === sentinel.id && p.state === 'pending', S.left(sentinelAt, DESK_MS), 'the third MCP proposal pending');
  const approved = await sw.api(['desk', 'approve', sentinel.id], 'interop-s3-7-sentinel');
  if (approved.status !== 200 || !approved.json || approved.json.ok !== true) throw new Error(`desk approve ${sentinel.id} answered ${approved.status} ${approved.text.slice(0, 200)}`);
  const ranAfter = await deskDone(sentinel.id, S.left(sentinelAt, DESK_MS), "the rig's desk saying done for the line approved after the rejection");
  if (!(ranAfter.collected > rejected.decided)) throw new Error(`the line approved after the rejection was collected at ${ranAfter.collected}, not after the rejection at ${rejected.decided}`);
  const looped = await until(async () => {
    const pl = ((await pm.state()).playlists || []).find((x) => x.id === mcpList);
    return pl && pl.loop === true ? pl : null;
  }, S.left(sentinelAt, DESK_MS), "PlayMap's playlist looping");
  const sentinelRows = playmapJournalRows(sentinelLine);
  if (sentinelRows.length !== 1 || sentinelRows[0].ok !== true) throw new Error(`PlayMap's journal holds ${JSON.stringify(sentinelRows)} for "${sentinelLine}", not one ok row`);
  const still = await sw.proposal(asked.id);
  if (still.state !== 'rejected' || still.collected !== null) throw new Error(`the rejected proposal now reads ${still.state}, collected ${still.collected}`);
  const ran = playmapJournalRows(line);
  if (ran.length) throw new Error(`PlayMap ran the rejected line: ${JSON.stringify(ran)}`);
  const cued = playmapJournalRows(cue);
  if (cued.length !== 1) throw new Error(`PlayMap's journal holds ${cued.length} rows of S3.5's "${cue}", not one`);
  const after = ((await pm.state()).playlists || []).find((x) => x.id === mcpList);
  if (!after || after.state !== before.state || after.index !== before.index) throw new Error(`PlayMap's playlist moved after the rejection: ${JSON.stringify(before)} → ${JSON.stringify(after)}`);
  // The report as a reader sees it (its markup gone, its entities read): the desk's row "Rejected <id> (<who>): <title> — <words>".
  const text = (await sw.report()).replace(/<[^>]+>/g, '\n').replace(/&#(\d+);/g, (_, n) => String.fromCharCode(Number(n)))
    .replace(/&quot;/g, '"').replace(/&#x27;|&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
  const short = asked.id.split('-')[0];
  const desk = text.split('\n').map((l) => l.trim()).find((l) => l.startsWith(`Rejected ${short} (`) && l.endsWith(` — ${words}`));
  if (!desk) throw new Error(`the rig's report holds no Desk row "Rejected ${short} (…): … — ${words}"`);
  await playmapOk(`PLAYLIST DELETE ${mcpList}`, 'PLAYLIST DELETE');
  mcpList = null;
  r.facts = {
    // `words` is the proposal's own record: Approvals.Reject keeps no words of the operator's, so it reads empty (a finding, docs/WHATS_NEXT.md).
    proposal: { id: asked.id, state: still.state, decided: rejected.decided, collected: still.collected, words: still.words === undefined ? null : still.words },
    afterRejection: { id: sentinel.id, line: sentinelLine, collected: ranAfter.collected, state: ranAfter.state, loop: looped.loop, journalRows: sentinelRows.length },
    playmap: { journalRows: 0, cueRows: cued.length, playlist: { state: after.state, index: after.index } },
    report: desk.slice(0, 240),
  };
  await lockoutAfter(r);
}));

/**
 * The plan's stuck words beside a measured time: followers show no time within stuckMs + 4 s, where a stuckMs exists. The rig's
 * verdict carries one only when its `can` lists `stuck`; while it does not, there is no number to add 4 s to, and the row keeps the
 * contract's deadline (3 s stuck plus a 2 s beat, × 1.5 + 2 s) and says so rather than make one up.
 */
function stuckPlan(measuredMs) {
  const can = rigVerdict ? rigVerdict.can : null;
  if (rigVerdict && can.includes('stuck') && Number.isInteger(rigVerdict.stuckMs)) {
    return { stuckMs: rigVerdict.stuckMs, withinMs: rigVerdict.stuckMs + 4000, within: measuredMs <= rigVerdict.stuckMs + 4000, measuredMs };
  }
  return { stuckMs: null, can, measuredMs, note: rigVerdict ? "the rig's verdict lists no stuck and carries no stuckMs at this head" : "no verdict of the rig's was read (S2.8 did not run)" };
}

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
    plan: stuckPlan(noTimeMs),
    blank: { state: blank.state, why: blank.timer.why, light: blank.light === undefined ? null : blank.light },
    stuckSamples: samples,
    resumeMs,
    after: { text: back.timer.text, seconds: after.seconds, rigSeconds: rig, movedS: Math.round(moved * 10) / 10 },
  };
  await lockoutAfter(r);
}));

test('S6.2b a stuck Stagewatch: Patterns shows no time', row({
  id: 'S6.2b',
  title: "a stuck Stagewatch: Patterns' link stays live and amber (its record live and proved) and says stuck with no time from it, and its time comes back once the rig's show turns over; the time to no time is recorded beside the plan's stuckMs + 4 s, or beside the rig's verdict offering no stuckMs",
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
    // The link stays live and amber: its record live and proved (not the silence a stale link would show), its row amber.
    if (!l.link || l.link.state !== 'live' || l.link.level !== 1) throw new Error(`Patterns says stuck, but its link's record is ${l.link && l.link.state}, level ${l.link && l.link.level}`);
    if (l.light !== 'amber') throw new Error(`Patterns says stuck with its row ${l.light}, not amber`);
    return l;
  }, S.left(stall.stalledAt, STUCK_MS), 'Patterns saying the rig is stuck, its link live and amber, with no time');
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
    plan: stuckPlan(stuckMs),
    stuck: { state: stuck.state, light: stuck.light, headline: stuck.headline, record: { state: stuck.link.state, show: stuck.link.show, light: stuck.link.light, level: stuck.link.level } },
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
  // What Patterns' link's record says once it has settled live again: the same state, level, proof, TLS and floor read for 2 s on
  // end (recorded, not asserted: at this pin the redial after a gone link is a public view at level 0, TLS at first use, under the
  // floor it kept, a finding docs/WHATS_NEXT.md names; S2.9 restarts Patterns).
  let seen = { key: '', since: 0 };
  const rec = await until(async () => {
    const l = (await apps.patterns.stagewatchLink()).link || {};
    // Not `since`: Patterns reads it again from the wall clock on each STATE, so it moves by milliseconds between reads.
    const key = JSON.stringify([l.state, l.level, l.proof, l.tls, l.floor]);
    if (key !== seen.key) seen = { key, since: Date.now() };
    return l.state === 'live' && Date.now() - seen.since >= 2000 ? l : null;
  }, GONE_MS, "Patterns' link record settled live after the cable");
  r.facts = { before, stale, staleMs, gone, goneMs, back, backMs: S.since(upAt),
    patternsRecord: { state: rec.state, level: rec.level, proof: rec.proof, tls: rec.tls, floor: rec.floor || null, words: rec.words } };
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

test("S2.9 Patterns' key revoked on its live link", row({
  id: 'S2.9',
  title: "live revocation, after Patterns restarted in its own home proves its link afresh (within 5 s of its ready line, as S0.2 reads it): the rig revokes Patterns' node key (the desk's Revoke, the rig's revoke line, answered once every socket the key admitted was checked again); within 5 s Patterns' link hears it refused on its live socket and says so, its record no longer live and proved but held, its refusal `key` (13.16.3's word for a key the acceptor refused) and its proof none; the rig's own record holds no link of Patterns' proved by any key and --state keeps no such key; and a revoked key is no guess: no address of this LAN is counted in either table, and the proof table counts none at Patterns' address",
  apps: ['stagewatch', 'patterns'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log(`stagewatch.${apps.stagewatch.starts}.err`), run.log('patterns.wire'));
  const sw = apps.stagewatch;
  const pat = apps.patterns;
  const key = sw.keyFor('patterns');
  // Patterns restarted in its own home, with its key, so its link proves itself afresh (a link that went gone, as in S6.1b, comes
  // back at this pin as a public view at level 0 with the key held back: a finding, not this row's).
  const was = (await pat.stagewatchLink()).link || {};
  await pat.stop();
  await pat.start(pat.stagewatch, READY_MS, key);
  pat.checkReady();
  if (pat.ready.node !== ids.patterns) throw new Error(`Patterns came back as ${pat.ready.node}, not ${ids.patterns}`);
  const reproved = await linkProves(sw, pat, pat.readyAt);
  const at = await sw.revoke(key.id);
  const refused = await until(async () => {
    const l = await pat.stagewatchLink();
    const rec = l.link || {};
    return rec.state === 'held' && rec.refusal === 'key' && rec.proof === 'none' ? l : null;
  }, S.left(at, HELLO_MS), "Patterns' link refused once its key was revoked");
  const refusedMs = S.inTime(at, HELLO_MS, "Patterns' link refused once its key was revoked");
  const rows = (await sw.nodes()).filter((n) => n.id === ids.patterns && n.verified === 'key');
  if (rows.length) throw new Error(`the rig still holds a link of Patterns' proved by a key: ${JSON.stringify(rows)}`);
  if (sw.savedKeys().some((k) => k.id === key.id)) throw new Error("--state still keeps Patterns' revoked key");
  const proofs = Object.entries((await sw.auth()).proofs).filter(([k]) => k.startsWith(lan.host('patterns').addr + ' '));
  if (proofs.length) throw new Error(`the rig counts wrong proofs at Patterns' address: ${JSON.stringify(proofs)}`);
  const rec = refused.link;
  r.facts = {
    beforeRestart: { state: was.state, level: was.level, proof: was.proof }, reproved: { provedMs: reproved.provedMs, level: reproved.record.level, proof: reproved.record.proof },
    revokedAt: at, refusedMs,
    patterns: { state: refused.state, record: { state: rec.state, refusal: rec.refusal, level: rec.level, proof: rec.proof, light: rec.light, words: rec.words }, headline: refused.headline },
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
