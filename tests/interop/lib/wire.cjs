// A line-wire client for Patterns' and PlayMap's TCP wires (Show Bus §7), as the switch speaks to them over the LAN. The client
// speaks first (Show Bus 1.3 §13.8.5: Patterns' wire writes nothing before a connection's first byte, which tells TLS from
// plain): it says HELLO <name>, and the door's "NONCE <32 hex>" comes before HELLO's answer (PlayMap's on connect, Patterns' on
// that first byte); the client answers it with AUTH-PROOF, the lowercase hex of HMAC-SHA256 keyed with the token as both apps
// compare it (dashes and whitespace removed, upper case) over the nonce and the HELLO name, so the token never crosses the wire.
// Requests go one at a time: a request's answer is the first line after it that begins OK or ERR; the pushes in between (STATE,
// CONTEXT, a new NONCE, a fact) are kept in arrival order and never taken for an answer. What a connection hears is kept for the
// sweep (row S7.1) through `capture`, as heard.
'use strict';
const crypto = require('node:crypto');
const net = require('node:net');
const fs = require('node:fs');
const { Inbox } = require('./inbox.cjs');

/** The token as both apps key the proof with it. */
const normalise = (token) => String(token || '').replace(/[-\s]/g, '').toUpperCase();

/** AUTH-PROOF's hex for a nonce and a HELLO name. */
const proof = (token, nonce, name) => crypto.createHmac('sha256', Buffer.from(normalise(token), 'utf8'))
  .update(Buffer.from(String(nonce) + String(name || ''), 'utf8')).digest('hex');

const ANSWER = /^(OK|ERR)( |$)/;
const NONCE = /^NONCE ([0-9a-f]{32})$/;

class Wire {
  /**
   * Connect to host:port; resolves once the socket is up, with nothing said either way: the first line is the client's (pair()'s
   * HELLO). capture: a file every line heard is appended to (0600), for the sweep.
   */
  static connect(host, port, { capture = null, ms = 5000 } = {}) {
    return new Promise((resolve, reject) => {
      const w = new Wire();
      w.where = `${host}:${port}`;
      w.lines = new Inbox();
      w.capture = capture ? fs.openSync(capture, 'a', 0o600) : null;
      w.socket = net.connect({ host, port });
      w.socket.setNoDelay(true);
      let buf = '';
      let settled = false;
      let timer = null;
      const fail = (e) => { clearTimeout(timer); if (!settled) { settled = true; w.socket.destroy(); reject(e); } };
      timer = setTimeout(() => fail(new Error(`${w.where}: not connected within ${ms} ms`)), ms);
      w.socket.on('data', (d) => {
        buf += d;
        let nl;
        while ((nl = buf.indexOf('\n')) >= 0) {
          const line = buf.slice(0, nl).replace(/\r$/, '');
          buf = buf.slice(nl + 1);
          if (w.capture !== null) fs.writeSync(w.capture, line + '\n');
          const m = NONCE.exec(line);
          if (m) w.nonce = m[1];
          w.lines.push({ at: Date.now(), line });
        }
      });
      w.socket.on('error', (e) => { w.error = e; fail(new Error(`${w.where}: ${e.code || e.message}`)); });
      w.socket.on('close', () => { w.closed = true; w.lines.end(); if (w.capture !== null) { fs.closeSync(w.capture); w.capture = null; } });
      w.socket.once('connect', () => {
        clearTimeout(timer);
        if (settled) return;
        settled = true;
        resolve(w);
      });
    });
  }

  /** Send one line and resolve with its answer (the first OK or ERR line after it), within ms. */
  async ask(line, ms = 5000) {
    if (this.closed) throw new Error(`${this.where} is closed`);
    const from = this.lines.items.length;
    this.socket.write(line + '\n');
    const answer = await this.lines.wait((l) => ANSWER.test(l.line), ms, `the answer to ${JSON.stringify(line.split(' ')[0])} from ${this.where}`, from);
    return answer.line;
  }

  /**
   * HELLO <name> (the connection's first line), then AUTH-PROOF for the newest nonce, which both doors say before HELLO's answer;
   * resolves once the door says "OK paired", else throws its words.
   */
  async pair(name, token) {
    const hello = await this.ask(`HELLO ${name}`);
    if (!hello.startsWith('OK')) throw new Error(`${this.where} answered HELLO with ${JSON.stringify(hello)}`);
    if (!this.nonce) throw new Error(`${this.where} sent no nonce to prove`);
    const said = await this.ask(`AUTH-PROOF ${proof(token, this.nonce, name)}`);
    if (said !== 'OK paired') throw new Error(`${this.where} answered the proof with ${JSON.stringify(said)}`);
    this.paired = true;
    return said;
  }

  /** A verb whose answer is "OK <json>": the JSON, else an error with the answer's words. */
  async json(line, ms = 5000) {
    const said = await this.ask(line, ms);
    const m = /^OK (?:@\S+ )?([[{].*)$/.exec(said);
    if (!m) throw new Error(`${this.where} answered ${JSON.stringify(line)} with ${JSON.stringify(said.slice(0, 200))}`);
    return JSON.parse(m[1]);
  }

  close() {
    if (!this.closed) this.socket.destroy();
  }
}

module.exports = { Wire, proof, normalise };
