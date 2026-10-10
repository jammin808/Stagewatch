// Animation primitives: critically damped springs (no overshoot, no wobble, frame-rate independent),
// colour springs and decaying pulses. Everything that moves on screen goes through these, so changes are
// always continuous — a new target mid-motion just bends the curve, it never jumps.

import { Color } from 'three';

const STEP = 1 / 120; // integration sub-step: stable for any frame rate

export class Spring {
  /** @param {number} value start value  @param {number} omega speed (higher = snappier; ~4 calm, ~10 quick) */
  constructor(value = 0, omega = 6) {
    this.v = value;
    this.target = value;
    this.vel = 0;
    this.omega = omega;
  }
  set(target) { this.target = target; return this; }
  snap(value) { this.v = this.target = value; this.vel = 0; return this; }
  step(dt) {
    const w = this.omega;
    let left = dt;
    while (left > 1e-6) {
      const h = Math.min(STEP, left);
      left -= h;
      const acc = w * w * (this.target - this.v) - 2 * w * this.vel;
      this.vel += acc * h;
      this.v += this.vel * h;
    }
    return this.v;
  }
}

/** A colour that glides to its target. `color` is the live value (shared with shader uniforms, never reallocated). */
export class ColorSpring {
  constructor(hex, omega = 5) {
    this.color = new Color(hex);
    this.r = new Spring(this.color.r, omega);
    this.g = new Spring(this.color.g, omega);
    this.b = new Spring(this.color.b, omega);
  }
  set(c) { this.r.set(c.r); this.g.set(c.g); this.b.set(c.b); return this; }
  snap(c) { this.r.snap(c.r); this.g.snap(c.g); this.b.snap(c.b); this.color.setRGB(c.r, c.g, c.b); return this; }
  step(dt) {
    this.color.setRGB(Math.max(0, this.r.step(dt)), Math.max(0, this.g.step(dt)), Math.max(0, this.b.step(dt)));
    return this.color;
  }
}

/** An envelope that is kicked up and decays smoothly (flashes, bursts, shockwaves). */
export class Pulse {
  constructor(decay = 3) { this.v = 0; this.decay = decay; }
  kick(amount) { this.v = Math.max(this.v, amount); }
  step(dt) { this.v *= Math.exp(-dt * this.decay); if (this.v < 1e-4) this.v = 0; return this.v; }
}

/** A 0→1 run that plays once when started (shockwave rings, flips). */
export class Run {
  constructor(seconds) { this.duration = seconds; this.t = 1; }
  start() { this.t = 0; }
  get active() { return this.t < 1; }
  step(dt) { if (this.t < 1) this.t = Math.min(1, this.t + dt / this.duration); return this.t; }
}

/** Smooth pseudo-random motion: a sum of incommensurate sines (no per-frame randomness, so no jitter). */
export function wobble(t, seed) {
  return (Math.sin(t * 1.7 + seed) * 0.5 + Math.sin(t * 2.9 + seed * 2.3) * 0.3 + Math.sin(t * 4.3 + seed * 3.7) * 0.2);
}

export const easeInOut = (x) => (x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2);
