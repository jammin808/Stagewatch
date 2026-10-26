// One run of the lane: its folder ($SW_INTEROP_OUT/run, made afresh and this user's alone by tools/interop/run.sh), what
// tools/interop/build.sh built (builds.json, the only place the driver learns where an app is), and the register of every
// secret the run makes or learns. Secrets go to the apps through 0600 files in run/secrets, a tmpfs of the run's own mount
// namespace that ends with the run (or, for AGMessage's admin token, its environment, its one door), never through argv,
// standard output or a log; the register lets a reader check that (the sweep, row S7.1) without printing one. A file an app
// keeps a secret in by design (its settings with its token, its TLS key) is named by the app (secretFiles()) and removed once
// the app has stopped; until then it is 0600 in a 0700 folder.
'use strict';
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const OUT = path.resolve(ROOT, process.env.SW_INTEROP_OUT || path.join('artifacts', 'interop'));
/** statfs's f_type for a tmpfs (linux/magic.h). */
const TMPFS_MAGIC = 0x01021994;

class Secrets {
  constructor(dir) { this.dir = dir; this.byName = new Map(); }

  /** Remember a secret under a name; the name is what any report says, never the value. */
  add(name, value) {
    if (typeof value === 'string' && value.length >= 4) this.byName.set(name, value);
    return value;
  }

  /** A secret written to an owner-only file in the run's secrets folder: its path. */
  file(name, value) {
    const where = path.join(this.dir, name);
    fs.writeFileSync(where, value + '\n', { mode: 0o600 });
    this.add(name, value);
    return where;
  }

  names() { return [...this.byName.keys()]; }

  /** The names of the registered secrets found in text (empty when none is). */
  foundIn(text) { return [...this.byName].filter(([, v]) => text.includes(v)).map(([n]) => n); }

  /** The driver's own words (an error, a matrix field) with every registered secret named instead of said. The apps' logs
   *  are never scrubbed: they stay as each app said them, for the sweep to read. */
  scrub(text) {
    let t = String(text);
    for (const [name, value] of this.byName) t = t.split(value).join(`<${name}>`);
    return t;
  }
}

/** 32 upper-case hex: a pairing token as the apps write one, already in the form their proofs normalise to. */
const token = () => crypto.randomBytes(16).toString('hex').toUpperCase();
const hex = (bytes) => crypto.randomBytes(bytes).toString('hex');

class Run {
  constructor() {
    const doc = JSON.parse(fs.readFileSync(path.join(OUT, 'builds.json'), 'utf8'));
    if (doc.notRun) throw new Error(`the builds were not made: ${doc.notRun}`);
    this.builds = doc;
    this.out = OUT;
    this.dir = path.join(OUT, 'run');
    const secrets = path.join(this.dir, 'secrets');
    // run.sh made the folder afresh and its secrets folder a tmpfs; anything else is refused before a secret is made.
    const made = fs.existsSync(secrets) && fs.statfsSync(secrets).type === TMPFS_MAGIC
      && fs.readdirSync(secrets).length === 0 && fs.readdirSync(this.dir).length === 1;
    if (!made) {
      throw new Error(`${this.dir} is not a run's folder as tools/interop/run.sh makes one (afresh, its secrets folder an empty `
        + 'tmpfs of the run\'s own): the driver runs through tools/test.sh interop');
    }
    for (const dir of [this.dir, secrets]) {
      if ((fs.statSync(dir).mode & 0o077) !== 0) throw new Error(`${dir} is open to others than this user`);
    }
    for (const sub of ['logs', 'apps']) fs.mkdirSync(path.join(this.dir, sub), { mode: 0o700 });
    this.secrets = new Secrets(secrets);
    this.started = new Date();
  }

  /**
   * Remove what an app keeps a secret in by design (its secretFiles(), each a file or a folder), once it has stopped; the
   * paths removed, relative to the lane's folder.
   */
  removeSecretFiles(paths) {
    const removed = [];
    for (const p of paths) {
      if (!fs.existsSync(p)) continue;
      fs.rmSync(p, { recursive: true, force: true });
      removed.push(this.rel(p));
    }
    return removed;
  }

  /** An app's folders: home (its own, given to it), user (its HOME), tmp (its TMPDIR); home is left for the app to make
   *  when `appMakesHome` (Patterns' bench refuses a folder it did not make or mark). */
  folders(app, appMakesHome = false) {
    const base = path.join(this.dir, 'apps', app);
    const f = { home: path.join(base, 'home'), user: path.join(base, 'user'), tmp: path.join(base, 'tmp') };
    for (const [k, dir] of Object.entries(f)) if (!(k === 'home' && appMakesHome)) fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
    return f;
  }

  log(name) { return path.join(this.dir, 'logs', name); }

  /** A path as the matrix names evidence: relative to the lane's folder. */
  rel(p) { return path.relative(this.out, p); }

  /** The machine the run is on, as the matrix's header says it. */
  static machine() {
    let distro = os.type();
    try {
      const m = /^PRETTY_NAME="?([^"\n]+)"?/m.exec(fs.readFileSync('/etc/os-release', 'utf8'));
      if (m) distro = m[1];
    } catch (e) { distro = `${os.type()} (no /etc/os-release: ${e.code})`; }
    return `${distro} ${os.release()} ${os.arch()}`;
  }
}

module.exports = { Run, Secrets, token, hex, ROOT, OUT };
