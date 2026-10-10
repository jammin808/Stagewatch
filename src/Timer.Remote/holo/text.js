// One line of text on a canvas texture: redraws only when its text changes. Alpha carries the glyph and its
// glow; the shaders colour it, so a colour change never needs a redraw.

import { CanvasTexture, LinearFilter } from 'three';

const DIGIT = /[0-9]/;
const FONT = '"Segoe UI", system-ui, -apple-system, Roboto, sans-serif';

export class TextLayer {
  constructor(width, height, weight) {
    this.canvas = document.createElement('canvas');
    this.canvas.width = width;
    this.canvas.height = height;
    this.ctx = this.canvas.getContext('2d');
    this.weight = weight;
    this.texture = new CanvasTexture(this.canvas);
    this.texture.minFilter = LinearFilter;
    this.texture.generateMipmaps = false;
    this.key = null;
    this.empty = true;
  }

  /** Returns true if the texture changed. */
  set(text, monoDigits) {
    text = text || '';
    if (text === this.key) return false;
    this.key = text;
    const { ctx, canvas } = this;
    const w = canvas.width, h = canvas.height;
    ctx.clearRect(0, 0, w, h);
    this.empty = text.length === 0;
    if (!this.empty) {
      // Size from a digits-as-zero version, so the size never pumps as digits change.
      let size = h * 0.7;
      ctx.font = `${this.weight} ${size}px ${FONT}`;
      const sizing = monoDigits ? text.replace(/[0-9]/g, '0') : text;
      const measured = ctx.measureText(sizing).width;
      const maxW = w * 0.92;
      if (measured > maxW) {
        size *= maxW / measured;
        ctx.font = `${this.weight} ${size}px ${FONT}`;
      }
      ctx.textBaseline = 'middle';
      ctx.textAlign = 'center';
      ctx.fillStyle = '#fff';
      const y = h / 2 + size * 0.04;
      for (const pass of [0, 1]) {   // wide soft glow, then crisp glyphs
        ctx.shadowColor = 'rgba(255,255,255,0.9)';
        ctx.shadowBlur = pass === 0 ? size * 0.22 : size * 0.05;
        ctx.globalAlpha = pass === 0 ? 0.55 : 1;
        if (monoDigits) this.drawMono(text, w / 2, y);
        else ctx.fillText(text, w / 2, y);
      }
      ctx.globalAlpha = 1;
      ctx.shadowBlur = 0;
    }
    this.texture.needsUpdate = true;
    return true;
  }

  /** Digits on a fixed advance, so "11:11" and "00:00" sit in the same place (no jitter while counting). */
  drawMono(text, cx, y) {
    const ctx = this.ctx;
    const dw = ctx.measureText('0').width;
    let total = 0;
    for (const ch of text) total += DIGIT.test(ch) ? dw : ctx.measureText(ch).width;
    let x = cx - total / 2;
    for (const ch of text) {
      const cw = DIGIT.test(ch) ? dw : ctx.measureText(ch).width;
      ctx.fillText(ch, x + cw / 2, y);
      x += cw;
    }
  }

  dirtyAll() { this.texture.needsUpdate = true; }
  dispose() { this.texture.dispose(); }
}
