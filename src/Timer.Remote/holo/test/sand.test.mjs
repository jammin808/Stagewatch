// The egg timer's sand must behave like a real one: amounts by volume, never by height.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CAPACITY, topSurface, topVolume, bottomPile, bottomVolume, totalVolume, NECK, TOP_FULL, FLOOR } from '../sand.js';

const near = (a, b, tol, msg) => assert.ok(Math.abs(a - b) <= tol, `${msg}: ${a} vs ${b}`);

test('full and empty', () => {
  near(topSurface(1), TOP_FULL, 1e-9, 'all the time left → top full');
  near(topSurface(0), NECK, 1e-9, 'no time left → top empty');
  assert.equal(bottomPile(0).y, -FLOOR, 'nothing fallen → bottom empty');
  assert.equal(bottomPile(0).heapH, 0);
});

test('the top holds exactly the fraction of time left', () => {
  for (const left of [0.05, 0.1, 0.25, 0.5, 0.75, 0.9]) {
    near(topVolume(topSurface(left)) / CAPACITY, left, 0.002, `top at ${left}`);
  }
});

test('the bottom (pile + heap) holds exactly the time gone', () => {
  for (const gone of [0.02, 0.1, 0.25, 0.5, 0.75, 1]) {
    const p = bottomPile(gone);
    const v = bottomVolume(p.y) + (Math.PI * p.heapR * p.heapR * p.heapH) / 3;
    near(v / CAPACITY, gone, 0.003, `bottom at ${gone}`);
  }
});

test('sand is conserved at every moment', () => {
  for (let i = 0; i <= 100; i++) near(totalVolume(i / 100) / CAPACITY, 1, 0.004, `total at ${i}%`);
});

test('half the time is half the sand, not half the height', () => {
  const half = topSurface(0.5);
  assert.ok(Math.abs(half - (NECK + TOP_FULL) / 2) > 0.02, 'a curved bulb: half the sand sits at a different height');
});

test('levels move one way as time passes', () => {
  let lastTop = Infinity, lastBottom = -Infinity;
  for (let i = 100; i >= 0; i--) {
    const t = topSurface(i / 100), b = bottomPile(1 - i / 100).y;
    assert.ok(t <= lastTop + 1e-12, 'top only falls');
    assert.ok(b >= lastBottom - 1e-12, 'bottom only rises');
    lastTop = t; lastBottom = b;
  }
});
