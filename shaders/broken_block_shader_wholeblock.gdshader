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

vec3 Boxmesh_UV_To_Voxel_UV(vec2 box_uv) {
	//vec3 normal = NORMAL;
	// assume boxmesh has 3x2 uv layout
	float facex = min(box_uv.x,0.999999f)*3.0;
	float facey = min(box_uv.y,0.999999f)*2.0;

	// get idx of uv wrapped around boxmesh
	int idx = int(floor(facex) + floor(facey)*3.0);
	// transform the boxmesh index to the indexing style of our blocks
	// (looking from +y, boxmesh starts at +z and wraps around anticlockwise (so +z, +x, -z, -x) before doing top then bottom)
	// (our blocks go bottom,top,left,right,back,front)
	switch (idx) {
		case 0: idx = 5; break;
		case 1: idx = 3; break;
		case 2: idx = 4; break;
		case 3: idx = 2; break;
		case 4: idx = 1; break;
		case 5: default : idx = 0; break;
	};

	float texture_frame = float(_tex_array_idx[idx]);

	return vec3(fract(facex),fract(facey),texture_frame);
}

void fragment() {
	vec3 coords = Boxmesh_UV_To_Voxel_UV(UV);
	vec2 uv = coords.xy;
	vec4 basecol = texture(_albedo,coords);
	float damageratio = 1.0;
	bool physicaldamage = (_damage_data>>5 & 1) == 1;
	bool firedamage = (_damage_data>>6 & 1) == 1;
	bool aciddamage = (_damage_data>>7 & 1) == 1;

	vec3[2] res = ApplyDamageShaders(
		uv,
		UV,
		damageratio,
		physicaldamage,
		firedamage,
		aciddamage,
		basecol.rgb,
		false
		);

	ALBEDO = res[0];
	EMISSION = res[1];

	const float normscale = 3.0;
	const float uvscale = 0.2; //0.2 for default box mesh

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