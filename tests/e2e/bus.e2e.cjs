// The hello, against the rig on the Show Bus (--bus): with its web server bound to the network it says the app's hello where
// --bus-to sends it — kind main, proto 3, an inst, cap windows, its own http port, at most 1200 bytes — and with --bind local it
// says nothing at all (Show Bus §2). It hears a show app's hello on its bus port and lists that app in its context's peers; an id
// two running apps say (one id, two insts, alternating) is listed nowhere, and the rig's log names both machines; its own id said
// by another machine is its own amber health (bus:hello) and never a peer.
'use strict';
const { test, after } = require('node:test');
const assert = require('node:assert/strict');
const dgram = require('node:dgram');
const { Rig, until } = require('./rig.cjs');

const HELLO_BYTES = 1200;   // §14 hello.bytes
const rigs = [];
const sockets = [];
after(async () => {
  for (const s of sockets) s.close();
  for (const r of rigs) await r.stop();
});

/** A UDP socket on 127.0.0.1 and a free port, keeping every datagram it hears (with when it came). */
function listener() {
  return new Promise((resolve, reject) => {
    const socket = dgram.createSocket('udp4');
    const heard = [];
    socket.on('message', (msg) => heard.push({ at: Date.now(), bytes: msg }));
    socket.once('error', reject);
    socket.bind(0, '127.0.0.1', () => { sockets.push(socket); resolve({ socket, port: socket.address().port, heard }); });
  });
}

/** A free UDP port (bound, read, closed): the rig's bus port, which no socket here binds. */
async function freeUdpPort() {
  const l = await listener();
  sockets.splice(sockets.indexOf(l.socket), 1);
  await new Promise((r) => l.socket.close(r));
  return l.port;
}

test('with its web server on the network, the rig says its hello where --bus-to sends it: main, proto 3, its inst, cap windows, its http port, within 1200 bytes', async () => {
  const to = await listener();
  const rig = await Rig.create({ args: ['--bind', 'all', '--bus', await freeUdpPort(), '--bus-to', `127.0.0.1:${to.port}`] });
  rigs.push(rig);
  assert.ok(rig.ready.bus > 0, 'the ready line names the bus port');
  const first = await until(() => to.heard[0], 3000, 'a hello');
  assert.ok(first.bytes.length <= HELLO_BYTES, `${first.bytes.length} bytes`);
  const hello = JSON.parse(first.bytes.toString('utf8'));
  assert.equal(hello.t, 'main');
  assert.equal(hello.v, 1);
  assert.equal(hello.proto, 3);
  assert.equal(hello.id, rig.ready.node);
  assert.match(hello.inst, /^[0-9a-f]{8}$/);
  assert.ok(hello.caps.includes('windows'), JSON.stringify(hello.caps));
  assert.equal(hello.ports.http, rig.port);
  assert.equal(hello.ports.ws, rig.port);
  assert.equal(hello.ports.https, undefined, 'the rig has no certificate, so no https door');
  assert.equal(hello.presenting, undefined, 'presenting is a Slides agent\'s alone');
  assert.ok(Object.values(hello).every((v) => v !== null), 'no field is null: ' + JSON.stringify(hello));
  // Every 2 s, the same inst: one process.
  const second = await until(() => to.heard[1], 3500, 'a second hello');
  assert.ok(second.at - first.at >= 1500 && second.at - first.at <= 2800, `${second.at - first.at} ms between hellos`);
  assert.equal(JSON.parse(second.bytes.toString('utf8')).inst, hello.inst);
});

test('the rig hears a show app\'s hello and lists it in its context\'s peers; one id said by two running apps is listed nowhere', async () => {
  const bus = await freeUdpPort();
  const to = await listener();
  const rig = await Rig.create({ args: ['--bind', 'all', '--bus', bus, '--bus-to', `127.0.0.1:${to.port}`] });
  rigs.push(rig);
  const send = dgram.createSocket('udp4');
  sockets.push(send);
  const hello = (id, inst, host) => Buffer.from(JSON.stringify({ t: 'playmap', v: 1, proto: 3, id, inst, name: 'Stage left', host, ports: { http: 9940 }, caps: ['video', 'context'] }));
  let beat = 0;
  const timer = setInterval(() => {
    beat += 1;
    send.send(hello('pm5e6f7a', 'a1b2c3d4', 'STAGE-L'), bus, '127.0.0.1');
    // A copied install: one id from two running processes, each saying its own inst.
    send.send(beat % 2 ? hello('pm00c0f1', 'aaaaaaaa', 'STAGE-R') : hello('pm00c0f1', 'bbbbbbbb', 'STAGE-R2'), bus, '127.0.0.1');
  }, 400);
  try {
    const ctx = await until(async () => {
      const c = await rig.context();
      return (c.peers || []).some((p) => p.id === 'pm5e6f7a') && rig.log.includes('two machines say they are pm00c0f1') ? c : null;
    }, 8000, 'the heard app in the context and the conflict in the log');
    const app = ctx.peers.find((p) => p.id === 'pm5e6f7a');
    assert.equal(app.t, 'playmap');
    assert.equal(app.name, 'Stage left');
    assert.equal(app.heard, 'live');
    assert.equal(app.verified, '', 'a hello alone proves nothing');
    // Still alternating, still listed nowhere.
    await new Promise((r) => setTimeout(r, 1500));
    const again = await rig.context();
    assert.ok(!(again.peers || []).some((p) => p.id === 'pm00c0f1'), 'an id in conflict adds no row: ' + JSON.stringify(again.peers));
    assert.match(rig.log, /two machines say they are pm00c0f1: STAGE-R2? at 127\.0\.0\.1 and STAGE-R2? at 127\.0\.0\.1/);
  } finally {
    clearInterval(timer);
  }
});

test('with --bind local the rig says no hello at all', async () => {
  const to = await listener();
  const rig = await Rig.create({ args: ['--bind', 'local', '--bus', await freeUdpPort(), '--bus-to', `127.0.0.1:${to.port}`] });
  rigs.push(rig);
  await new Promise((r) => setTimeout(r, 5000));
  assert.equal(to.heard.length, 0, 'heard: ' + to.heard.map((h) => h.bytes.toString('utf8')).join(' | '));
  assert.match(rig.log, /hello: Not said — Remote is set to this PC; other apps reach it at 127\.0\.0\.1:\d+/);
});

test('its own id said by another machine (a copy of the install) makes its context\'s bus:hello amber, naming that machine, and is never a peer', async () => {
  const bus = await freeUdpPort();
  const to = await listener();
  const rig = await Rig.create({ args: ['--bind', 'all', '--bus', bus, '--bus-to', `127.0.0.1:${to.port}`] });
  rigs.push(rig);
  // Its own hello first (so it knows its inst), then a copy saying its id with another inst.
  const own = JSON.parse((await until(() => to.heard[0], 3000, 'its own hello')).bytes.toString('utf8'));
  const send = dgram.createSocket('udp4');
  sockets.push(send);
  const copy = Buffer.from(JSON.stringify({ t: 'main', v: 1, proto: 3, id: rig.ready.node, inst: own.inst === '0badc0de' ? '0badc0df' : '0badc0de', name: 'Copy', host: 'COPY-PC', ports: { http: 9924 } }));
  const timer = setInterval(() => send.send(copy, bus, '127.0.0.1'), 400);
  try {
    const ctx = await until(async () => {
      const c = await rig.context();
      return (c.items || []).some((i) => i.id === 'bus:hello') ? c : null;
    }, 8000, 'bus:hello in the context');
    const item = ctx.items.find((i) => i.id === 'bus:hello');
    assert.equal(item.kind, 'health');
    assert.equal(item.light, 'amber');
    assert.match(item.state, /another machine says it is this Stagewatch: COPY-PC at 127\.0\.0\.1/);
    assert.ok(!(ctx.peers || []).some((p) => p.id === rig.ready.node), 'its own id is never a peer: ' + JSON.stringify(ctx.peers));
    assert.match(rig.log, /hello: .* — another machine says it is this Stagewatch: COPY-PC at 127\.0\.0\.1 — give one of them a new id/);
  } finally {
    clearInterval(timer);
  }
});
