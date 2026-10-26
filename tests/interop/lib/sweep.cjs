// The sweep (row S7.1): every file a run kept, read whole, searched for every secret the run's register holds, in either
// case (a hex token said in lower case is still that token). What it reads: every app's standard output and error, every
// log, journal, settings and recovery file an app wrote in its home, every line a wire connection heard (STATE, CONTEXT, EYE),
// every capture the driver kept of a door it read (a frame, a context, a report and its CSV) and the matrix; what it never
// reads: the run's own secrets folder (the register, a tmpfs gone with the run) and the files an app keeps a secret in by
// design, which are removed once the app has stopped and named in the matrix, before the sweep runs. A finding names the
// file and the secret's name, never the secret.
'use strict';
const fs = require('node:fs');
const path = require('node:path');

/** Every file under each root (recursively), but the paths in skip and what lies under them. */
function files(roots, skip) {
  const out = [];
  const skipped = (p) => skip.some((s) => p === s || p.startsWith(s + path.sep));
  const walk = (p) => {
    if (skipped(p) || !fs.existsSync(p)) return;
    const st = fs.lstatSync(p);
    if (st.isDirectory()) for (const name of fs.readdirSync(p).sort()) walk(path.join(p, name));
    else if (st.isFile()) out.push(p);
  };
  for (const r of roots) walk(r);
  return out;
}

/** The names of the register's secrets found in text, in either case. */
function namesIn(secrets, text) {
  const t = String(text).toLowerCase();
  return [...secrets.byName].filter(([, v]) => t.includes(v.toLowerCase())).map(([n]) => n);
}

/**
 * Sweep roots (files and folders) and texts (name → text, what the driver holds in memory, as the matrix) for the register's
 * secrets: { files, bytes, found: [{ where, names }] }.
 */
function sweep(secrets, { roots = [], skip = [], texts = {} } = {}) {
  const found = [];
  let bytes = 0;
  const list = files(roots, skip);
  for (const f of list) {
    const buf = fs.readFileSync(f);
    bytes += buf.length;
    const names = namesIn(secrets, buf.toString('utf8'));
    if (names.length) found.push({ where: f, names });
  }
  for (const [name, text] of Object.entries(texts)) {
    const names = namesIn(secrets, text);
    if (names.length) found.push({ where: name, names });
  }
  return { files: list.length, bytes, found };
}

module.exports = { sweep, namesIn, files };
