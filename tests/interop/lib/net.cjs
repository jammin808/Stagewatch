// What the switch (the driver, at 10.99.0.1) asks of an app over the LAN: the first line a wire says to a connection (an
// unpaired one hears "NONCE <hex>" first) and HTTP GETs, with no credential unless a row gives one. node:http and node:net
// read no proxy variable, and none reaches into the lane's namespaces.
'use strict';
const http = require('node:http');
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
function get(url, ms = 5000, headers = {}) {
  return new Promise((resolve, reject) => {
    const req = http.get(url, { timeout: ms, agent: false, headers }, (res) => {
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
  });
}

module.exports = { greeting, get };
