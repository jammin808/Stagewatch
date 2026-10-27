// The browser workflows' temporary folders: each made in the temp folder (os.tmpdir(), TMPDIR off Windows) by tempDir and
// removed by its maker with removeDir when its test ends, a removal a process still holds retried, each wait longer, then said.
// When a test file's process ends, any folder it made that still stands is named, removed (it is that process's own) and fails
// the file: its tests may all have passed, but a run that leaves what it made behind has not. Nothing else in the temp folder
// is ever touched: an earlier run's leftovers and another process's are not this process's to remove. A rig started by
// rig.cjs makes nothing of its own there (RIG_ENV), so a rig ended with SIGKILL leaves nothing behind either.
'use strict';
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

/** How many times fs.rmSync retries a removal a process still holds (EBUSY, EPERM, ENOTEMPTY); each wait is RETRY_MS longer. */
const RETRIES = 5;
const RETRY_MS = 100;

const made = new Set();

/** A folder of this process's own in the temp folder, `<prefix>XXXXXX`; removeDir removes it. */
function tempDir(prefix) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), prefix));
  made.add(dir);
  return dir;
}

/** Remove a folder tempDir made, with all it holds: true once it is gone; false, said on standard error, if it still stands. */
function removeDir(dir) {
  try {
    fs.rmSync(dir, { recursive: true, force: true, maxRetries: RETRIES, retryDelay: RETRY_MS });
  } catch (e) {
    process.stderr.write(`e2e: ${dir} could not be removed (${e.code || e.message}); this file's end names it again\n`);
    return false;
  }
  made.delete(dir);
  return true;
}

/** The folders this process made that still stand. */
function leftBehind() {
  return [...made].filter((d) => fs.existsSync(d));
}

process.on('exit', () => {
  const left = leftBehind();
  if (left.length === 0) return;
  const stand = left.filter((d) => !removeDir(d));
  process.stderr.write(`e2e: ${left.length} temporary folder(s) this file's tests made were not removed by them: ${left.join(', ')}`
    + (stand.length === 0 ? ' (removed now)\n' : `; ${stand.length} could not be removed now either: ${stand.join(', ')}\n`));
  process.exitCode = 1;
});

module.exports = { tempDir, removeDir, leftBehind };
