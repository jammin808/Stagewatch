// Stagewatch in the lane: the rig (tools/Stagewatch.Rig, the real web, Companion and OSC servers around a simulated show) in
// its own namespace, on its default ports on every interface, with a stable node id, a journal, the PIN from a file and every
// secret it makes written to a file instead of its output (--pin-file, --secrets-out). Its first output line is its ready
// line, which holds no secret; its standard input takes control lines (auth, heard, stall, exit) and its end stops it
// (--exit-on-eof).
// A restart is the same rig again: the same node id, --state, journal, PIN, certificate, address and ports, in a process of its
// own whose output is kept apart (stagewatch.2.out, ...).
// On the show network (`bus`) it is the app as a show laptop runs it: on the Show Bus port (--bus 9928: its hello said, the
// others' heard), serving its certificate beside plain HTTP (--tls, kept in its home, so a restart serves the one its peers
// pinned) and making node keys (--node-keys), each handed to one app through that app's own settings (keyFor). The rig makes
// the keys on its first start only (--state keeps their hashes and what each speaks for): their secrets come from the secrets
// file of that start, registered by name, and a restart's file holds none. Its certificate's password, kept unsealed beside the
// key off Windows, is registered too.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { AppProcess, baseEnv } = require('../lib/proc.cjs');
const { hex } = require('../lib/run.cjs');
const { get, request } = require('../lib/net.cjs');

const PORTS = { http: 19940, companion: 19941, osc: 19942 };
const NODE_ID = 'interop-sw-1';
/** The Show Bus port (§2): the rig's hello goes out on it and the others' come in. */
const BUS_PORT = 9928;

class Stagewatch {
  /** `bus`: { keys } puts the rig on the show network with that many node keys (see the header); omitted, it is off it. */
  constructor(run, host, bus = null) {
    this.run = run;
    this.host = host;
    this.bus = bus;
    this.folders = run.folders('stagewatch');
    // 16 hex: long enough that finding it in a capture means it leaked, never a timestamp that happens to hold it.
    this.pinFile = run.secrets.file('stagewatch.pin', hex(8));
    this.secretsOut = path.join(run.secrets.dir, 'stagewatch.secrets.json');
    this.state = path.join(this.folders.home, 'windows.json');
    this.journal = path.join(this.folders.home, 'journal.jsonl');
    this.tlsFolder = path.join(this.folders.home, 'tls');
    /** The node keys this rig made, in its ready line's order ({ id, secret }); handed out by keyFor. */
    this.keys = [];
    this.handed = new Map();
  }

  get address() { return `${this.host.addr}:${PORTS.http}`; }

  /** Its door over TLS, held to the certificate it said in its ready line. */
  get secure() { return `https://${this.address}`; }

  /** Start it (again, after a kill: the same id, state, journal, PIN, address and ports); resolves once its ready line is read
   *  (within ms), with the ready line's JSON. */
  async start(ms = 60000) {
    this.starts = (this.starts || 0) + 1;
    this.proc = new AppProcess(this.run, {
      name: this.starts === 1 ? 'stagewatch' : `stagewatch.${this.starts}`,
      ns: this.host.ns,
      command: 'dotnet',
      args: [this.run.builds.stagewatch.rig, '--port', String(PORTS.http), '--companion', String(PORTS.companion), '--osc', String(PORTS.osc),
        '--bind', 'all', '--pin-file', this.pinFile, '--secrets-out', this.secretsOut, '--state', this.state, '--journal', this.journal,
        '--node-id', NODE_ID, '--exit-on-eof',
        ...(this.bus ? ['--bus', String(BUS_PORT), '--tls', this.tlsFolder, '--node-keys', String(this.bus.keys)] : [])],
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
    if (this.pin !== undefined && secrets.pin !== this.pin) throw new Error('the restarted rig took another PIN than its file holds');
    this.pin = this.run.secrets.add('stagewatch.pin', secrets.pin);
    for (const w of secrets.windows || []) this.run.secrets.add(`stagewatch.window.${w.name}`, w.path);
    for (const k of secrets.nodeKeys || []) {
      this.run.secrets.add(`stagewatch.nodeKey.${k.id}`, k.secret);
      this.keys.push({ id: k.id, secret: k.secret });
    }
    // The certificate a restart serves is the one the first start made: what every peer pinned.
    if (this.tls !== undefined && ready.tls !== this.tls) throw new Error(`the restarted rig serves the certificate ${ready.tls}, not ${this.tls}`);
    this.tls = ready.tls;
    // Off Windows its certificate's password is kept unsealed in node.key (the rig has no DPAPI): registered, so the sweep finds
    // it anywhere it may not be, before the folder that holds it by design is removed.
    if (this.bus) this.run.secrets.add('stagewatch.tls.password', fs.readFileSync(path.join(this.tlsFolder, 'node.key'), 'utf8'));
    return ready;
  }

  /**
   * A node key the rig made, for one app: the next one not yet handed out, the same one again for the same app. The secret goes
   * only to that app's own settings (a 0600 file); what any report says is the key's id.
   */
  keyFor(app) {
    if (this.handed.has(app)) return this.handed.get(app);
    const key = this.keys[this.handed.size];
    if (!key) throw new Error(`the rig made ${this.keys.length} node keys; none is left for ${app}`);
    this.handed.set(app, key);
    return key;
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
    if (this.bus) {
      if (r.bus !== BUS_PORT) wrong.push(`bus ${r.bus}, not ${BUS_PORT}`);
      if (!/^sha256:[0-9a-f]{64}$/.test(r.tls || '')) wrong.push(`tls ${r.tls}, not a certificate's fingerprint`);
      // A first start names every key it made; a restart makes none (--state keeps them).
      const made = (r.nodeKeys || []).map((k) => k.id);
      if (this.starts === 1 && made.length !== this.bus.keys) wrong.push(`${made.length} node keys made, not ${this.bus.keys}`);
      if (this.starts > 1 && made.length) wrong.push(`a restart made node keys: ${made.join(', ')}`);
      if ((r.nodeKeys || []).some((k) => Object.keys(k).join() !== 'id')) wrong.push('a node key in the ready line names more than its id');
    } else if (r.bus !== 0 || r.tls !== null) wrong.push(`off the show network, but bus ${r.bus}, tls ${r.tls}`);
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

  /**
   * The rig's two lockout tables as they stand, each address → failures in its open one-minute window (an address a table
   * does not list has none): `pins`, the PIN's (wrong PINs), and `viewPasses`, the view passes' (wrong window addresses,
   * viewing codes and crew links).
   */
  async auth() {
    const said = await this.control('auth', (j) => j.rig === 'auth' || j.rig === 'refused');
    if (said.rig !== 'auth') throw new Error(`auth refused: ${said.error}`);
    if (!Array.isArray(said.addresses) || !Array.isArray(said.viewPasses)) throw new Error("the rig's auth line holds no PIN table or no view passes' table");
    const table = (rows) => Object.fromEntries(rows.map((a) => [a.address, a.failures]));
    return { pins: table(said.addresses), viewPasses: table(said.viewPasses) };
  }

  /**
   * Its hello table as its `heard` line reads it: { bus, listening, peers: [{ t, id, address, addresses, ageS, conflict }] },
   * the hellos it hears on the Show Bus alone (a node link or a posted context adds no row), no secret in it.
   */
  async heard() {
    const said = await this.control('heard', (j) => j.rig === 'heard' || j.rig === 'refused');
    if (said.rig !== 'heard') throw new Error(`heard refused: ${said.error}`);
    if (!Array.isArray(said.peers)) throw new Error("the rig's heard line holds no peers");
    return said;
  }

  /**
   * The PIN's lockout reader reads this network's addresses as the rows assert them: one wrong PIN from `from` (sent to the
   * rig at `at`, on a route that only reads) is that address with one more failure, and the right PIN clears it, so the table
   * is as it was for the rows after.
   */
  async proveLockoutReader(from, at = this.host.addr) {
    const route = `http://${at}:${PORTS.http}/api/context`;
    const before = (await this.auth()).pins[from] || 0;
    const wrong = await get(route, 5000, { 'X-Stagewatch-Pin': 'not-the-pin' });
    if (wrong.status < 400) throw new Error(`a wrong PIN on ${route} answered ${wrong.status}`);
    const counted = (await this.auth()).pins[from] || 0;
    if (counted !== before + 1) throw new Error(`a wrong PIN from ${from} read as ${counted} failures, not ${before + 1}: the lockout reader does not see this network's addresses`);
    const right = await get(route, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (right.status !== 200) throw new Error(`the right PIN on ${route} answered ${right.status}`);
    const cleared = (await this.auth()).pins[from] || 0;
    if (cleared !== 0) throw new Error(`the right PIN left ${cleared} failures against ${from}`);
    return { wrongStatus: wrong.status, counted, cleared };
  }

  /**
   * The view passes' reader reads this network's addresses as the rows assert them: one wrong window address from `from`
   * (GET /w/<an address no window has>, sent to the rig at `at`) is that address with one more wrong view pass. Nothing but
   * the end of its one-minute window clears a wrong view pass (a right credential never does, Show Bus 12.2), so a topology
   * runs this control after its last row's read, and no read after it asserts `from`.
   */
  async proveViewPassReader(from, at = this.host.addr) {
    const route = `http://${at}:${PORTS.http}/w/interop-no-such-window`;
    const before = (await this.auth()).viewPasses[from] || 0;
    const wrong = await get(route);
    if (wrong.status !== 404) throw new Error(`a window address no window has, ${route}, answered ${wrong.status}, not 404`);
    const counted = (await this.auth()).viewPasses[from] || 0;
    if (counted !== before + 1) throw new Error(`a wrong window address from ${from} read as ${counted} wrong view passes, not ${before + 1}: the view passes' reader does not see this network's addresses`);
    return { wrongStatus: wrong.status, before, counted };
  }

  /** Its own context (§6) as the operator's pages read it, with the PIN, over TLS when it serves its certificate. */
  async context() { return (await this.contextRead()).json; }

  /** The same read with its size in bytes as the rig sent it. */
  async contextRead() {
    const res = this.tls
      ? await request('GET', `${this.secure}/api/context`, { headers: { 'X-Stagewatch-Pin': this.pin }, pin: this.tls })
      : await get(`http://${this.address}/api/context`, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (res.status !== 200 || !res.json) throw new Error(`the rig answered GET /api/context with ${res.status}`);
    return { json: res.json, bytes: Buffer.byteLength(res.text, 'utf8') };
  }

  /** A show app's context posted as that app posts its own (POST /api/peers/context, a node key in Authorization), over TLS
   *  held to the rig's certificate: its status and JSON. */
  postContext(key, body) {
    return request('POST', `${this.secure}/api/peers/context`, { headers: { Authorization: 'Bearer ' + key.secret }, body, pin: this.tls });
  }

  /** What --state holds of each node key now: its id, the node it speaks for and that node's kind (ids and names, never a secret). */
  savedKeys() {
    const saved = JSON.parse(fs.readFileSync(this.state, 'utf8'));
    return (saved.keys || []).map((k) => ({ id: k.id, node: k.node ?? null, nodeKind: k.nodeKind ?? null }));
  }

  /** A show command over HTTP with the operator's PIN (POST /api/<command>); its receipt, which must say it was applied. */
  async command(name) {
    const res = await request('POST', `http://${this.address}/api/${name}`, { headers: { 'X-Stagewatch-Pin': this.pin } });
    if (res.status !== 200 || !res.json || res.json.ok !== true) throw new Error(`the rig answered POST /api/${name} with ${res.status} ${res.text.slice(0, 160)}`);
    return res.json;
  }

  /** The frame as any screen reads it now (GET /api/state, no PIN). */
  async frame() {
    const res = await get(`http://${this.address}/api/state`);
    if (res.status !== 200 || !res.json) throw new Error(`the rig answered GET /api/state with ${res.status}`);
    return res.json;
  }

  /**
   * Stop its show thread for ms (1..30000) through the rig's own `stall` line. Resolves with the unix ms the rig says the
   * thread stopped, and `running(within)`, which resolves with the unix ms it says the thread turned over again; both times
   * are the rig's own, never when the driver asked.
   */
  async stall(ms) {
    const from = this.proc.out.items.length;
    const said = await this.control(`stall ${ms}`, (j) => j.rig === 'stalled' || (j.rig === 'refused' && j.line === 'stall'));
    if (said.rig !== 'stalled') throw new Error(`stall refused: ${said.error}`);
    const parsed = (l) => { try { return JSON.parse(l.line); } catch { return null; } };
    const running = async (within) => {
      const l = await this.proc.out.wait((x) => { const j = parsed(x); return j && j.rig === 'running'; }, within, 'the rig running again', from);
      return parsed(l).at;
    };
    return { stalledAt: said.at, running };
  }

  /** Killed as a crash is (SIGKILL: no word to anyone, its sockets reset by the kernel); resolves once it has ended. */
  kill() { return this.proc.stop('SIGKILL', 0); }

  stop() { return this.proc ? this.proc.stop('eof') : null; }

  /** What it keeps a secret in by design: --state (the windows' addresses beside the keys' hashes) and its certificate's folder
   *  (the private key, and its password unsealed off Windows). */
  secretFiles() { return [this.state, this.tlsFolder]; }
}

module.exports = { Stagewatch, PORTS, NODE_ID, BUS_PORT };
