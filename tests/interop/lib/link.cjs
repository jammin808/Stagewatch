// LINK on a node link (Show Bus 1.3 §13.6.5) as a connector says it, from the switch: a WebSocket over TLS held to the pinned
// certificate (nothing of the upgrade is written until the server has shown the certificate pinned), /ws?node=<id>&t=<kind>&link=1
// with no Authorization header, the acceptor's offer read, and one link message sent: the client proof over the transcript T, made
// with the key given (13.6.1-2, the kit's own construction in tools/showbus/peerlink-peer.mjs). The verdict is read and, when it
// says ok, its server proof is checked with the same K over the same T, so a verdict the lane takes as ok is the acceptor's proof
// and not its words alone. The key is used only in this process to make the proof: it is never written to the socket, a log or a
// result. One connection, its messages kept in order; closed by the row that opened it.
'use strict';
const crypto = require('node:crypto');
const tls = require('node:tls');
const { fingerprint } = require('./net.cjs');

/** §14: peerlink.label, the transcript's first line and the label of every derivation. */
const LABEL = 'showbus-peerlink-1';
/** The offer a connector makes (link=1) and the version it speaks. */
const OFFER = 1;

const utf8 = (s) => Buffer.from(s, 'utf8');
const sha256 = (b) => crypto.createHash('sha256').update(b).digest();
const hmacHex = (k, s) => crypto.createHmac('sha256', k).update(utf8(s)).digest('hex');

/** 13.5.1: a key's K, and its kid. */
const keyK = (secret) => sha256(utf8(secret));
const kidOf = (secret) => sha256(Buffer.concat([utf8(`${LABEL} kid\n`), keyK(secret)])).toString('hex').slice(0, 16);

/** 13.6.1: T, its ten lines joined by LF. */
const transcript = (f) => [LABEL, 'carrier=ws', `offer=${f.offer}`, `speaks=${f.speaks}`, `server=${f.server}`, `client=${f.client}`,
  `kid=${f.kid}`, `snonce=${f.snonce}`, `cnonce=${f.cnonce}`, `bind=${f.bind}`].join('\n');

/** One client WebSocket frame: FIN, the opcode, masked as a client's must be. */
function frame(opcode, payload) {
  const mask = crypto.randomBytes(4);
  const n = payload.length;
  const head = n < 126 ? Buffer.from([0x80 | opcode, 0x80 | n])
    : n < 65536 ? Buffer.from([0x80 | opcode, 0x80 | 126, n >> 8, n & 0xff])
      : Buffer.concat([Buffer.from([0x80 | opcode, 0x80 | 127]), (() => { const b = Buffer.alloc(8); b.writeBigUInt64BE(BigInt(n)); return b; })()]);
  const body = Buffer.from(payload);
  for (let i = 0; i < n; i++) body[i] ^= mask[i % 4];
  return Buffer.concat([head, mask, body]);
}

class LinkSocket {
  constructor(socket) {
    this.socket = socket;
    this.messages = [];
    this.waiters = [];
    this.closed = false;
    this.buf = Buffer.alloc(0);
  }

  /** The text messages the server sent, parsed; frames other than text are answered (ping) or end it (close). */
  feed(chunk) {
    this.buf = Buffer.concat([this.buf, chunk]);
    for (;;) {
      if (this.buf.length < 2) return;
      const op = this.buf[0] & 0x0f;
      let n = this.buf[1] & 0x7f;
      let at = 2;
      if (n === 126) { if (this.buf.length < 4) return; n = this.buf.readUInt16BE(2); at = 4; }
      else if (n === 127) { if (this.buf.length < 10) return; n = Number(this.buf.readBigUInt64BE(2)); at = 10; }
      if (this.buf.length < at + n) return;
      const payload = this.buf.subarray(at, at + n);
      this.buf = this.buf.subarray(at + n);
      if (op === 0x1) {
        let json = null;
        try { json = JSON.parse(payload.toString('utf8')); } catch { json = null; }
        this.messages.push(json);
        this.wake();
      } else if (op === 0x9) {
        this.socket.write(frame(0xa, payload));
      } else if (op === 0x8) {
        this.end();
        return;
      }
    }
  }

  wake() { for (const w of this.waiters.splice(0)) w(); }

  end() { if (!this.closed) { this.closed = true; this.socket.destroy(); this.wake(); } }

  send(obj) { this.socket.write(frame(0x1, utf8(JSON.stringify(obj)))); }

  /** The first message from index `from` that passes pred, within ms. */
  next(pred, ms, what, from = 0) {
    const deadline = Date.now() + ms;
    return new Promise((resolve, reject) => {
      const look = () => {
        const i = this.messages.findIndex((m, k) => k >= from && m && pred(m));
        if (i >= 0) { clearTimeout(timer); resolve(this.messages[i]); return; }
        if (this.closed) { clearTimeout(timer); reject(new Error(`the link closed before ${what}`)); return; }
        this.waiters.push(look);
      };
      const timer = setTimeout(() => reject(new Error(`no ${what} within ${ms} ms`)), Math.max(1, deadline - Date.now()));
      look();
    });
  }

  close() {
    if (this.closed) return;
    try { this.socket.write(frame(0x8, Buffer.alloc(0))); } catch { /* the socket went first: nothing left to say */ }
    this.end();
  }
}

/**
 * Dial `host:port`'s /ws over TLS held to `pin` as node `{ t, id }`, offer LINK and prove with `secret` (a node key's) under `kid`
 * (the key's own unless given: a wrong proof for a key in force is that key's kid with another secret's proof). Resolves with
 * { link, offer, verdict, proved }: `proved` is true only when the verdict says ok and its proof verifies with K over T. Throws when
 * the offer is not one (bind must name the pinned certificate).
 */
async function dialLink({ host, port, pin, node, secret, kid = kidOf(secret), ms = 5000 }) {
  const socket = await new Promise((resolve, reject) => {
    const s = tls.connect({ host, port, rejectUnauthorized: false, servername: undefined });
    const timer = setTimeout(() => { s.destroy(); reject(new Error(`${host}:${port}: no TLS handshake within ${ms} ms`)); }, ms);
    s.once('error', (e) => { clearTimeout(timer); reject(e); });
    s.once('secureConnect', () => {
      clearTimeout(timer);
      const shown = s.getPeerCertificate();
      const fp = shown && shown.raw ? fingerprint(shown.raw) : '';
      if (fp !== pin) { s.destroy(); reject(new Error(`${host}:${port} showed ${fp || '(none)'}, not the pinned ${pin}: nothing sent`)); return; }
      resolve(s);
    });
  });
  const wsKey = crypto.randomBytes(16).toString('base64');
  const query = `node=${encodeURIComponent(node.id)}&t=${encodeURIComponent(node.t)}&link=${OFFER}`;
  socket.write(`GET /ws?${query} HTTP/1.1\r\nHost: ${host}:${port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n`
    + `Sec-WebSocket-Key: ${wsKey}\r\nSec-WebSocket-Version: 13\r\n\r\n`);
  const link = new LinkSocket(socket);
  await new Promise((resolve, reject) => {
    let head = Buffer.alloc(0);
    const timer = setTimeout(() => { socket.destroy(); reject(new Error(`${host}:${port}: no upgrade within ${ms} ms`)); }, ms);
    const onData = (d) => {
      head = Buffer.concat([head, d]);
      const end = head.indexOf('\r\n\r\n');
      if (end < 0) return;
      socket.off('data', onData);
      clearTimeout(timer);
      const status = head.subarray(0, end).toString('latin1').split('\r\n')[0];
      if (!/^HTTP\/1\.1 101 /.test(status)) { socket.destroy(); reject(new Error(`${host}:${port}/ws answered ${status}`)); return; }
      socket.on('data', (c) => link.feed(c));
      socket.on('close', () => link.end());
      socket.on('error', () => link.end());
      if (head.length > end + 4) link.feed(head.subarray(end + 4));
      resolve();
    };
    socket.on('data', onData);
  });
  const offer = await link.next((m) => m.type === 'link', ms, "the acceptor's offer");
  if (offer.bind !== `tls:${pin}` || !offer.server || typeof offer.snonce !== 'string') {
    link.close();
    throw new Error(`the offer is not one this connector can prove over: ${JSON.stringify(offer)}`);
  }
  const cnonce = crypto.randomBytes(16).toString('hex');
  const T = transcript({ offer: OFFER, speaks: offer.speaks, server: `${offer.server.t}:${offer.server.id}`, client: `${node.t}:${node.id}`, kid,
    snonce: offer.snonce, cnonce, bind: offer.bind });
  const K = keyK(secret);
  link.send({ type: 'link', client: { t: node.t, id: node.id }, cnonce, kid, proof: hmacHex(K, `client\n${T}`) });
  const verdict = await link.next((m) => m.type === 'auth', ms, 'the verdict', link.messages.indexOf(offer) + 1);
  const expected = hmacHex(K, `server\n${T}`);
  const proved = verdict.ok === true && typeof verdict.proof === 'string' && verdict.proof.length === expected.length
    && crypto.timingSafeEqual(Buffer.from(verdict.proof), Buffer.from(expected));
  return { link, offer, verdict, proved, kid };
}

module.exports = { dialLink, kidOf };
