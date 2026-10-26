// An app started inside a host's network namespace (nsenter --net=<ns>, which enters it and runs the app as the same
// process, so the pid is the app's own), with an environment of its own (no variable of the driver's: no proxy, no token,
// only PATH, its own HOME and TMPDIR and what the app is given) and its standard output and error captured line by line to
// run/logs/<name>.out and .err. A line is redacted before it is kept anywhere only where an app prints a secret by design
// and by name (AGMessage's banner); everything else is kept as said, so the sweep can find what an app should not have said.
'use strict';
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const { Inbox } = require('./inbox.cjs');

/** The variables every app gets: the tools on PATH and its own folders, nothing of the driver's. */
function baseEnv(folders) {
  return {
    PATH: process.env.PATH,
    HOME: folders.user,
    TMPDIR: folders.tmp,
    LANG: 'C.UTF-8',
    DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    DOTNET_NOLOGO: '1',
    ...(process.env.DOTNET_ROOT ? { DOTNET_ROOT: process.env.DOTNET_ROOT } : {}),
  };
}

class AppProcess {
  /**
   * name: the logs' name; ns: the network namespace (a /proc/<pid>/ns/net path); command and args: what runs there;
   * env: its whole environment; stdin: true keeps a pipe to it (control lines; its end stops apps that read it);
   * redact(line): the line as kept (default: as said).
   */
  constructor(run, { name, ns, command, args, env, cwd, stdin = false, redact = (l) => l }) {
    this.name = name;
    this.out = new Inbox();
    this.err = new Inbox();
    this.exit = null;
    this.spawnedAt = Date.now();
    this.logs = { out: run.log(`${name}.out`), err: run.log(`${name}.err`) };
    const outLog = fs.openSync(this.logs.out, 'a', 0o600);
    const errLog = fs.openSync(this.logs.err, 'a', 0o600);
    this.proc = spawn('nsenter', [`--net=${ns}`, '--', command, ...args], {
      cwd, env, stdio: [stdin ? 'pipe' : 'ignore', 'pipe', 'pipe'],
    });
    this.pid = this.proc.pid;
    if (stdin) this.proc.stdin.on('error', () => { /* an app that is gone takes no more lines */ });
    const lines = (stream, inbox, fd) => {
      let buf = '';
      stream.on('data', (d) => {
        buf += d;
        let nl;
        while ((nl = buf.indexOf('\n')) >= 0) {
          const line = redact(buf.slice(0, nl).replace(/\r$/, ''));
          buf = buf.slice(nl + 1);
          fs.writeSync(fd, line + '\n');
          inbox.push({ at: Date.now(), line });
        }
      });
      stream.on('end', () => {
        if (buf) {
          const line = redact(buf);
          fs.writeSync(fd, line + '\n');
          inbox.push({ at: Date.now(), line });
        }
        inbox.end();
        fs.closeSync(fd);
      });
    };
    lines(this.proc.stdout, this.out, outLog);
    lines(this.proc.stderr, this.err, errLog);
    this.exited = new Promise((resolve) => {
      this.proc.on('exit', (code, signal) => { this.exit = { code, signal, at: Date.now() }; resolve(this.exit); });
      this.proc.on('error', (e) => { this.exit = { code: -1, signal: null, error: e.message, at: Date.now() }; resolve(this.exit); });
    });
  }

  get running() { return this.exit === null; }

  /** Why a wait on it ended badly: the error's words, then how it ended (if it has) and its last lines of standard error. */
  why(e) {
    const ended = this.exit ? ` (it ended: ${JSON.stringify({ code: this.exit.code, signal: this.exit.signal, error: this.exit.error })})` : '';
    const tail = this.err.items.slice(-4).map((i) => i.line);
    return new Error(`${e.message}${ended}${tail.length ? `; its standard error ends ${JSON.stringify(tail)}` : ''}`);
  }

  /** The network namespace the app's own process is in, as the kernel names it (net:[inode]). */
  namespace() { return fs.readlinkSync(`/proc/${this.pid}/ns/net`); }

  /** A control line on the app's standard input. */
  say(line) { this.proc.stdin.write(line + '\n'); }

  /**
   * Stop it: `gently` is how it is asked to stop on its own (end its input, or a signal); after `graceMs` it is killed.
   * Resolves with how it ended ({ code, signal }).
   */
  async stop(gently = 'SIGTERM', graceMs = 15000) {
    if (!this.running) return this.exit;
    if (gently === 'eof') this.proc.stdin.end();
    else this.proc.kill(gently);
    const timer = setTimeout(() => { if (this.running) this.proc.kill('SIGKILL'); }, graceMs);
    const how = await this.exited;
    clearTimeout(timer);
    return how;
  }
}

module.exports = { AppProcess, baseEnv };
