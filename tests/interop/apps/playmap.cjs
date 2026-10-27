// PlayMap in the lane: its node host (tools/PlayMap.Node, `engine --headless`, as Mapper's own module e2e runs it) in its own
// namespace with no display, so it runs on NullBackends (it plays and answers, and draws nothing). Its settings are written
// to playmap.node.json in its home before it starts (a file meant to hold its token and Patterns'): its own pairing token, no
// address trusted, Patterns followed at Patterns' own address with Patterns' token, and Stagewatch at the rig's address with
// the node key the rig made for it when the rig makes keys, and none otherwise (PlayMap moves the key into its own credential
// store at its start and writes the settings file again without it). It is ready when its log says "is up" with both doors
// open and a renderer.
// The lane reads it as a deck would: over its wire from the switch, paired by proof (lib/wire.cjs): STATE, NODES and CONTEXT,
// lines that carry no secret. A line that carries one (LINK STAGEWATCH <rig> <key>, as its operator types it, and LINK
// STAGEWATCH OFF beside it) is said as its operator says it at PlayMap's own machine: by apps/playmap-operator.cjs, started in
// its namespace, on its wire at 127.0.0.1, so no key crosses the run's LAN; the key reaches the helper in an owner-only file
// of the run's secrets folder.
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

  /** Start it following `patterns` ({ addr, port, token }) and `stagewatch` (host:port) with `key` ({ id, secret }, a node key
   *  the rig made) or none; resolves once it is up (within ms). */
  async start(patterns, stagewatch, ms = 60000, key = null) {
    this.key = key;
    fs.writeFileSync(this.settings, JSON.stringify({
      nodeId: this.nodeId, name: 'Interop PlayMap', token: this.token, bind: '', httpPort: DOORS.http, wirePort: DOORS.wire,
      syncPort: 0, trustLoopback: false, trustedAddresses: [], requireHttps: false, announce: true,
      patternsHost: `${patterns.addr}:${patterns.port}`, patternsToken: patterns.token,
      stagewatchHost: stagewatch, stagewatchKey: key ? key.secret : '', stagewatchEnabled: true, companion: { mdns: false },
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

  /** Its own context (§6) as its wire gives it now (CONTEXT), with its size in bytes as the wire carried it. */
  async context() {
    const said = await this.say('CONTEXT');
    if (!/^OK [{]/.test(said)) throw new Error(`PlayMap answered CONTEXT with ${JSON.stringify(said.slice(0, 120))}`);
    const text = said.slice(3);
    return { json: JSON.parse(text), bytes: Buffer.byteLength(text, 'utf8') };
  }

  /** A verb on its wire, paired; its answer line. */
  async say(line) { return (await this.wireConn()).ask(line); }

  /** Its link to Stagewatch as STATE gives it now: { state, host, paired, detail, timer }. */
  async stagewatchLink() { return (await this.state()).links.stagewatch; }

  /** Its operator at its own machine (apps/playmap-operator.cjs in its namespace, paired on its wire at 127.0.0.1 with its
   *  token from an owner-only file); started once, at the first line that needs it. */
  operator() {
    if (this.op && this.op.running) return this.op;
    const tokenFile = this.run.secrets.file('playmap.token', this.token);
    this.op = new AppProcess(this.run, {
      name: 'playmap-operator',
      ns: this.host.ns,
      command: process.execPath,
      args: [path.join(__dirname, 'playmap-operator.cjs'), '--token-file', tokenFile, '--secrets-dir', this.run.secrets.dir,
        '--capture', this.run.log('playmap-operator.wire'), '--port', String(DOORS.wire)],
      env: baseEnv(this.folders),
      cwd: this.folders.user,
      stdin: true,
    });
    return this.op;
  }

  /** A line to its operator; the line PlayMap said back, as the operator printed it. `what` names the line in a failure's
   *  words, never a secret. */
  async ask(line, what, ms = 8000) {
    const op = this.operator();
    const from = op.out.items.length;
    const parsed = (l) => { try { return JSON.parse(l.line); } catch { return null; } };
    const word = line.split(' ')[0];
    const answer = op.out.wait((l) => { const j = parsed(l); return j && (j.operator === word || j.operator === 'refused'); }, ms,
      `PlayMap's operator's answer to ${what}`, from).catch((e) => { throw op.why(e); });
    op.say(line);
    const j = parsed(await answer);
    if (j.error || typeof j.said !== 'string') throw new Error(`PlayMap's operator, saying ${what}: ${j.error || 'no answer line'}`);
    return j.said;
  }

  /**
   * LINK STAGEWATCH, as its operator types it at its own machine (operator()): Stagewatch at `address` (host:port) with `key`
   * ({ id, secret }, a node key the rig made), which PlayMap keeps in its own credential store. The key goes to the operator in
   * an owner-only file of the run's secrets folder and from there only into the line it says to 127.0.0.1. The answer is a
   * receipt (Show Bus §3) that holds no secret: accepted (the link started) or applied, never refused; what the link then does
   * is read from STATE, not from the receipt. Resolves with the receipt.
   */
  async linkStagewatch(address, key) {
    const file = this.run.secrets.file(`stagewatch.nodeKey.${key.id}`, key.secret);
    const receipt = await this.receipt(`link ${address} ${path.basename(file)}`, `LINK STAGEWATCH (key ${key.id})`);
    this.key = key;
    return receipt;
  }

  /** LINK STAGEWATCH OFF, as its operator types it: it follows no Stagewatch, and STATE says its link off once that is done.
   *  Resolves with the receipt. */
  unlinkStagewatch() { return this.receipt('off', 'LINK STAGEWATCH OFF'); }

  /** A line its operator says whose answer is a receipt (Show Bus §3), accepted or applied and holding no secret: the receipt.
   *  `what` names the line in a failure's words, never its secret. */
  async receipt(line, what) {
    const said = await this.ask(line, what);
    const leaked = this.run.secrets.foundIn(said);
    if (leaked.length) throw new Error(`PlayMap's answer to ${what} holds ${leaked.join(', ')}`);
    let receipt = null;
    try { receipt = /^OK [{]/.test(said) ? JSON.parse(said.slice(3)) : null; } catch { receipt = null; }
    if (!receipt || !['accepted', 'applied'].includes(receipt.stage) || receipt.error) {
      throw new Error(`PlayMap answered ${what} with ${JSON.stringify(said.slice(0, 160))}`);
    }
    return receipt;
  }

  closeWire() { if (this.conn) { this.conn.close(); this.conn = null; } }

  /** Stop its operator (the end of its input ends it); how it ended, or null when it never started. */
  async closeOperator() {
    if (!this.op) return null;
    const how = await this.op.stop('eof', 5000);
    this.op = null;
    return how;
  }

  async stop() {
    this.closeWire();
    await this.closeOperator();
    return this.proc ? this.proc.stop('SIGTERM') : null;
  }

  /** What it keeps a secret in by design: playmap.node.json (its token and Patterns') and its TLS key's folder. */
  secretFiles() { return [this.settings, path.join(this.folders.home, 'keys')]; }
}

module.exports = { PlayMap, DOORS };
