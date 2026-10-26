// The interop lane on its LAN topology: four real apps, each in a network namespace of its own on one bridge, at its own
// address (Stagewatch's rig 10.99.0.2, Patterns' bench 10.99.0.3, PlayMap's node 10.99.0.4, AGMessage 10.99.0.5) with its
// own loopback and every app on its default ports; the driver is the switch (10.99.0.1) and never listens on the Show Bus
// port. The rows run in order, each a node:test test that writes its matrix row; S0.1 starts the apps (the rig first, which
// the others link to) and every later row reads the apps it started. After every row the rig's lockout table is read: the
// switch's, PlayMap's and AGMessage's addresses are never counted (no app holds a key the rig made yet, and a request with
// none is not a guess), and Patterns' count is recorded beside the rows. When the rows end the apps are stopped, the files
// each keeps a secret in by design are removed, and the matrix is written, whatever happened.
'use strict';
const { test, before, after } = require('node:test');
const fs = require('node:fs');
const { Run } = require('./lib/run.cjs');
const { Lan } = require('./lib/lan.cjs');
const { Matrix } = require('./lib/matrix.cjs');
const { Stagewatch } = require('./apps/stagewatch.cjs');
const { Patterns } = require('./apps/patterns.cjs');
const { PlayMap } = require('./apps/playmap.cjs');
const { AGMessage } = require('./apps/agmessage.cjs');

/** The hosts in address order: .2, .3, .4, .5. */
const HOSTS = ['stagewatch', 'patterns', 'playmap', 'agmessage'];
/** S0.1's deadline: each app is ready within 60 s of its start, a cold .NET start on a CI runner included. */
const READY_MS = 60000;

let run;
let lan;
let matrix;
const apps = {};

before(async () => {
  run = new Run();
  matrix = new Matrix(run, 'lan');
  lan = await Lan.start(run, 'lan', HOSTS);
});

after(async () => {
  const exits = {};
  const removed = [];
  for (const [name, app] of Object.entries(apps).reverse()) {
    exits[name] = await app.stop();
    removed.push(...run.removeSecretFiles(app.secretFiles()));
  }
  if (matrix) {
    matrix.measure('exits', exits);
    matrix.removed(removed);
    matrix.write();
  }
  if (lan) await lan.close();
});

/** A row: the matrix records it when it runs (after `before`), and the test fails when the row does. */
const row = (spec, fn) => () => matrix.row(spec, fn)();

/**
 * The rig's lockout table read after a row: the addresses that must never be counted are asserted 0 (an address the rig
 * does not list has none) and written in the row; Patterns' count is recorded beside the rows.
 */
async function lockoutAfter(r) {
  const table = await apps.stagewatch.auth();
  const never = [lan.map.switch, lan.host('playmap').addr, lan.host('agmessage').addr];
  for (const a of never) r.auth[a] = table[a] || 0;
  const patterns = lan.host('patterns').addr;
  matrix.measure('authFailures', { ...(matrix.doc.measured.authFailures || {}), [`${r.id} ${patterns}`]: table[patterns] || 0 });
  const counted = never.filter((a) => r.auth[a] > 0);
  if (counted.length) throw new Error(`the rig counted failures against ${counted.map((a) => `${a} (${r.auth[a]})`).join(', ')}`);
}

test('S0.1 each app is ready in its own namespace', row({
  id: 'S0.1',
  title: 'each app is ready in its own namespace',
  apps: HOSTS,
  deadlineMs: READY_MS,
}, async (r) => {
  r.evidence.push(...HOSTS.flatMap((n) => [run.log(`${n}.out`), run.log(`${n}.err`)]), run.log('lan.out'));
  const sw = new Stagewatch(run, lan.host('stagewatch'));
  apps.stagewatch = sw;
  await sw.start(READY_MS);
  sw.checkReady();
  await sw.reachable();
  matrix.said('stagewatch', `rig ${sw.ready.node}`);

  const pat = new Patterns(run, lan.host('patterns'));
  apps.patterns = pat;
  await pat.start(sw.address, READY_MS);
  pat.checkReady();
  await pat.reachable();
  matrix.said('patterns', `bench ${pat.ready.build}, node ${pat.ready.node}`);

  const pm = new PlayMap(run, lan.host('playmap'));
  apps.playmap = pm;
  r.evidence.push(pm.engineLog);
  await pm.start({ addr: pat.wire.addr, port: pat.wire.port, token: pat.token }, sw.address, READY_MS);
  pm.checkReady();
  await pm.reachable();
  matrix.said('playmap', `node ${pm.nodeId}, renderer ${pm.up.renderer}`);

  const agm = new AGMessage(run, lan.host('agmessage'));
  apps.agmessage = agm;
  await agm.start(READY_MS);
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
  await lockoutAfter(r);
  // The control that keeps that reading honest: the reader sees a failure from this LAN's switch, and the right PIN clears it.
  r.facts.lockoutReader = await sw.proveLockoutReader(lan.map.switch);
}));
