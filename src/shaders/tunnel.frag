// Tunnel: flying through rings of light; speed follows the music, rings flash on the beat, walls carry the spectrum.
vec3 scene(vec2 p, vec2 uv) {
    float r = length(p), a = atan(p.y, p.x);
    float speed = 0.6 + energy() * 1.8;
    float z = 0.35 / max(r, 0.02) + iTime * speed;
    float ang = a / 6.2831 + 0.5;
    float m = abs(fract(ang * 2.0) - 0.5) * 2.0;       // mirrored angle: no seam where the angle wraps
    float rings = smoothstep(0.82, 1.0, sin(z * 6.2831 * 0.8) * 0.5 + 0.5);
    float seg = spec(m) * smoothstep(0.0, 0.2, fract(z * 0.5));
    vec3 col = pal(z * 0.05 + m * 0.5) * (0.25 + seg * 1.6);
    col += pal(z * 0.05 + 0.5) * rings * (0.6 + kick() * 1.8);
    col *= smoothstep(0.0, 0.35, r);                   // dark center
    col += pal(0.75) * exp(-r * 6.0) * (0.3 + uBass);  // light at the end of the tunnel
    return col * (0.6 + 0.4 * fbm(vec2(m * 6.0, z)));
}
