#version 330 core
// Shared by every scene. A scene file defines:  vec3 scene(vec2 p, vec2 uv)
//   p  = centered coordinates, y from -1 to 1 (x wider on a wide screen);  uv = 0..1 across the screen.
out vec4 fragColor;
uniform vec2  iResolution;
uniform float iTime;          // seconds (slows down in calm mode)
uniform float uBass, uMid, uTreble, uLevel;   // smoothed band energies, about 0..1.5
uniform float uBeat;          // 1 on a beat, decays to 0
uniform float uCalm;          // 0 normal, 1 calm
uniform sampler2D uSpec;      // 64 spectrum bands in one row
uniform vec3  uPal[4];        // palette: from the album cover, or the scene's own

float spec(float x) { return texture(uSpec, vec2(clamp(x, 0.0, 1.0), 0.5)).r; }
float energy() { return mix(uLevel, uLevel * 0.35, uCalm); }
float kick()   { return uBeat * (1.0 - uCalm * 0.85); }

vec3 pal(float t) {
    t = fract(t) * 4.0;
    int i = int(floor(t)); float f = smoothstep(0.0, 1.0, fract(t));
    vec3 a = uPal[i & 3], b = uPal[(i + 1) & 3];
    return mix(a, b, f);
}

float hash(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}
float fbm(vec2 p) { float v = 0.0, a = 0.5; mat2 r = mat2(0.8, 0.6, -0.6, 0.8); for (int i = 0; i < 6; i++) { v += a * noise(p); p = r * p * 2.02; a *= 0.5; } return v; }
mat2 rot(float a) { float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }

vec3 scene(vec2 p, vec2 uv);

void main() {
    vec2 uv = gl_FragCoord.xy / iResolution;
    vec2 p = (gl_FragCoord.xy * 2.0 - iResolution) / iResolution.y;
    vec3 col = scene(p, uv);
    col *= 1.0 - 0.35 * dot(uv - 0.5, uv - 0.5) * 2.0;          // soft vignette
    col = col / (1.0 + col * 0.6);                                // gentle tone map
    col = pow(max(col, 0.0), vec3(0.92));
    col += (hash(gl_FragCoord.xy + iTime) - 0.5) / 255.0;         // dither: no banding in smooth gradients
    fragColor = vec4(col, 1.0);
}
