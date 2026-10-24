// One grammar on every transport: the same window commands over HTTP (with a receipt), the Companion module's TCP
// protocol (with a receipt), OSC and MCP (an AI tool), each checked by what the window list then says — and the AI's
// view of the room names the windows. No browser: this is the wire alone.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const net = require('node:net');
const dgram = require('node:dgram');
const { Rig, until, PIN } = require('./rig.cjs');

let rig;
before(async () => { rig = await Rig.create({ wire: true }); });
after(async () => { if (rig) await rig.stop(); });

/** OSC 1.0: a message with string arguments. */
function osc(address, ...args) {
  const pad = (b) => Buffer.concat([b, Buffer.alloc(4 - (b.length % 4))]);
  const str = (s) => pad(Buffer.from(s, 'utf8'));
  return Buffer.concat([str(address), str(',' + 's'.repeat(args.length)), ...args.map(str)]);
}

/** Send lines to the Companion port; resolve with the first line that starts with one of `wanted`. */
function companion(lines, wanted) {
  return new Promise((resolve, reject) => {
    const sock = net.connect(rig.companion, '127.0.0.1');
    let buf = '';
    const timer = setTimeout(() => { sock.destroy(); reject(new Error('no answer from the Companion port:\n' + buf)); }, 5000);
    sock.on('data', (d) => {
      buf += d;
      for (const line of buf.split('\n')) {
        if (line.startsWith('READY')) { for (const l of lines) sock.write(l + '\n'); buf = buf.replace(line, ''); }
        if (wanted.some((w) => line.startsWith(w))) { clearTimeout(timer); sock.destroy(); resolve(line.trim()); return; }
      }
    });
    sock.on('error', (e) => { clearTimeout(timer); reject(e); });
  });
}

async function mcp(method, params) {
  const res = await fetch(rig.base + '/mcp', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + PIN },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method, params }),
  });
  return res.json();
}

test('HTTP: a window command answers with a receipt, refusals included', async () => {
  const made = await rig.run('window new lobby /clock');
  assert.equal(made.ok, true, JSON.stringify(made));
  assert.equal(made.stage, 'applied');
  const bad = await rig.run('window show lobby https://elsewhere.example/');
  assert.equal(bad.ok, false);
  const unknown = await rig.run('window explode lobby');
  assert.equal(unknown.ok, false, 'an unknown action is refused, never defaulted');
});

test('Companion TCP: the module\'s lines run, with receipts', async () => {
  const ok = await companion(['@c1 WINDOW HOLD lobby TEXT "Back at 2pm"'], ['OK @c1', 'ERROR @c1']);
  assert.match(ok, /^OK @c1 applied/);
  const w = await rig.window('lobby');
  assert.equal(w.hold, true);
  assert.equal(w.holdText, 'Back at 2pm');
  const refused = await companion(['@c2 WINDOW RELEASE nobody'], ['OK @c2', 'ERROR @c2']);
  assert.match(refused, /^ERROR @c2 .*No window called/);
});

test('OSC: /stagewatch/window with string arguments', async () => {
  const sock = dgram.createSocket('udp4');
  await new Promise((r) => sock.send(osc('/stagewatch/window', 'release', 'lobby'), rig.osc, '127.0.0.1', r));
  sock.close();
  await until(async () => (await rig.window('lobby')).hold === false, 3000, 'released over OSC');
});

test('MCP: an AI tool sees the windows in the room and moves one through the same grammar', async () => {
  const tools = await mcp('tools/list', {});
  assert.ok(tools.result.tools.some((t) => t.name === 'run_command'));
  const done = await mcp('tools/call', { name: 'run_command', arguments: { command: 'window show lobby /timeline', reason: 'the panel is next' } });
  assert.match(done.result.content[0].text, /^Done: window show lobby \/timeline/);
  assert.equal((await rig.window('lobby')).content, '/timeline');
  const ctx = await until(async () => {
    const r = await mcp('tools/call', { name: 'get_context', arguments: {} });
    const c = JSON.parse(r.result.content[0].text);
    return c.items && c.items.find((i) => i.id === 'window:lobby') && c;
  }, 3000, 'the window in the context');
  const item = ctx.items.find((i) => i.id === 'window:lobby');
  assert.equal(item.light, 'grey', 'nobody has opened it: grey, never guessed');
  assert.ok(ctx.commands.some((c) => c.verb.startsWith('window show') && c.risk === 'high'), 'the verbs an AI may propose, with their risk');
});
