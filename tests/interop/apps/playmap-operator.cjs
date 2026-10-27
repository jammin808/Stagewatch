// PlayMap's operator at PlayMap's own machine: this helper runs inside PlayMap's network namespace (the driver starts it there
// with nsenter --net), so a line that carries a secret reaches PlayMap's wire at 127.0.0.1 and never crosses the run's LAN.
// It pairs once by proof (lib/wire.cjs: the token never crosses even this connection), with PlayMap's token read from an
// owner-only file, keeps that one connection for its life, and answers JSON lines on its standard input, one JSON line each
// on its standard output:
//   link <host:port> <name>  LINK STAGEWATCH <host:port> <the node key in the owner-only file <name> of --secrets-dir>
//                                                     → {"operator":"link","said":"<PlayMap's answer line>"}
//   off                      LINK STAGEWATCH OFF      → {"operator":"off","said":"<PlayMap's answer line>"}
//   <other>                  refused, never guessed   → {"operator":"refused","line":"<first word>","error":"…"}
// A failure is {"operator":"<word>","error":"…"}, its words never a secret. What it prints is PlayMap's answer as said, which
// holds no secret: the driver holds it to the run's register, and the sweep (row S7.1) reads this helper's output and every
// line its connection heard (--capture). A key is read from its file for the one line that says it and kept nowhere else. The
// end of its input ends it.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const readline = require('node:readline');
const { Wire } = require('../lib/wire.cjs');

const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const tokenFile = opt('--token-file');
const secretsDir = opt('--secrets-dir');
const capture = opt('--capture');
const port = Number(opt('--port'));
if (!tokenFile || !secretsDir || !capture || !Number.isInteger(port) || port <= 0) {
  process.stderr.write('playmap-operator: --token-file <file> --secrets-dir <dir> --capture <file> --port <port>\n');
  process.exit(2);
}

/** The name it pairs as: what PlayMap's sessions and journal say this connection is. */
const HELLO = 'Operator';
/** A Stagewatch address as LINK STAGEWATCH takes one here: a host and a port, no more. */
const ADDRESS = /^[0-9A-Za-z.-]{1,253}:[0-9]{1,5}$/;
/** A secret's file name in the run's secrets folder: one name, never a path. */
const NAME = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;

const out = (o) => process.stdout.write(JSON.stringify(o) + '\n');

/** The first line of an owner-only file, refused when others may read it. */
function secretOf(file) {
  if ((fs.statSync(file).mode & 0o077) !== 0) throw new Error(`${path.basename(file)} is open to others than this user`);
  return fs.readFileSync(file, 'utf8').split('\n')[0].trim();
}

let conn = null;
async function wire() {
  if (conn && !conn.closed) return conn;
  const w = await Wire.connect('127.0.0.1', port, { capture });
  await w.pair(HELLO, secretOf(tokenFile));
  conn = w;
  return w;
}

async function handle(line) {
  const words = line.trim().split(/\s+/);
  const word = words[0] || '';
  try {
    switch (word) {
      case 'link': {
        if (words.length !== 3 || !ADDRESS.test(words[1]) || !NAME.test(words[2])) {
          out({ operator: 'refused', line: word, error: 'link takes <host:port> <name of a file in the secrets folder>' });
          return;
        }
        const key = secretOf(path.join(secretsDir, words[2]));
        if (!key) throw new Error(`${words[2]} holds no key`);
        out({ operator: 'link', said: await (await wire()).ask(`LINK STAGEWATCH ${words[1]} ${key}`) });
        return;
      }
      case 'off':
        if (words.length !== 1) { out({ operator: 'refused', line: word, error: 'off takes no words' }); return; }
        out({ operator: 'off', said: await (await wire()).ask('LINK STAGEWATCH OFF') });
        return;
      default:
        out({ operator: 'refused', line: word.slice(0, 32), error: 'unknown (link <host:port> <name>, off)' });
    }
  } catch (e) {
    out({ operator: word.slice(0, 32), error: e.message });
  }
}

let queue = Promise.resolve();
const rl = readline.createInterface({ input: process.stdin });
rl.on('line', (line) => { queue = queue.then(() => handle(line)); });
rl.on('close', () => { queue.then(() => { if (conn) conn.close(); process.exit(0); }); });
