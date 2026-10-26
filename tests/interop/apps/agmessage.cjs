// AGMessage in the lane: its server (app/server.js, on the Node builds.json names: one its engines field accepts) in its own
// namespace on its default port, plain HTTP (AGM_TLS=0), with its data, keys and uploads in the run's folder. Its admin token
// reaches the server only through ADMIN_TOKEN, its one door for it, which only the same user can read, and is kept in the
// run's owner-only secrets folder for the lane's own readers. The server prints that token in its start banner by design;
// the capture redacts that one line by name before it is kept anywhere. It is ready when it says it is running and GET
// /api/health from the switch, over the LAN, answers 200. Its Stagewatch link is typed in its settings file before it starts
// (data/settings.json, where its admin page would save it; no key in it). The lane reads it as its admin does, from its own
// 127.0.0.1 (apps/agmessage-admin.cjs, started inside its namespace), and learns its context token there (admin:settings,
// handed over in an owner-only file in the run's secrets folder), so the sweep looks for that token too.
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
    this.tokenFile = run.secrets.file('agmessage.adminToken', this.adminToken);   // for the admin helper and the sweep; the server reads ADMIN_TOKEN
    this.contextTokenFile = path.join(run.secrets.dir, 'agmessage.contextToken');  // the admin helper writes it, the driver registers it
    for (const sub of ['data', 'keys', 'uploads']) fs.mkdirSync(path.join(this.folders.home, sub), { mode: 0o700 });
  }

  get base() { return `http://${this.host.addr}:${PORT}`; }

  /** Start it linked to `stagewatch` (host:port, typed in its settings before it starts); resolves once it says it is running
   *  and its health answers (within ms). */
  async start(stagewatch, ms = 60000) {
    const h = this.folders.home;
    fs.writeFileSync(path.join(h, 'data', 'settings.json'), JSON.stringify({ stagewatchUrl: `http://${stagewatch}` }, null, 2), { mode: 0o600 });
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

  /** Its admin, signed in from its own 127.0.0.1 (the helper in its namespace); started once, at the first read. */
  async adminHelper() {
    if (this.admin && this.admin.running) return this.admin;
    const admin = new AppProcess(this.run, {
      name: 'agmessage-admin',
      ns: this.host.ns,
      command: process.execPath,
      args: [path.join(__dirname, 'agmessage-admin.cjs'), '--token-file', this.tokenFile, '--context-token-out', this.contextTokenFile,
        '--port', String(PORT)],
      env: baseEnv(this.folders),
      cwd: this.folders.user,
      stdin: true,
    });
    this.admin = admin;
    const said = await this.ask('login');
    if (!said.ok) throw new Error(`AGMessage's admin sign-in answered ${said.status}: ${said.error}`);
    return admin;
  }

  /** A line to the admin helper; its answer. */
  async ask(line, ms = 8000) {
    const from = this.admin.out.items.length;
    const parsed = (l) => { try { return JSON.parse(l.line); } catch { return null; } };
    const word = line.split(' ')[0];
    const answer = this.admin.out.wait((l) => { const j = parsed(l); return j && (j.admin === word || j.admin === 'refused'); }, ms, `the admin's answer to "${word}"`, from);
    this.admin.say(line);
    const j = parsed(await answer);
    if (j.error && j.admin !== 'login') throw new Error(`AGMessage's admin: ${j.error}`);
    return j;
  }

  /** Its context as its admin reads it (Show Bus §6, with every peer it hears and its Stagewatch link). */
  async context() {
    await this.adminHelper();
    const said = await this.ask('context');
    if (said.status !== 200 || !said.context) throw new Error(`AGMessage's context answered ${said.status}`);
    return said.context;
  }

  /** Its health as the host laptop reads it: its own facts, its Stagewatch link's status among them (`stage`). */
  async hostHealth() {
    await this.adminHelper();
    const said = await this.ask('health');
    if (said.status !== 200 || !said.health) throw new Error(`AGMessage's health answered ${said.status}`);
    return said.health;
  }

  /**
   * Its admin's settings (admin:settings on the admin's socket, as its admin page reads them): its Stagewatch link
   * (stagewatchUrl, stage), every peer it hears with the address its hello came from and its status, and its context token,
   * which the reply names by its place: the helper hands the token over in its owner-only file, and it is registered here.
   */
  async settings() {
    await this.adminHelper();
    const said = await this.ask('settings');
    if (!said.ok || !said.settings) throw new Error(`AGMessage's admin settings: ${said.error || 'no settings'}`);
    if (said.settings.contextToken && !this.contextToken) {
      const token = fs.readFileSync(this.contextTokenFile, 'utf8').split('\n')[0].trim();
      if (!/^agn_[A-Za-z0-9_-]{16,}$/.test(token)) throw new Error("AGMessage's context token, as its helper handed it over, is not one");
      this.contextToken = this.run.secrets.add('agmessage.contextToken', token);
    }
    return said.settings;
  }

  /**
   * Learn its context token (settings()) and keep the sweep for it honest: from where the lane reads, GET /api/context with
   * no token is refused (401) and with this token answered (200) as AGMessage's own context, so the value the sweep looks for
   * is the one AGMessage takes. Resolves with the statuses and the context's node id.
   */
  async learnContextToken() {
    await this.settings();
    if (!this.contextToken) throw new Error("AGMessage's admin settings named no context token");
    const none = await get(`${this.base}/api/context`);
    if (none.status !== 401) throw new Error(`GET ${this.base}/api/context with no token answered ${none.status}, not 401`);
    const right = await get(`${this.base}/api/context`, 5000, { Authorization: `Bearer ${this.contextToken}` });
    if (right.status !== 200 || !right.json || !right.json.node) throw new Error(`GET ${this.base}/api/context with its context token answered ${right.status}`);
    return { withoutToken: none.status, withToken: right.status, node: right.json.node.id };
  }

  async closeAdmin() {
    if (this.admin) { await this.admin.stop('eof', 5000); this.admin = null; }
  }

  async stop() { await this.closeAdmin(); return this.proc ? this.proc.stop('SIGTERM') : null; }

  /** What it keeps a secret in by design: its server's secret key (in its data folder) and its key folder (AGM_KEY_DIR). */
  secretFiles() { return [path.join(this.folders.home, 'data', 'secret.key'), path.join(this.folders.home, 'keys')]; }
}

module.exports = { AGMessage, PORT };
