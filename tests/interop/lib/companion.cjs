// The rig's Companion door (TCP, CompanionServer) as the Companion module holds it, from the switch over the LAN: its first
// line is READY, `SUBSCRIBE JSON` turns the stream into SNAPSHOT frames (the operator's frame, whole: Companion is a trusted
// caller) with CONFIG beside them, and `PING <token>` is answered `PONG <token>`, with STUCK after it while the rig's show thread
// has stopped turning over. What the module's key shows for 'ready for doors' is read by the module's own rule (networkReady in
// companion-module/src/state.ts): unknown with no frame, and from a frame with no verdict or with the stuck verdict; else yes or
// no from the frame's network.ready. As the module does (main.ts, its connection set down on STUCK), a pong that says STUCK and a
// closed connection throw the frame away, so yes or no after a stall is read only from a frame the rig sent after it. What the
// connection hears is kept for the sweep (row S7.1) through `capture`, as heard.
'use strict';
const fs = require('node:fs');
const net = require('node:net');
const { Inbox } = require('./inbox.cjs');

class CompanionDeck {
  /** Connect to host:port, wait for READY, subscribe to the JSON frames and wait for the first SNAPSHOT (within ms). */
  static async connect(host, port, capture, ms = 5000) {
    const d = new CompanionDeck();
    d.where = `${host}:${port}`;
    d.lines = new Inbox();
    d.snapshot = null;
    d.stuck = false;
    d.capture = fs.openSync(capture, 'a', 0o600);
    d.socket = net.connect({ host, port });
    d.socket.setNoDelay(true);
    let buf = '';
    d.socket.on('data', (chunk) => {
      buf += chunk;
      let nl;
      while ((nl = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, nl).replace(/\r$/, '');
        buf = buf.slice(nl + 1);
        if (line.startsWith('SNAPSHOT ')) {
          let frame = null;
          try { frame = JSON.parse(line.slice('SNAPSHOT '.length)); } catch { frame = null; }
          // A frame comes several times a second: the capture keeps the first and each one whose verdict changed, and the
          // inbox only that a frame came.
          const said = JSON.stringify(frame && frame.network);
          if (d.capture !== null && said !== d.lastSaid) fs.writeSync(d.capture, line + '\n');
          d.lastSaid = said;
          d.snapshot = frame;
          d.lines.push({ at: Date.now(), line: 'SNAPSHOT' });
          continue;
        }
        if (d.capture !== null) fs.writeSync(d.capture, line + '\n');
        d.lines.push({ at: Date.now(), line });
      }
    });
    d.socket.on('error', (e) => { d.error = e; });
    d.socket.on('close', () => {
      d.closed = true;
      d.snapshot = null;
      d.lines.end();
      if (d.capture !== null) { fs.closeSync(d.capture); d.capture = null; }
    });
    try {
      await d.lines.wait((l) => l.line.startsWith('READY'), ms, `READY from the rig's Companion door at ${d.where}`);
      d.say('SUBSCRIBE JSON');
      await d.lines.wait((l) => l.line === 'SNAPSHOT', ms, `a first SNAPSHOT from ${d.where}`);
    } catch (e) {
      d.close();
      throw e;
    }
    return d;
  }

  say(line) { this.socket.write(line + '\n'); }

  /** A ping as the module sends one: resolves with the answer's words after the token ("" or "STUCK"), and keeps them. */
  async ping(ms = 3000) {
    const token = `interop${Date.now().toString(36)}`;
    const from = this.lines.items.length;
    this.say(`PING ${token}`);
    const pong = await this.lines.wait((l) => l.line.startsWith(`PONG ${token}`), ms, `the rig's PONG at ${this.where}`, from);
    this.stuck = /\bSTUCK\b/.test(pong.line.slice(`PONG ${token}`.length));
    if (this.stuck) this.snapshot = null;
    return this.stuck ? 'STUCK' : '';
  }

  /** What the module's network_ready reads now, by its own rule (see the header), after a ping that says whether it is stuck. */
  async networkReady() {
    await this.ping();
    const n = this.snapshot ? this.snapshot.network : null;
    if (!n || typeof n.ready !== 'boolean' || n.stuck === true) return 'unknown';
    return n.ready ? 'yes' : 'no';
  }

  close() { this.socket.destroy(); }
}

module.exports = { CompanionDeck };
