shader_type spatial;
render_mode cull_back, unshaded;

uniform sampler2D _noise;
uniform sampler2D _spot_noise;
uniform vec3 burned_colour : source_color;

void fragment() {
	float noiseval = texture(_spot_noise,UV).r;
	noiseval += 0.05*sin(TIME);
	noiseval = pow(noiseval,2);

	ALBEDO = burned_colour*noiseval;
}