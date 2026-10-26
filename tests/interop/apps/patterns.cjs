// Patterns in the lane: its bench host (tools/Patterns.Bench, the real desk booted headless through HeadlessDesk) in its own
// namespace on its default doors (HTTP 9696, wire 9697) on every interface, its pairing token read from an owner-only file,
// its Stagewatch link typed at the rig's own address (the address the rig's hello comes from) with no key until the rig makes
// keys for the lane, clips synthetic and control lines on its standard input, whose end stops it. Its first output line is
// its ready line, with no secret in it.
'use strict';
const path = require('node:path');
const { AppProcess, baseEnv } = require('../lib/proc.cjs');
const { token } = require('../lib/run.cjs');
const { greeting } = require('../lib/net.cjs');

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

  /** Start it linked to `stagewatch` (host:port); resolves once its ready line is read (within ms), with its JSON. */
  async start(stagewatch, ms = 60000) {
    this.stagewatch = stagewatch;
    this.proc = new AppProcess(this.run, {
      name: 'patterns',
      ns: this.host.ns,
      command: 'dotnet',
      args: [this.run.builds.patterns.bench, '--home', this.folders.home, '--token-file', this.tokenFile,
        '--name', 'Interop Patterns', '--stagewatch', stagewatch, '--clips', 'synthetic', '--control', 'stdin'],
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
    if (r.key !== false) wrong.push('a Stagewatch key, before the rig makes keys for the lane');
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

  stop() { return this.proc ? this.proc.stop('eof') : null; }

  /** What it keeps a secret in by design: its settings with its pairing token (the file, its .bak and its backups) and its
   *  TLS certificate with the private key. */
  secretFiles() {
    const h = this.folders.home;
    return ['patterns.settings.json', 'patterns.settings.json.bak', 'backups', 'patterns.tls.json'].map((f) => path.join(h, f));
  }
}

module.exports = { Patterns, DOORS };
