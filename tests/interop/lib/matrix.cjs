// The matrix: one row per cross-app fact, each pass or fail (or "not run", only for a lane that could not start), with its
// measured time, its deadline, the rig's lockout counts read after it (per address, its wrong PINs and its wrong view
// passes) and its evidence files; and what the run measured
// beside the rows. Written as $SW_INTEROP_OUT/<topology>/matrix.json and matrix.md after every run of that topology, a
// failed one included. A row is written only for a test that ran: a row that cannot pass yet is not in the lane at all.
'use strict';
const fs = require('node:fs');
const path = require('node:path');

class Matrix {
  constructor(run, topology) {
    this.run = run;
    this.doc = {
      v: 1,
      at: run.started.toISOString(),
      machine: run.constructor.machine(),
      topology,
      builds: Matrix.builds(run.builds),
      rows: [],
      measured: {},
      secretFilesRemoved: [],
    };
  }

  static builds(b) {
    const short = (c) => c.slice(0, 12);
    return {
      stagewatch: `${short(b.stagewatch.commit)} ${b.stagewatch.version} (${b.stagewatch.note})`,
      patterns: `${short(b.patterns.commit)} (${b.patterns.note})`,
      playmap: `${short(b.mapper.commit)} (${b.mapper.note})`,
      agmessage: `${short(b.agmessage.commit)} ${b.agmessage.version} on node ${b.agmessage.nodeVersion} (${b.agmessage.note})`,
    };
  }

  /** A build's own words, learned from the running app (its ready line or health), added to the header. */
  said(app, words) { this.doc.builds[app] += ` · ${words}`; }

  measure(key, value) { this.doc.measured[key] = value; }

  /** The files the apps kept a secret in by design, removed once they stopped (paths relative to the topology's folder),
   *  added to those removed before. */
  removed(paths) { this.doc.secretFilesRemoved = [...new Set([...this.doc.secretFilesRemoved, ...paths])]; }

  /**
   * A row as a node:test body: runs fn(row) (which may fill row.evidence, row.auth and row.facts), times it, records pass
   * or fail with the error's words, and rethrows so the test fails too. deadlineMs is the row's deadline, or one per step
   * for a row of steps (each counted from its own step's event, as the row says), or null for a row with none; ms is the
   * whole row's time.
   */
  row({ id, title, apps, deadlineMs }, fn) {
    return async () => {
      const row = { id, title, apps, verdict: 'fail', ms: 0, deadlineMs, auth: {}, evidence: [], facts: {} };
      this.doc.rows.push(row);
      const start = Date.now();
      try {
        await fn(row);
        row.verdict = 'pass';
      } catch (e) {
        row.error = this.run.secrets.scrub(e.message);
        throw new Error(row.error);
      } finally {
        row.ms = Date.now() - start;
        row.evidence = [...new Set(row.evidence.filter((p) => fs.existsSync(p)).map((p) => this.run.rel(p)))];
      }
    };
  }

  write() {
    const out = this.run.out;
    fs.writeFileSync(path.join(out, 'matrix.json'), JSON.stringify(this.doc, null, 1) + '\n');
    const d = this.doc;
    const lines = [
      `# Interop matrix — ${d.topology}, ${d.at}`,
      '',
      `Machine: ${d.machine}`,
      '',
      ...Object.entries(d.builds).map(([app, b]) => `- ${app}: ${b}`),
      '',
      '| Row | Fact | Apps | Verdict | ms | Deadline ms | Lockout after it (wrong PINs/view passes) | Evidence |',
      '|---|---|---|---|---|---|---|---|',
      ...d.rows.map((r) => `| ${r.id} | ${r.title} | ${r.apps.join(', ')} | ${r.verdict}${r.error ? ': ' + r.error.replace(/\|/g, '/').slice(0, 300) : ''} | ${r.ms} | ${r.deadlineMs === null ? '—' : [].concat(r.deadlineMs).join(' / ')} | ${Object.entries(r.auth).map(([a, n]) => `${a} ${n.pins}/${n.viewPasses}`).join(', ')} | ${r.evidence.join(', ')} |`),
      '',
      'What each row read:',
      '',
      '```json',
      JSON.stringify(Object.fromEntries(d.rows.map((r) => [r.id, r.facts])), null, 1),
      '```',
      '',
      'Measured beside the rows:',
      '',
      '```json',
      JSON.stringify(d.measured, null, 1),
      '```',
      '',
      `Removed once the apps stopped, as files they keep a secret in by design: ${d.secretFilesRemoved.join(', ') || 'none'}.`,
      '',
    ];
    fs.writeFileSync(path.join(out, 'matrix.md'), lines.join('\n'));
  }
}

module.exports = { Matrix };
