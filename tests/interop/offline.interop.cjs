// The interop lane on the offline laptop's topology: the four real apps on one host with no network at all, only its loopback
// (lan.py offline, the run's own network namespace with lo up and nothing else), every app on its default ports and the driver
// beside them. Nothing is heard on the Show Bus here (a hello has no network to go out on: discovery with no network is Show
// Bus 1.4's carrier, row S1b.2, not built), so every link is an address the maintainer types: 127.0.0.1 and the port. The rig
// runs as the laptop runs the app with no network: no Show Bus port, its certificate served beside plain HTTP, and two node
// keys, one for Patterns and one for PlayMap, each handed over in that app's own settings (AGMessage is given none: its keyed
// rows are another unit's). Every app that dials the rig does so from 127.0.0.1, which is also the operator's own browsers'
// address, so after every row both of the rig's lockout tables (wrong PINs; wrong view passes) hold 0 for 127.0.0.1. Each
// table's reader is shown to see that address by a control: the PIN's in S1b.1 (a wrong PIN counted, the right one clearing
// it), the view passes' in S7.1, after the last read (a wrong window address counted; only the end of its minute clears it).
// When the rows end the apps are stopped, the files each keeps a secret in by design are removed, and the matrix is written,
// whatever happened.
//
// Deadlines are the contract's number × 1.5 + 2 s, as on the LAN (lan.interop.cjs): every link live within the gone bound
// (12 s → 20 s), counted from the moment the last app said it was ready.
'use strict';
const { test, before, after } = require('node:test');
const fs = require('node:fs');
const os = require('node:os');
const { Run } = require('./lib/run.cjs');
const { Lan } = require('./lib/lan.cjs');
const { Matrix } = require('./lib/matrix.cjs');
const { Stagewatch, PORTS } = require('./apps/stagewatch.cjs');
const { Patterns, DOORS } = require('./apps/patterns.cjs');
const { PlayMap } = require('./apps/playmap.cjs');
const { AGMessage } = require('./apps/agmessage.cjs');
const { until } = require('./lib/inbox.cjs');
const S = require('./lib/scenarios.cjs');

const APPS = ['stagewatch', 'patterns', 'playmap', 'agmessage'];
/** Each app is ready within 60 s of its start, a cold .NET start on a CI runner included. */
const READY_MS = 60000;
/** Every link live within the gone bound (12 s × 1.5 + 2 s), as after the rig's restart on the LAN. */
const LINK_MS = 20000;
/** What the maintainer types on one machine, and the only address this laptop has. */
const LOOPBACK = '127.0.0.1';
/** The rig's node keys: Patterns' and PlayMap's. */
const RIG_KEYS = 2;

let run;
let lan;
let matrix;
let laptop;
const apps = {};
const ids = {};

before(async () => {
  run = new Run('offline');
  matrix = new Matrix(run, 'offline');
  lan = await Lan.start(run, 'offline');
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

/** The rig's two lockout tables after a row: 127.0.0.1, every app's address and the operator's own, is never counted. */
const lockoutAfter = (r) => S.lockoutAfter(apps.stagewatch, r, [LOOPBACK]);

test('S1b.1 typed 127.0.0.1 addresses link every pair', row({
  id: 'S1b.1',
  title: "on a laptop with no network, the addresses typed as 127.0.0.1 link every pair: PlayMap follows Patterns, paired by proof; every app's link to the rig is live, Patterns' and PlayMap's speaking with their own node keys; and 127.0.0.1 is counted in neither of the rig's lockout tables",
  apps: APPS,
  deadlineMs: [READY_MS, LINK_MS],
}, async (r) => {
  r.evidence.push(...APPS.flatMap((n) => [run.log(`${n}.out`), run.log(`${n}.err`)]), run.log('lan.out'), run.log('patterns.wire'),
    run.log('playmap.wire'), run.log('agmessage-admin.out'));
  if (laptop.addr !== LOOPBACK || lan.map.switch !== '') throw new Error(`the offline topology is not the loopback alone: ${JSON.stringify(lan.map)}`);
  const rigAt = `${LOOPBACK}:${PORTS.http}`;
  const sw = new Stagewatch(run, laptop, { keys: RIG_KEYS, hello: false });
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

  // One host with no network: every app's process is in the laptop's one namespace, the driver's own, whose only interface
  // is its loopback.
  const laptopNs = fs.readlinkSync('/proc/self/ns/net');
  for (const name of APPS) {
    const ns = apps[name].proc.namespace();
    if (ns !== laptopNs) throw new Error(`${name} runs in ${ns}, not the laptop's ${laptopNs}`);
  }
  const interfaces = Object.keys(os.networkInterfaces());
  if (interfaces.join() !== 'lo') throw new Error(`the offline laptop has interfaces ${interfaces.join(', ')}, not lo alone`);
  // The control that keeps the rows' lockout readings honest: the PIN's reader sees a wrong PIN from 127.0.0.1, and the right
  // PIN clears it.
  const lockoutReader = await sw.proveLockoutReader(LOOPBACK, LOOPBACK);
  const agmessageContextToken = await agm.learnContextToken();
  if (agmessageContextToken.node !== ids.agmessage) throw new Error(`AGMessage's context read with its token is ${agmessageContextToken.node}'s`);

  // PlayMap follows Patterns at 127.0.0.1, paired by proof.
  const followed = await until(async () => {
    const l = (await pm.state()).links.patterns;
    if (l.state !== 'live' || l.paired !== true) throw new Error(`PlayMap's link to Patterns is ${l.state}, paired ${l.paired}: ${l.detail}`);
    if (l.host !== `${LOOPBACK}:${DOORS.wire}`) throw new Error(`PlayMap's link to Patterns is to ${l.host}, not ${LOOPBACK}:${DOORS.wire}`);
    return { state: l.state, paired: l.paired, host: l.host };
  }, S.left(readyAt, LINK_MS), "PlayMap's link to Patterns live and paired at 127.0.0.1");
  // Every link to the rig live at 127.0.0.1, and Patterns' and PlayMap's node links speaking with their own keys, as the rig's
  // own record says.
  const links = await until(async () => {
    const l = await S.stagewatchLinks(apps);
    if (!S.allLive(l)) throw new Error(`not every link to the rig is live: ${JSON.stringify(l)}`);
    if (l.playmap.host !== rigAt) throw new Error(`PlayMap's link dials ${l.playmap.host}, not ${rigAt}`);
    const url = (await agm.settings()).stagewatchUrl;
    if (url !== `http://${rigAt}`) throw new Error(`AGMessage's settings link it to ${url}, not http://${rigAt}`);
    return { ...l, agmessage: { ...l.agmessage, url } };
  }, S.left(readyAt, LINK_MS), `every link to the rig live at ${rigAt}`);
  const keyed = await until(async () => {
    const rows = await sw.nodes();
    const wrong = [];
    for (const [app, id] of [['patterns', ids.patterns], ['playmap', ids.playmap]]) {
      const x = rows.find((n) => n.id === id);
      if (!x || x.verified !== 'key' || x.keyId !== sw.keyFor(app).id || x.address !== LOOPBACK) wrong.push(`${app} ${JSON.stringify(x || null)}`);
    }
    if (wrong.length) throw new Error(`not speaking with its own key from 127.0.0.1: ${wrong.join('; ')}`);
    return rows.map((n) => ({ t: n.t, id: n.id, verified: n.verified, keyId: n.keyId, address: n.address }));
  }, S.left(readyAt, LINK_MS), "Patterns' and PlayMap's node links speaking with their own keys");
  const linkedMs = S.inTime(readyAt, LINK_MS, 'every pair linked at 127.0.0.1');

  r.facts = {
    readyMs: Object.fromEntries(APPS.map((n) => [n, apps[n].readyMs])),
    interfaces,
    lockoutReader,
    agmessageContextToken,
    followed,
    links,
    keyed,
    linkedMs,
  };
  await lockoutAfter(r);
}));

test("S7.4 no app's address is ever counted", row({
  id: 'S7.4',
  title: "no address is ever counted: every row before this one read both of the rig's lockout tables after it with 127.0.0.1 at 0, and now neither table counts any address at all",
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
  // window address from 127.0.0.1 (only its minute clears it, so no read after this one asserts it).
  const viewPassReader = { [LOOPBACK]: await apps.stagewatch.proveViewPassReader(LOOPBACK, LOOPBACK) };
  r.facts = { viewPassReader, ...(await S.noSecretAnywhere({ run, matrix, apps })) };
  matrix.removed(r.facts.removed);
  matrix.measure('exits', r.facts.exits);
}));
