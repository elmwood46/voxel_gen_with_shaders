shader_type spatial;

uniform float timeScaleFactor = 0.04;
uniform sampler2D noise_texture; // Albedo texture for the noise effect
uniform vec3 emission_color : source_color;
uniform float emission_strength : hint_range(0.0, 10.0);

// Local time based on scale factor
float localTime() {
    return TIME * timeScaleFactor;
}

mat2 makem2(float theta) {
    float c = cos(theta);
    float s = sin(theta);
    return mat2(vec2(c, -s), vec2(s, c));
}

float noise(vec2 x) {
    return texture(noise_texture, x).x; // Use UV coordinates for noise sampling
}

vec2 gradn(vec2 p) {
    float ep = 0.09;
    float gradx = noise(vec2(p.x + ep, p.y)) - noise(vec2(p.x - ep, p.y));
    float grady = noise(vec2(p.x, p.y + ep)) - noise(vec2(p.x, p.y - ep));
    return vec2(gradx, grady);
}

float flow(vec2 p) {
    float z = 2.0;
    float rz = 0.0;
    vec2 bp = p;

    for (float i = 1.0; i < 7.0; ++i) {
        p += localTime() * 0.6;
        bp += localTime() * 1.9;
        vec2 gr = gradn(i * p * 0.34 + localTime() * 1.0);
        gr *= makem2(localTime() * 6.0 - (0.05 * p.x + 0.03 * p.y) * 40.0);
        p += gr * 0.5;
        rz += (sin(noise(p) * 7.0) * 0.5 + 0.5) / z;
        p = mix(bp, p, 0.77);
        z *= 1.4;
        p *= 2.0;
        bp *= 1.9;
    }
    return rz;
}

void fragment() {
    vec3 emission = emission_color * emission_strength;

	 // Use the UV coordinates for texture sampling and animation
	vec2 world_pos_xz = (INV_VIEW_MATRIX * vec4(VERTEX.xyz, 1.0)).xz;
    vec2 uv = world_pos_xz;
    //uv *= 3.0; // Scale UVs for larger effect

    float rz = flow(uv);
    vec3 col = vec3(0.2, 0.07, 0.01) / rz;
    col = pow(col, vec3(1.4));

    ALBEDO = col;
	EMISSION = emission;
}