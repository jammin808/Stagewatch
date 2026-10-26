// What the switch (the driver, at 10.99.0.1) asks of an app over the LAN: the first line a wire says to a connection (an
// unpaired one hears "NONCE <hex>" first) and HTTP requests, with no credential unless a row gives one. An https:// request is
// held to a pinned certificate (Show Bus §10: the pin, never a name or an authority): nothing of it, its headers included, is
// sent until the server has shown the certificate whose fingerprint was pinned. node:http, node:https and node:net read no
// proxy variable, and none reaches into the lane's namespaces.
'use strict';
const crypto = require('node:crypto');
const http = require('node:http');
const https = require('node:https');
const net = require('node:net');

/** The first line a TCP door says to a fresh connection, within ms. */
function greeting(host, port, ms = 5000) {
  return new Promise((resolve, reject) => {
    const s = net.connect({ host, port });
    let buf = '';
    const done = (err, line) => { clearTimeout(timer); s.destroy(); if (err) reject(err); else resolve(line); };
    const timer = setTimeout(() => done(new Error(`${host}:${port} said no line within ${ms} ms`)), ms);
    s.on('data', (d) => {
      buf += d;
      const nl = buf.indexOf('\n');
      if (nl >= 0) done(null, buf.slice(0, nl).replace(/\r$/, ''));
    });
    s.on('error', (e) => done(new Error(`${host}:${port}: ${e.code || e.message}`)));
    s.on('end', () => done(new Error(`${host}:${port} closed before a line`)));
  });
}

/** A GET (with no credential unless `headers` carries one): its status, text and JSON (null when the body is not JSON). */
function get(url, ms = 5000, headers = {}) { return request('GET', url, { ms, headers }); }

/** A certificate's fingerprint as the show network names it: "sha256:" and the lower-case hex of SHA-256 over its DER bytes. */
const fingerprint = (der) => 'sha256:' + crypto.createHash('sha256').update(der).digest('hex');

/**
 * A request with a method and an optional body (sent as JSON): its status, text and JSON. An https:// url needs `pin` (a
 * fingerprint): the request is written only once the server's certificate is that one, and fails naming what was shown
 * otherwise.
 */
function request(method, url, { ms = 5000, headers = {}, body, pin } = {}) {
  return new Promise((resolve, reject) => {
    const data = body === undefined ? null : Buffer.from(JSON.stringify(body));
    const all = data ? { ...headers, 'Content-Type': 'application/json', 'Content-Length': data.length } : headers;
    const secure = url.startsWith('https:');
    if (secure && !/^sha256:[0-9a-f]{64}$/.test(pin || '')) { reject(new Error(`${url}: no certificate pinned to hold it to`)); return; }
    // The certificate is checked against the pin here, not by an authority: a self-signed one is the show network's kind.
    const opts = secure ? { method, timeout: ms, agent: false, headers: all, rejectUnauthorized: false } : { method, timeout: ms, agent: false, headers: all };
    const req = (secure ? https : http).request(url, opts, (res) => {
      let text = '';
      res.setEncoding('utf8');
      res.on('data', (d) => { text += d; });
      res.on('end', () => {
        let json = null;
        try { json = JSON.parse(text); } catch { json = null; }
        resolve({ status: res.statusCode, text, json });
      });
    });
    req.on('timeout', () => req.destroy(new Error(`${url}: no answer within ${ms} ms`)));
    req.on('error', reject);
    if (!secure) {
      req.end(data || undefined);
      return;
    }
    // Headers and body wait for the handshake: written only to the pinned certificate.
    req.on('socket', (socket) => socket.once('secureConnect', () => {
      const shown = socket.getPeerCertificate();
      const fp = shown && shown.raw ? fingerprint(shown.raw) : '';
      if (fp !== pin) { req.destroy(new Error(`${url} showed the certificate ${fp || '(none)'}, not the one pinned (${pin}): nothing sent`)); return; }
      req.end(data || undefined);
    }));
  });
}

module.exports = { greeting, get, request, fingerprint };
