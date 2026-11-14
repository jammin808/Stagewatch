// The interop lane on the show laptop's topology: the four real apps on one host, the maintainer's rig, in the run's own
// network namespace (lan.py laptop: its Wi-Fi a veth wifi0 at 10.99.0.2/24 with a default route, the access point at
// 10.99.0.1 its far end), every app on its default ports and the driver beside them. Each app is typed the others' addresses
// as the maintainer types them on one machine: 127.0.0.1 and the port. Two facts of one host are under test here, not hidden:
// the hellos are heard from the laptop's own addresses, never another's (its Wi-Fi's by the broadcast, and 127.0.0.1 where a
// sender's loopback copy lands, one listener's, which differs from run to run, so an app may name either), and every app that
// dials the rig does so at 127.0.0.1, from the laptop's addresses, which are also the operator's own browsers'
// addresses, so after every row both of the rig's lockout tables (wrong PINs; wrong view passes) hold 0 for 127.0.0.1 and for
// the laptop's address. Each table's reader is shown to see those two addresses by a control from each: the PIN's in S1c.1
// (a wrong PIN counted, the right one clearing it), the view passes' in S7.1, after the last read (a wrong window address
// counted; only the end of its minute clears it). The rig runs as the show laptop runs the app: on the Show Bus port, serving
// its certificate beside plain HTTP, and making two node keys, one for Patterns and one for PlayMap, each handed over in that
// app's own settings (AGMessage is given none: its keyed rows are another unit's); so every app that dials it speaks with a
// credential, or with none, and none is a guess. S1c.3 watches the laptop for two minutes with every app linked, and S7.4
// reads that no row counted any address and that neither table counts one at the end. When the rows end the apps are
// stopped, the files each keeps a secret in by design are removed, and the matrix is written, whatever happened.
//
// Deadlines are the contract's number × 1.5 + 2 s, as on the LAN (lan.interop.cjs): a hello every 2 s → heard within 5 s, a
// link to the rig live within the gone bound (12 s → 20 s), each counted from the moment the last app said it was ready.
'use strict';
const { test, before, after } = require('node:test');
const fs = require('node:fs');
const { Run } = require('./lib/run.cjs');
const { Lan } = require('./lib/lan.cjs');
const { Matrix } = require('./lib/matrix.cjs');
const { Stagewatch, PORTS } = require('./apps/stagewatch.cjs');
const { Patterns, DOORS } = require('./apps/patterns.cjs');
const { PlayMap } = require('./apps/playmap.cjs');
const { AGMessage } = require('./apps/agmessage.cjs');
const { until, sleep } = require('./lib/inbox.cjs');
const S = require('./lib/scenarios.cjs');

const APPS = ['stagewatch', 'patterns', 'playmap', 'agmessage'];
/** Each app is ready within 60 s of its start, a cold .NET start on a CI runner included. */
const READY_MS = 60000;
/** A hello every 2 s: heard within 5 s. A link's step (a clip, a blackout) answers within the same. */
const HELLO_MS = 5000;
/** Every link to the rig live within the gone bound (12 s × 1.5 + 2 s), as after the rig's restart on the LAN. */
const LINK_MS = 20000;
/** What the maintainer types on one machine. */
const LOOPBACK = '127.0.0.1';
/** The rig's node keys: Patterns' and PlayMap's. */
const RIG_KEYS = 2;
/** S1c.3 watches the laptop this long with every app linked: past a minute's lockout window twice over. */
const WATCH_MS = 120000;
/** S1c.3 reads the lockout tables this often during its watch. */
const WATCH_EVERY_MS = 5000;

let run;
let lan;
let matrix;
let laptop;
const apps = {};
const ids = {};

before(async () => {
  run = new Run('laptop');
  matrix = new Matrix(run, 'laptop');
  lan = await Lan.start(run, 'laptop');
  laptop = lan.host('laptop');
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

/** The rig's two lockout tables after a row: the laptop's addresses, every app's and the operator's own, are never counted. */
const lockoutAfter = (r) => S.lockoutAfter(apps.stagewatch, r, [LOOPBACK, laptop.addr]);

test('S1c.1 every app hears every other on one laptop', row({
  id: 'S1c.1',
  title: "every app hears every other on one laptop, each hello from the laptop's own addresses (its Wi-Fi's or 127.0.0.1), and every link to the rig is live at 127.0.0.1",
  apps: APPS,
  deadlineMs: [READY_MS, HELLO_MS, LINK_MS],
}, async (r) => {
  r.evidence.push(...APPS.flatMap((n) => [run.log(`${n}.out`), run.log(`${n}.err`)]), run.log('lan.out'), run.log('patterns.wire'),
    run.log('playmap.wire'), run.log('agmessage-admin.out'));
  const rigAt = `${LOOPBACK}:${PORTS.http}`;
  const sw = new Stagewatch(run, laptop, { keys: RIG_KEYS });
  apps.stagewatch = sw;
  await sw.start(READY_MS);
  sw.checkReady();
  matrix.said('stagewatch', `rig ${sw.ready.node}`);

  const pat = new Patterns(run, laptop);
  apps.patterns = pat;
  await pat.start(rigAt, READY_MS, sw.keyFor('patterns'));
  pat.checkReady();
  matrix.said('patterns', `bench ${pat.ready.build}, node ${pat.ready.node}`);
  ids.patterns = pat.ready.node;

  const pm = new PlayMap(run, laptop);
  apps.playmap = pm;
  r.evidence.push(pm.engineLog);
  await pm.start({ addr: LOOPBACK, port: DOORS.wire, token: pat.token }, rigAt, READY_MS, sw.keyFor('playmap'));
  pm.checkReady();
  matrix.said('playmap', `node ${pm.nodeId}, renderer ${pm.up.renderer}`);
  ids.playmap = pm.nodeId;

  const agm = new AGMessage(run, laptop);
  apps.agmessage = agm;
  await agm.start(rigAt, READY_MS);
  const readyAt = Date.now();
  agm.checkReady();
  matrix.said('agmessage', `build ${agm.health.build}`);
  ids.agmessage = (await agm.context()).node.id;

  // One host: every app's process is in the laptop's one namespace, the driver's own.
  const laptopNs = fs.readlinkSync('/proc/self/ns/net');
  for (const name of APPS) {
    const ns = apps[name].proc.namespace();
    if (ns !== laptopNs) throw new Error(`${name} runs in ${ns}, not the laptop's ${laptopNs}`);
  }
  // The controls that keep the rows' lockout readings honest: the PIN's reader sees a wrong PIN from each of the laptop's
  // addresses, and the right PIN clears it.
  const lockoutReader = { [LOOPBACK]: await sw.proveLockoutReader(LOOPBACK, LOOPBACK), [laptop.addr]: await sw.proveLockoutReader(laptop.addr, laptop.addr) };
  const agmessageContextToken = await agm.learnContextToken();
  if (agmessageContextToken.node !== ids.agmessage) throw new Error(`AGMessage's context read with its token is ${agmessageContextToken.node}'s`);

  // Each hello from the laptop's own addresses: its Wi-Fi's (the broadcast) or 127.0.0.1 (a loopback copy), as each app names
  // the one it heard last.
  const own = [laptop.addr, LOOPBACK];
  const patterns = await S.patternsHears(pat, [
    { kind: 'playmap', id: ids.playmap, addr: own },
    { kind: 'messenger', id: ids.agmessage, addr: own },
  ], S.left(readyAt, HELLO_MS));
  const playmap = await S.playmapHears(pm, [
    { t: 'patterns', id: ids.patterns, addr: own, followed: true },
    { t: 'messenger', id: ids.agmessage, addr: own },
  ], S.left(readyAt, HELLO_MS));
  const agmessage = await S.agmessageHears(agm, [{ t: 'patterns', id: ids.patterns, addr: own }], S.left(readyAt, HELLO_MS));
  const heardMs = S.since(readyAt);

  // Every link to the rig live, each at the address the maintainer typed: PlayMap's link dials 127.0.0.1 and trusts the time
  // it shows, Patterns follows the rig it was typed (named 127.0.0.1 until the rig's hello names its room, and the room's name
  // once it does), AGMessage's settings name it and its link is connected.
  const room = (await sw.context()).node.name;
  const links = await until(async () => {
    const l = await S.stagewatchLinks(apps);
    if (!S.allLive(l)) throw new Error(`not every link to the rig is live: ${JSON.stringify(l)}`);
    if (l.playmap.host !== rigAt) throw new Error(`PlayMap's link dials ${l.playmap.host}, not ${rigAt}`);
    if (l.patterns.name !== LOOPBACK && l.patterns.name !== room) throw new Error(`Patterns follows ${l.patterns.name}, not ${LOOPBACK} or ${room}`);
    const url = (await agm.settings()).stagewatchUrl;
    if (url !== `http://${rigAt}`) throw new Error(`AGMessage's settings link it to ${url}, not http://${rigAt}`);
    return { ...l, agmessage: { ...l.agmessage, url } };
  }, S.left(readyAt, LINK_MS), `every link to the rig live at ${rigAt}`);

  r.facts = {
    readyMs: Object.fromEntries(APPS.map((n) => [n, apps[n].readyMs])),
    laptop: laptop.addr,
    lockoutReader,
    agmessageContextToken,
    heardMs,
    patterns,
    playmap,
    agmessage,
    links,
    linksMs: S.since(readyAt),
  };
  await lockoutAfter(r);
}));

test('S1c.2 PlayMap follows Patterns at 127.0.0.1', row({
  id: 'S1c.2',
  title: "PlayMap follows Patterns' clip and blackout at 127.0.0.1, paired by proof",
  apps: ['playmap', 'patterns'],
  deadlineMs: HELLO_MS,
}, async (r) => {
  r.evidence.push(run.log('playmap.wire'), run.log('patterns.wire'), apps.playmap.engineLog);
  r.facts = await S.playmapFollowsPatterns(apps.playmap, apps.patterns, `${LOOPBACK}:${DOORS.wire}`, HELLO_MS);
  await lockoutAfter(r);
}));

test('S1c.3 the rig on the laptop, and nobody locks the laptop out', row({
  id: 'S1c.3',
  title: "the rig on the show laptop's Show Bus: it hears every app from the laptop's own addresses and every app hears it; Patterns and PlayMap speak with their own node keys; then for two minutes, with every app linked, neither of the rig's lockout tables counts 127.0.0.1 or the laptop's address at any read",
  apps: APPS,
  deadlineMs: [HELLO_MS, WATCH_MS],
}, async (r) => {
  r.evidence.push(run.log('stagewatch.out'), run.log('patterns.wire'), run.log('playmap.wire'), run.log('agmessage-admin.out'));
  const sw = apps.stagewatch;
  const own = [laptop.addr, LOOPBACK];
  const t = Date.now();
  const rig = await S.rigHears(sw, [
    { t: 'patterns', id: ids.patterns, addr: own },
    { t: 'playmap', id: ids.playmap, addr: own },
    { t: 'messenger', id: ids.agmessage, addr: own, helloOnly: true },
  ], HELLO_MS);
  const node = sw.ready.node;
  // Patterns draws the rig it follows no better than its link's record (Show Bus 1.3 §13.16.4): `own` reads the rig's own context.
  const ownContext = async () => { const c = await sw.context(); return { light: c.light, headline: c.headline }; };
  const patterns = await S.patternsHears(apps.patterns, [{ kind: 'main', id: node, addr: own, linked: true, own: ownContext }], S.left(t, HELLO_MS));
  const playmap = await S.playmapHears(apps.playmap, [{ t: 'main', id: node, addr: own }], S.left(t, HELLO_MS));
  const agmessage = await S.agmessageHears(apps.agmessage, [{ t: 'main', id: node, addr: own }], S.left(t, HELLO_MS));
  const heardMs = S.inTime(t, HELLO_MS, 'the rig and every app hearing each other');
  // Patterns' and PlayMap's node links speak with their own keys, as the rig's own record says.
  const keyed = await until(async () => {
    const rows = await sw.nodes();
    const wrong = [];
    for (const [app, id] of [['patterns', ids.patterns], ['playmap', ids.playmap]]) {
      const x = rows.find((n) => n.id === id);
      if (!x || x.verified !== 'key' || x.keyId !== sw.keyFor(app).id) wrong.push(`${app} ${JSON.stringify(x || null)}`);
    }
    if (wrong.length) throw new Error(`not speaking with its own key: ${wrong.join('; ')}`);
    return rows.map((n) => ({ t: n.t, id: n.id, verified: n.verified, keyId: n.keyId }));
  }, HELLO_MS, "Patterns' and PlayMap's node links speaking with their own keys");
  // The watch: every app linked at every read, and the laptop's two addresses counted in neither table.
  const watchFrom = Date.now();
  const watch = { reads: 0, linked: 0 };
  while (S.since(watchFrom) < WATCH_MS) {
    await sleep(WATCH_EVERY_MS);
    const l = await S.stagewatchLinks(apps);
    if (!S.allLive(l)) throw new Error(`${S.since(watchFrom)} ms into the watch not every link to the rig is live: ${JSON.stringify(l)}`);
    watch.linked++;
    const { counted } = await S.readLockout(sw, [LOOPBACK, laptop.addr]);
    if (counted.length) throw new Error(`${S.since(watchFrom)} ms into the watch the rig counted failures against ${counted.join(', ')}`);
    watch.reads++;
  }
  r.facts = { rig, patterns, playmap, agmessage, heardMs, keyed, watch: { ms: S.since(watchFrom), ...watch } };
  await lockoutAfter(r);
}));

test("S7.4 no app's address is ever counted", row({
  id: 'S7.4',
  title: "no address is ever counted: every row before this one read both of the rig's lockout tables after it with the laptop's addresses at 0, and now neither table counts any address at all",
  apps: APPS,
  deadlineMs: null,
}, async (r) => {
  r.facts = await S.nobodyCounted(apps.stagewatch, matrix, 'S7.4');
  await lockoutAfter(r);
}));

test('S7.1 no secret anywhere', row({
  id: 'S7.1',
  title: "no secret of the register (the PIN, the apps' tokens, AGMessage's admin and context tokens) in any log, frame, STATE, context, report or capture the run kept",
  apps: APPS,
  deadlineMs: null,
}, async (r) => {
  await lockoutAfter(r);
  // The control that keeps every row's view-pass reading honest, after the last one: the view passes' reader sees a wrong
  // window address from each of the laptop's addresses (only its minute clears it, so no read after this one asserts them).
  const viewPassReader = {
    [LOOPBACK]: await apps.stagewatch.proveViewPassReader(LOOPBACK, LOOPBACK),
    [laptop.addr]: await apps.stagewatch.proveViewPassReader(laptop.addr, laptop.addr),
  };
  r.facts = { viewPassReader, ...(await S.noSecretAnywhere({ run, matrix, apps })) };
  matrix.removed(r.facts.removed);
  matrix.measure('exits', r.facts.exits);
}));
