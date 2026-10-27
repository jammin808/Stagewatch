// Fake playout peers on the line wire, for the rig's playout links: a PlayMap node and a Patterns desk as Show Bus 1.1 peers speak
// to a level-0 connector. Each greets with a NONCE of 32 lowercase hex and pairs only on the AUTH-PROOF of its token (13.6.8: the
// token with dashes, spaces, tabs, CRs and LFs removed and upper-cased, HMAC-SHA256 over the nonce and the HELLO name); answers the
// probe and pings with timed pongs, STATUS with its STATE, CONTEXT with its context and (PlayMap) WHOAMI; and each command by the
// test's own answer, by the line without its id. Every line it hears is kept (heard), so a test can say what reached it — and what
// never did, the token among it. PlayMap pushes its STATE on HELLO before the pairing, as its 9941 does; push() says a line (a STATE
// as either peer pushes one while its clip plays) on every paired connection. Loopback only; each listens on a port of its own and
// closes with close().
'use strict';
const net = require('node:net');
const crypto = require('node:crypto');

/** 13.6.8's key: the token as 1.1 desks compare it. */
function legacyKey(token) {
  return token.replace(/[-\s]/g, '').toUpperCase();
}

/** 13.6.8's proof of `token` over `nonce` for HELLO name `name`, lowercase hex. */
function proof(token, nonce, name = 'Stagewatch') {
  return crypto.createHmac('sha256', Buffer.from(legacyKey(token), 'utf8')).update(nonce + name, 'utf8').digest('hex');
}

function split(line) {
  if (!line.startsWith('@')) return { tag: '', body: line };
  const sp = line.indexOf(' ');
  return sp < 0 ? { tag: line, body: '' } : { tag: line.slice(0, sp), body: line.slice(sp + 1) };
}

const ok = (tag, words = '') => 'OK' + (tag ? ' ' + tag : '') + (words ? ' ' + words : '');
const err = (tag, words) => 'ERR' + (tag ? ' ' + tag : '') + ' ' + words;

class FakePeer {
  /**
   * kind: 'playmap' | 'patterns'; token: its pairing token; state(): its STATE JSON now; context: its context JSON; whoami: PlayMap's
   * WHOAMI JSON (none for Patterns); commands(body, tag, reply): the answer lines for a command (an array, or null for none), where
   * reply(lines, ms) sends later lines on the same connection.
   */
  constructor({ kind, token, state, context, whoami = null, commands = () => null }) {
    Object.assign(this, { kind, token, state, context, whoami, commands });
    this.heard = [];
    this.sockets = new Set();
    this.paired = new Set();
    this.server = net.createServer((sock) => this.serve(sock));
  }

  listen() {
    return new Promise((resolve, reject) => {
      this.server.once('error', reject);
      this.server.listen(0, '127.0.0.1', () => { this.port = this.server.address().port; resolve(this); });
    });
  }

  serve(sock) {
    this.sockets.add(sock);
    sock.setNoDelay(true);
    let nonce = crypto.randomBytes(16).toString('hex');
    let paired = false;
    let buf = '';
    const say = (line) => { if (!sock.destroyed) sock.write(line + '\n'); };
    say('NONCE ' + nonce);
    sock.on('data', (d) => {
      buf += d;
      let nl;
      while ((nl = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, nl).replace(/\r$/, '');
        buf = buf.slice(nl + 1);
        if (!line) continue;
        this.heard.push(line);
        const { tag, body } = split(line);
        if (body.startsWith('HELLO')) {
          say(ok(tag));
          if (this.kind === 'playmap') say('STATE ' + this.state());
        } else if (body === 'NONCE') {
          nonce = crypto.randomBytes(16).toString('hex');
          say(ok(tag, nonce));
        } else if (body.startsWith('AUTH-PROOF ')) {
          paired = body.slice(11) === proof(this.token, nonce);
          if (paired) this.paired.add(sock); else this.paired.delete(sock);
          say(paired ? ok(tag, 'paired') : err(tag, 'wrong token'));
        } else if (body.startsWith('AUTH ')) {
          say(err(tag, 'use AUTH-PROOF'));
        } else if (body.startsWith('PING')) {
          const now = String(Date.now());
          say(ok(tag, 'PONG ' + (body.length > 5 ? body.slice(5) : now) + ' ' + now + ' ' + now));
        } else if (!paired) {
          say(err(tag, 'not paired'));
        } else if (body === 'STATUS') {
          say(ok(tag, this.state()));
        } else if (body === 'CONTEXT') {
          say(ok(tag, this.context));
        } else if (body === 'WHOAMI') {
          say(this.whoami ? ok(tag, this.whoami) : err(tag, 'unknown verb WHOAMI'));
        } else {
          const later = (lines, ms) => setTimeout(() => lines.forEach(say), ms);
          const answer = this.commands(body, tag, later);
          if (answer) answer.forEach(say);
        }
      }
    });
    sock.on('error', () => {});
    sock.on('close', () => { this.sockets.delete(sock); this.paired.delete(sock); });
  }

  /** Says `line` on every paired connection: a STATE pushed, as either peer pushes one while its clip plays. */
  push(line) {
    for (const s of this.paired) if (!s.destroyed) s.write(line + '\n');
  }

  /** The commands it heard (lines with a request id that are not the link's own asks), without their ids. */
  commandsHeard() {
    return this.heard.map(split).filter((l) => /^@s\d+-/.test(l.tag)).map((l) => l.body);
  }

  close() {
    for (const s of this.sockets) s.destroy();
    return new Promise((resolve) => this.server.close(() => resolve()));
  }
}

/** A PlayMap node on its line wire (9941's shape), listening on a free loopback port. */
async function fakePlayMap(options) {
  return new FakePeer({ kind: 'playmap', ...options }).listen();
}

/** A Patterns desk on its line wire (9697's shape), listening on a free loopback port. */
async function fakePatterns(options) {
  return new FakePeer({ kind: 'patterns', ...options }).listen();
}

module.exports = { fakePlayMap, fakePatterns, proof, legacyKey, ok, err };
