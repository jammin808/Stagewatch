// The LAN of a run: tools/interop/lan.py started in the driver's own network namespace (the switch room), its first line the
// map of hosts, then cables pulled and put back through its standard input. Ending its input ends it and its namespaces'
// holders; an app already running in a namespace keeps that namespace until the run's PID namespace ends.
'use strict';
const { spawn } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const { Inbox } = require('./inbox.cjs');
const { ROOT } = require('./run.cjs');

class Lan {
  static async start(run, topology, hosts = []) {
    const lan = new Lan();
    const log = fs.openSync(run.log('lan.out'), 'a', 0o600);
    lan.proc = spawn('python3', ['-I', path.join(ROOT, 'tools', 'interop', 'lan.py'), topology, ...hosts], {
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    lan.lines = new Inbox();
    let buf = '';
    let errors = '';
    lan.proc.stdout.on('data', (d) => {
      buf += d;
      let nl;
      while ((nl = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, nl);
        buf = buf.slice(nl + 1);
        fs.writeSync(log, line + '\n');
        let json = null;
        try { json = JSON.parse(line); } catch (e) { json = { error: `not JSON: ${line.slice(0, 200)} (${e.message})` }; }
        lan.lines.push({ at: Date.now(), line, json });
      }
    });
    lan.proc.stderr.on('data', (d) => { errors += d; fs.writeSync(log, d); });
    lan.proc.on('exit', () => lan.lines.end());
    lan.proc.stdin.on('error', () => { /* lan.py has ended */ });
    const first = await lan.lines.wait(() => true, 15000, "lan.py's map").catch((e) => { throw new Error(`${e.message} ${errors}`); });
    if (first.json.error) throw new Error(`lan.py: ${first.json.error}`);
    lan.map = first.json;
    return lan;
  }

  host(name) {
    const h = this.map.hosts[name];
    if (!h) throw new Error(`no host ${name} on this LAN`);
    return h;
  }

  /** Pull ('down') or put back ('up') a host's cable; resolves once lan.py says it did. */
  async cable(name, state) {
    const from = this.lines.items.length;
    this.proc.stdin.write(`${state} ${name}\n`);
    const said = await this.lines.wait((l) => l.json.cable === name || l.json.error, 5000, `the cable of ${name} ${state}`, from);
    if (said.json.error) throw new Error(`lan.py: ${said.json.error}`);
    return said.json;
  }

  async close() {
    if (this.proc.exitCode !== null || this.proc.signalCode !== null) return;
    const ended = new Promise((r) => this.proc.once('exit', r));
    this.proc.stdin.end();
    await ended;
  }
}

module.exports = { Lan };
