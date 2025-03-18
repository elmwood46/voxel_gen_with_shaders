shader_type spatial;
render_mode ensure_correct_normals;

group_uniforms Textures;
uniform sampler2DArray _albedo : filter_nearest;
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
uniform int[6] _tex_array_idx;
uniform int _damage_data;

#include "res://shaders/chunk_block_damage_func.gdshaderinc"

void fragment() {
	// use top face as the broken block texture
	vec3 coords = vec3(UV,float(_tex_array_idx[1]));
	vec2 uv = UV;
	vec4 basecol = texture(_albedo,coords);
	float damageratio = 1.0;
	bool physicaldamage = (_damage_data>>5 & 1) == 1;
	bool firedamage = (_damage_data>>6 & 1) == 1;
	bool aciddamage = (_damage_data>>7 & 1) == 1;

	vec3[2] res = ApplyDamageShaders(uv, UV, damageratio, physicaldamage, firedamage, aciddamage, basecol.rgb, true);

	ALBEDO = res[0];
	EMISSION = res[1];

	float normscale = 3.0;

	if (aciddamage || physicaldamage)
	{
		NORMAL_MAP = texture(_normalmap, uv).rgb;
		NORMAL_MAP_DEPTH = normscale;
	}

	if (aciddamage)
	{
		SPECULAR = 1.0;
		ROUGHNESS = texture(_roughness, uv).r * normscale;
	}
}