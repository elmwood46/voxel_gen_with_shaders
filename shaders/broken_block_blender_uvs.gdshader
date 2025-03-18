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

vec3 Blender_UV_To_Voxel_UV(vec2 box_uv) {
	// top face by default
	int idx = 1;

	if (box_uv.x<0.375) // bottom face
	{
		idx = 0;
	}
	else if (box_uv.x < 0.625) // central face
	{
		int yidx = int(floor((box_uv.y-0.0001)/0.25));
		switch (yidx) {
			case 0: idx = 2; break;
			case 1: idx = 5; break;
			case 2: idx = 3; break;
			case 3: idx = 4; break;
			default: idx = 1; break;
		}
	}
	else // top face
	{
		idx = 1;
	}

	float texture_frame = float(_tex_array_idx[idx]);

	return vec3(box_uv,texture_frame);
}

void fragment() {
	vec3 coords = Blender_UV_To_Voxel_UV(UV);
	vec2 uv = coords.xy;
	vec4 basecol = texture(_albedo,coords);
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
		NORMAL_MAP = texture(_normalmap, uvscale*uv).rgb;
		NORMAL_MAP_DEPTH = normscale;
	}

	if (aciddamage)
	{
		SPECULAR = 1.0;
		ROUGHNESS = texture(_roughness, uvscale*uv).r * normscale;
	}
}