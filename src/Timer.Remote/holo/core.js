// The show-facing core shared by every 3D scene: the text (timer, cue, message, info, progress), what the
// frames mean (colours, attention, events), and every animated value. Scenes (hologram, egg timer) read `fx`
// to drive their own elements, and position `group`. One place decides how the show looks; scenes only stage it.

import { AdditiveBlending, Color, DoubleSide, Group, Mesh, PlaneGeometry, ShaderMaterial } from 'three';
import { TextLayer } from './text.js';
import { BASIC_VERT, TEXT_FRAG, BAR_FRAG } from './shaders.js';
import { Spring, ColorSpring, Pulse, Run, wobble } from './anim.js';

export const TEXT_W = 4.2;

const PHASE_ATTENTION = { normal: 0, warning: 0.35, wrapup: 0.6, overtime: 0.9 };
const URGENCY = { info: '#d6ecff', warning: '#f59e0b', critical: '#ef4444' };
const HOLO_CYAN = new Color('#22d3ee');
const AMBER = new Color('#f59e0b');
const RED = new Color('#ef4444');

function hexColor(c, fallback) {
  const out = new Color(fallback);
  if (typeof c === 'string' && /^#?[0-9a-f]{6}$/i.test(c.trim())) out.set(c.trim().startsWith('#') ? c.trim() : '#' + c.trim());
  return out;
}

export function createCore(shared) {
  const group = new Group();
  const timerText = new TextLayer(2048, 512, 800);
  const cueText = new TextLayer(1024, 192, 800);
  const msgText = new TextLayer(2048, 256, 700);
  const infoText = new TextLayer(2048, 160, 600);

  // Live colours (springs); materials hold references, so a colour change is just a uniform update.
  const timerTint = new ColorSpring('#5eead4', 4);
  const msgTint = new ColorSpring('#d6ecff', 6);
  const ambient = new ColorSpring('#5eead4', 3.5);   // beam, particles, glass glow
  const waveTint = new Color('#ffffff');

  function textMaterial(map, tint, opacity) {
    return new ShaderMaterial({
      uniforms: {
        map: { value: map }, tint: { value: tint }, time: shared.scanT,
        opacity: { value: opacity }, reflection: { value: 0 }, glitch: { value: 0.15 }, chroma: { value: 0 },
        pulse: { value: 0 }, reveal: { value: 1 }, scanAmount: { value: 1 },
      },
      vertexShader: BASIC_VERT, fragmentShader: TEXT_FRAG,
      transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide,
    });
  }

  function textMesh(layer, tint, width, height, y) {
    const geo = new PlaneGeometry(width, height);
    const main = new Mesh(geo, textMaterial(layer.texture, tint, 1));
    main.position.y = y;
    const ghosts = [1, 2].map((g) => {
      const m = new Mesh(geo, textMaterial(layer.texture, tint, 0.22 / g));
      m.position.set(0, y, -0.07 * g);
      m.scale.setScalar(1 + 0.012 * g);
      return m;
    });
    const g = new Group();
    g.add(main, ...ghosts);
    group.add(g);
    return { layer, group: g, main, ghosts, all: [main, ...ghosts] };
  }

  const timer = textMesh(timerText, timerTint.color, TEXT_W, TEXT_W / 4, 0.55);
  const cue = textMesh(cueText, timerTint.color, TEXT_W * 0.42, TEXT_W * 0.42 * 192 / 1024, 1.33);
  const msg = textMesh(msgText, msgTint.color, TEXT_W, TEXT_W / 8, -0.32);
  const info = textMesh(infoText, timerTint.color, TEXT_W, TEXT_W * 160 / 2048, -0.72);
  const texts = [timer, cue, msg, info];

  const barMat = new ShaderMaterial({
    uniforms: { tint: { value: timerTint.color }, progress: { value: 1 }, time: shared.scanT, opacity: { value: 1 }, pulse: { value: 0 } },
    vertexShader: BASIC_VERT, fragmentShader: BAR_FRAG,
    transparent: true, depthWrite: false, blending: AdditiveBlending,
  });
  const bar = new Mesh(new PlaneGeometry(TEXT_W * 0.86, 0.045), barMat);
  group.add(bar);

  // ---------------------------------------------------------------- animated state
  const attention = new Spring(0, 3);
  const reveal = new Spring(1, 7);            // message build-in / dissolve
  const visible = new Spring(1, 9);           // blackout fade
  const beam = new Spring(0.7, 2.5);          // projector power: up when running
  const dustSpeed = new Spring(1, 2);
  const swirl = new Spring(0, 2);
  const progress = new Spring(1, 5);
  const flash = new Pulse(2.2);               // stage changes, messages arriving
  const burst = new Pulse(5);                 // critical glitch bursts
  const wave = new Run(1.4);                  // shockwave ring
  let waveStrength = 0;

  const fx = {
    timerTint: timerTint.color, msgTint: msgTint.color, ambient: ambient.color, waveTint,
    attention: 0, warn: 0, crit: 0, pulse: 0, flash: 0, burst: 0, beam: 0.7, dustSpeed: 1, swirl: 0, surge: 0,
    wave: 1, waveStrength: 0, visible: 1, progress: 1, level: 1, running: false, overtime: false,
    shakeX: 0, shakeY: 0, animate: true, events: { refill: false, phase: false, msgIn: false, start: false },
    holoCast: true,
  };

  // ---------------------------------------------------------------- frames → targets and events
  let frame = null, prev = null, dirty = false, lastFrameAt = 0;
  let shownMsg = '';
  let flashRate = 0;
  let progressTarget = 1;
  const tmp = new Color();
  const tmp2 = new Color();

  function phaseColour(s, out) {
    out.copy(hexColor(s.color, '#ffffff'));
    // A white "normal" colour gets the hologram's cyan cast; any colour the operator chose is kept as is.
    if (fx.holoCast && (s.phase === 'normal' || !s.phase) && Math.min(out.r, out.g, out.b) > 0.8) out.lerp(HOLO_CYAN, 0.45);
    return out;
  }

  function update(f) {
    if (!f || f.type !== 'state') return;
    frame = f;
    dirty = true;
    lastFrameAt = performance.now();
  }

  function apply(s) {
    const show = s.show || {};
    const countdown = s.mode === 'countdown';
    const text = countdown ? (s.speakerText !== undefined ? s.speakerText : s.text) : s.text;
    const progressOnly = countdown && text === '';
    timerText.set(show.timer === false || progressOnly ? '' : text, true);
    cueText.set((show.cue || progressOnly) && s.cue ? s.cue : '', false);
    const infoParts = [];
    if (show.title && s.title) infoParts.push(s.title);
    if (show.finishBy && s.finishBy) infoParts.push(s.finishBy);
    else if (show.endTime && s.endTime) infoParts.push(s.endTime);
    if (show.next && s.next) infoParts.push('Next: ' + s.next);
    if (show.timeOfDay && s.mode !== 'tod' && s.timeOfDay) infoParts.push(s.timeOfDay);
    infoText.set(infoParts.join('   ·   '), false);
    cue.group.visible = !cueText.empty;
    info.group.visible = !infoText.empty;

    // Message: materialise in, dissolve out, rebuild on change.
    const newMsg = show.message && s.message ? s.message : '';
    const urgency = s.urgency || 'info';
    if (newMsg !== shownMsg) {
      if (newMsg === '') reveal.set(0);
      else {
        msgText.set(newMsg, false);
        msg.group.visible = true;
        if (fx.animate) { reveal.snap(0); reveal.set(1); } else reveal.snap(1);
        fx.events.msgIn = true;
        flash.kick(urgency === 'critical' ? 0.9 : urgency === 'warning' ? 0.55 : 0.25);
        if (urgency === 'critical' && fx.animate) { burst.kick(1); wave.start(); waveStrength = 1; waveTint.set(URGENCY.critical); }
      }
      shownMsg = newMsg;
    }
    if (newMsg) {
      tmp.set(URGENCY[urgency] || URGENCY.info);
      if (fx.animate) msgTint.set(tmp); else msgTint.snap(tmp);
    }

    // Colours.
    phaseColour(s, tmp);
    if (fx.animate) timerTint.set(tmp); else timerTint.snap(tmp);
    tmp2.copy(tmp);
    if (newMsg && urgency === 'critical') tmp2.copy(RED);
    else if (newMsg && urgency === 'warning') tmp2.lerp(AMBER, 0.6);
    if (fx.animate) ambient.set(tmp2); else ambient.snap(tmp2);

    // Attention: the stronger of the stage and the message.
    const msgAttention = newMsg ? (urgency === 'critical' ? 1 : urgency === 'warning' ? 0.5 : 0.05) : 0;
    const a = Math.max(PHASE_ATTENTION[s.phase] || 0, msgAttention, s.flashTimer ? 0.6 : 0);
    attention.set(a);

    // Events.
    if (prev) {
      if (prev.phase !== s.phase) {
        fx.events.phase = true;
        flash.kick(0.7);
        if (fx.animate) { wave.start(); waveStrength = 0.8; waveTint.copy(tmp); }
      }
      const wasRunning = prev.state === 'running', isRunning = s.state === 'running';
      if (!wasRunning && isRunning) { fx.events.start = true; flash.kick(0.3); burst.kick(0.35); }
      // A fresh, longer time loaded (reset, new item): the egg timer turns over.
      if (countdown && (s.seconds > prev.seconds + 3 || (s.total !== prev.total && s.seconds >= s.total - 1))) fx.events.refill = true;
    }
    fx.running = s.state === 'running';
    fx.overtime = s.phase === 'overtime';
    const hasBar = (show.progress || progressOnly) && countdown && s.total > 0;
    progressTarget = hasBar ? Math.max(0, Math.min(1, s.seconds / s.total)) : -1;
    fx.level = countdown && s.total > 0 ? Math.max(0, Math.min(1, s.seconds / s.total)) : (s.mode === 'countup' ? 0.5 : 1);
    visible.set(s.blackout ? 0 : 1);
    flashRate = s.flashTimer ? (s.flash === 'fast' ? 3 : 1) : 0;
    beam.set(fx.running ? 1 : 0.62);
    prev = s;
  }

  // ---------------------------------------------------------------- per frame
  let nextBurst = 2;
  let nextAlarm = 0;

  function step(dt, t, now) {
    for (const k in fx.events) fx.events[k] = false;
    if (dirty && frame) { dirty = false; apply(frame); }
    const animate = fx.animate;

    fx.attention = attention.step(dt);
    fx.warn = Math.min(1, fx.attention / 0.5);
    fx.crit = Math.max(0, (fx.attention - 0.7) / 0.3);
    timerTint.step(dt); msgTint.step(dt); ambient.step(dt);
    fx.visible = visible.step(dt);
    fx.beam = beam.step(dt);

    // Smooth the bar between 1 s frames: keep moving at the clock's pace while running.
    if (frame && fx.running && frame.total > 0 && progressTarget >= 0) {
      const since = (now - lastFrameAt) / 1000;
      progress.set(Math.max(0, Math.min(1, (frame.seconds - Math.min(since, 1.5)) / frame.total)));
      fx.level = progress.target;
    } else progress.set(progressTarget < 0 ? 0 : progressTarget);
    fx.progress = progress.step(dt);

    // Attention effects (optional): breathing for warnings, faster pulse + glitch bursts + alarms for critical.
    if (animate) {
      const breathe = 0.5 + 0.5 * Math.sin(t * Math.PI * 2 * 0.8);
      const alarm = 0.5 + 0.5 * Math.sin(t * Math.PI * 2 * 2.1);
      fx.pulse = fx.warn * 0.16 * breathe * (1 - fx.crit) + fx.crit * 0.42 * alarm + flash.step(dt) * 0.9;
      if (fx.crit > 0.3) {
        nextBurst -= dt;
        if (nextBurst <= 0) { burst.kick(0.5 + 0.5 * Math.random()); nextBurst = 0.9 + Math.random() * 2.2 / fx.crit; }
        nextAlarm -= dt;
        if (nextAlarm <= 0 && !wave.active) { wave.start(); waveStrength = 0.9 * fx.crit; waveTint.copy(ambient.color); nextAlarm = 2.4; }
      }
      fx.burst = burst.step(dt);
      fx.flash = flash.v;
      dustSpeed.set(1 + fx.warn * 0.8 + fx.crit * 2.2);
      swirl.set(fx.warn * 0.3 + fx.crit * 0.7);
      fx.shakeX = (fx.burst * 0.035 + fx.crit * 0.006) * wobble(t * 9, 1.3);
      fx.shakeY = (fx.burst * 0.02 + fx.crit * 0.004) * wobble(t * 11, 4.1);
    } else {
      fx.pulse = 0; fx.burst = 0; fx.flash = 0; fx.shakeX = fx.shakeY = 0;
      dustSpeed.set(1); swirl.set(0);
    }
    fx.dustSpeed = dustSpeed.step(dt);
    fx.swirl = swirl.step(dt);
    fx.surge = animate ? fx.crit : 0;
    fx.wave = wave.step(dt);
    fx.waveStrength = wave.active ? waveStrength : 0;

    // Message reveal; clear the texture once it has fully dissolved.
    let r = reveal.step(dt);
    // Springs approach their target for ever: finish the build / dissolve cleanly at the end.
    if (reveal.target === 1 && r > 0.985) r = reveal.snap(1).v;
    if (reveal.target === 0 && r < 0.01 && !msgText.empty) { msgText.set('', false); msg.group.visible = false; }

    // Flash (the operator's flash key): a smooth throb rather than a hard blink.
    const flashWave = flashRate ? 0.3 + 0.7 * (0.5 + 0.5 * Math.cos(t * Math.PI * 2 * flashRate)) : 1;
    const o = fx.visible * flashWave;
    const glitch = 0.15 * scanAmount + fx.burst * 1.2;
    const chroma = fx.burst * 0.8 + fx.crit * 0.12;
    for (const m of texts) {
      const isMsg = m === msg;
      m.all.forEach((mesh, i) => {
        const u = mesh.material.uniforms;
        u.opacity.value = (i === 0 ? 1 : 0.22 / i) * (isMsg ? fx.visible : o);
        u.pulse.value = fx.pulse;
        u.glitch.value = glitch;
        u.chroma.value = chroma;
        u.reveal.value = isMsg ? Math.max(0, Math.min(1, r)) : 1;
        u.scanAmount.value = scanAmount;
      });
    }
    barMat.uniforms.opacity.value = fx.visible;
    barMat.uniforms.progress.value = fx.progress;
    barMat.uniforms.pulse.value = fx.pulse * 0.6;
    bar.visible = progressTarget >= 0;
    return fx;
  }

  let scanAmount = 1;
  /** Scene style: hologram (scanlines, ghosts, cyan cast) or clean (egg timer). */
  function setStyle(holo) {
    scanAmount = holo ? 1 : 0.08;
    fx.holoCast = holo;
    for (const m of texts) m.ghosts.forEach((g) => (g.visible = holo && ghostCount > 0));
    if (prev) { dirty = true; frame = frame || prev; }
  }
  let ghostCount = 2;
  function setGhosts(n) {
    ghostCount = n;
    for (const m of texts) m.ghosts.forEach((g, i) => (g.visible = scanAmount > 0.5 && i < n));
  }

  function dirtyAll() { for (const m of texts) m.layer.dirtyAll(); }

  function dispose() { for (const m of texts) m.layer.dispose(); }

  return { group, fx, update, step, setStyle, setGhosts, dirtyAll, dispose, timerLayer: timerText, textMaterial, timerTint: timerTint.color };
}
