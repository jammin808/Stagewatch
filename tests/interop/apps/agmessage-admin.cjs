// AGMessage's admin, as the host laptop's own browser is: this helper runs inside AGMessage's network namespace (the driver
// starts it there with nsenter --net), so it reaches the server at 127.0.0.1, the only address admin is taken from. It reads
// the admin token from an owner-only file, signs in once and keeps the session cookie in memory (neither is ever written
// out), and answers JSON lines on its standard input, one JSON line each on its standard output:
//   login     POST /api/admin/login with the token          → {"admin":"login","ok":true|false,"status":<code>,"error"?}
//   context   GET /api/context as the signed-in admin        → {"admin":"context","status":<code>,"context":{…}}
//   health    GET /api/health from the host laptop           → {"admin":"health","status":<code>,"health":{…}}
//   settings  admin:settings on the admin's socket           → {"admin":"settings","ok":true|false,"settings":{…},"error"?}
//   <other>   refused, never guessed                         → {"admin":"refused","line":"<first word>","error":"unknown (login, context, health, settings)"}
// The end of its input ends it. What it prints is what the server said to the admin with no secret in it: the context is
// Show Bus §6's (facts, counts and peers, no token), health the host's facts, and settings the admin page's reply with its
// context token (the read-only token other nodes read /api/context with) said by its place, never by its value: the token
// goes to the owner-only file --context-token-out names, in the run's secrets folder, for the driver to register.
// The admin's socket is socket.io's first transport (Engine.IO 4 over HTTP long-polling, every request plain node:http) with
// the session cookie in its handshake, as the admin page's own socket is: one session for the helper's life, its pings
// answered, the events the server sends it ignored, each emit answered by its own ack.
'use strict';
const fs = require('node:fs');
const http = require('node:http');
const readline = require('node:readline');

const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const tokenFile = opt('--token-file');
const contextTokenOut = opt('--context-token-out');
const port = Number(opt('--port'));
if (!tokenFile || !contextTokenOut || !Number.isInteger(port) || port <= 0) {
  process.stderr.write('agmessage-admin: --token-file <file> --context-token-out <file> --port <port>\n');
  process.exit(2);
}

let cookie = '';
const out = (o) => process.stdout.write(JSON.stringify(o) + '\n');

/** One request to the server at 127.0.0.1: a JSON body (`json`) or a text one (`text`); its status, text and JSON. */
function request(method, path, { json, text, ms = 5000 } = {}) {
  return new Promise((resolve, reject) => {
    const data = json !== undefined ? Buffer.from(JSON.stringify(json)) : text !== undefined ? Buffer.from(text, 'utf8') : null;
    const headers = { Host: `127.0.0.1:${port}` };
    if (data) {
      headers['Content-Type'] = json !== undefined ? 'application/json' : 'text/plain;charset=UTF-8';
      headers['Content-Length'] = data.length;
    }
    if (cookie) headers.Cookie = cookie;
    const req = http.request({ host: '127.0.0.1', port, method, path, headers, agent: false, timeout: ms }, (res) => {
      let body = '';
      res.setEncoding('utf8');
      res.on('data', (d) => { body += d; });
      res.on('end', () => {
        let parsed = null;
        try { parsed = JSON.parse(body); } catch { parsed = null; }
        resolve({ status: res.statusCode, text: body, json: parsed, setCookie: res.headers['set-cookie'] || [] });
      });
    });
    req.on('timeout', () => req.destroy(new Error(`${method} ${path.split('?')[0]}: no answer within ${ms} ms`)));
    req.on('error', reject);
    if (data) req.write(data);
    req.end();
  });
}

/** Engine.IO 4's separator between the packets of one long-polling payload. */
const SEPARATOR = String.fromCharCode(30);

/** The admin's socket: one socket.io session over long-polling (see the head of this file). */
class AdminSocket {
  constructor() { this.acks = new Map(); this.next = 0; this.open = false; }

  where() { return `/socket.io/?EIO=4&transport=polling${this.sid ? `&sid=${encodeURIComponent(this.sid)}` : ''}`; }

  /** The handshake (with the cookie), then socket.io's CONNECT to its main namespace; resolves once the server accepts it. */
  async connect(ms = 5000) {
    const hs = await request('GET', this.where());
    const first = hs.status === 200 ? hs.text.split(SEPARATOR)[0] : '';
    if (first[0] !== '0') throw new Error(`the socket's handshake answered ${hs.status}`);
    const open = JSON.parse(first.slice(1));
    this.sid = open.sid;
    this.pollMs = open.pingInterval + open.pingTimeout + 5000;
    this.open = true;
    const connected = new Promise((resolve, reject) => { this.connected = { resolve, reject }; });
    await this.post('40');
    this.poll();
    let timer;
    const late = new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(`no CONNECT within ${ms} ms`)), ms); });
    try { await Promise.race([connected, late]); } finally { clearTimeout(timer); }
  }

  async post(packet) {
    const res = await request('POST', this.where(), { text: packet });
    if (res.status !== 200) throw new Error(`the socket's POST answered ${res.status}`);
  }

  /** The long poll: one GET at a time for the session's life, each payload's packets read in order. */
  async poll() {
    while (this.open) {
      let res;
      try { res = await request('GET', this.where(), { ms: this.pollMs }); } catch (e) { this.fail(e); return; }
      if (res.status !== 200) { this.fail(new Error(`the socket's poll answered ${res.status}`)); return; }
      for (const p of res.text.split(SEPARATOR)) this.packet(p);
    }
  }

  packet(p) {
    if (p === '2') { this.post('3').catch((e) => this.fail(e)); return; }      // a ping: its pong
    if (p === '1') { this.fail(new Error('the server closed the socket')); return; }
    if (p[0] !== '4') return;                                                   // noop
    const type = p[1];
    if (type === '0') { if (this.connected) this.connected.resolve(); return; }
    if (type === '4') { this.fail(new Error(`the server refused the socket: ${p.slice(2, 200)}`)); return; }
    if (type !== '3') return;                                                   // an event the server sends: not the helper's
    const m = /^(\d+)(.*)$/s.exec(p.slice(2));
    const ack = m && this.acks.get(Number(m[1]));
    if (!ack) return;
    this.acks.delete(Number(m[1]));
    let said;
    try { said = JSON.parse(m[2]); } catch (e) { ack.reject(new Error(`an ack that is not JSON: ${e.message}`)); return; }
    ack.resolve(Array.isArray(said) ? said[0] : undefined);
  }

  fail(e) {
    this.open = false;
    if (this.connected) this.connected.reject(e);
    for (const a of this.acks.values()) a.reject(e);
    this.acks.clear();
  }

  /** An event with no argument but its ack; resolves with the ack's first argument within ms. */
  emit(event, ms = 5000) {
    if (!this.open) return Promise.reject(new Error('the socket is closed'));
    const id = ++this.next;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.acks.delete(id); reject(new Error(`no answer to ${event} within ${ms} ms`)); }, ms);
      this.acks.set(id, { resolve: (v) => { clearTimeout(timer); resolve(v); }, reject: (e) => { clearTimeout(timer); reject(e); } });
      this.post(`42${id}${JSON.stringify([event])}`).catch((e) => {
        const a = this.acks.get(id);
        if (a) { this.acks.delete(id); a.reject(e); }
      });
    });
  }
}

let socket = null;
async function adminSocket() {
  if (socket && socket.open) return socket;
  if (!cookie) throw new Error('not signed in');
  socket = new AdminSocket();
  await socket.connect();
  return socket;
}

/** Where the reply's context token was said, in the line the helper prints. */
const TOKEN_PLACE = '<redacted: the context token, handed over in its owner-only file>';

async function handle(line) {
  const words = line.trim().split(/\s+/);
  const word = words[0] || '';
  try {
    if (words.length > 1 && ['login', 'context', 'health', 'settings'].includes(word)) {
      out({ admin: 'refused', line: word, error: `${word} takes no words` });
      return;
    }
    switch (word) {
      case 'login': {
        const token = fs.readFileSync(tokenFile, 'utf8').split('\n')[0].trim();
        const res = await request('POST', '/api/admin/login', { json: { token } });
        const sid = res.setCookie.map((c) => /^agm_admin=([^;]+)/.exec(c)).find(Boolean);
        if (res.status === 200 && sid) cookie = `agm_admin=${sid[1]}`;
        out({ admin: 'login', ok: res.status === 200 && !!sid, status: res.status, ...(res.status === 200 ? {} : { error: res.json && res.json.error }) });
        return;
      }
      case 'context': {
        const res = await request('GET', '/api/context');
        out({ admin: 'context', status: res.status, context: res.json });
        return;
      }
      case 'health': {
        const res = await request('GET', '/api/health');
        out({ admin: 'health', status: res.status, health: res.json });
        return;
      }
      case 'settings': {
        const reply = await (await adminSocket()).emit('admin:settings');
        if (!reply || typeof reply !== 'object' || reply.ok !== true) {
          out({ admin: 'settings', ok: false, error: (reply && reply.error) || 'no reply' });
          return;
        }
        const { contextToken, ...rest } = reply;
        if (typeof contextToken === 'string' && contextToken) fs.writeFileSync(contextTokenOut, contextToken + '\n', { mode: 0o600 });
        out({ admin: 'settings', ok: true, settings: { ...rest, contextToken: contextToken ? TOKEN_PLACE : '' } });
        return;
      }
      default:
        out({ admin: 'refused', line: word, error: 'unknown (login, context, health, settings)' });
    }
  } catch (e) {
    out({ admin: word, error: e.message });
  }
}

let queue = Promise.resolve();
const rl = readline.createInterface({ input: process.stdin });
rl.on('line', (line) => { queue = queue.then(() => handle(line)); });
rl.on('close', () => { queue.then(() => process.exit(0)); });
