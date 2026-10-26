// A stuck show, against the rig: its show thread stops (the control line `stall <ms>`), and a show app's link on /ws
// hears what Stagewatch itself says then — the plain beat {"type":"beat"} while the last frame is still fresh, then exactly
// {"type":"beat","live":false}, every 2 s, and no frame — until the show turns over again, when a frame comes first. A link
// dialled while the show is stuck hears its verdict, then only the stuck beat, never the frozen frame. Pings are answered
// throughout (they never wait for the show thread), and a command sent meanwhile is only accepted, then applied once the
// show runs. A key first used while the show is stuck is still kept when the rig is stopped. These bytes are the ones
// Patterns' fake Stagewatch replays for a stuck show.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { Rig } = require('./rig.cjs');

const BEAT = '{"type":"beat"}';
const STUCK = '{"type":"beat","live":false}';
const STUCK_AFTER = 3000;   // SnapshotHub.StuckAfter
const BEAT_EVERY = 2000;    // WebServer.HeartbeatEvery
const STALL = 9000;

let rig, link, key2, key3;
const links = [];
before(async () => {
  rig = await Rig.create({ nodeKeys: 3 });
  [, key2, key3] = rig.keys;
  link = await rig.link('node=pat00001&t=patterns', rig.keys[0].secret);
  links.push(link);
  await link.next((j) => j.type === 'auth' && j.ok === true, 5000, 'the verdict');
  await link.next((j) => j.type === 'state', 5000, 'a first frame');
});
after(async () => {
  for (const l of links) await l.close();
  if (rig) await rig.stop();
});

const pause = (ms) => new Promise((r) => setTimeout(r, ms));

test('a stalled show sends the plain beat, then the stuck beat, exactly, every 2 s and no frame; a link dialled meanwhile hears its verdict and the stuck beat only; then a frame first, and pings are answered throughout', async () => {
  const pings = [];
  let t = 0;
  const pinger = setInterval(() => { t += 1; pings.push({ t, at: Date.now() }); link.send({ cmd: 'ping', t }); }, 400);
  let stall, running, during, late;
  const from = link.messages.length;
  try {
    stall = await rig.stall(STALL);
    assert.equal(stall.ms, STALL);
    assert.equal(typeof stall.at, 'number');
    // A command meanwhile: the show thread hasn't run it, so its receipt says only accepted.
    during = await rig.run('message Stalled');
    // A show app that dials once the show is stuck: a Stagewatch stuck before that app's first frame.
    await link.inbox.wait((m) => m.text === STUCK, STUCK_AFTER + BEAT_EVERY + 2000, 'the first stuck beat', from);
    late = await rig.link('node=pm000001&t=playmap', key2.secret);
    links.push(late);
    running = await stall.running;
    await link.next((j) => j.type === 'state' && j.message === 'Stalled', 5000, 'the command applied once the show runs', from);
    await pause(2500);
  } finally {
    clearInterval(pinger);
  }
  assert.equal(during.ok, true, JSON.stringify(during));
  assert.equal(during.stage, 'accepted', 'a command the show thread has not run is only accepted');

  const S = stall.receivedAt;
  const heard = link.messages.slice(from);
  const notPong = heard.filter((m) => !(m.json && m.json.type === 'pong'));
  const firstStuck = notPong.findIndex((m) => m.text === STUCK);
  assert.ok(firstStuck >= 0, 'the stuck beat: ' + JSON.stringify(notPong.map((m) => m.text.slice(0, 40))));
  // The rig's clock and this one are the machine's: stall.at is when its show thread stopped.
  assert.ok(notPong[firstStuck].at - stall.at <= STUCK_AFTER + BEAT_EVERY + 1000, `the stuck beat ${notPong[firstStuck].at - stall.at} ms after the stall`);
  // Before it, while the last frame is not yet stale, the plain beat, exactly: a quiet show, which a peer never reads as stuck.
  const plain = notPong.slice(0, firstStuck).filter((m) => m.at >= stall.at);
  assert.ok(plain.some((m) => m.text === BEAT), 'the plain beat before the stuck one: ' + JSON.stringify(plain.map((m) => m.text.slice(0, 40))));
  assert.ok(plain.every((m) => m.text === BEAT || (m.json && m.json.type === 'state')), 'only frames and plain beats before the stuck beat');

  // From the first stuck beat until the show turns over, only stuck beats: no frame, no plain beat.
  const recovered = notPong.findIndex((m, i) => i > firstStuck && m.text !== STUCK);
  assert.ok(recovered > firstStuck, 'the show came back');
  const stuck = notPong.slice(firstStuck, recovered);
  assert.ok(stuck.length >= 2, `the stuck beat repeats (${stuck.length})`);
  for (let i = 1; i < stuck.length; i++) {
    const gap = stuck[i].at - stuck[i - 1].at;
    assert.ok(gap >= BEAT_EVERY - 500 && gap <= BEAT_EVERY + 1000, `stuck beats ${gap} ms apart`);
  }
  assert.ok(!heard.some((m) => m.json && m.json.type === 'state' && m.at > S + 500 && m.at < notPong[recovered].at), 'no frame while stuck');

  // Once it runs again, a frame comes first, and no stuck beat after it.
  const first = notPong[recovered];
  assert.equal(first.json.type, 'state', 'a frame first: ' + first.text.slice(0, 80));
  assert.ok(first.at >= stall.at + STALL - 50, `the frame came ${first.at - stall.at} ms after the stall began`);
  assert.ok(first.at - running.receivedAt < 1000, 'the frame follows the show running again');
  assert.ok(!notPong.slice(recovered).some((m) => m.text === STUCK), 'no stuck beat once the show runs');

  // The link dialled while stuck: its verdict, then only the stuck beat (never the frozen frame), then a frame first.
  const lateHeard = late.messages;
  assert.deepEqual(lateHeard[0].json, { type: 'auth', ok: true, scope: 'node', node: { t: 'playmap', id: 'pm000001', verified: 'key' } });
  const lateBack = lateHeard.findIndex((m, i) => i > 0 && m.text !== STUCK);
  assert.ok(lateBack > 1, 'the late link heard the stuck beat: ' + JSON.stringify(lateHeard.map((m) => m.text.slice(0, 40))));
  assert.ok(lateHeard[1].at - lateHeard[0].at < 1000, `the stuck beat ${lateHeard[1].at - lateHeard[0].at} ms after the verdict`);
  for (let i = 2; i < lateBack; i++) {
    const gap = lateHeard[i].at - lateHeard[i - 1].at;
    assert.ok(gap >= BEAT_EVERY - 500 && gap <= BEAT_EVERY + 1000, `the late link's stuck beats ${gap} ms apart`);
  }
  assert.equal(lateHeard[lateBack].json.type, 'state', 'the late link\'s first frame comes once the show runs: ' + lateHeard[lateBack].text.slice(0, 80));
  assert.ok(lateHeard[lateBack].at >= stall.at + STALL - 50, `the late link's first frame ${lateHeard[lateBack].at - stall.at} ms after the stall began`);
  assert.ok(!lateHeard.slice(lateBack).some((m) => m.text === STUCK), 'no stuck beat on the late link once the show runs');

  // Pings are answered while the show is stuck: the wire never waits for the show thread.
  const pongs = new Map(heard.filter((m) => m.json && m.json.type === 'pong').map((m) => [m.json.t, m.at]));
  const answered = pings.filter((p) => pongs.has(p.t));
  assert.ok(pings.length >= 20 && answered.length >= pings.length - 1, `${answered.length} of ${pings.length} pings answered`);
  const inStall = answered.filter((p) => p.at > S + 200 && p.at < S + STALL - 200);
  assert.ok(inStall.length >= 15, `${inStall.length} pings answered during the stall`);
  for (const p of inStall) assert.ok(pongs.get(p.t) - p.at < 1000, `ping ${p.t} answered in ${pongs.get(p.t) - p.at} ms while stuck`);
});

test('the control reader refuses what it does not know, a stall out of range and a second stall while one runs', async () => {
  const unknown = await rig.control('wiggle 3', (j) => j.rig === 'refused');
  assert.equal(unknown.line, 'wiggle');
  assert.equal(unknown.error, 'unknown (stall <ms>, auth, heard, exit)');
  for (const bad of ['stall', 'stall 0', 'stall 30001', 'stall 1.5', 'stall -4', 'stall 5 6']) {
    const r = await rig.control(bad, (j) => j.rig === 'refused');
    assert.equal(r.line, 'stall', bad);
    assert.equal(r.error, 'stall takes a time of 1-30000 ms', bad);
  }
  const stall = await rig.stall(1500);
  const second = await rig.control('stall 100', (j) => j.rig === 'refused');
  assert.equal(second.error, 'already stalled');
  const running = await stall.running;
  assert.ok(running.at - stall.at >= 1490, `ran again ${running.at - stall.at} ms later`);
  const again = await rig.stall(10);
  await again.running;
});

test('a key first used while the show is stalled is kept when the rig is stopped (Ctrl+C): its binding is saved and the rig exits 0', async () => {
  const stall = await rig.stall(20000);
  const late = await rig.link('node=agm00001&t=messenger', key3.secret);
  links.push(late);
  const verdict = await late.next(() => true, 5000, 'the verdict');
  assert.equal(verdict.json.node.verified, 'key');
  // The save is the show thread's, and the show thread is stalled: nothing is written yet.
  assert.equal(rig.savedState().keys.find((k) => k.id === key3.id).node, null, 'not saved while the show thread is stalled');

  const ended = await rig.stop('SIGINT');
  assert.deepEqual(ended, { code: 0, signal: null }, 'a clean exit:\n' + rig.log);
  const running = await stall.running;
  assert.ok(running.at - stall.at < 20000, 'stopping the rig ended the stall early');
  const saved = rig.savedState().keys.find((k) => k.id === key3.id);
  assert.equal(saved.node, 'agm00001', 'a restart never leaves a used key free');
  assert.equal(saved.nodeKind, 'messenger');
  assert.ok(!/Unhandled exception|ObjectDisposedException/.test(rig.log), rig.log);
});
