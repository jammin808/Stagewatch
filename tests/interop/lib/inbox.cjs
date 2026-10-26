// Lines kept in arrival order, each with the time it arrived, and waits for the first one (from an index on) that passes a
// test. A wait that ends unanswered says what it waited for and the last lines it saw, which are already redacted where the
// capture redacts (lib/proc.cjs), so its words carry nothing the logs do not.
'use strict';

class Inbox {
  constructor() { this.items = []; this.waiters = []; this.ended = false; }

  push(item) {
    this.items.push(item);
    this.waiters = this.waiters.filter((w) => !w.check());
  }

  end() {
    this.ended = true;
    this.waiters = this.waiters.filter((w) => !w.check());
  }

  /** The first item at or after `from` for which pred(item) holds, within ms; else an error naming `what`. */
  wait(pred, ms, what, from = 0) {
    return new Promise((resolve, reject) => {
      const w = {
        check: () => {
          for (let i = from; i < this.items.length; i++) {
            if (pred(this.items[i])) { clearTimeout(w.timer); resolve(this.items[i]); return true; }
          }
          if (this.ended) { clearTimeout(w.timer); reject(new Error(`ended before ${what}: ${this.tail(from)}`)); return true; }
          return false;
        },
      };
      w.timer = setTimeout(() => {
        this.waiters = this.waiters.filter((x) => x !== w);
        reject(new Error(`no ${what} within ${ms} ms: ${this.tail(from)}`));
      }, ms);
      if (!w.check()) this.waiters.push(w);
    });
  }

  tail(from) { return JSON.stringify(this.items.slice(from).slice(-6).map((i) => i.line)); }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** An error that ends a wait at once (until() rethrows it): what is waited for can no longer come, as when its app ended. */
function fatal(message) {
  const e = new Error(message);
  e.fatal = true;
  return e;
}

/**
 * Poll fn() every 100 ms until it gives a truthy value, which it returns; after ms, an error with the last value or error. An
 * error fn() throws is kept as the last and the polling goes on (the answer may still come), except one made by fatal(),
 * which ends the wait with that error then.
 */
async function until(fn, ms, what) {
  const end = Date.now() + ms;
  let last = null;
  for (;;) {
    try {
      const value = await fn();
      if (value) return value;
      last = value;
    } catch (e) {
      if (e && e.fatal) throw e;
      last = e.message;
    }
    if (Date.now() >= end) throw new Error(`no ${what} within ${ms} ms (last: ${JSON.stringify(last)})`);
    await sleep(100);
  }
}

module.exports = { Inbox, until, fatal, sleep };
