// Scene: a 3D egg timer (hourglass) — lit glass with reflections, a wood-and-brass frame, a real shadow on a
// softly lit floor, and sand that tracks the countdown. Sand levels are clipping planes on fixed geometry, so
// nothing is rebuilt per frame. It reacts to the show: the stream runs only while the timer runs, it turns over
// when a new time is loaded, the sand takes the stage colour, the glass glows with warnings and it rocks and
// shakes when something is critical.

import {
  AdditiveBlending, BackSide, BufferGeometry, CanvasTexture, CircleGeometry, Color, ConeGeometry, CylinderGeometry,
  DirectionalLight, DoubleSide, Float32BufferAttribute, Group, HemisphereLight, LatheGeometry, Mesh, MeshPhysicalMaterial,
  MeshStandardMaterial, Plane, PlaneGeometry, PMREMGenerator, PointLight, Points, ShaderMaterial, Vector2, Vector3,
} from 'three';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { BASIC_VERT, CONE_VERT, FRESNEL_FRAG, STREAM_FRAG, DUST_VERT, DUST_FRAG } from './shaders.js';
import { ColorSpring, Run, Spring, easeInOut } from './anim.js';
import { TEXT_W } from './core.js';
import { H, bulb, inner, topSurface as topSandLevel, bottomPile, NECK, TOP_FULL, FLOOR } from './sand.js';

const GOLD = new Color('#e2b866');
const DUST_MAX = 220;


function lathe(fn, y0, y1, steps, sign) {
  const pts = [];
  for (let i = 0; i <= steps; i++) {
    const y = y0 + (y1 - y0) * (i / steps);
    pts.push(new Vector2(fn(Math.abs(y) / H), y * sign));
  }
  return new LatheGeometry(pts, 72);
}


export function createEggTimer(core, shared, renderer, scene) {
  const fx = core.fx;
  const root = new Group();
  // The stand: glass, floor, shadow and motes always move together (layout positions and scales the stand);
  // inside it, glassGroup alone flips, rocks and shakes.
  const stand = new Group();
  const glassGroup = new Group();
  root.add(stand);
  stand.add(glassGroup);

  // ---------------------------------------------------------------- materials
  const sandTint = new ColorSpring('#e2b866', 3);
  // three.js copies colours into materials, so the live sand colour is copied into these every frame.
  const sandMats = [];
  const sandMat = (planes) => {
    const m = new MeshStandardMaterial({
      color: sandTint.color, roughness: 0.92, metalness: 0, emissive: sandTint.color, emissiveIntensity: 0.1,
      side: DoubleSide, clippingPlanes: planes,
    });
    sandMats.push(m);
    return m;
  };
  const topPlane = new Plane(new Vector3(0, -1, 0), 0);
  const bottomPlane = new Plane(new Vector3(0, -1, 0), 0);
  const topLocal = new Plane(new Vector3(0, -1, 0), 0);
  const bottomLocal = new Plane(new Vector3(0, -1, 0), 0);

  const glass = new MeshPhysicalMaterial({
    color: 0xffffff, metalness: 0, roughness: 0.04, transparent: true, opacity: 0.2,
    clearcoat: 1, clearcoatRoughness: 0.04, envMapIntensity: 1.4, side: DoubleSide, depthWrite: false,
  });
  const wood = new MeshStandardMaterial({ color: '#6b4423', roughness: 0.5, metalness: 0.05, envMapIntensity: 0.6 });
  const brass = new MeshStandardMaterial({ color: '#c9a24f', roughness: 0.22, metalness: 0.95, envMapIntensity: 1.2 });

  // ---------------------------------------------------------------- geometry
  const glassMesh = new Mesh(lathe(bulb, -H, H, 80, 1), glass);
  glassMesh.renderOrder = 2;
  const glowMat = new ShaderMaterial({
    uniforms: { tint: { value: fx.ambient }, intensity: { value: 0.2 } },
    vertexShader: CONE_VERT, fragmentShader: FRESNEL_FRAG,
    transparent: true, depthWrite: false, blending: AdditiveBlending, side: BackSide,
  });
  const glow = new Mesh(glassMesh.geometry, glowMat);
  glow.scale.setScalar(1.06);
  glow.renderOrder = 3;

  const topSand = new Mesh(lathe(inner, NECK, TOP_FULL, 90, 1), sandMat([topPlane]));
  const topSurface = new Mesh(new CircleGeometry(1, 48), sandMat(null));
  topSurface.rotation.x = -Math.PI / 2;
  const bottomSand = new Mesh(lathe(inner, NECK, FLOOR, 90, -1), sandMat([bottomPlane]));
  const heap = new Mesh(new ConeGeometry(1, 1, 48), sandMat(null));
  [topSand, topSurface, bottomSand, heap].forEach((m) => { m.castShadow = true; m.renderOrder = 1; });

  const streamMat = new ShaderMaterial({
    uniforms: { tint: { value: sandTint.color }, flowT: { value: 0 }, opacity: { value: 1 } },
    vertexShader: BASIC_VERT, fragmentShader: STREAM_FRAG, transparent: true, depthWrite: false,
  });
  const stream = new Mesh(new CylinderGeometry(0.013, 0.013, 1, 10, 1, true), streamMat);

  const capGeo = new CylinderGeometry(0.76, 0.78, 0.12, 64);
  const trimGeo = new CylinderGeometry(0.8, 0.8, 0.03, 64);
  const capTop = new Mesh(capGeo, wood), capBottom = new Mesh(capGeo, wood);
  const trimTop = new Mesh(trimGeo, brass), trimBottom = new Mesh(trimGeo, brass);
  capTop.position.y = H + 0.06; capBottom.position.y = -H - 0.06;
  trimTop.position.y = H + 0.135; trimBottom.position.y = -H - 0.135;
  const pillarGeo = new CylinderGeometry(0.034, 0.034, 2 * H + 0.12, 16);
  const pillars = [0, 1, 2].map((i) => {
    const p = new Mesh(pillarGeo, brass);
    const a = Math.PI / 2 + i * (Math.PI * 2 / 3);
    p.position.set(Math.cos(a) * 0.68, 0, Math.sin(a) * 0.68);
    return p;
  });
  [capTop, capBottom, trimTop, trimBottom, ...pillars].forEach((m) => { m.castShadow = true; m.receiveShadow = true; });
  glassGroup.add(capTop, capBottom, trimTop, trimBottom, ...pillars, topSand, topSurface, bottomSand, heap, stream, glassMesh, glow);

  // ---------------------------------------------------------------- floor, light and shadow
  const fade = document.createElement('canvas');
  fade.width = fade.height = 256;
  const fctx = fade.getContext('2d');
  const grad = fctx.createRadialGradient(128, 128, 10, 128, 128, 128);
  grad.addColorStop(0, '#ffffff'); grad.addColorStop(0.55, '#888888'); grad.addColorStop(1, '#000000');
  fctx.fillStyle = grad; fctx.fillRect(0, 0, 256, 256);
  // The floor is a backdrop: drawn first and never hiding anything behind it (glass, text).
  const floorMat = new MeshStandardMaterial({
    color: '#232733', roughness: 0.85, metalness: 0, transparent: true, depthWrite: false, alphaMap: new CanvasTexture(fade),
  });
  const floor = new Mesh(new PlaneGeometry(4.2, 4.2), floorMat);
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = -H - 0.15;   // just under the base, in the stand's own space
  floor.receiveShadow = true;
  floor.renderOrder = -10;
  stand.add(floor);

  const hemi = new HemisphereLight(0xcfe0ff, 0x1a120a, 0.55);
  const key = new DirectionalLight(0xfff1dc, 2.4);
  key.position.set(2.2, 4.2, 2.8);
  key.castShadow = true;
  key.shadow.mapSize.set(1024, 1024);
  key.shadow.camera.left = -2; key.shadow.camera.right = 2; key.shadow.camera.top = 2; key.shadow.camera.bottom = -2;
  key.shadow.camera.near = 0.5; key.shadow.camera.far = 10;
  key.shadow.radius = 4;
  key.shadow.bias = -0.0006;
  const rim = new PointLight(0xffffff, 4, 8, 1.6);    // coloured by the show: warnings glow on the glass and brass
  rim.position.set(-1.6, 1.4, -1.8);
  const under = new PointLight(0xffffff, 0, 3, 2);    // a glow in the sand on attention
  under.position.set(0, -H * 0.6, 0.3);
  root.add(hemi, key, key.target, rim);
  glassGroup.add(under);

  // ---------------------------------------------------------------- motes (optional particles)
  const seeds = new Float32Array(DUST_MAX);
  for (let i = 0; i < DUST_MAX; i++) seeds[i] = Math.random();
  const dustGeo = new BufferGeometry();
  dustGeo.setAttribute('position', new Float32BufferAttribute(new Float32Array(DUST_MAX * 3), 3));
  dustGeo.setAttribute('seed', new Float32BufferAttribute(seeds, 1));
  const dustMat = new ShaderMaterial({
    uniforms: {
      tint: { value: fx.ambient }, dustT: shared.dustT, opacity: { value: 0.7 }, height: { value: 2.8 }, radius: { value: 1.1 },
      size: { value: 22 }, swirl: { value: 0 }, surge: { value: 0 },
    },
    vertexShader: DUST_VERT, fragmentShader: DUST_FRAG, transparent: true, depthWrite: false, blending: AdditiveBlending,
  });
  const motes = new Points(dustGeo, dustMat);
  motes.frustumCulled = false;
  motes.position.y = -H - 0.1;
  stand.add(motes);

  // ---------------------------------------------------------------- state
  let envReady = false;
  let landscape = true;
  let dustCount = DUST_MAX;
  const level = new Spring(1, 4);       // sand remaining, eased
  const flip = new Run(1.15);
  let flipFrom = 0;
  let flowT = 0;
  const tmp = new Color();

  function enter() {
    if (!envReady) {
      const pmrem = new PMREMGenerator(renderer);
      scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
      pmrem.dispose();
      envReady = true;
    }
    renderer.shadowMap.enabled = true;
    root.visible = true;
    root.add(core.group);
    core.setStyle(false);
    level.snap(fx.level);
  }

  function leave() {
    root.visible = false;
    renderer.shadowMap.enabled = false;
  }

  function layout(camera, aspect) {
    landscape = aspect >= 1.05;
    if (landscape) {
      stand.position.set(-1.9, 0.05, 0);
      stand.scale.setScalar(0.9);
      core.group.position.set(1.05, 0.05, 0);
      core.group.scale.setScalar(0.74);
      const halfW = 3.05 / aspect;
      const half = Math.max(halfW, 1.62);
      camera.position.set(-0.4, 0.55, half / Math.tan((camera.fov * Math.PI) / 360) * 1.05);
      camera.lookAt(-0.45, -0.05, 0);
    } else {
      stand.position.set(0, 1.0, 0);
      stand.scale.setScalar(0.9);
      core.group.position.set(0, -1.75, 0);
      core.group.scale.setScalar(0.62);
      const halfW = 1.5 / aspect;
      const half = Math.max(halfW, 2.45);
      camera.position.set(0, 0.6, half / Math.tan((camera.fov * Math.PI) / 360) * 1.05);
      camera.lookAt(0, -0.05, 0);
    }
    key.target.position.copy(stand.position);
    key.position.set(stand.position.x + 2.2, stand.position.y + 4.2, 2.8);
    rim.position.set(stand.position.x - 1.6, stand.position.y + 1.4, -1.8);
  }

  function update(dt, t) {
    const vis = fx.visible;
    root.visible = vis > 0.01;

    // Sand colour: gold while normal, the stage colour as time runs low.
    tmp.copy(fx.timerTint);
    // (A stage colour is deepened a little: the bright key light would otherwise wash red into salmon.)
    if (Math.min(tmp.r, tmp.g, tmp.b) > 0.8) tmp.copy(GOLD); else tmp.lerp(GOLD, 0.1).multiplyScalar(0.72);
    if (fx.animate) sandTint.set(tmp); else sandTint.snap(tmp);
    sandTint.step(dt);
    for (const m of sandMats) { m.color.copy(sandTint.color); m.emissive.copy(sandTint.color); }

    // Turn over when a new time is loaded: the sand freezes while it rotates, then the full bulb is on top.
    if (fx.events.refill && fx.animate && !flip.active) { flip.start(); flipFrom = level.v; }
    const flipping = flip.active;
    const f = flip.step(dt);
    if (!flipping) level.set(fx.level); else if (f >= 1) level.snap(fx.level);
    const L = flipping ? flipFrom : (fx.animate ? level.step(dt) : level.snap(fx.level).v);

    // Rock (overtime / critical) and shake (bursts), flip rotation.
    const rock = fx.animate ? (fx.overtime ? 0.05 : 0) * Math.sin(t * 1.6) + fx.crit * 0.03 * Math.sin(t * 3.1) : 0;
    glassGroup.rotation.z = (flipping ? Math.PI * easeInOut(f) : 0) + rock;
    glassGroup.rotation.y = Math.sin(t * 0.25) * 0.15 + 0.35;
    // Local to the stand: a gentle bob, plus the attention shake.
    glassGroup.position.x = fx.shakeX * 1.5;
    glassGroup.position.y = Math.sin(t * 0.9) * 0.015 + fx.shakeY;
    core.group.position.x = (landscape ? 1.05 : 0) + fx.shakeX * 0.5;

    // Sand levels as local clipping heights, carried into world space every frame (the glass moves).
    // By volume, like a real egg timer: the top holds exactly the time left, the bottom (pile + heap) the time gone.
    const yTop = topSandLevel(L);
    const pile = bottomPile(1 - L);
    const yBot = pile.y;
    topSand.visible = L > 0.004;
    topSurface.visible = topSand.visible;
    topSurface.position.y = yTop;
    topSurface.scale.setScalar(inner(yTop / H));
    const heapH = pile.heapH;
    const showBottom = L < 0.996;
    bottomSand.visible = showBottom;
    heap.visible = showBottom && !flipping;
    heap.position.y = yBot + heapH / 2;
    heap.scale.set(pile.heapR, Math.max(0.0001, heapH), pile.heapR);
    stand.updateMatrixWorld(true);
    topLocal.constant = yTop;
    bottomLocal.constant = yBot;
    topPlane.copy(topLocal).applyMatrix4(glassGroup.matrixWorld);
    bottomPlane.copy(bottomLocal).applyMatrix4(glassGroup.matrixWorld);

    // The falling stream: only while running and there is sand to fall.
    const flowing = fx.running && L > 0.004 && !flipping;
    stream.visible = flowing;
    if (flowing) {
      const top = 0, bottom = yBot + heapH;
      stream.scale.y = Math.max(0.01, top - bottom);
      stream.position.y = (top + bottom) / 2;
      flowT += dt;
      streamMat.uniforms.flowT.value = flowT;
    }

    // Light and glow follow the show: the rim light takes the ambient colour; attention brightens the glass.
    rim.color.copy(fx.ambient);
    rim.intensity = (3 + fx.warn * 6 + fx.crit * 10 + fx.flash * 8) * vis;
    under.color.copy(fx.ambient);
    under.intensity = (fx.warn * 0.8 + fx.crit * 2.2 + fx.flash * 1.5) * vis;
    glowMat.uniforms.intensity.value = (0.15 + fx.warn * 0.35 + fx.crit * 0.55 + fx.pulse * 0.9) * vis;
    glass.opacity = 0.2 * vis;
    glass.envMapIntensity = (fx.running ? 1.4 : 1.1) * vis;
    dustMat.uniforms.opacity.value = 0.7 * vis;
    dustMat.uniforms.swirl.value = fx.swirl;
    dustMat.uniforms.surge.value = fx.surge;
    motes.visible = dustCount > 0;
    dustGeo.setDrawRange(0, dustCount);
  }

  return {
    root, enter, leave, layout, update,
    setFloor() {},
    setDust(n) { dustCount = Math.min(DUST_MAX, Math.round(n * DUST_MAX / 600)); },
    setShadows(on) { key.castShadow = on; },
  };
}
