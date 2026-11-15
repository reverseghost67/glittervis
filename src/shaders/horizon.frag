// Horizon: the elegant one. The spectrum as a glowing city of bars on a horizon, mirrored in still water.
vec3 scene(vec2 p, vec2 uv) {
    vec3 col = mix(uPal[0] * 0.25, vec3(0.0), clamp(p.y * 1.2, 0.0, 1.0));     // dusk sky
    float hz = -0.15;
    bool water = p.y < hz;
    float y = water ? hz - (p.y - hz) * 1.0 : p.y;                              // mirror below the horizon
    float x = p.x / (iResolution.x / iResolution.y) * 0.5 + 0.5;                // 0..1 across the screen
    float band = floor(x * 64.0) / 64.0;
    float mirrorX = abs(band - 0.5) * 2.0;                                       // bass in the middle, highs at the edges
    float h = spec(mirrorX) * 0.75 + 0.01;
    float inBar = step(fract(x * 64.0), 0.78) * step(hz, y) * step(y, hz + h);
    float glow = exp(-max(y - hz - h, 0.0) * 18.0) * step(hz, y) * 0.25 * step(fract(x * 64.0), 0.78);
    vec3 barCol = pal(mirrorX * 0.8 + 0.1) * (1.1 + kick() * 0.6);
    vec3 c = barCol * (inBar * (0.5 + (y - hz) / max(h, 0.01) * 0.8) + glow);
    if (water) {
        float ripple = noise(vec2(p.x * 40.0, p.y * 6.0 + iTime * 1.5));
        c *= 0.45 * (0.7 + 0.3 * ripple) * smoothstep(-1.0, hz, p.y);
    }
    col += c;
    col += pal(0.5) * 0.15 * exp(-abs(p.y - hz) * 40.0) * (0.5 + energy());     // horizon line
    return col;
}
