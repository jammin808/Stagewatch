// Stagewatch 3D displays — the hologram and the egg timer, on three.js.
//
// Architecture (game-loop style, built to never take a show down):
//   transport  → update(frame)  : only stores the latest frame (WebSocket in browsers, WebView2 messages in the output)
//   core       : frames → colours, attention and events; every animated value is a critically damped spring
//   scenes     : hologram / egg timer stage the core's text and react to its fx; one is active at a time
//   loop       : requestAnimationFrame; text textures redraw only when text changes; no per-frame allocation
//   governor   : watches frame time and steps resolution / particles / ghosts / shadows down (and back up)
//   fail-safe  : no WebGL, a lost GPU context that doesn't come back, or any render error → onFail() once
//
// Public API (global StagewatchHolo): start(container, options) → { update(frame), configure(opts), stop() }
//   options / configure: { scene: 'holo'|'egg', floor, particles, animate, background, onFail, onBeat }
//   Anything not set explicitly follows the show's settings carried in each frame.

import { ACESFilmicToneMapping, Color, PCFSoftShadowMap, PerspectiveCamera, Scene, WebGLRenderer } from 'three';
import { createCore } from './core.js';
import { createHologram } from './hologram.js';
import { createEggTimer } from './egg.js';

const TIERS = [
  { dpr: 2.0, dust: 600, ghosts: 2, shadows: true },
  { dpr: 1.5, dust: 320, ghosts: 2, shadows: true },
  { dpr: 1.0, dust: 140, ghosts: 1, shadows: true },
  { dpr: 0.75, dust: 0, ghosts: 0, shadows: false },
];

function hexColor(c, fallback) {
  const out = new Color(fallback);
  if (typeof c === 'string' && /^#?[0-9a-f]{6}$/i.test(c.trim())) out.set(c.trim().startsWith('#') ? c.trim() : '#' + c.trim());
  return out;
}

function start(container, options) {
  const opts = Object.assign({ onFail: null, onBeat: null }, options || {});
  // Explicit choices (URL / host) win over the show's settings.
  const forced = { scene: opts.scene, floor: opts.floor, particles: opts.particles, animate: opts.animate };
  let failed = false;
  let renderer;

  function fail(reason) {
    if (failed) return;
    failed = true;
    try { stop(); } catch (e) { /* already down */ }
    if (opts.onFail) { try { opts.onFail(String(reason)); } catch (e) { /* host's problem */ } }
  }

  try {
    renderer = new WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'high-performance' });
  } catch (e) {
    fail('WebGL is not available: ' + (e && e.message ? e.message : e));
    return { update() {}, configure() {}, stop() {}, failed: true };
  }
  renderer.setClearColor(hexColor(opts.background, '#000000'), 1);
  renderer.localClippingEnabled = true;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = PCFSoftShadowMap;
  renderer.toneMapping = ACESFilmicToneMapping;   // lit materials only (egg timer); the hologram's shaders are untouched
  const canvas = renderer.domElement;
  canvas.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;display:block';
  container.appendChild(canvas);

  const scene = new Scene();
  const camera = new PerspectiveCamera(32, 16 / 9, 0.1, 100);
  // Integrated clocks: their rate can change smoothly without anything jumping.
  const shared = { scanT: { value: 0 }, dustT: { value: 0 } };

  const core = createCore(shared);
  const scenes = { holo: createHologram(core, shared), egg: createEggTimer(core, shared, renderer, scene) };
  scene.add(scenes.holo.root, scenes.egg.root);
  scenes.holo.leave();
  scenes.egg.leave();
  let active = null;
  let sceneName = '';

  const settings = { scene: 'holo', floor: true, particles: true, animate: true };
  const effective = (key) => (forced[key] !== undefined && forced[key] !== null ? forced[key] : settings[key]);

  function applySettings() {
    const name = effective('scene') === 'egg' ? 'egg' : 'holo';
    if (name !== sceneName) {
      if (active) active.leave();
      active = scenes[name];
      sceneName = name;
      active.enter();
    }
    core.fx.animate = effective('animate') !== false;
    scenes.holo.setFloor(effective('floor') !== false);
    applyTier();
  }

  function update(f) {
    if (failed || !f || f.type !== 'state') return;
    // The show's 3D settings ride along in every frame.
    let changed = false;
    const s = f.scene === 'egg' ? 'egg' : 'holo';
    if (s !== settings.scene) { settings.scene = s; changed = true; }
    if (typeof f.holoFloor === 'boolean' && f.holoFloor !== settings.floor) { settings.floor = f.holoFloor; changed = true; }
    if (typeof f.particles === 'boolean' && f.particles !== settings.particles) { settings.particles = f.particles; changed = true; }
    if (typeof f.animate === 'boolean' && f.animate !== settings.animate) { settings.animate = f.animate; changed = true; }
    if (changed) applySettings();
    core.update(f);
  }

  function configure(o) {
    if (!o) return;
    for (const k of ['scene', 'floor', 'particles', 'animate']) if (o[k] !== undefined) forced[k] = o[k];
    if (o.background) renderer.setClearColor(hexColor(o.background, '#000000'), 1);
    applySettings();
  }

  // ---------------------------------------------------------------- sizing and quality
  let tier = 0;
  function resize() {
    const w = Math.max(1, container.clientWidth), h = Math.max(1, container.clientHeight);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, TIERS[tier].dpr));
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    if (active) active.layout(camera, camera.aspect);
    camera.updateProjectionMatrix();
  }
  const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(resize) : null;
  if (ro) ro.observe(container); else window.addEventListener('resize', resize);

  function applyTier() {
    const t = TIERS[tier];
    const particles = effective('particles') !== false;
    scenes.holo.setDust(particles ? t.dust : 0);
    scenes.egg.setDust(particles ? t.dust : 0);
    scenes.egg.setShadows(t.shadows);
    core.setGhosts(t.ghosts);
    resize();
  }
  applySettings();

  // ---------------------------------------------------------------- GPU loss and recovery
  let lostTimer = 0;
  canvas.addEventListener('webglcontextlost', (e) => {
    e.preventDefault();
    lostTimer = window.setTimeout(() => fail('the graphics context was lost'), 3000);
  });
  canvas.addEventListener('webglcontextrestored', () => {
    window.clearTimeout(lostTimer);
    core.dirtyAll();
  });

  // ---------------------------------------------------------------- the loop
  let raf = 0, running = true;
  let last = performance.now(), t = 0;
  let avg = 16.7, slowFor = 0, fastFor = 0;
  let frames = 0, beatAt = last;

  function loop(now) {
    if (!running) return;
    raf = requestAnimationFrame(loop);
    try {
      // Clamp: a long hiccup must not jump animations, but slow devices (4+ fps) still animate in real time.
      const dt = Math.min(0.25, Math.max(0, (now - last) / 1000));
      last = now;
      t += dt;
      const fx = core.step(dt, t, now);
      shared.scanT.value += dt;
      shared.dustT.value += dt * fx.dustSpeed;
      active.update(dt, t);
      renderer.render(scene, camera);

      const ms = dt * 1000;
      if (ms > 0) avg += (ms - avg) * 0.05;
      if (avg > 24) { slowFor += dt; fastFor = 0; } else if (avg < 13) { fastFor += dt; slowFor = 0; } else { slowFor = fastFor = 0; }
      if (slowFor > 2 && tier < TIERS.length - 1) { tier++; slowFor = 0; applyTier(); }
      if (fastFor > 8 && tier > 0) { tier--; fastFor = 0; applyTier(); }

      frames++;
      if (now - beatAt >= 1000) {
        const fps = Math.round((frames * 1000) / (now - beatAt));
        frames = 0;
        beatAt = now;
        if (opts.onBeat) opts.onBeat({ fps, tier, scene: sceneName });
      }
    } catch (e) {
      fail('render error: ' + (e && e.message ? e.message : e));
    }
  }
  raf = requestAnimationFrame(loop);

  const onVis = () => { last = performance.now(); };
  document.addEventListener('visibilitychange', onVis);

  function stop() {
    if (!running) return;
    running = false;
    cancelAnimationFrame(raf);
    document.removeEventListener('visibilitychange', onVis);
    if (ro) ro.disconnect(); else window.removeEventListener('resize', resize);
    core.dispose();
    scene.traverse((o) => {
      if (o.geometry) o.geometry.dispose();
      if (o.material) (Array.isArray(o.material) ? o.material : [o.material]).forEach((m) => m.dispose());
    });
    if (scene.environment) scene.environment.dispose();
    renderer.dispose();
    if (canvas.parentNode) canvas.parentNode.removeChild(canvas);
  }

  if (opts.debug) window.__stagewatchHolo = { fx: core.fx, get tier() { return tier; } };
  return { update, configure, stop, get tier() { return tier; }, get scene() { return sceneName; } };
}

function supported() {
  try {
    const c = document.createElement('canvas');
    return !!(window.WebGLRenderingContext && (c.getContext('webgl2') || c.getContext('webgl')));
  } catch (e) {
    return false;
  }
}

window.StagewatchHolo = { start, supported, version: '2' };
