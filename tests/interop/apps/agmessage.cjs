// AGMessage in the lane: its server (app/server.js, on the Node builds.json names: one its engines field accepts) in its own
// namespace on its default port, plain HTTP (AGM_TLS=0), with its data, keys and uploads in the run's folder. Its admin token
// reaches the server only through ADMIN_TOKEN, its one door for it, which only the same user can read, and is kept in the
// run's owner-only secrets folder for the lane's own readers. The server prints that token in its start banner by design;
// the capture redacts that one line by name before it is kept anywhere. It is ready when it says it is running and GET
// /api/health from the switch, over the LAN, answers 200.
'use strict';
const path = require('node:path');
const fs = require('node:fs');
const { AppProcess, baseEnv } = require('../lib/proc.cjs');
const { hex } = require('../lib/run.cjs');
const { get } = require('../lib/net.cjs');

const PORT = 3023;
const BANNER_TOKEN = /^(\s*Token:\s*)\S+/;

class AGMessage {
  constructor(run, host) {
    this.run = run;
    this.host = host;
    this.folders = run.folders('agmessage');
    this.adminToken = hex(16);
    run.secrets.file('agmessage.adminToken', this.adminToken);   // for the admin helper and the sweep; the server reads ADMIN_TOKEN
    for (const sub of ['data', 'keys', 'uploads']) fs.mkdirSync(path.join(this.folders.home, sub), { mode: 0o700 });
  }

  get base() { return `http://${this.host.addr}:${PORT}`; }

  /** Start it; resolves once it says it is running and its health answers (within ms). */
  async start(ms = 60000) {
    const h = this.folders.home;
    this.proc = new AppProcess(this.run, {
      name: 'agmessage',
      ns: this.host.ns,
      command: this.run.builds.agmessage.node,
      args: [this.run.builds.agmessage.server],
      env: {
        ...baseEnv(this.folders),
        PORT: String(PORT), HOST: '0.0.0.0', AGM_TLS: '0', ANTHROPIC_API_KEY: '',
        AGM_DATA_DIR: path.join(h, 'data'), AGM_KEY_DIR: path.join(h, 'keys'), AGM_UPLOAD_DIR: path.join(h, 'uploads'),
        ADMIN_TOKEN: this.adminToken,
      },
      cwd: this.folders.user,
      redact: (line) => line.replace(BANNER_TOKEN, '$1<redacted: the admin token, printed by design>'),
    });
    const running = await this.proc.out.wait((l) => / is running$/.test(l.line), ms, 'AGMessage saying it is running')
      .catch((e) => { throw this.proc.why(e); });
    const health = await get(`${this.base}/api/health`).catch((e) => { throw this.proc.why(e); });
    if (health.status !== 200 || !health.json || health.json.ok !== true) {
      throw new Error(`GET ${this.base}/api/health answered ${health.status} ${health.text.slice(0, 120)}`);
    }
    this.readyMs = Date.now() - this.proc.spawnedAt;
    this.running = running.line.trim();
    this.health = health.json;
    return health.json;
  }

  /** It says the build it is, the one the clone holds; its output and error kept no secret once its banner was redacted. */
  checkReady() {
    const wrong = [];
    const commit = this.run.builds.agmessage.commit.slice(0, 7);
    if (this.health.version !== this.run.builds.agmessage.version) wrong.push(`version ${this.health.version}`);
    if (!String(this.health.build || '').includes(commit)) wrong.push(`build ${this.health.build}, not ${commit}`);
    for (const [stream, inbox] of [['output', this.proc.out], ['error', this.proc.err]]) {
      const leaked = this.run.secrets.foundIn(inbox.items.map((i) => i.line).join('\n'));
      if (leaked.length) wrong.push(`its kept ${stream} holds ${leaked.join(', ')}`);
    }
    if (wrong.length) throw new Error(`AGMessage: ${wrong.join('; ')}`);
  }

  stop() { return this.proc ? this.proc.stop('SIGTERM') : null; }

  /** What it keeps a secret in by design: its server's secret key (in its data folder) and its key folder (AGM_KEY_DIR). */
  secretFiles() { return [path.join(this.folders.home, 'data', 'secret.key'), path.join(this.folders.home, 'keys')]; }
}

module.exports = { AGMessage, PORT };
