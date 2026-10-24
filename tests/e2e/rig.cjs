// The rig for browser workflows: starts tools/Stagewatch.Rig on free ports, talks to it over HTTP like any remote, and
// stops it. Restarting with the same state file and port is how a test makes "Stagewatch restarted" happen.
'use strict';
const { spawn } = require('node:child_process');
const net = require('node:net');
const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');

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

class Rig {
  constructor(port, state) { this.port = port; this.state = state; this.proc = null; this.log = ''; this.companion = 0; this.osc = 0; }

  /** A rig on free ports; with { wire: true } also its Companion (TCP) and OSC (UDP) servers. */
  static async create(options = {}) {
    if (!fs.existsSync(DLL)) throw new Error(`the rig isn't built (${DLL}): dotnet build tools/Stagewatch.Rig`);
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sw-rig-'));
    const rig = new Rig(await freePort(), path.join(dir, 'windows.json'));
    if (options.wire) { rig.companion = await freePort(); rig.osc = await freePort(); }
    await rig.start();
    return rig;
  }

  get base() { return `http://127.0.0.1:${this.port}`; }

  start() {
    return new Promise((resolve, reject) => {
      const proc = spawn('dotnet', [DLL, '--port', String(this.port), '--companion', String(this.companion), '--osc', String(this.osc), '--pin', PIN,
        '--state', this.state, '--exit-after', '600'],
        { stdio: ['ignore', 'pipe', 'pipe'] });
      this.proc = proc;
      let out = '';
      const timer = setTimeout(() => reject(new Error('the rig did not start in 20 s:\n' + this.log)), 20000);
      proc.stdout.on('data', (d) => {
        out += d;
        const line = out.split('\n')[0];
        if (out.includes('\n') && line.includes('"rig":"ready"')) { clearTimeout(timer); resolve(JSON.parse(line)); }
      });
      proc.stderr.on('data', (d) => { this.log += d; });
      proc.on('exit', (code) => { clearTimeout(timer); if (code !== 0 && !this.stopping) reject(new Error(`the rig exited ${code}:\n${this.log}`)); });
    });
  }

  /** Stop the rig as a crash would look to a screen: the process is gone, its sockets with it. */
  stop() {
    if (!this.proc || this.proc.exitCode !== null) return Promise.resolve();
    this.stopping = true;
    return new Promise((resolve) => { this.proc.once('exit', () => { this.stopping = false; resolve(); }); this.proc.kill('SIGKILL'); });
  }

  /** One command line through the HTTP API, with a receipt (attempts are not facts: the receipt says what happened). */
  async run(line) {
    const parts = line.trim().split(/\s+/).map(encodeURIComponent);
    const res = await fetch(`${this.base}/api/${parts.join('/')}`, { headers: { 'X-Stagewatch-Pin': PIN, 'X-Request-Id': 'e2e' + Date.now() } });
    return res.json();
  }

  async windows() {
    const res = await fetch(`${this.base}/api/windows`, { headers: { 'X-Stagewatch-Pin': PIN } });
    return (await res.json()).windows;
  }

  async window(name) { return (await this.windows()).find((w) => w.name === name); }

  async context() {
    const res = await fetch(`${this.base}/api/context`, { headers: { 'X-Stagewatch-Pin': PIN } });
    return res.json();
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

module.exports = { Rig, until, PIN, ROOT };
