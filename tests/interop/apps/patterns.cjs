// Patterns in the lane: its bench host (tools/Patterns.Bench, the real desk booted headless through HeadlessDesk) in its own
// namespace on its default doors (HTTP 9696, wire 9697) on every interface, its pairing token read from an owner-only file,
// its Stagewatch link typed at the rig's own address (the address the rig's hello comes from), with the node key the rig made
// for it when the rig makes keys (read from an owner-only file, --stagewatch-key-file) and none otherwise, clips synthetic and
// control lines on its standard input, whose end stops it. Its first output line is its ready line, with no secret in it. The
// lane reads it as a deck would: over its wire, paired by proof (lib/wire.cjs), its STATE asked for with STATUS.
'use strict';
const path = require('node:path');
const { AppProcess, baseEnv } = require('../lib/proc.cjs');
const { token } = require('../lib/run.cjs');
const { greeting } = require('../lib/net.cjs');
const { Wire } = require('../lib/wire.cjs');

/** The name the lane's wire connections give in HELLO. */
const HELLO = 'Interop';

const DOORS = { http: 9696, wire: 9697 };

class Patterns {
  constructor(run, host) {
    this.run = run;
    this.host = host;
    this.folders = run.folders('patterns', true);   // the bench makes its own home (0700) and refuses one it did not make
    this.token = token();
    this.tokenFile = run.secrets.file('patterns.token', this.token);
  }

  get wire() { return { addr: this.host.addr, port: DOORS.wire }; }

  /** Start it linked to `stagewatch` (host:port), with `key` ({ id, secret }, a node key the rig made) or none; resolves once its
   *  ready line is read (within ms), with its JSON. */
  async start(stagewatch, ms = 60000, key = null) {
    this.stagewatch = stagewatch;
    this.key = key;
    const keyArgs = key ? ['--stagewatch-key-file', this.run.secrets.file(`stagewatch.nodeKey.${key.id}.patterns`, key.secret)] : [];
    this.proc = new AppProcess(this.run, {
      name: 'patterns',
      ns: this.host.ns,
      command: 'dotnet',
      args: [this.run.builds.patterns.bench, '--home', this.folders.home, '--token-file', this.tokenFile,
        '--name', 'Interop Patterns', '--stagewatch', stagewatch, ...keyArgs, '--clips', 'synthetic', '--control', 'stdin'],
      env: baseEnv(this.folders),
      cwd: this.folders.user,
      stdin: true,
    });
    const first = await this.proc.out.wait(() => true, ms, "the bench's ready line").catch((e) => { throw this.proc.why(e); });
    let ready;
    try { ready = JSON.parse(first.line); } catch { ready = null; }
    if (!ready || ready.patterns !== 'ready') throw new Error(`the bench's first line is not its ready line: ${first.line.slice(0, 300)}`);
    this.readyMs = first.at - this.proc.spawnedAt;
    this.ready = ready;
    this.readyLine = first.line;
    return ready;
  }

  /** What makes the ready line this bench's, said with no secret in it. */
  checkReady() {
    const r = this.ready;
    const wrong = [];
    if (r.http !== DOORS.http || r.wire !== DOORS.wire) wrong.push(`doors ${r.http}/${r.wire}`);
    if (r.stagewatch !== this.stagewatch) wrong.push(`stagewatch ${r.stagewatch}`);
    if (r.key !== !!this.key) wrong.push(this.key ? 'no Stagewatch key, though the rig made it one' : 'a Stagewatch key, though the rig made it none');
    if (r.trustLoopback !== false) wrong.push('trusts loopback');
    if (r.clips !== 'synthetic') wrong.push(`clips ${r.clips}`);
    if (!/^[0-9a-f]{8}$/.test(r.node || '')) wrong.push(`node id ${r.node}`);
    const leaked = this.run.secrets.foundIn(this.readyLine);
    if (leaked.length) wrong.push(`the ready line holds ${leaked.join(', ')}`);
    if (wrong.length) throw new Error(`the bench's ready line: ${wrong.join('; ')}`);
  }

  /** From the switch, over the LAN: an unpaired connection to its wire hears a nonce first, and nothing else. */
  async reachable() {
    const line = await greeting(this.host.addr, DOORS.wire);
    if (!/^NONCE [0-9a-f]{32}$/.test(line)) throw new Error(`Patterns' wire at ${this.host.addr}:${DOORS.wire} said ${JSON.stringify(line.slice(0, 80))}, not a nonce`);
    return line.slice(0, 5);
  }

  /** Its wire from the switch, paired by proof; one connection kept for the reads until the bench ends. */
  async wireConn() {
    if (this.conn && !this.conn.closed) return this.conn;
    const conn = await Wire.connect(this.host.addr, DOORS.wire, { capture: this.run.log('patterns.wire') });
    await conn.pair(HELLO, this.token);
    this.conn = conn;
    return conn;
  }

  /** STATE as its wire gives it now (STATUS). */
  async state() { return (await this.wireConn()).json('STATUS'); }

  /** The God's Eye picture as its wire gives it now (EYE): every card, a show app's naming the address it was heard from. */
  async eyeJson() { return (await this.wireConn()).json('EYE'); }

  /** A verb on its wire, paired; its answer line. */
  async say(line) { return (await this.wireConn()).ask(line); }

  /** Its own context (§6) as its wire gives it now (CONTEXT), with its size in bytes as the wire carried it. */
  async context() {
    const said = await this.say('CONTEXT');
    if (!/^OK [{]/.test(said)) throw new Error(`Patterns answered CONTEXT with ${JSON.stringify(said.slice(0, 120))}`);
    const text = said.slice(3);
    return { json: JSON.parse(text), bytes: Buffer.byteLength(text, 'utf8') };
  }

  /** A control line on its standard input; resolves with the first JSON answer that passes `answered`. */
  async control(line, answered = () => true, ms = 10000) {
    const from = this.proc.out.items.length;
    const parsed = (l) => { try { return JSON.parse(l.line); } catch { return null; } };
    const answer = this.proc.out.wait((l) => { const j = parsed(l); return j && answered(j); }, ms, `the answer to "${line.split(' ')[0]}"`, from);
    this.proc.say(line);
    return parsed(await answer);
  }

  /** A synthetic clip of `seconds` on its air (the bench's `clip` line); the answer ({ ok, key, lengthMs }) or its words. */
  async clip(name, seconds) {
    const said = await this.control(`clip ${name} ${seconds}`, (j) => j.control === 'clip');
    if (!said.ok) throw new Error(`the bench refused the clip: ${said.error}`);
    return said;
  }

  closeWire() { if (this.conn) { this.conn.close(); this.conn = null; } }

  stop() { this.closeWire(); return this.proc ? this.proc.stop('eof') : null; }

  /** What it keeps a secret in by design: its settings with its pairing token (the file, its .bak and its backups), the
   *  recovery record that holds the same (and its .bak), and its TLS certificate with the private key. */
  secretFiles() {
    const h = this.folders.home;
    return ['patterns.settings.json', 'patterns.settings.json.bak', 'backups', 'patterns.recovery.json', 'patterns.recovery.json.bak',
      'patterns.tls.json'].map((f) => path.join(h, f));
  }
}

module.exports = { Patterns, DOORS };
