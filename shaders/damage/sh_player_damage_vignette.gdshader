shader_type spatial;
render_mode unshaded;

uniform sampler2D texture_albedo;
uniform float damage_ratio : hint_range(0.0, 1.0, 0.01);

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	vec4 sample = texture(texture_albedo,UV);
	ALBEDO = sample.rgb;
	ALPHA = sample.a * damage_ratio;
}