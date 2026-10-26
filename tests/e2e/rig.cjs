// The rig for browser workflows: starts tools/Stagewatch.Rig on free ports, talks to it over HTTP like any remote, and
// stops it. Restarting with the same state file and port is how a test makes "Stagewatch restarted" happen. Its first line
// on standard output is its ready line, or it has not started. Its standard input takes control lines (stall <ms>, auth,
// exit), answered in JSON lines on its standard output (a stall twice: stalled, then running); a show app's link on /ws is
// dialled as a peer dials it (Link). The interop lane's options ride on create: a stable node id, a journal, the PIN from a
// file and every secret to a file instead of standard output; each keeps its meaning across a restart.
'use strict';
const { spawn } = require('node:child_process');
const net = require('node:net');
const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');
const WebSocket = require('ws');

const ROOT = path.resolve(__dirname, '..', '..');
const DLL = process.env.STAGEWATCH_RIG || path.join(ROOT, 'tools', 'Stagewatch.Rig', 'bin', 'Debug', 'net10.0-windows', 'Stagewatch.Rig.dll');
const PIN = '4321';

function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer();
    s.unref();
    s.on('error', reject);
    s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => resolve(p)); });
  });
}

function parse(text) {
  try { return JSON.parse(text); } catch (e) { return null; }
}

/** Lines (or messages) kept in arrival order, and waits for the first one from an index on that passes a test. */
class Inbox {
  constructor() { this.items = []; this.waiters = []; this.ended = false; }

  push(item) {
    this.items.push(item);
    this.waiters = this.waiters.filter((w) => !w.check());
  }

  end() {
    this.ended = true;
    this.waiters = this.waiters.filter((w) => !w.check());
  }

  /** The first item at or after index `from` for which pred(item) is true, within ms (else an error naming `what`). */
  wait(pred, ms, what, from = 0) {
    return new Promise((resolve, reject) => {
      const w = {
        check: () => {
          for (let i = from; i < this.items.length; i++) {
            if (pred(this.items[i])) { clearTimeout(w.timer); resolve(this.items[i]); return true; }
          }
          if (this.ended) { clearTimeout(w.timer); reject(new Error(`ended before ${what}: ${JSON.stringify(this.items.slice(from).slice(-8))}`)); return true; }
          return false;
        },
      };
      w.timer = setTimeout(() => {
        this.waiters = this.waiters.filter((x) => x !== w);
        reject(new Error(`timed out after ${ms} ms waiting for ${what}: ${JSON.stringify(this.items.slice(from).slice(-8))}`));
      }, ms);
      if (!w.check()) this.waiters.push(w);
    });
  }
}

/**
 * A show app's link on /ws as a peer dials it: `?node=<id>&t=<kind>`, its credential (a node key or the PIN) in the
 * Authorization header, never the URL. Every message is kept with the time it arrived (`at`), its text exactly as sent
 * (`text`) and its JSON (`json`).
 */
class Link {
  constructor(url, credential) {
    this.inbox = new Inbox();
    this.ws = new WebSocket(url, credential ? { headers: { Authorization: 'Bearer ' + credential } } : {});
    this.ws.on('message', (data) => {
      const text = data.toString('utf8');
      this.inbox.push({ at: Date.now(), text, json: parse(text) });
    });
    this.ws.on('close', () => this.inbox.end());
    this.ws.on('error', () => this.inbox.end());
    this.opened = new Promise((resolve, reject) => {
      this.ws.once('open', resolve);
      this.ws.once('error', reject);
      this.ws.once('unexpected-response', (req, res) => reject(new Error(`/ws answered ${res.statusCode}`)));
    });
  }

  get messages() { return this.inbox.items; }
  get open() { return this.ws.readyState === WebSocket.OPEN; }

  /** The first message (from index `from`) whose JSON passes pred. */
  next(pred, ms = 5000, what = 'a message', from = 0) { return this.inbox.wait((m) => m.json && pred(m.json, m), ms, what, from); }

  send(obj) { this.ws.send(JSON.stringify(obj)); }

  close() {
    if (this.ws.readyState === WebSocket.CLOSED) return Promise.resolve();
    return new Promise((resolve) => { this.ws.once('close', resolve); this.ws.close(); });
  }
}

class Rig {
  constructor(port, dir, options) {
    this.port = port; this.dir = dir; this.state = path.join(dir, 'windows.json'); this.proc = null; this.log = ''; this.companion = 0; this.osc = 0;
    this.nodeKeyCount = options.nodeKeys || 0;
    this.extraArgs = options.args || [];   // more options, as given (e.g. --bind all --bus <port> --bus-to 127.0.0.1:<port>)
    this.nodeId = options.nodeId || null;
    // --journal: a file in the rig's folder (true), '-' for one in memory, or a path as given.
    this.journal = options.journal === true ? path.join(dir, 'journal.jsonl') : options.journal || null;
    // --pin-file: true writes `pin` (default PIN) into a file of the rig's own; a path is used as given, its first line the PIN.
    this.pin = options.pin === undefined ? PIN : options.pin;
    this.pinFile = null;
    if (options.pinFile === true) {
      this.pinFile = path.join(dir, 'pin.txt');
      fs.writeFileSync(this.pinFile, this.pin + '\n', { mode: 0o600 });
    } else if (options.pinFile) {
      this.pinFile = options.pinFile;
      this.pin = fs.readFileSync(this.pinFile, 'utf8').split('\n')[0].replace(/\r$/, '');
    }
    this.secretsOut = options.secretsOut === true ? path.join(dir, 'secrets.json') : options.secretsOut || null;
    this.secrets = null;   // with --secrets-out: what the file said at the last start ({ pin, windows, nodeKeys })
    this.ready = null;
    this.exit = null;   // how the last run ended: { code, signal }
    this.keys = [];     // every node key made for this state file, with its secret (the ready line or the secrets file names each once)
    this.out = new Inbox();
  }

  /**
   * A rig on free ports; with { wire: true } also its Companion (TCP) and OSC (UDP) servers; { nodeKeys: N } makes N node keys;
   * { nodeId } a stable --node-id; { journal: true | '-' | path }; { pinFile: true | path } (with true, { pin } is the PIN written,
   * '' for none); { secretsOut: true | path }; { args: [...] } adds options as given.
   */
  static async create(options = {}) {
    if (!fs.existsSync(DLL)) throw new Error(`the rig isn't built (${DLL}): dotnet build tools/Stagewatch.Rig`);
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sw-rig-'));
    const rig = new Rig(await freePort(), dir, options);
    if (options.wire) { rig.companion = await freePort(); rig.osc = await freePort(); }
    await rig.start();
    return rig;
  }

  get base() { return `http://127.0.0.1:${this.port}`; }
  get wsBase() { return `ws://127.0.0.1:${this.port}`; }

  start() {
    return new Promise((resolve, reject) => {
      const args = [DLL, '--port', String(this.port), '--companion', String(this.companion), '--osc', String(this.osc),
        ...(this.pinFile ? ['--pin-file', this.pinFile] : ['--pin', this.pin]), '--state', this.state, '--exit-after', '600'];
      if (this.nodeKeyCount > 0) args.push('--node-keys', String(this.nodeKeyCount));
      if (this.nodeId) args.push('--node-id', this.nodeId);
      if (this.journal) args.push('--journal', this.journal);
      if (this.secretsOut) args.push('--secrets-out', this.secretsOut);
      args.push(...this.extraArgs.map(String));
      const proc = spawn('dotnet', args, { stdio: ['pipe', 'pipe', 'pipe'] });
      this.proc = proc;
      const out = new Inbox();   // this run's lines: a restart's run has its own
      this.out = out;
      this.exit = null;
      this.stopping = false;
      let buf = '';
      let ready = false;
      let failed = false;
      const timer = setTimeout(() => reject(new Error('the rig did not start in 20 s:\n' + this.log)), 20000);
      proc.stdout.on('data', (d) => {
        if (failed) return;
        buf += d;
        let nl;
        while ((nl = buf.indexOf('\n')) >= 0) {
          const line = buf.slice(0, nl).trim();
          buf = buf.slice(nl + 1);
          const json = parse(line);
          if (!ready) {
            // The contract other lanes build on: the first line is the ready line, never one found further down.
            if (!json || json.rig !== 'ready') {
              failed = true;
              clearTimeout(timer);
              this.stopping = true;
              proc.kill('SIGKILL');
              reject(new Error(`the rig's first line is not its ready line: ${JSON.stringify(line.slice(0, 200))}\n${this.log}`));
              return;
            }
            ready = true;
            clearTimeout(timer);
            this.ready = json;
            // With --secrets-out the file is written whole before the ready line: the secrets come from there.
            if (this.secretsOut) {
              this.secrets = JSON.parse(fs.readFileSync(this.secretsOut, 'utf8'));
              this.pin = this.secrets.pin;
            }
            const made = this.secretsOut ? this.secrets.nodeKeys : json.nodeKeys || [];
            for (const k of made) if (!this.keys.some((x) => x.id === k.id)) this.keys.push(k);
            resolve(json);
            continue;
          }
          if (!line) continue;
          out.push({ at: Date.now(), line, json });
        }
      });
      proc.stderr.on('data', (d) => { this.log += d; });
      proc.stdin.on('error', () => {});   // a rig that is gone takes no more lines
      proc.on('exit', (code, signal) => {
        clearTimeout(timer);
        this.exit = { code, signal };
        if (code !== 0 && !this.stopping) reject(new Error(`the rig exited ${code}:\n${this.log}`));
      });
      // Its last lines (a stall's running, said as it stops) are read before its standard output closes, not by its exit.
      proc.on('close', () => out.end());
    });
  }

  /**
   * Stop the rig. SIGKILL (the default) is a crash as a screen sees it: the process is gone, its sockets with it. SIGINT is
   * Ctrl+C: the rig stops its servers and its show thread and exits on its own. Resolves with how it ended ({ code, signal }).
   */
  stop(signal = 'SIGKILL') {
    if (!this.proc || this.proc.exitCode !== null || this.proc.signalCode !== null) return Promise.resolve(this.exit);
    this.stopping = true;
    return new Promise((resolve) => {
      this.proc.once('exit', (code, sig) => { this.stopping = false; resolve({ code, signal: sig }); });
      this.proc.kill(signal);
    });
  }

  /** One control line on the rig's standard input; resolves with the first answer line that passes `answered`. */
  control(line, answered = () => true, ms = 5000) {
    const from = this.out.items.length;
    const answer = this.out.wait((l) => l.json && answered(l.json), ms, `the answer to "${line}"`, from);
    this.proc.stdin.write(line + '\n');
    return answer.then((l) => ({ ...l.json, receivedAt: l.at }));
  }

  /**
   * `stall <ms>`: the rig's show thread stops that long. Resolves once the rig says it stalled, with `running`, a promise
   * of the line it says when its show thread turns over again (each with `receivedAt`, when this process read it).
   */
  async stall(ms) {
    const from = this.out.items.length;
    const said = await this.control(`stall ${ms}`, (j) => j.rig === 'stalled' || j.rig === 'refused');
    if (said.rig !== 'stalled') throw new Error('stall refused: ' + JSON.stringify(said));
    const running = this.out.wait((l) => l.json && l.json.rig === 'running', ms + 10000, 'the rig running again', from)
      .then((l) => ({ ...l.json, receivedAt: l.at }));
    return { ...said, running };
  }

  /** A show app's link on /ws: `query` is e.g. "node=pat00001&t=patterns"; `credential` a node key, the PIN, or none. */
  async link(query, credential) {
    const l = new Link(`${this.wsBase}/ws?${query}`, credential);
    await l.opened;
    return l;
  }

  /** One command line through the HTTP API, with a receipt (attempts are not facts: the receipt says what happened). */
  async run(line) {
    const parts = line.trim().split(/\s+/).map(encodeURIComponent);
    const res = await fetch(`${this.base}/api/${parts.join('/')}`, { headers: { 'X-Stagewatch-Pin': this.pin, 'X-Request-Id': 'e2e' + Date.now() } });
    return res.json();
  }

  /** An HTTP request with a credential in the Authorization header (a node key or the PIN): its status and JSON. */
  async request(method, route, credential, body) {
    const headers = {};
    if (credential) headers.Authorization = 'Bearer ' + credential;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const res = await fetch(this.base + route, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await res.text();
    return { status: res.status, json: parse(text), text };
  }

  async windows() {
    const res = await fetch(`${this.base}/api/windows`, { headers: { 'X-Stagewatch-Pin': this.pin } });
    return (await res.json()).windows;
  }

  async window(name) { return (await this.windows()).find((w) => w.name === name); }

  async screens() {
    const res = await fetch(`${this.base}/api/screens`, { headers: { 'X-Stagewatch-Pin': this.pin } });
    return (await res.json()).screens;
  }

  async context() {
    const res = await fetch(`${this.base}/api/context`, { headers: { 'X-Stagewatch-Pin': this.pin } });
    return res.json();
  }

  /** What --state holds now: the windows, and the node keys with the node each speaks for (never a secret). */
  savedState() {
    const raw = JSON.parse(fs.readFileSync(this.state, 'utf8'));
    return Array.isArray(raw) ? { windows: raw, keys: [] } : raw;
  }
}

/** Poll until fn() is truthy (or throw with the last value after ms). */
async function until(fn, ms = 5000, what = 'condition') {
  const end = Date.now() + ms;
  let last;
  while (Date.now() < end) {
    last = await fn();
    if (last) return last;
    await new Promise((r) => setTimeout(r, 50));
  }
  throw new Error(`timed out waiting for ${what} (last: ${JSON.stringify(last)})`);
}

module.exports = { Rig, Link, until, freePort, PIN, ROOT, DLL };
