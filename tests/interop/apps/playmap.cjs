// PlayMap in the lane: its node host (tools/PlayMap.Node, `engine --headless`, as Mapper's own module e2e runs it) in its own
// namespace with no display, so it runs on NullBackends (it plays and answers, and draws nothing). Its settings are written
// to playmap.node.json in its home before it starts (a file meant to hold its token and Patterns'): its own pairing token, no
// address trusted, Patterns followed at Patterns' own address with Patterns' token, and Stagewatch at the rig's address with
// no key until the rig makes keys for the lane. It is ready when its log says "is up" with both doors open and a renderer.
// The lane reads it as a deck would: over its wire, paired by proof (lib/wire.cjs): STATE, NODES and CONTEXT.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { AppProcess, baseEnv } = require('../lib/proc.cjs');
const { token, hex } = require('../lib/run.cjs');
const { greeting } = require('../lib/net.cjs');
const { until, fatal } = require('../lib/inbox.cjs');
const { Wire } = require('../lib/wire.cjs');

/** The name the lane's wire connections give in HELLO. */
const HELLO = 'Interop';

const DOORS = { http: 9940, wire: 9941 };
const UP = /is up: wire (\d+|closed), web (\d+|closed), renderer ([^;]+);/;

class PlayMap {
  constructor(run, host) {
    this.run = run;
    this.host = host;
    this.folders = run.folders('playmap');
    this.token = run.secrets.add('playmap.token', token());
    this.nodeId = hex(4);
    this.settings = path.join(this.folders.home, 'playmap.node.json');
    this.engineLog = path.join(this.folders.home, 'playmap.engine.log');
  }

  /** Start it following `patterns` ({ addr, port, token }) and `stagewatch` (host:port); resolves once it is up (within ms). */
  async start(patterns, stagewatch, ms = 60000) {
    fs.writeFileSync(this.settings, JSON.stringify({
      nodeId: this.nodeId, name: 'Interop PlayMap', token: this.token, bind: '', httpPort: DOORS.http, wirePort: DOORS.wire,
      syncPort: 0, trustLoopback: false, trustedAddresses: [], requireHttps: false, announce: true,
      patternsHost: `${patterns.addr}:${patterns.port}`, patternsToken: patterns.token,
      stagewatchHost: stagewatch, stagewatchKey: '', stagewatchEnabled: true, companion: { mdns: false },
    }, null, 1), { mode: 0o600 });
    this.proc = new AppProcess(this.run, {
      name: 'playmap',
      ns: this.host.ns,
      command: 'dotnet',
      args: [this.run.builds.mapper.node, 'engine', '--headless', '--home', this.folders.home, '--no-recover'],
      env: baseEnv(this.folders),
      cwd: this.folders.user,
    });
    const up = await until(() => {
      if (!this.proc.running) throw fatal(`the engine exited (${JSON.stringify(this.proc.exit)}) before it was up`);
      const text = fs.existsSync(this.engineLog) ? fs.readFileSync(this.engineLog, 'utf8') : '';
      const line = text.split('\n').find((l) => UP.test(l));
      const m = line && UP.exec(line);
      return m ? { at: Date.now(), wire: m[1], web: m[2], renderer: m[3].trim(), line } : null;
    }, ms, "PlayMap's 'is up' line").catch((e) => { throw this.proc.why(e); });
    if (!this.proc.running) throw new Error(`the engine exited (${JSON.stringify(this.proc.exit)}) as it came up`);
    this.readyMs = up.at - this.proc.spawnedAt;
    this.up = up;
    return up;
  }

  /** Both doors open on their own ports, a renderer named, and no secret in the line. */
  checkReady() {
    const u = this.up;
    const wrong = [];
    if (u.wire !== String(DOORS.wire) || u.web !== String(DOORS.http)) wrong.push(`doors wire ${u.wire}, web ${u.web}`);
    if (!u.renderer) wrong.push('no renderer named');
    if (!u.line.includes(`(${this.nodeId})`)) wrong.push('not the node id its settings name');
    const leaked = this.run.secrets.foundIn(u.line);
    if (leaked.length) wrong.push(`the line holds ${leaked.join(', ')}`);
    if (wrong.length) throw new Error(`PlayMap's 'is up' line: ${wrong.join('; ')}`);
  }

  /** From the switch, over the LAN: an unpaired connection to its wire hears a nonce first. */
  async reachable() {
    const line = await greeting(this.host.addr, DOORS.wire);
    if (!/^NONCE [0-9a-f]{32}$/.test(line)) throw new Error(`PlayMap's wire at ${this.host.addr}:${DOORS.wire} said ${JSON.stringify(line.slice(0, 80))}, not a nonce`);
    return line.slice(0, 5);
  }

  /** Its wire from the switch, paired by proof; one connection kept for the reads until the node ends. */
  async wireConn() {
    if (this.conn && !this.conn.closed) return this.conn;
    const conn = await Wire.connect(this.host.addr, DOORS.wire, { capture: this.run.log('playmap.wire') });
    await conn.pair(HELLO, this.token);
    this.conn = conn;
    return conn;
  }

  /** STATE as its wire gives it now. */
  async state() { return (await this.wireConn()).json('STATE'); }

  /** NODES: the show apps it hears, as its wire gives them now. */
  async nodes() { return (await this.wireConn()).json('NODES'); }

  /** A verb on its wire, paired; its answer line. */
  async say(line) { return (await this.wireConn()).ask(line); }

  closeWire() { if (this.conn) { this.conn.close(); this.conn = null; } }

  stop() { this.closeWire(); return this.proc ? this.proc.stop('SIGTERM') : null; }

  /** What it keeps a secret in by design: playmap.node.json (its token and Patterns') and its TLS key's folder. */
  secretFiles() { return [this.settings, path.join(this.folders.home, 'keys')]; }
}

module.exports = { PlayMap, DOORS };
