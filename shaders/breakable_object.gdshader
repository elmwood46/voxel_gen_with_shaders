shader_type spatial;
render_mode ensure_correct_normals;

group_uniforms Textures;
uniform sampler2D _texture_albedo : filter_nearest;
uniform sampler2D _displacement : filter_nearest;
uniform sampler2D _roughness : filter_nearest;
uniform sampler2D _normalmap : filter_nearest;
uniform sampler2D _cracks_texture: filter_nearest;
uniform sampler2D _noise;
uniform sampler2D _spot_noise;
uniform sampler2D _acidcurvetex;
uniform sampler2D _firecurvetex;

group_uniforms Colours;
uniform vec4 _bordercol : source_color;
uniform vec4 _emissioncol : source_color;
uniform vec3 _burncol : source_color;
uniform vec3 _acidcol : source_color;
uniform vec3 _acidedge : source_color;

group_uniforms BlockInfo;
uniform int _damage_data;

group_uniforms ExplosionFuse;
uniform sampler2D _fuse_pulse_curve;
uniform vec4 _fuse_pulse_colour : source_color;
uniform float _fuse_ratio;
uniform bool _fuse_is_active;

#include "res://shaders/chunk_block_damage_func.gdshaderinc"

void fragment() {
	vec4 basecol = texture(_texture_albedo,UV);
	float damageratio = float(_damage_data&0x1f)/31.0;
	bool physicaldamage = (_damage_data>>5 & 1) == 1;
	bool firedamage = (_damage_data>>6 & 1) == 1;
	bool aciddamage = (_damage_data>>7 & 1) == 1;

	const float normscale = 1.0;
	const float uvscale = 1.0; //0.2 for default box mesh

	vec3[2] res = ApplyDamageShaders(
		UV,
		UV,
		damageratio,
		physicaldamage,
		firedamage,
		aciddamage,
		basecol.rgb,
		true
		);

	ALBEDO = res[0];
	EMISSION = res[1];

	if (aciddamage || physicaldamage)
	{
		NORMAL_MAP = texture(_normalmap, uvscale*UV).rgb;
		NORMAL_MAP_DEPTH = normscale;
	}

	if (aciddamage)
	{
		SPECULAR = 1.0;
		ROUGHNESS = texture(_roughness, uvscale*UV).r * normscale;
	}

	if (_fuse_is_active)
	{
		float pulseRatio = texture(_fuse_pulse_curve, vec2(_fuse_ratio,0.0)).r;
		ALBEDO = mix(ALBEDO, _fuse_pulse_colour.rgb,pulseRatio);
		EMISSION += pulseRatio*2.0;
	}
}