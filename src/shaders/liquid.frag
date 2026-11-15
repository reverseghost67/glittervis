// Liquid light: slow folds of glowing color, like oil and light projectors; mids stir the flow, beats ripple it.
vec3 scene(vec2 p, vec2 uv) {
    float t = iTime * 0.08;
    vec2 q = p * 0.9;
    vec2 a = vec2(fbm(q + t), fbm(q + vec2(5.2, 1.3) - t));
    vec2 b = vec2(fbm(q + 3.0 * a + vec2(1.7, 9.2) + t * 1.3), fbm(q + 3.0 * a + vec2(8.3, 2.8) - t));
    float f = fbm(q + (2.5 + uMid * 1.5) * b);
    float rip = sin(length(p) * 14.0 - iTime * 4.0) * kick() * 0.08;
    vec3 col = pal(f * 1.3 + length(b) * 0.4 + rip);
    col *= 0.35 + 1.4 * f * f + energy() * 0.6;
    col += pal(f + 0.33) * pow(length(a), 3.0) * 0.6;
    return col;
}
