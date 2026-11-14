// Stagewatch in the lane: the rig (tools/Stagewatch.Rig, the real web, Companion and OSC servers around a simulated show) in
// its own namespace, on its default ports on every interface, with a stable node id, a journal, the PIN from a file and every
// secret it makes written to a file instead of its output (--pin-file, --secrets-out). Its first output line is its ready
// line, which holds no secret; its standard input takes control lines (auth, heard, nodes, stall, exit) and its end stops it
// (--exit-on-eof).
// A restart is the same rig again: the same node id, --state, journal, PIN, certificate, address and ports, in a process of its
// own whose output is kept apart (stagewatch.2.out, ...).
// On the show network (`bus`) it is the app as a show laptop runs it: on the Show Bus port (--bus 9928: its hello said, the
// others' heard), serving its certificate beside plain HTTP (--tls, kept in its home, so a restart serves the one its peers
// pinned) and making node keys (--node-keys), each handed to one app through that app's own settings (keyFor). The rig makes
// the keys on its first start only (--state keeps their hashes and what each speaks for): their secrets come from the secrets
// file of that start, registered by name, and a restart's file holds none. Its certificate's password, kept unsealed beside the
// key off Windows, is registered too.
// A second rig beside it in the same namespace (`own`: its own name, ports, node id and folders) runs playout links (`playout`:
// --playout NAME=HOST:PORT[,KIND], each link's pairing token handed over in the one variable the rig reads for that link,
// STAGEWATCH_RIG_PLAYOUT_TOKEN_<NAME>, never argv or a file), off the show network, as the VT rows run them: the room's rig and
// its rows stay as they are while it runs, and it is stopped once those rows end.
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
  /**
   * `bus`: { keys } puts the rig on the show network with that many node keys (see the header); { keys, hello: false } gives it
   * the keys and its certificate with no Show Bus port (a laptop with no network, where every address is typed); omitted, it
   * is off it.
   */
  /**
   * `own`: { name, ports, nodeId, playout: [NAME=HOST:PORT[,KIND]], tokens: { NAME: token } } for a rig beside the room's (its
   * logs, folders and secrets under `name`); omitted, it is the room's rig.
   */
  constructor(run, host, bus = null, own = null) {
    this.run = run;
    this.host = host;
    this.bus = bus;
    this.onBus = !!bus && bus.hello !== false;
    this.name = own ? own.name : 'stagewatch';
    this.ports = own ? own.ports : PORTS;
    this.nodeId = own ? own.nodeId : NODE_ID;
    this.playout = own ? own.playout : [];
    this.tokens = own ? own.tokens : {};
    this.folders = run.folders(this.name);
    // 16 hex: long enough that finding it in a capture means it leaked, never a timestamp that happens to hold it.
    this.pinFile = run.secrets.file(`${this.name}.pin`, hex(8));
    this.secretsOut = path.join(run.secrets.dir, `${this.name}.secrets.json`);
    this.state = path.join(this.folders.home, 'windows.json');
    this.journal = path.join(this.folders.home, 'journal.jsonl');
    this.tlsFolder = path.join(this.folders.home, 'tls');
    /** The node keys this rig made, in its ready line's order ({ id, secret }); handed out by keyFor. */
    this.keys = [];
    this.handed = new Map();
  }

  get address() { return `${this.host.addr}:${this.ports.http}`; }

  /** Its door over TLS, held to the certificate it said in its ready line. */
  get secure() { return `https://${this.address}`; }

  /** Start it (again, after a kill: the same id, state, journal, PIN, address and ports); resolves once its ready line is read
   *  (within ms), with the ready line's JSON. */
  async start(ms = 60000) {
    this.starts = (this.starts || 0) + 1;
    // Each playout link's token in the variable the rig reads for that link alone (its name upper case, '-' as '_').
    const tokens = Object.fromEntries(Object.entries(this.tokens).map(([link, t]) => [`STAGEWATCH_RIG_PLAYOUT_TOKEN_${link.toUpperCase().replace(/-/g, '_')}`, t]));
    this.proc = new AppProcess(this.run, {
      name: this.starts === 1 ? this.name : `${this.name}.${this.starts}`,
      ns: this.host.ns,
      command: 'dotnet',
      args: [this.run.builds.stagewatch.rig, '--port', String(this.ports.http), '--companion', String(this.ports.companion), '--osc', String(this.ports.osc),
        '--bind', 'all', '--pin-file', this.pinFile, '--secrets-out', this.secretsOut, '--state', this.state, '--journal', this.journal,
        '--node-id', this.nodeId, '--exit-on-eof',
        ...(this.bus ? [...(this.onBus ? ['--bus', String(BUS_PORT)] : []), '--tls', this.tlsFolder, '--node-keys', String(this.bus.keys)] : []),
        ...this.playout.flatMap((l) => ['--playout', l])],
      env: { ...baseEnv(this.folders), ...tokens },
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
    this.pin = this.run.secrets.add(`${this.name}.pin`, secrets.pin);
    for (const w of secrets.windows || []) this.run.secrets.add(`${this.name}.window.${w.name}`, w.path);
    for (const k of secrets.nodeKeys || []) {
      this.run.secrets.add(`${this.name}.nodeKey.${k.id}`, k.secret);
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
    if (r.http !== this.ports.http) wrong.push(`http ${r.http}`);
    if (r.node !== this.nodeId) wrong.push(`node ${r.node}`);
    const links = (r.playout || []).map((l) => `${l.name}=${l.address},${l.kind}`);
    const asked = this.playout.map((l) => (l.includes(',') ? l : `${l},auto`));
    if (JSON.stringify(links) !== JSON.stringify(asked)) wrong.push(`playout ${JSON.stringify(links)}, not ${JSON.stringify(asked)}`);
    if (r.bind !== 'all') wrong.push(`bind ${r.bind}`);
    if (r.pin !== 'set') wrong.push(`pin is not "set"`);
    if (r.secrets !== 'file') wrong.push(`secrets ${r.secrets}`);
    if (r.journal !== true) wrong.push('no journal');
    if (this.bus) {
      const port = this.onBus ? BUS_PORT : 0;
      if (r.bus !== port) wrong.push(`bus ${r.bus}, not ${port}`);
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
    // LINK's wrong proofs, per device and key id (a key in force only): `address kid` → failures.
    const proofs = Object.fromEntries((said.proofs || []).map((p) => [`${p.address} ${p.kid}`, p.failures]));
    return { pins: table(said.addresses), viewPasses: table(said.viewPasses), proofs };
  }

  /**
   * The desk's Revoke for one of the node keys the rig made, by its id (the rig's `revoke` line): resolves with the rig's own time
   * of it once the web server has checked every socket the key admitted again.
   */
  async revoke(id) {
    const said = await this.control(`revoke ${id}`, (j) => j.rig === 'revoked' || (j.rig === 'refused' && j.line === 'revoke'));
    if (said.rig !== 'revoked' || said.id !== id) throw new Error(`revoke ${id} answered ${JSON.stringify(said)}`);
    return said.at;
  }

  /**
   * The windows as --state keeps them, each by its name with its grants (node, kind, name, how), never an address: every window's
   * and grant's address in it is registered as a secret first, so the sweep looks for each one everywhere it may not be.
   */
  savedWindows() {
    const saved = JSON.parse(fs.readFileSync(this.state, 'utf8'));
    return (saved.windows || []).map((w) => {
      if (w.token) this.run.secrets.add(`${this.name}.window.${w.name}.token`, w.token);
      const grants = (w.grants || []).map((g) => {
        if (g.token) this.run.secrets.add(`${this.name}.window.${w.name}.grant.${g.node}`, g.token);
        return { node: g.node, t: g.t, name: g.name, how: g.how };
      });
      return { id: w.id, name: w.name, content: w.content, rev: w.rev, grants };
    });
  }

  /**
   * The windows as the operator's pages read them (GET /api/windows with the PIN), each window's own address registered as a
   * secret and left out of what this returns (D11: the path read with the PIN is redacted at capture).
   */
  async windows() {
    const res = await get(`http://${this.address}/api/windows`, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (res.status !== 200 || !res.json || !Array.isArray(res.json.windows)) throw new Error(`the rig answered GET /api/windows with ${res.status}`);
    return res.json.windows.map(({ path: address, ...w }) => {
      if (address) this.run.secrets.add(`${this.name}.window.${w.name}.path`, address);
      return w;
    });
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
   * Its own record of the show apps' node links on /ws as its `nodes` line reads it: [{ t, id, verified, keyId, address, links,
   * words }], one row per id named and what speaks for it (key, operator or none), the key that admitted it by its id, and its
   * own words for a link it verifies none; a socket that only claims an id is not one. No secret in it.
   */
  async nodes() {
    const said = await this.control('nodes', (j) => j.rig === 'nodes' || j.rig === 'refused');
    if (said.rig !== 'nodes') throw new Error(`nodes refused: ${said.error}`);
    if (!Array.isArray(said.nodes)) throw new Error("the rig's nodes line holds no nodes");
    const leaked = this.run.secrets.foundIn(JSON.stringify(said));
    if (leaked.length) throw new Error(`the rig's nodes line holds ${leaked.join(', ')}`);
    return said.nodes;
  }

  /**
   * Its playout links as its `links` line reads them: [{ name, kind, node, pairing, clock: { offsetMs, rttMs, errMs, apart } | null,
   * record }], each link's kind and node as its peer said them, where its pairing stands, the clock its matched pongs believe and
   * its 13.16.3 record. No secret in it.
   */
  async links() {
    const said = await this.control('links', (j) => j.rig === 'links' || j.rig === 'refused');
    if (said.rig !== 'links') throw new Error(`links refused: ${said.error}`);
    if (!Array.isArray(said.links)) throw new Error("the rig's links line holds no links");
    const leaked = this.run.secrets.foundIn(JSON.stringify(said));
    if (leaked.length) throw new Error(`the rig's links line holds ${leaked.join(', ')}`);
    return said.links;
  }

  /** Its config as the operator's pages read it (GET /api/config with the PIN). */
  async config() {
    const res = await get(`http://${this.address}/api/config`, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (res.status !== 200 || !res.json) throw new Error(`the rig answered GET /api/config with ${res.status}`);
    return res.json;
  }

  /**
   * The PIN's lockout reader reads this network's addresses as the rows assert them: one wrong PIN from `from` (sent to the
   * rig at `at`, on a route that only reads) is that address with one more failure, and the right PIN clears it, so the table
   * is as it was for the rows after.
   */
  async proveLockoutReader(from, at = this.host.addr) {
    const route = `http://${at}:${this.ports.http}/api/context`;
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
    const route = `http://${at}:${this.ports.http}/w/interop-no-such-window`;
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

  /** The operator's frame as the operator's own pages read it (GET /api/state?detail=1 with the PIN): it ends desk, network. */
  async operatorFrame() {
    const res = await get(`http://${this.address}/api/state?detail=1`, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (res.status !== 200 || !res.json) throw new Error(`the rig answered GET /api/state?detail=1 with ${res.status}`);
    return res.json;
  }

  /**
   * A command with a receipt, as the operator's pages and Companion send one (GET /api/<segments>, each argument one path
   * segment, with the PIN and a request id): { status, json }, whatever it said, for the row to read.
   */
  async api(segments, id) {
    const route = `http://${this.address}/api/${segments.map(encodeURIComponent).join('/')}`;
    const res = await get(route, 30000, { 'X-Stagewatch-Pin': this.pin, 'X-Request-Id': id });
    return { status: res.status, json: res.json, text: res.text };
  }

  /** Every proposal the rig's desk holds as the operator reads them (GET /api/proposals with the PIN). */
  async proposals() {
    const res = await get(`http://${this.address}/api/proposals`, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (res.status !== 200 || !res.json || !Array.isArray(res.json.proposals)) throw new Error(`the rig answered GET /api/proposals with ${res.status}`);
    return res.json.proposals;
  }

  /** One proposal by its id as the rig's desk keeps it (GET /api/proposals/<id> with the PIN). */
  async proposal(id) {
    const res = await get(`http://${this.address}/api/proposals/${encodeURIComponent(id)}`, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (res.status !== 200 || !res.json) throw new Error(`the rig answered GET /api/proposals/${id} with ${res.status}`);
    return res.json;
  }

  /**
   * A JSON-RPC call to the rig's MCP endpoint (POST /mcp) as an AI agent's client makes one, with the operator's PIN as its bearer:
   * the reply's JSON.
   */
  async mcp(method, params) {
    const res = await request('POST', `http://${this.address}/mcp`, { headers: { Authorization: 'Bearer ' + this.pin }, body: { jsonrpc: '2.0', id: 1, method, params } });
    if (res.status !== 200 || !res.json) throw new Error(`the rig answered POST /mcp with ${res.status} ${res.text.slice(0, 160)}`);
    return res.json;
  }

  /** The show report as the operator's pages read it (GET /report with the PIN): its HTML. */
  async report() {
    const res = await get(`http://${this.address}/report`, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (res.status !== 200) throw new Error(`the rig answered GET /report with ${res.status}`);
    return res.text;
  }

  /** The room's one verdict as its route serves it (GET /api/preflight with the PIN). */
  async preflight() {
    const res = await get(`http://${this.address}/api/preflight`, 5000, { 'X-Stagewatch-Pin': this.pin });
    if (res.status !== 200 || !res.json) throw new Error(`the rig answered GET /api/preflight with ${res.status} ${res.text.slice(0, 160)}`);
    return res.json;
  }

  /**
   * The rig's Companion door as the Companion module holds it (lib/companion.cjs): a connection from the switch that subscribes
   * to the JSON frames and pings, kept until the rig stops or `closeCompanion`; opened again after a restart.
   */
  async companion() {
    if (this.deck && !this.deck.closed) return this.deck;
    const { CompanionDeck } = require('../lib/companion.cjs');
    this.deck = await CompanionDeck.connect(this.host.addr, this.ports.companion, this.run.log(`${this.name}.companion.${this.starts}`));
    return this.deck;
  }

  closeCompanion() { if (this.deck) { this.deck.close(); this.deck = null; } }

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
  kill() { this.closeCompanion(); return this.proc.stop('SIGKILL', 0); }

  stop() { this.closeCompanion(); return this.proc ? this.proc.stop('eof') : null; }

  /** What it keeps a secret in by design: --state (the windows' addresses beside the keys' hashes) and its certificate's folder
   *  (the private key, and its password unsealed off Windows). */
  secretFiles() { return [this.state, this.tlsFolder]; }
}

module.exports = { Stagewatch, PORTS, NODE_ID, BUS_PORT };
