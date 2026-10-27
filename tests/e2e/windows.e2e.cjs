// Windows Stagewatch controls, in a real browser against the rig: what a window shows is what it was told, swapped in
// only once drawn; the holding page; cue and release; a page that won't load; two windows in one browser; blackout and
// identify; link loss each way and a restarted Stagewatch; a revoked address. Run: tools/test.sh e2e
'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const { Rig, until } = require('./rig.cjs');

const OUT = process.env.SW_E2E_OUT || '';
let rig, browser;

before(async () => {
  rig = await Rig.create();
  browser = await chromium.launch();
});
after(async () => {
  // The rig goes whatever the browser's close threw: a rig left running would outlive its folder's removal at this file's end.
  try {
    if (browser) await browser.close();
  } finally {
    if (rig) await rig.dispose();
  }
});

async function shot(page, name) {
  if (!OUT) return;
  fs.mkdirSync(OUT, { recursive: true });
  await page.screenshot({ path: path.join(OUT, name + '.png') });
}

/** A window's page, open, with a log of every value its data-showing attribute took. */
async function openWindow(context, name) {
  const w = await rig.window(name);
  const page = await context.newPage();
  await page.addInitScript(() => {
    window.__showing = [];
    // Watching the document (the root element doesn't exist yet when this runs).
    new MutationObserver(() => {
      const v = document.documentElement && document.documentElement.getAttribute('data-showing');
      if (v !== null && window.__showing[window.__showing.length - 1] !== v) window.__showing.push(v);
    }).observe(document, { attributes: true, subtree: true, attributeFilter: ['data-showing'] });
  });
  await page.goto(rig.base + w.path);
  return page;
}
const attr = (page, a) => page.evaluate((n) => document.documentElement.getAttribute(n), a);
const waitAttr = (page, a, v, ms = 8000) => page.waitForFunction(([n, x]) => document.documentElement.getAttribute(n) === x, [a, v], { timeout: ms });
const shell = (page) => page.evaluate(() => window.SWShell.state());
/** The text of the frame on screen. */
async function frontText(page) {
  const i = await page.evaluate(() => [...document.querySelectorAll('iframe')]
    .findIndex((f) => getComputedStyle(f).visibility === 'visible' && getComputedStyle(f).opacity === '1'));
  return i < 0 ? '' : page.frameLocator('iframe').nth(i).locator('body').innerText();
}

test('a new window shows its holding page, then the page it is given, swapped in only once drawn', async () => {
  const made = await rig.run('window new lobby');
  assert.equal(made.ok, true, JSON.stringify(made));
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
  const page = await openWindow(ctx, 'lobby');
  await waitAttr(page, 'data-showing', 'holding');
  await waitAttr(page, 'data-link', 'ok');
  assert.equal((await page.locator('#hold .idle').innerText()).toLowerCase(), 'lobby', 'an unused window says its name, faintly');

  const sent = Date.now();
  const r = await rig.run('window show lobby /clock');
  assert.equal(r.ok, true, JSON.stringify(r));
  await waitAttr(page, 'data-showing', '/clock');
  const took = Date.now() - sent;
  const text = await frontText(page);
  assert.match(text, /\d{1,2}:\d{2}/, 'the clock is drawn when it is shown');
  assert.deepEqual(await page.evaluate(() => window.__showing), ['holding', '/clock'], 'nothing in between: no blank, no half-built page');
  const w = await until(async () => { const x = await rig.window('lobby'); return x.inStep && x; }, 3000, 'lobby in step');
  assert.equal(w.shown.content, '/clock');
  assert.ok(w.shown.ms < 3000 && took < 5000, `swap ${w.shown.ms} ms, command to screen ${took} ms`);
  await shot(page, 'window-clock');

  const again = await rig.run('window show lobby /clock');
  assert.equal(again.ok, false, 'a show that changes nothing is refused');
  assert.match(again.error, /already shows/);
  await ctx.close();
});

test('hold keeps the page loaded underneath; release shows it at once without reloading it', async () => {
  const ctx = await browser.newContext();
  const page = await openWindow(ctx, 'lobby');
  await waitAttr(page, 'data-showing', '/clock');
  const frame = () => page.frames().find((f) => f.url().endsWith('/clock'));
  await frame().evaluate(() => { window.__mark = 'still here'; });

  assert.equal((await rig.run('window hold lobby text Back at 2pm')).ok, true);
  await waitAttr(page, 'data-hold', '1');
  assert.equal(await page.locator('#hold .words').innerText(), 'Back at 2pm');
  await shot(page, 'window-holding');

  const sent = Date.now();
  assert.equal((await rig.run('window release lobby')).ok, true);
  await waitAttr(page, 'data-hold', '0');
  assert.ok(Date.now() - sent < 2000, 'release is instant');
  assert.equal(await frame().evaluate(() => window.__mark), 'still here', 'the page was never reloaded');
  await ctx.close();
});

test('cue loads a page behind the holding page; release takes it at once', async () => {
  const ctx = await browser.newContext();
  const page = await openWindow(ctx, 'lobby');
  await waitAttr(page, 'data-showing', '/clock');
  assert.equal((await rig.run('window cue lobby /timeline')).ok, true);
  await waitAttr(page, 'data-hold', '1');
  await until(async () => (await shell(page)).page === '/timeline', 8000, 'timeline loaded behind the holding page');
  assert.equal(await attr(page, 'data-hold'), '1', 'still holding while it loads');
  const sent = Date.now();
  assert.equal((await rig.run('window release lobby')).ok, true);
  await waitAttr(page, 'data-hold', '0');
  assert.equal(await attr(page, 'data-showing'), '/timeline');
  assert.ok(Date.now() - sent < 2000, 'the cued page is up at once');
  await ctx.close();
});

test('a page that will not load never reaches the screen: the old page stays and the reason goes back', async () => {
  const ctx = await browser.newContext();
  const page = await openWindow(ctx, 'lobby');
  await waitAttr(page, 'data-showing', '/timeline');
  assert.equal((await rig.run('window show lobby /no-such-page')).ok, true, 'the server accepts a page path');
  await page.waitForFunction(() => document.documentElement.getAttribute('data-error') !== '', null, { timeout: 10000 });
  assert.equal(await attr(page, 'data-showing'), '/timeline', 'the old page stays up');
  assert.match(await attr(page, 'data-error'), /isn't a Stagewatch page/);
  const w = await until(async () => { const x = await rig.window('lobby'); return x.shown && x.shown.error && x; }, 3000, 'the error reported');
  assert.equal(w.inStep, false);
  // The context is rebuilt once a second: wait for the one that has the window's report.
  const item = await until(async () => { const c = await rig.context(); const i = c.items.find((x) => x.id === 'window:lobby'); return i && /couldn't show/.test(i.state) && i; }, 4000, 'the failure in the context');
  assert.equal(item.light, 'amber');
  assert.match(item.state, /couldn't show \/no-such-page/);
  assert.equal((await rig.run('window show lobby /clock')).ok, true);
  await waitAttr(page, 'data-showing', '/clock');
  assert.equal(await attr(page, 'data-error'), '');
  await ctx.close();
});

test('two windows in one browser profile (shared storage, as in OBS or vMix) are two windows', async () => {
  assert.equal((await rig.run('window new foyer /countdown?to=next')).ok, true);
  const ctx = await browser.newContext();
  const lobby = await openWindow(ctx, 'lobby');
  const foyer = await openWindow(ctx, 'foyer');
  await waitAttr(lobby, 'data-showing', '/clock');
  await waitAttr(foyer, 'data-showing', '/countdown?to=next');
  assert.equal((await rig.run('window hold foyer')).ok, true);
  await waitAttr(foyer, 'data-hold', '1');
  assert.equal(await attr(lobby, 'data-hold'), '0', 'holding one window leaves the other alone');
  assert.equal((await rig.run('window release foyer')).ok, true);
  await ctx.close();
});

test('blackout covers every window; identify shows a window its name', async () => {
  const ctx = await browser.newContext();
  const page = await openWindow(ctx, 'lobby');
  await waitAttr(page, 'data-showing', '/clock');
  assert.equal((await rig.run('blackout on')).ok, true);
  await page.waitForFunction(() => getComputedStyle(document.getElementById('black')).display === 'block', null, { timeout: 5000 });
  assert.equal((await rig.run('blackout off')).ok, true);
  await page.waitForFunction(() => getComputedStyle(document.getElementById('black')).display === 'none', null, { timeout: 5000 });
  assert.equal((await rig.run('window identify lobby')).ok, true);
  await page.waitForFunction(() => getComputedStyle(document.getElementById('ident')).display === 'flex', null, { timeout: 5000 });
  assert.equal(await page.locator('#ident .big').innerText(), 'lobby');
  await ctx.close();
});

test('a dropped link shows the holding page (or NOT CONNECTED), and a restarted Stagewatch puts every window back', async () => {
  assert.equal((await rig.run('window loss foyer warn')).ok, true);
  const ctx = await browser.newContext();
  const lobby = await openWindow(ctx, 'lobby');
  const foyer = await openWindow(ctx, 'foyer');
  await waitAttr(lobby, 'data-showing', '/clock');
  await waitAttr(foyer, 'data-showing', '/countdown?to=next');

  await rig.stop();
  await waitAttr(lobby, 'data-link', 'lost', 12000);
  assert.equal(await attr(lobby, 'data-hold'), '1', 'an audience window goes to its holding page, never a stale clock');
  await waitAttr(foyer, 'data-link', 'lost', 12000);
  assert.equal(await foyer.evaluate(() => document.documentElement.classList.contains('lost-warn')), true, 'a crew window dims and says NOT CONNECTED');
  assert.equal(await foyer.locator('#lost').isVisible(), true);
  await shot(foyer, 'window-lost-warn');

  await rig.start();
  await waitAttr(lobby, 'data-link', 'ok', 15000);
  await waitAttr(lobby, 'data-hold', '0');
  assert.equal(await attr(lobby, 'data-showing'), '/clock', 'the window comes back to what it was showing');
  await waitAttr(foyer, 'data-link', 'ok', 15000);
  assert.equal(await foyer.evaluate(() => document.documentElement.classList.contains('lost-warn')), false);
  await until(async () => (await rig.window('lobby')).inStep, 5000, 'lobby in step after the restart');
  await ctx.close();
});

test('revoke: the old address goes dark and stays dark; the new one works', async () => {
  const ctx = await browser.newContext();
  const page = await openWindow(ctx, 'foyer');
  await waitAttr(page, 'data-showing', '/countdown?to=next');
  const old = (await rig.window('foyer')).path;
  assert.equal((await rig.run('window revoke foyer')).ok, true);
  await waitAttr(page, 'data-link', 'gone');
  assert.equal(await page.locator('#gone').isVisible(), true);
  const res = await fetch(rig.base + old);
  assert.equal(res.status, 404);
  const fresh = (await rig.window('foyer')).path;
  assert.notEqual(fresh, old);
  const again = await ctx.newPage();
  await again.goto(rig.base + fresh);
  await waitAttr(again, 'data-showing', '/countdown?to=next');
  await ctx.close();
});
