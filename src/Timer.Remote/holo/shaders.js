// GLSL for the 3D displays. Every animated quantity is a uniform driven by springs on the CPU; the shaders
// only draw. Time-like uniforms (scan, dust) are integrated on the CPU so speed changes never cause jumps.

export const BASIC_VERT = /* glsl */ `
  varying vec2 vUv;
  void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`;

// Text as light. reveal: 0 = gone, 1 = fully built (materialises top-down with a bright dissolving edge).
// scanAmount: 1 = hologram scanlines, ~0.1 = clean (egg timer). pulse: attention brightness. chroma: fringe width.
export const TEXT_FRAG = /* glsl */ `
  uniform sampler2D map;
  uniform vec3 tint;
  uniform float time;
  uniform float opacity;
  uniform float reflection;
  uniform float glitch;
  uniform float chroma;
  uniform float pulse;
  uniform float reveal;
  uniform float scanAmount;
  varying vec2 vUv;
  float hash(float n) { return fract(sin(n) * 43758.5453); }
  float hash2(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
  void main() {
    vec2 uv = vUv;
    float line = floor(uv.y * 90.0 + floor(time * 8.0) * 7.0);
    uv.x += (hash(line) - 0.5) * 0.03 * glitch * step(0.9 - glitch * 0.25, hash(line + 3.1));
    float off = 0.0012 + chroma * 0.006;
    float aR = texture2D(map, uv + vec2(off, 0.0)).a;
    float aG = texture2D(map, uv).a;
    float aB = texture2D(map, uv - vec2(off, 0.0)).a;
    float a = max(aG, max(aR, aB) * 0.6);
    // Colour stays the tint's hue: the hot core is a lighter shade of the tint, not white, and attention
    // brightens the hue rather than washing it out.
    float core = smoothstep(0.85, 1.0, aG);
    vec3 col = tint * vec3(aR, aG, aB) * 1.25 + mix(tint, vec3(1.0), 0.35) * core * 0.3;
    float scan = 1.0 - scanAmount * (0.2 - 0.2 * sin(vUv.y * 420.0 - time * 7.0));
    float sweep = scanAmount * smoothstep(0.035, 0.0, abs(fract(vUv.y * 0.6 - time * 0.12) - 0.5));
    float flick = 1.0 - scanAmount * (0.05 - 0.05 * sin(time * 41.0) * sin(time * 17.0));
    // Materialise: a dissolving edge travels top to bottom; everything above it exists.
    float n = hash2(floor(vUv * vec2(220.0, 60.0)));
    float d = (1.0 - vUv.y) + n * 0.12 - reveal * 1.14;
    float built = step(d, 0.0);
    float edge = smoothstep(0.07, 0.0, abs(d)) * step(0.02, reveal) * step(reveal, 0.98);
    float fade = mix(1.0, smoothstep(0.15, 1.0, vUv.y) * 0.22, reflection);
    vec3 outc = (col * scan * flick + tint * sweep * a * 0.6) * (1.0 + pulse * 0.7) + mix(tint, vec3(1.0), 0.5) * edge * 1.6;
    gl_FragColor = vec4(outc, (a * built + edge * 0.9) * opacity * fade * (0.85 + 0.15 * scan));
  }`;

export const BAR_FRAG = /* glsl */ `
  uniform vec3 tint;
  uniform float progress;
  uniform float time;
  uniform float opacity;
  uniform float pulse;
  varying vec2 vUv;
  void main() {
    float filled = step(vUv.x, progress);
    float edge = smoothstep(0.5, 0.0, abs(vUv.y - 0.5));
    float shimmer = 0.85 + 0.15 * sin(vUv.x * 60.0 - time * 5.0);
    float head = smoothstep(0.02, 0.0, abs(vUv.x - progress)) * 1.5;
    vec3 col = tint * (filled * shimmer * 1.3 + 0.25 + head) * (1.0 + pulse);
    gl_FragColor = vec4(col, (filled * 0.9 + 0.3 + head) * edge * opacity);
  }`;

export const CONE_VERT = /* glsl */ `
  varying vec2 vUv; varying vec3 vNormalV; varying vec3 vViewDir;
  void main() {
    vUv = uv;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vNormalV = normalize(normalMatrix * normal);
    vViewDir = normalize(-mv.xyz);
    gl_Position = projectionMatrix * mv;
  }`;
export const CONE_FRAG = /* glsl */ `
  uniform vec3 tint; uniform float time; uniform float opacity; uniform float power;
  varying vec2 vUv; varying vec3 vNormalV; varying vec3 vViewDir;
  void main() {
    float rim = 1.0 - abs(dot(vNormalV, vViewDir));
    float h = 1.0 - vUv.y;
    float rays = 0.75 + 0.25 * sin(vUv.x * 80.0 + time * 0.7);
    float a = pow(h, 2.2) * (0.25 + rim * 0.5) * rays;
    gl_FragColor = vec4(tint * 1.2, a * opacity * 0.35 * power);
  }`;

// Floor disc with turning rings, plus a shockwave ring (wave: 0→1 as it expands; waveStrength fades it).
export const DISC_FRAG = /* glsl */ `
  uniform vec3 tint; uniform vec3 waveTint; uniform float time; uniform float opacity; uniform float power;
  uniform float wave; uniform float waveStrength;
  varying vec2 vUv;
  void main() {
    vec2 p = vUv - 0.5;
    float r = length(p) * 2.0;
    float ang = atan(p.y, p.x);
    float rings = smoothstep(0.03, 0.0, abs(fract(r * 4.0 - time * 0.15) - 0.5) - 0.44);
    float ticks = step(0.92, fract(ang * 12.0 / 6.2831 + time * 0.05)) * smoothstep(1.0, 0.85, r) * step(0.8, r);
    float glow = smoothstep(1.0, 0.0, r);
    float a = (glow * 0.45 + rings * 0.35 + ticks * 0.6) * step(r, 1.0) * power;
    float wr = wave * 1.25;
    float w = smoothstep(0.09, 0.0, abs(r - wr)) * waveStrength * (1.0 - wave);
    gl_FragColor = vec4(tint * (0.6 + glow) * a + waveTint * w * 2.0, (a + w) * opacity);
  }`;

// Dust: positions are a pure function of seed and an integrated clock (dustT), so the CPU never touches a vertex.
// swirl: spirals harder; surge: rises faster and spreads (attention).
export const DUST_VERT = /* glsl */ `
  attribute float seed;
  uniform float dustT; uniform float height; uniform float radius; uniform float size; uniform float swirl; uniform float surge;
  varying float vAlpha;
  void main() {
    float t = fract(seed * 7.13 + dustT * (0.02 + seed * 0.03));
    float ang = seed * 61.7 + dustT * (0.1 + swirl * 1.2) * (seed - 0.5) * 2.0;
    float r = radius * (1.0 - t * (0.55 - surge * 0.4)) * sqrt(fract(seed * 13.7));
    vec3 p = vec3(cos(ang) * r, t * height * (1.0 + surge * 0.35), sin(ang) * r);
    vAlpha = smoothstep(0.0, 0.1, t) * smoothstep(1.0, 0.6, t);
    vec4 mv = modelViewMatrix * vec4(p, 1.0);
    gl_PointSize = size * (1.0 + surge * 0.6) * (1.0 / -mv.z);
    gl_Position = projectionMatrix * mv;
  }`;
export const DUST_FRAG = /* glsl */ `
  uniform vec3 tint; uniform float opacity;
  varying float vAlpha;
  void main() {
    float d = length(gl_PointCoord - 0.5);
    gl_FragColor = vec4(tint, smoothstep(0.5, 0.0, d) * vAlpha * opacity * 0.8);
  }`;

// Egg timer: an additive fresnel shell around the glass that glows with attention.
export const FRESNEL_FRAG = /* glsl */ `
  uniform vec3 tint; uniform float intensity;
  varying vec2 vUv; varying vec3 vNormalV; varying vec3 vViewDir;
  void main() {
    float f = pow(1.0 - abs(dot(vNormalV, vViewDir)), 2.5);
    gl_FragColor = vec4(tint * f * intensity, f * intensity);
  }`;

// Egg timer: the falling stream of sand — grains scroll down an integrated clock.
export const STREAM_FRAG = /* glsl */ `
  uniform vec3 tint; uniform float flowT; uniform float opacity;
  varying vec2 vUv;
  float hash2(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
  void main() {
    float g = hash2(floor(vec2(vUv.x * 6.0, vUv.y * 70.0 + flowT * 40.0)));
    float a = (0.55 + 0.45 * step(0.45, g)) * opacity;
    gl_FragColor = vec4(tint * (0.8 + 0.4 * g), a);
  }`;
