// Galaxy: a slowly turning spiral of glowing dust; the core breathes with the bass, the arms shimmer with the treble.
vec3 scene(vec2 p, vec2 uv) {
    vec2 q = p * rot(iTime * 0.03) * 1.1;
    q.y *= 1.6;                                        // tilted disc
    float r = length(q), a = atan(q.y, q.x);
    float arms = sin(a * 2.0 - log(r + 0.001) * 4.5 + iTime * 0.1);
    float dust = fbm(vec2(a * 3.0 + r * 4.0, r * 6.0 - iTime * 0.05));
    float spiral = smoothstep(0.1, 1.0, arms * 0.5 + 0.5) * exp(-r * 1.6) * (0.4 + dust);
    vec3 col = pal(r * 0.6 + dust * 0.4) * spiral * (1.2 + energy());
    col += pal(0.95) * exp(-r * (7.0 - uBass * 3.0)) * (0.8 + kick() * 0.8);
    float twinkle = step(0.975, hash(floor(q * 70.0))) * exp(-r * 1.2) * (0.3 + uTreble * 2.0);
    col += vec3(1.0, 0.95, 0.9) * twinkle;
    return col;
}
