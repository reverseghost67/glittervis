// Kaleidoscope: the psychedelic one, in the spirit of MilkDrop, folded into 8 mirrored slices that pulse with the beat.
vec3 scene(vec2 p, vec2 uv) {
    float r = length(p);
    float a = atan(p.y, p.x) + iTime * 0.05;
    float k = 8.0;
    a = abs(mod(a, 6.2831 / k) - 3.14159 / k);
    vec2 q = vec2(cos(a), sin(a)) * r;
    q *= 1.2 + 0.25 * sin(iTime * 0.2) - kick() * 0.12;
    float f = fbm(q * 2.5 + vec2(iTime * 0.15, -iTime * 0.1));
    float g = fbm(q * 5.0 - f * 2.0 + iTime * 0.1);
    vec3 col = pal(f + g * 0.6 + r * 0.3 - iTime * 0.03) * (0.25 + g * 1.4);
    col *= 0.6 + energy() * 0.9;
    col += pal(r + 0.5) * smoothstep(0.02, 0.0, abs(fract(r * 3.0 - iTime * 0.3) - 0.5) - 0.48) * kick() * 0.8;
    return col;
}
