// The rig as the interop lane drives it: a node id that survives a restart; with --pin-file and --secrets-out no secret on
// standard output or standard error, every one in its file, whole and owner-only, and each one working; the lockout tables
// read by the `auth` control line (three wrong PINs, then the right one; a wrong window address counted apart), answered
// while the show thread is stalled; unknown control lines, and words auth and exit do not take, refused; the end of standard
// input stopping the rig only with --exit-on-eof, and `exit` stopping it as Ctrl+C does; a journal kept across a restart,
// with the report and the context's recent lines answering from it (or kept in memory, no file written); a start that cannot
// listen (exit 3) or cannot write its secrets file (exit 4) keeping no key whose secret never went out; and options it cannot
// take refused with exit code 2, never guessed.
'use strict';
const { test, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { Rig, until, DLL, freePort } = require('./rig.cjs');

const UNKNOWN = 'unknown (stall <ms>, auth, heard, exit)';
const rigs = [];
async function make(options) {
  const rig = await Rig.create(options);
  rigs.push(rig);
  return rig;
}
after(async () => {
  for (const r of rigs) await r.stop();
});

const pause = (ms) => new Promise((r) => setTimeout(r, ms));

/** A show app's link on /ws that the rig refuses before the upgrade, with 400. */
async function refusedLink(rig, query, credential) {
  return assert.rejects(rig.link(query, credential), /\/ws answered 400/);
}

test('--node-id is the id in the ready line, the context and the node links, the same after a restart; without it each start draws its own', async () => {
  const rig = await make({ nodeId: 'interop-sw-1', nodeKeys: 1 });
  const [key] = rig.keys;
  for (const run of ['first start', 'after a restart']) {
    assert.equal(rig.ready.node, 'interop-sw-1', run);
    const context = await until(async () => { const c = await rig.context(); return c && c.node ? c : null; }, 5000, 'a context');
    assert.deepEqual([context.node.t, context.node.id], ['main', 'interop-sw-1'], run);
    // A node link may never name this Stagewatch's own id: the rig's id is the one it says, on every start.
    await refusedLink(rig, 'node=interop-sw-1&t=patterns', key.secret);
    const other = await rig.link('node=pat00001&t=patterns', key.secret);
    const verdict = await other.next(() => true, 5000, 'the verdict');
    assert.equal(verdict.json.ok, true, run + ': ' + verdict.text);
    await other.close();
    if (run === 'first start') {
      await rig.stop();
      await rig.start();
    }
  }

  const drawn = await make({});
  const first = drawn.ready.node;
  assert.match(first, /^rig-[A-Za-z0-9_-]{5,36}$/);
  await drawn.stop();
  await drawn.start();
  assert.match(drawn.ready.node, /^rig-/);
  assert.notEqual(drawn.ready.node, first, 'without --node-id each start draws its own id');
});

test('--node-id takes the edges of the hello\'s rule, 6 and 40 characters, and says each in the ready line and the context', async () => {
  const rig = await make({ nodeId: 'abcdef' });
  const forty = 'n0de-' + 'x'.repeat(34) + '_';
  assert.equal(forty.length, 40);
  for (const id of ['abcdef', forty]) {
    if (id === forty) {
      await rig.stop();
      rig.nodeId = forty;
      await rig.start();
    }
    assert.equal(rig.ready.node, id);
    const context = await until(async () => { const c = await rig.context(); return c && c.node ? c : null; }, 5000, 'a context');
    assert.equal(context.node.id, id);
  }
});

test('with --pin-file and --secrets-out no PIN, window address or key secret reaches standard output or error; the file holds each, whole and owner-only, and each works', async () => {
  const pin = 'Zq7xPin';
  const rig = await make({ pinFile: true, pin, secretsOut: true, nodeKeys: 2 });
  const firstOut = rig.out;
  const firstReady = rig.ready;
  assert.deepEqual([firstReady.pin, firstReady.secrets, firstReady.journal, firstReady.bind], ['set', 'file', false, 'local']);
  assert.deepEqual(firstReady.nodeKeys.map((k) => Object.keys(k)), [['id'], ['id']], 'the ready line names keys, never their secrets');
  assert.equal(rig.secrets.pin, pin);
  assert.deepEqual(rig.keys.map((k) => k.id), firstReady.nodeKeys.map((k) => k.id));
  assert.ok(rig.keys.every((k) => /^swk_/.test(k.secret)), 'the file has each key made in this run with its secret');
  if (process.platform !== 'win32') assert.equal(fs.statSync(rig.secretsOut).mode & 0o777, 0o600, 'the secrets file is the user\'s alone');

  // The PIN from the file is the rig's PIN: a window made with it, then a restart, which has the window from --state.
  assert.equal((await rig.run('window new lobby')).ok, true);
  const listed = await rig.window('lobby');
  assert.equal((await rig.control('wiggle', (j) => j.rig === 'refused')).error, UNKNOWN);
  await rig.control('auth', (j) => j.rig === 'auth');
  await rig.stop('SIGINT');
  await rig.start();
  assert.deepEqual(rig.ready.windows, [{ name: 'lobby' }], 'the ready line names the window, never its address');
  assert.deepEqual(rig.ready.nodeKeys, [], 'no key made in this run');
  assert.deepEqual(rig.secrets.windows, [{ name: 'lobby', path: listed.path }], 'the file has the window\'s address');
  assert.deepEqual(rig.secrets.nodeKeys, []);
  if (process.platform !== 'win32') assert.equal(fs.statSync(rig.secretsOut).mode & 0o777, 0o600, 'rewritten, still the user\'s alone');

  // Each secret works: the window's address opens its page, a key made in the first run speaks for a node, the PIN runs a command.
  assert.equal((await fetch(rig.base + listed.path)).status, 200);
  const link = await rig.link('node=pat00001&t=patterns', rig.keys[0].secret);
  const verdict = await link.next(() => true, 5000, 'the verdict');
  assert.equal(verdict.json.ok, true, verdict.text);
  await link.close();
  assert.equal((await rig.run('note secrets checked')).ok, true);

  const secrets = [pin, listed.path, listed.path.slice(3), ...rig.keys.map((k) => k.secret)];
  const said = [JSON.stringify(firstReady), ...firstOut.items.map((l) => l.line), JSON.stringify(rig.ready), ...rig.out.items.map((l) => l.line), rig.log];
  for (const secret of secrets) {
    for (const text of said) assert.ok(!text.includes(secret), `a secret reached standard output or error: ${text.slice(0, 160)}`);
  }

  // An empty first line is no PIN: the ready line says "none", and nothing asks for one.
  const open = await make({ pinFile: true, pin: '' });
  assert.equal(open.ready.pin, 'none');
  const res = await fetch(open.base + '/api/note?text=open');
  assert.equal(res.status, 200, 'no PIN asked');
});

test('the ready line carries the PIN only from argv with no --secrets-out: from argv with --secrets-out it is "set" and in the file alone; from --pin-file alone it is "set" while windows and keys still print', async () => {
  const said = (rig, ...more) => [...more, JSON.stringify(rig.ready), ...rig.out.items.map((l) => l.line), rig.log];

  const argvPin = 'Zq7xArgv';
  const argv = await make({ pin: argvPin, secretsOut: true });
  assert.deepEqual([argv.ready.pin, argv.ready.secrets], ['set', 'file']);
  assert.equal(argv.secrets.pin, argvPin, 'the file has the PIN given in argv');
  assert.equal((await argv.run('note the argv PIN works')).ok, true);
  await argv.control('auth', (j) => j.rig === 'auth');
  for (const text of said(argv)) assert.ok(!text.includes(argvPin), `the argv PIN reached standard output or error: ${text.slice(0, 160)}`);

  const filePin = 'Zq7xFile';
  const fromFile = await make({ pinFile: true, pin: filePin, nodeKeys: 1 });
  assert.deepEqual([fromFile.ready.pin, fromFile.ready.secrets], ['set', 'stdout']);
  assert.match(fromFile.ready.nodeKeys[0].secret, /^swk_/, 'without --secrets-out a key\'s secret is in the ready line');
  const firstReady = JSON.stringify(fromFile.ready);
  assert.equal((await fromFile.run('window new lobby')).ok, true);
  const listed = await fromFile.window('lobby');
  await fromFile.stop('SIGINT');
  await fromFile.start();
  assert.equal(fromFile.ready.pin, 'set');
  assert.deepEqual(fromFile.ready.windows, [{ name: 'lobby', path: listed.path }], 'without --secrets-out a window carries its address');
  for (const text of said(fromFile, firstReady)) assert.ok(!text.includes(filePin), `the file's PIN reached standard output or error: ${text.slice(0, 160)}`);
});

let plain;
test('auth reads the lockout tables: three wrong PINs are 127.0.0.1 with 3, the right PIN clears them; a wrong window address is a view pass of its own; answered while the show is stalled', async () => {
  plain = await make({});
  const auth = () => plain.control('auth', (j) => j.rig === 'auth');
  const first = await auth();
  assert.deepEqual([first.addresses, first.viewPasses], [[], []]);
  assert.equal(typeof first.at, 'number');

  for (let i = 0; i < 3; i++) await fetch(plain.base + '/api/note?text=x', { headers: { 'X-Stagewatch-Pin': '0000' } });
  const three = await auth();
  assert.equal(three.addresses.length, 1, JSON.stringify(three));
  const [row] = three.addresses;
  assert.deepEqual([row.address, row.failures], ['127.0.0.1', 3]);
  assert.ok(row.windowAgeMs >= 0 && row.windowAgeMs < 60000, `the window's age ${row.windowAgeMs}`);
  assert.deepEqual(three.viewPasses, []);

  // The tables are read off the show thread: a stalled show still answers, and nothing in them changed.
  const stall = await plain.stall(3000);
  const during = await auth();
  assert.ok(during.at - stall.at < 1500, `answered ${during.at - stall.at} ms into the stall`);
  assert.equal(during.addresses[0].failures, 3);
  await stall.running;

  assert.equal((await plain.run('note the right PIN')).ok, true);
  assert.deepEqual((await auth()).addresses, [], 'the right PIN clears the PIN\'s count');

  await fetch(plain.base + '/w/' + 'a'.repeat(26));
  assert.equal((await plain.run('note the right PIN again')).ok, true);
  const view = await auth();
  assert.deepEqual(view.addresses, []);
  assert.deepEqual(view.viewPasses.map((v) => [v.address, v.failures]), [['127.0.0.1', 1]], 'a wrong window address counts apart, and the right PIN leaves it');
});

test('the control reader refuses an unknown line, and words auth, heard and exit do not take; heard off the Show Bus lists no one', async () => {
  const unknown = await plain.control('wiggle 3', (j) => j.rig === 'refused');
  assert.deepEqual(unknown, { rig: 'refused', line: 'wiggle', error: UNKNOWN, receivedAt: unknown.receivedAt });
  const heard = await plain.control('heard', (j) => j.rig === 'heard');
  assert.deepEqual([heard.bus, heard.listening, heard.peers], [0, false, []], 'without --bus it hears nothing');
  for (const word of ['auth', 'heard', 'exit']) {
    const r = await plain.control(word + ' now', (j) => j.rig === 'refused');
    assert.deepEqual([r.line, r.error], [word, word + ' takes no words']);
  }
  assert.equal((await plain.control('auth', (j) => j.rig === 'auth')).rig, 'auth', 'still running after a refused exit');
  assert.equal(plain.exit, null);
});

test('the end of standard input stops the rig only with --exit-on-eof; exit stops it as Ctrl+C does, a stall with it', async () => {
  const eof = await make({ args: ['--exit-on-eof'] });
  eof.proc.stdin.end();
  assert.deepEqual(await until(() => eof.exit, 10000, 'the rig ending at the end of its input'), { code: 0, signal: null }, eof.log);

  const stays = await make({});
  stays.proc.stdin.end();
  await pause(1500);
  assert.equal(stays.exit, null, 'without --exit-on-eof the end of input ends the reader alone');
  assert.equal((await fetch(stays.base + '/api/state')).status, 200);

  const leaving = await make({});
  const stall = await leaving.stall(20000);
  const bye = await leaving.control('exit', (j) => j.rig === 'exiting');
  assert.deepEqual(Object.keys(bye), ['rig', 'receivedAt']);
  const running = await stall.running;
  assert.ok(running.at - stall.at < 10000, 'exit ended the stall early');
  assert.deepEqual(await until(() => leaving.exit, 10000, 'the rig ending on exit'), { code: 0, signal: null }, leaving.log);
});

test('--journal keeps the show journal; /report and /api/report.csv answer from it and the running order, the context\'s recent lines are its rows, and a restart goes on with it; without it there is no report', async () => {
  const rig = await make({ journal: true });
  assert.equal(rig.ready.journal, true);
  for (const line of ['start', 'message Hello-rig', 'next', 'note Interop-row-1']) assert.equal((await rig.run(line)).ok, true, line);

  const headers = { 'X-Stagewatch-Pin': rig.pin };
  const csvRows = async () => {
    const res = await fetch(rig.base + '/api/report.csv', { headers });
    assert.equal(res.status, 200);
    return (await res.text()).trim().split('\r\n').map((l) => l.split(','));
  };
  const html = await fetch(rig.base + '/report', { headers });
  assert.equal(html.status, 200);
  assert.match(await html.text(), /Rig Hall[\s\S]*Welcome/);
  const rows = await csvRows();
  assert.equal(rows[0][7], 'Actual start');
  const [welcome, keynote] = [rows[1], rows[2]];
  assert.deepEqual([welcome[1], keynote[1]], ['Welcome', 'Keynote']);
  assert.ok(welcome[7] && welcome[8], 'the first item started and ended: ' + welcome.join(','));
  assert.ok(keynote[7] && !keynote[8], 'the second started and runs: ' + keynote.join(','));

  const kinds = () => fs.readFileSync(rig.journal, 'utf8').trim().split('\n').map((l) => JSON.parse(l)).map((e) => `${e.k} ${e.i} ${e.x}`);
  const want = ['ItemStart 0 Welcome', 'Message 0 Hello-rig', 'ItemEnd 0 Welcome', 'ItemStart 1 Keynote', 'Note 1 Interop-row-1'];
  await until(() => kinds().length >= want.length, 5000, 'the journal written');
  assert.deepEqual(kinds(), want);
  const recent = await until(async () => {
    const c = await rig.context();
    return c && c.recent && c.recent.some((r) => r.text.endsWith('Note: Interop-row-1')) ? c.recent : null;
  }, 5000, 'the note in the context\'s recent lines');
  assert.ok(recent.some((r) => r.text.endsWith('ItemStart: Welcome')), JSON.stringify(recent));

  await rig.stop('SIGINT');
  await rig.start();
  assert.equal((await rig.run('note after-restart')).ok, true);
  await until(() => kinds().length === want.length + 1, 5000, 'the journal appended to');
  assert.deepEqual(kinds(), [...want, 'Note 0 after-restart']);
  const again = await csvRows();
  assert.deepEqual([again[1][7], again[1][8]], [welcome[7], welcome[8]], 'a restarted rig reports what the journal held');

  const none = await make({});
  assert.equal(none.ready.journal, false);
  for (const route of ['/report', '/api/report.csv']) {
    assert.equal((await fetch(none.base + route, { headers: { 'X-Stagewatch-Pin': none.pin } })).status, 404, route);
  }
});

test('--journal - keeps the journal in memory: the report and the recent lines answer from it, no file is written, and a restart starts it afresh', async () => {
  const rig = await make({ journal: '-' });
  assert.equal(rig.ready.journal, true);
  for (const line of ['start', 'note In-memory-row']) assert.equal((await rig.run(line)).ok, true, line);
  const welcome = async () => {
    const res = await fetch(rig.base + '/api/report.csv', { headers: { 'X-Stagewatch-Pin': rig.pin } });
    assert.equal(res.status, 200);
    return (await res.text()).trim().split('\r\n').map((l) => l.split(','))[1];
  };
  const row = await welcome();
  assert.equal(row[1], 'Welcome');
  assert.ok(row[7], 'the first item started: ' + row.join(','));
  await until(async () => {
    const c = await rig.context();
    return c && c.recent && c.recent.some((r) => r.text.endsWith('Note: In-memory-row'));
  }, 5000, 'the note in the context\'s recent lines');

  await rig.stop('SIGINT');
  await rig.start();
  assert.equal((await welcome())[7], '', 'a journal in memory ends with the rig');
  assert.deepEqual(fs.readdirSync(rig.dir).filter((f) => f !== 'windows.json'), [], 'nothing written beside the rig\'s state');
  assert.equal(fs.existsSync(path.resolve('-')), false, 'no file named "-"');
});

test('a start that cannot listen (exit 3) or cannot write --secrets-out (exit 4) keeps no key whose secret never went out; the next start makes it afresh', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sw-rig-keys-'));
  const state = path.join(dir, 'windows.json');
  const blocker = path.join(dir, 'blocker');
  fs.writeFileSync(blocker, 'a file, never a folder\n');
  const busy = net.createServer();
  await new Promise((resolve) => busy.listen(0, '127.0.0.1', resolve));
  const runs = [
    { code: 3, args: ['--port', String(busy.address().port)], words: /couldn't listen/ },
    { code: 4, args: ['--port', String(await freePort()), '--secrets-out', path.join(blocker, 'secrets.json')], words: /couldn't write --secrets-out/ },
  ];
  try {
    for (const { code, args, words } of runs) {
      const r = spawnSync('dotnet', [DLL, '--companion', '0', '--osc', '0', '--state', state, '--node-keys', '1', ...args], { encoding: 'utf8', timeout: 30000 });
      assert.equal(r.status, code, `exit ${r.status}\n${r.stderr}`);
      assert.match(r.stderr, words);
      assert.match(r.stderr, /the 1 made in this start are taken out of --state again/);
      assert.equal(r.stdout, '', 'no ready line');
      assert.deepEqual(JSON.parse(fs.readFileSync(state, 'utf8')).keys, [], `exit ${code}: no key kept whose secret never went out`);
    }
  } finally {
    busy.close();
  }

  const rig = new Rig(await freePort(), dir, { nodeKeys: 1, secretsOut: true });
  rigs.push(rig);
  await rig.start();
  assert.equal(rig.ready.nodeKeys.length, 1, 'the next start makes the key afresh');
  assert.deepEqual(rig.savedState().keys.map((k) => k.id), [rig.keys[0].id]);
  const link = await rig.link('node=pat00001&t=patterns', rig.keys[0].secret);
  const verdict = await link.next(() => true, 5000, 'the verdict');
  assert.equal(verdict.json.ok, true, verdict.text);
  await link.close();
});

test('options the rig cannot take are refused with words and exit code 2, never guessed, and the words never carry a file\'s content', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sw-rig-opts-'));
  const pinFile = path.join(dir, 'pin.txt');
  fs.writeFileSync(pinFile, 'Kp93Secret\n');
  const long = path.join(dir, 'long.txt');
  fs.writeFileSync(long, 'Lq'.repeat(129));
  const cases = [
    [['--node-id', 'bad id'], /--node-id is 6-40 letters, digits, - or _/],
    [['--node-id', 'abcde'], /--node-id is 6-40/],
    [['--node-id', 'x'.repeat(41)], /--node-id is 6-40/],
    [['--node-id', 'interop-sw-1', '--node-id', 'interop-sw-2'], /--node-id once/],
    [['--pin', '1', '--pin-file', pinFile], /--pin-file once, and never with --pin$/m],
    [['--pin-file', pinFile, '--pin', '1'], /--pin once, and never with --pin-file/],
    [['--pin-file', path.join(dir, 'missing.txt')], /--pin-file: /],
    [['--pin-file', long], /--pin-file: the first line is over 256 characters/],
    [['--journal'], /--journal needs a value/],
    [['--journal', path.join(pinFile, 'journal.jsonl')], /--journal: couldn't open it for appending: /],
    [['--secrets-out', ''], /--secrets-out needs a file/],
    [['--exit-on-eof=yes'], /unknown option --exit-on-eof=yes/],
    [['--tls', ''], /--tls needs a folder/],
    [['--tls', path.join(dir, 'a'), '--tls', path.join(dir, 'b')], /--tls once/],
    [['--tls', path.join(pinFile, 'tls')], /--tls: couldn't make the folder: /],
    // A folder that is there and holds anything else is never the rig's to write its key in (nor narrowed: it is not its own).
    [['--tls', dir], /--tls: .* holds what is not its certificate \(long\.txt, open, pin\.txt\): name a folder of the rig's own/],
  ];
  // Off Windows a folder others may open is refused as it stands, never narrowed (as `--tls /tmp` or `--tls .` would be).
  const open = path.join(dir, 'open');
  fs.mkdirSync(open, { mode: 0o755 });
  fs.chmodSync(open, 0o755);
  if (process.platform !== 'win32') cases.push([['--tls', open], /--tls: .* is open to other users \(mode 755\): name a folder of the rig's own/]);
  for (const [args, words] of cases) {
    const r = spawnSync('dotnet', [DLL, '--port', '0', '--companion', '0', '--osc', '0', ...args], { encoding: 'utf8', timeout: 30000 });
    assert.equal(r.status, 2, `${args.join(' ')}: exit ${r.status}\n${r.stderr}`);
    assert.match(r.stderr, words, args.join(' '));
    assert.equal(r.stdout, '', 'nothing on standard output: ' + args.join(' '));
    assert.ok(!r.stderr.includes('Kp93Secret') && !r.stderr.includes('LqLq'), 'the words carry no file\'s content: ' + r.stderr);
  }
  assert.deepEqual(fs.readdirSync(dir).sort(), ['long.txt', 'open', 'pin.txt'], 'nothing written in a folder refused');
  assert.deepEqual(fs.readdirSync(open), []);
  if (process.platform !== 'win32') assert.equal(fs.statSync(open).mode & 0o777, 0o755, 'a folder refused keeps its mode');
  fs.rmSync(dir, { recursive: true, force: true });
});
