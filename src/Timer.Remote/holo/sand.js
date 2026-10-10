// Egg timer sand, by volume — like a real one. The glass is a solid of revolution, so the sand in a bulb up to a
// height is ∫ π r(y)² dy over the inner profile. For a fraction of time left, the top bulb holds exactly that
// fraction of the sand and the bottom bulb (pile + heap) holds the rest; the total never changes. Pure maths,
// no three.js, so it is unit-tested (test/sand.test.mjs).

export const H = 1.15;       // bulb height (neck to cap)
export const RN = 0.075;     // neck radius
export const R = 0.62;       // widest
export const RC = 0.5;       // where the glass meets the cap

/** Glass radius at height u (0 = neck, 1 = cap) — an egg-shaped bulb. */
export function bulb(u) {
  if (u <= 0.82) return RN + (R - RN) * Math.sqrt(1 - Math.pow(1 - u / 0.82, 2.2));
  const k = (u - 0.82) / 0.18;
  return R - (R - RC) * k * k;
}

/** Inside of the glass (where the sand is). */
export const inner = (u) => Math.max(0.02, bulb(u) * 0.9 - 0.012);

/** The sand starts just above the neck in the top bulb, and fills to here when full. */
export const NECK = 0.02;
export const TOP_FULL = H * 0.88;
/** The bottom bulb's floor (depth below the neck). */
export const FLOOR = H * 0.96;

const N = 1024;
const radiusAt = (dist) => inner(Math.min(1, Math.abs(dist) / H));

// Cumulative volume tables (trapezoid rule on a fine grid), built once.
// top[i]: sand in the top bulb from NECK up to yTop(i);  bottom[i]: sand from the floor up to depth dBottom(i).
const yTop = (i) => NECK + (TOP_FULL - NECK) * (i / N);
const dBottom = (i) => FLOOR - (FLOOR - NECK) * (i / N);
const top = new Float64Array(N + 1);
const bottom = new Float64Array(N + 1);
for (let i = 1; i <= N; i++) {
  const dy = (TOP_FULL - NECK) / N;
  const a = radiusAt(yTop(i - 1)), b = radiusAt(yTop(i));
  top[i] = top[i - 1] + Math.PI * (a * a + b * b) / 2 * dy;
  const dz = (FLOOR - NECK) / N;
  const c = radiusAt(dBottom(i - 1)), d = radiusAt(dBottom(i));
  bottom[i] = bottom[i - 1] + Math.PI * (c * c + d * d) / 2 * dz;
}

/** All the sand: what the top bulb holds when full. */
export const CAPACITY = top[N];

/** Interpolated inverse of a cumulative table: the index position where the volume reaches v. */
function invert(table, v) {
  if (v <= 0) return 0;
  if (v >= table[N]) return N;
  let lo = 0, hi = N;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (table[mid] < v) lo = mid; else hi = mid;
  }
  const span = table[hi] - table[lo];
  return lo + (span > 0 ? (v - table[lo]) / span : 0);
}

const lerpTable = (table, pos) => {
  const i = Math.max(0, Math.min(N - 1, Math.floor(pos)));
  return table[i] + (table[i + 1] - table[i]) * (pos - i);
};

/** Sand in the top bulb from the neck to height y. */
export function topVolume(y) {
  const pos = ((Math.max(NECK, Math.min(TOP_FULL, y)) - NECK) / (TOP_FULL - NECK)) * N;
  return lerpTable(top, pos);
}

/** Sand in the bottom bulb from the floor up to height y (y is negative: below the neck). */
export function bottomVolume(y) {
  const depth = Math.max(NECK, Math.min(FLOOR, -y));
  const pos = ((FLOOR - depth) / (FLOOR - NECK)) * N;
  return lerpTable(bottom, pos);
}

/** Height of the top sand's flat surface when `remaining` (0..1) of the time is left. */
export function topSurface(remaining) {
  const r = Math.max(0, Math.min(1, remaining));
  return yTop(invert(top, r * CAPACITY));
}

/**
 * The bottom pile when `fallen` (0..1) of the sand has come through: the pile's flat level (y, below the neck),
 * and the heap cone on top of it (height and radius). Pile + heap = exactly `fallen` of the sand.
 */
export function bottomPile(fallen) {
  const f = Math.max(0, Math.min(1, fallen));
  const want = f * CAPACITY;
  if (want <= 0) return { y: -FLOOR, heapH: 0, heapR: radiusAt(FLOOR) };
  // A real pile has a heap: a low cone, a little taller as more sand arrives.
  const heapH = 0.03 + 0.11 * Math.min(1, f * 3);
  let y = -FLOOR;
  for (let pass = 0; pass < 3; pass++) {           // heap radius depends on the level: converge in a few passes
    const rr = radiusAt(y);
    const heapV = Math.min(want, (Math.PI * rr * rr * heapH) / 3);
    const pileV = want - heapV;
    y = -dBottom(invert(bottom, pileV));
  }
  const rr = radiusAt(y);
  // If the sand is less than a full-width heap, shrink the heap to match exactly.
  const heapV = (Math.PI * rr * rr * heapH) / 3;
  const h = heapV > want ? (3 * want) / (Math.PI * rr * rr) : heapH;
  return { y, heapH: h, heapR: rr };
}

/** Total sand in both bulbs for a given time left — always CAPACITY (used by the tests). */
export function totalVolume(remaining) {
  const p = bottomPile(1 - remaining);
  return topVolume(topSurface(remaining)) + bottomVolume(p.y) + (Math.PI * p.heapR * p.heapR * p.heapH) / 3;
}
