// Scene: the hologram — text floating in a projector beam with dust, a ringed base, shockwave rings and a floor
// reflection. Everything reacts to the core's fx (colour, attention, flashes, waves, surge).

import {
  AdditiveBlending, BufferGeometry, CylinderGeometry, DoubleSide, Float32BufferAttribute, Group, Mesh, PlaneGeometry,
  Points, ShaderMaterial,
} from 'three';
import { BASIC_VERT, CONE_VERT, CONE_FRAG, DISC_FRAG, DUST_VERT, DUST_FRAG } from './shaders.js';
import { TEXT_W } from './core.js';

export const DUST_MAX = 600;

export function createHologram(core, shared) {
  const root = new Group();
  const fx = core.fx;

  // Reflection of the timer on the floor (mirrored, fading).
  const reflectionMat = core.textMaterial(core.timerLayer.texture, core.timerTint, 1);
  reflectionMat.uniforms.reflection.value = 1;
  const reflection = new Mesh(new PlaneGeometry(TEXT_W, TEXT_W / 4), reflectionMat);
  reflection.scale.y = -1;
  reflection.rotation.x = -0.18;

  const projector = new Group();
  const discMat = new ShaderMaterial({
    uniforms: {
      tint: { value: fx.ambient }, waveTint: { value: fx.waveTint }, time: shared.scanT, opacity: { value: 0.9 },
      power: { value: 1 }, wave: { value: 1 }, waveStrength: { value: 0 },
    },
    vertexShader: BASIC_VERT, fragmentShader: DISC_FRAG,
    transparent: true, depthWrite: false, blending: AdditiveBlending,
  });
  const disc = new Mesh(new PlaneGeometry(4.4, 4.4), discMat);
  disc.rotation.x = -Math.PI / 2;
  const coneMat = new ShaderMaterial({
    uniforms: { tint: { value: fx.ambient }, time: shared.scanT, opacity: { value: 1 }, power: { value: 1 } },
    vertexShader: CONE_VERT, fragmentShader: CONE_FRAG,
    transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide,
  });
  const cone = new Mesh(new CylinderGeometry(2.3, 1.1, 2.6, 64, 1, true), coneMat);
  cone.position.y = 1.3;

  const seeds = new Float32Array(DUST_MAX);
  for (let i = 0; i < DUST_MAX; i++) seeds[i] = Math.random();
  const dustGeo = new BufferGeometry();
  dustGeo.setAttribute('position', new Float32BufferAttribute(new Float32Array(DUST_MAX * 3), 3));
  dustGeo.setAttribute('seed', new Float32BufferAttribute(seeds, 1));
  const dustMat = new ShaderMaterial({
    uniforms: {
      tint: { value: fx.ambient }, dustT: shared.dustT, opacity: { value: 1 }, height: { value: 2.8 }, radius: { value: 1.9 },
      size: { value: 30 }, swirl: { value: 0 }, surge: { value: 0 },
    },
    vertexShader: DUST_VERT, fragmentShader: DUST_FRAG,
    transparent: true, depthWrite: false, blending: AdditiveBlending,
  });
  const dust = new Points(dustGeo, dustMat);
  dust.frustumCulled = false;
  projector.add(disc, cone, dust, reflection);
  projector.position.y = -1.25;
  const REFLECT_Y = -(TEXT_W / 4) / 2 - 0.06;
  reflection.position.y = REFLECT_Y;
  root.add(projector);

  let floorOn = true;
  let dustCount = DUST_MAX;

  function enter() {
    root.visible = true;
    root.add(core.group);
    core.group.position.set(0, 0, 0);
    core.group.scale.setScalar(1);
    core.setStyle(true);
  }

  function leave() { root.visible = false; }

  function layout(camera, aspect) {
    const halfW = (TEXT_W * 0.56) / aspect;
    const fitH = floorOn ? 1.95 : 1.25;
    const half = Math.max(halfW, fitH);
    const centreY = floorOn ? -0.2 : 0.35;
    camera.position.set(0, centreY + 0.35, half / Math.tan((camera.fov * Math.PI) / 360) * 1.02);
    camera.lookAt(0, centreY, 0);
  }

  function update(dt, t) {
    const g = core.group;
    // Hover, plus attention shake.
    g.position.set(fx.shakeX, Math.sin(t * 0.8) * 0.045 + fx.shakeY, 0);
    g.rotation.y = Math.sin(t * 0.27) * 0.07;
    g.rotation.x = -0.04 + Math.sin(t * 0.43) * 0.015;
    projector.visible = floorOn && fx.visible > 0.01;
    reflection.position.y = REFLECT_Y - g.position.y;
    reflectionMat.uniforms.opacity.value = fx.visible;
    reflectionMat.uniforms.pulse.value = fx.pulse;
    const power = (0.55 + 0.45 * fx.beam) * (1 + fx.pulse * 0.8);
    discMat.uniforms.opacity.value = 0.9 * fx.visible;
    discMat.uniforms.power.value = power;
    discMat.uniforms.wave.value = fx.wave;
    discMat.uniforms.waveStrength.value = fx.waveStrength;
    coneMat.uniforms.opacity.value = fx.visible;
    coneMat.uniforms.power.value = power;
    dustMat.uniforms.opacity.value = fx.visible * (0.8 + 0.2 * fx.beam);
    dustMat.uniforms.swirl.value = fx.swirl;
    dustMat.uniforms.surge.value = fx.surge;
    dust.visible = dustCount > 0;
    dustGeo.setDrawRange(0, dustCount);
  }

  return {
    root, enter, leave, layout, update,
    setFloor(on) { floorOn = on; },
    setDust(n) { dustCount = Math.min(DUST_MAX, n); },
    get floor() { return floorOn; },
  };
}
