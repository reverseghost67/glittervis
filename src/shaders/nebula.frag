// Nebula: drifting deep-space clouds that swell with the bass, stars that sparkle with the treble.
vec3 scene(vec2 p, vec2 uv) {
    float t = iTime * 0.04;
    vec2 q = p * 1.3 * rot(t * 0.6);
    float warp = fbm(q * 1.5 + t);
    float d = fbm(q * 2.0 + vec2(warp * 2.2, -warp) + t * 0.7);
    float swell = 0.55 + energy() * 0.9 + kick() * 0.35;
    vec3 col = pal(d * 1.4 + t * 0.5) * pow(d, 2.2) * 2.4 * swell;
    col += pal(d + 0.5) * pow(fbm(q * 4.0 - t), 4.0) * 1.6 * (0.5 + uMid);
    // stars
    vec2 g = floor(p * 90.0), f = fract(p * 90.0) - 0.5;
    float s = hash(g);
    float star = step(0.985, s) * smoothstep(0.08, 0.0, length(f)) * (0.5 + 0.5 * sin(iTime * 3.0 * s + s * 40.0));
    col += vec3(0.9, 0.95, 1.0) * star * (0.6 + uTreble * 1.6);
    col += pal(0.9) * 0.18 * kick() * exp(-length(p) * 1.5);
    return col;
}
