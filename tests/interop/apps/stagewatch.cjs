// Stagewatch in the lane: the rig (tools/Stagewatch.Rig, the real web, Companion and OSC servers around a simulated show) in
// its own namespace, on its default ports on every interface, with a stable node id, a journal, the PIN from a file and every
// secret it makes written to a file instead of its output (--pin-file, --secrets-out). Its first output line is its ready
// line, which holds no secret; its standard input takes control lines (auth, stall, exit) and its end stops it (--exit-on-eof).
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { AppProcess, baseEnv } = require('../lib/proc.cjs');
const { hex } = require('../lib/run.cjs');
const { get } = require('../lib/net.cjs');

const PORTS = { http: 19940, companion: 19941, osc: 19942 };
const NODE_ID = 'interop-sw-1';

class Stagewatch {
  constructor(run, host) {
    this.run = run;
    this.host = host;
    this.folders = run.folders('stagewatch');
    // 16 hex: long enough that finding it in a capture means it leaked, never a timestamp that happens to hold it.
    this.pinFile = run.secrets.file('stagewatch.pin', hex(8));
    this.secretsOut = path.join(run.secrets.dir, 'stagewatch.secrets.json');
    this.state = path.join(this.folders.home, 'windows.json');
    this.journal = path.join(this.folders.home, 'journal.jsonl');
  }

  get address() { return `${this.host.addr}:${PORTS.http}`; }

  /** Start it; resolves once its ready line is read (within ms), with the ready line's JSON. */
  async start(ms = 60000) {
    this.proc = new AppProcess(this.run, {
      name: 'stagewatch',
      ns: this.host.ns,
      command: 'dotnet',
      args: [this.run.builds.stagewatch.rig, '--port', String(PORTS.http), '--companion', String(PORTS.companion), '--osc', String(PORTS.osc),
        '--bind', 'all', '--pin-file', this.pinFile, '--secrets-out', this.secretsOut, '--state', this.state, '--journal', this.journal,
        '--node-id', NODE_ID, '--exit-on-eof'],
      env: baseEnv(this.folders),
      cwd: this.folders.home,
      stdin: true,
    });
    const first = await this.proc.out.wait(() => true, ms, "the rig's ready line").catch((e) => { throw this.proc.why(e); });
    let ready;
    try { ready = JSON.parse(first.line); } catch { ready = null; }
    if (!ready || ready.rig !== 'ready') throw new Error(`the rig's first line is not its ready line: ${first.line.slice(0, 200)}`);
    this.readyMs = first.at - this.proc.spawnedAt;
    this.ready = ready;
    this.readyLine = first.line;
    // The file is written whole before the ready line: every secret this start made, registered by name.
    const secrets = JSON.parse(fs.readFileSync(this.secretsOut, 'utf8'));
    this.pin = this.run.secrets.add('stagewatch.pin', secrets.pin);
    for (const w of secrets.windows || []) this.run.secrets.add(`stagewatch.window.${w.name}`, w.path);
    for (const k of secrets.nodeKeys || []) this.run.secrets.add(`stagewatch.nodeKey.${k.id}`, k.secret);
    return ready;
  }

  /** What makes the ready line the rig's ready line, said by the rig with no secret in it. */
  checkReady() {
    const r = this.ready;
    const wrong = [];
    if (r.http !== PORTS.http) wrong.push(`http ${r.http}`);
    if (r.node !== NODE_ID) wrong.push(`node ${r.node}`);
    if (r.bind !== 'all') wrong.push(`bind ${r.bind}`);
    if (r.pin !== 'set') wrong.push(`pin is not "set"`);
    if (r.secrets !== 'file') wrong.push(`secrets ${r.secrets}`);
    if (r.journal !== true) wrong.push('no journal');
    const leaked = this.run.secrets.foundIn(this.readyLine);
    if (leaked.length) wrong.push(`the ready line holds ${leaked.join(', ')}`);
    if (wrong.length) throw new Error(`the rig's ready line: ${wrong.join('; ')}`);
  }

  /** From the switch, over the LAN, with no credential: the page every browser opens first. */
  async reachable() {
    const res = await get(`http://${this.address}/`);
    if (res.status !== 200) throw new Error(`GET http://${this.address}/ answered ${res.status}`);
    return res.status;
  }

  /** A control line; resolves with the first answer line that passes `answered`. */
  async control(line, answered = () => true, ms = 5000) {
    const from = this.proc.out.items.length;
    const parsed = (l) => { try { return JSON.parse(l.line); } catch { return null; } };
    const answer = this.proc.out.wait((l) => { const j = parsed(l); return j && answered(j); }, ms, `the answer to "${line.split(' ')[0]}"`, from);
    this.proc.say(line);
    return parsed(await answer);
  }

  /** The rig's lockout table as it stands: address → failures (an address it does not list has none). */
  async auth() {
    const said = await this.control('auth', (j) => j.rig === 'auth' || j.rig === 'refused');
    if (said.rig !== 'auth') throw new Error(`auth refused: ${said.error}`);
    return Object.fromEntries((said.addresses || []).map((a) => [a.address, a.failures]));
  }

  /**
   * The lockout reader reads this LAN's addresses as the rows assert them: one wrong PIN from `from` (on a route that only
   * reads) is that address with one more failure, and the right PIN clears it, so the table is as it was for the rows after.
   */
  async proveLockoutReader(from) {
    const route = `http://${this.address}/api/context`;
    const before = (await this.auth())[from] || 0;
    const wrong = await get(route, 5000, { 'X-Stagewatch-Pin': 'not-the-pin' });
    if (wrong.status < 400) throw new Error(`a wrong PIN on ${route} answered ${wrong.status}`);
    const counted = (await this.auth())[from] || 0;
    if (counted !== before + 1) throw new Error(`a wrong PIN from ${from} read as ${counted} failures, not ${before + 1}: the lockout reader does not see this LAN's addresses`);
    const right = await get(route, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (right.status !== 200) throw new Error(`the right PIN on ${route} answered ${right.status}`);
    const cleared = (await this.auth())[from] || 0;
    if (cleared !== 0) throw new Error(`the right PIN left ${cleared} failures against ${from}`);
    return { wrongStatus: wrong.status, counted, cleared };
  }

  stop() { return this.proc ? this.proc.stop('eof') : null; }

  /** What it keeps a secret in by design: --state (the windows' addresses beside the keys' hashes). */
  secretFiles() { return [this.state]; }
}

module.exports = { Stagewatch, PORTS, NODE_ID };
