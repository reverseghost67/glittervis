// Aurora: curtains of light over a dark horizon; each curtain's height follows part of the spectrum.
vec3 scene(vec2 p, vec2 uv) {
    vec3 col = mix(vec3(0.005, 0.01, 0.03), uPal[0] * 0.15, uv.y);
    float t = iTime * 0.15;
    for (int i = 0; i < 5; i++) {
        float fi = float(i);
        float x = p.x * (0.6 + fi * 0.12) + fi * 1.7;
        float wave = sin(x * 1.3 + t * (1.0 + fi * 0.3)) * 0.18 + fbm(vec2(x * 0.8, t + fi)) * 0.5;
        float h = 0.15 + spec(0.05 + fi * 0.18) * 0.55 + energy() * 0.15;
        float base = -0.2 + wave * 0.6;
        float y = p.y - base;
        float curtain = smoothstep(0.0, 0.02, y) * exp(-y / max(h, 0.05) * 2.2);
        float rays = 0.6 + 0.4 * fbm(vec2(x * 8.0, t * 2.0 + fi));
        col += pal(fi * 0.21 + t * 0.05) * curtain * rays * (0.45 + kick() * 0.35);
    }
    // stars and a faint ground glow
    float s = step(0.992, hash(floor(p * 120.0))) * smoothstep(-0.1, 0.6, p.y);
    col += vec3(0.8) * s * 0.6;
    col += uPal[2] * 0.08 * exp(-abs(p.y + 0.85) * 8.0);
    col *= smoothstep(-0.95, -0.8, p.y) * 0.85 + 0.15;
    return col;
}
