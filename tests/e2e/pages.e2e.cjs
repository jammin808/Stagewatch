// Every public page draws a frame from the rig (never a placeholder like --:--), on its own and inside a window, with no
// script errors. What PageShot's screenshots can't tell (a page that never got its frame) is checked here.
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const { Rig } = require('./rig.cjs');

const OUT = process.env.SW_E2E_OUT || '';
let rig, browser;
before(async () => {
  rig = await Rig.create();
  browser = await chromium.launch();
  await rig.run('start');
});
after(async () => {
  if (browser) await browser.close();
  if (rig) await rig.stop();
});

// Each page and what "drew a frame" means on it (run inside the page).
const PAGES = [
  ['/viewer', () => /\d\d:\d\d/.test(document.getElementById('timer').textContent)],
  ['/clock', () => /\d{1,2}:\d\d/.test(document.getElementById('time').textContent)],
  ['/countdown?to=next', () => /\d/.test(document.getElementById('big').textContent)],
  ['/timeline', () => document.getElementById('lane').children.length > 0],
  ['/audience', () => document.getElementById('agenda').children.length > 0],
  ['/layout?name=lobby', () => document.getElementById('screen').children.length > 0],
  ['/holding', () => getComputedStyle(document.getElementById('idle')).visibility === 'visible'],
];

for (const [page, drew] of PAGES) {
  test(`${page} draws a frame, on its own and inside a window`, async () => {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    const p = await ctx.newPage();
    const errors = [];
    p.on('pageerror', (e) => errors.push(e.message));
    await p.goto(rig.base + page);
    await p.waitForFunction(drew, null, { timeout: 8000 });
    if (OUT) {
      fs.mkdirSync(OUT, { recursive: true });
      await p.screenshot({ path: path.join(OUT, 'page' + page.replace(/[^a-z0-9]+/gi, '_') + '.png') });
    }

    // The same page framed by a window: frames come from the window's link, not the page's own.
    const name = 'p' + page.replace(/[^a-z0-9]/gi, '').slice(0, 20);
    const made = await rig.run(`window new ${name} ${page}`);
    assert.equal(made.ok, true, JSON.stringify(made));
    const w = await rig.window(name);
    const win = await ctx.newPage();
    win.on('pageerror', (e) => errors.push('window: ' + e.message));
    const sockets = [];
    win.on('websocket', (ws) => sockets.push(ws.url()));
    await win.goto(rig.base + w.path);
    await win.waitForFunction((x) => document.documentElement.getAttribute('data-showing') === x, page, { timeout: 8000 });
    const front = await win.evaluate(() => [...document.querySelectorAll('iframe')].findIndex((f) => getComputedStyle(f).visibility === 'visible'));
    const framed = win.frameLocator('iframe').nth(front);
    assert.ok(await framed.locator('body').evaluate(drew), `${page} drew inside the window`);
    assert.equal(sockets.length, 1, `one link per window, not one per page (${sockets.join(', ')})`);
    assert.deepEqual(errors, []);
    await rig.run(`window delete ${name}`);
    await ctx.close();
  });
}
