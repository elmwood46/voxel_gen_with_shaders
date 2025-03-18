shader_type spatial;

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
uniform sampler2D _damage_pulse_curvetex;

group_uniforms Damage_Pulse;
uniform bool _pulse_when_damaged;

group_uniforms Colours;
uniform vec4 _bordercol : source_color;
uniform vec4 _emissioncol : source_color;
uniform vec3 _burncol : source_color;
uniform vec3 _acidcol : source_color;
uniform vec3 _acidedge : source_color;
uniform vec3 _pulsecol : source_color;

group_uniforms Debug;
uniform bool ENABLE_DEBUG = false;
uniform float debug_damage_amount : hint_range(0.0,1.0);
uniform bool debug_physical;
uniform bool debug_acid;
uniform bool debug_fire;

uniform int _grass_lod_tex_array_pos;
uniform int _stone_lod_tex_array_pos;
uniform int _leaves_lod_tex_array_pos;
uniform int _dirt_lod_tex_array_pos;

// include has to come after uniforms are established
#include "res://shaders/chunk_block_damage_func.gdshaderinc"

void fragment() {
	// stored metadata in vertex colour buffer
	// texture array frame index in color.r
	// uv stretch factors in color.gb
	// the block's accrued damage (0-31) and damage type flags are stored in color.a
	vec4 metadata = round(COLOR*255.0);
	vec2 uv = fract(UV*metadata.gb);
	vec3 coords = vec3(uv, metadata.r);
	vec4 basecol;

	// only sample texture if not using LOD
	if (int(metadata.r) == _grass_lod_tex_array_pos
	|| int(metadata.r) == _stone_lod_tex_array_pos
	|| int(metadata.r) == _leaves_lod_tex_array_pos
	|| int(metadata.r) == _dirt_lod_tex_array_pos)
	{
		if (int(metadata.r) == _stone_lod_tex_array_pos)
		{
			basecol = vec4(0.34, 0.34, 0.34, 1.0);
		}
		else if (int(metadata.r) == _leaves_lod_tex_array_pos)
		{
			basecol = vec4(0.34, 0.47, 0.30, 1.0);
		}
		else if (int(metadata.r) == _dirt_lod_tex_array_pos)
		{
			basecol = vec4(0.42, 0.35, 0.28, 1.0);
		}
		else // grass texture LOD is default
		{
			basecol = vec4(0.21, 0.38, 0.21, 1.0);
		}
	}
	else
	{
		// sample from texture array
		basecol = texture(_albedo,coords);
	}

	// calculate damages
	int damagedata = int(metadata.a);
	float damageratio = float(damagedata&0x1f)/31.0;
	bool physicaldamage = damageratio > 0.0 && (damagedata>>5 & 1) == 1;
	bool firedamage = damageratio > 0.0 && (damagedata>>6 & 1) == 1;
	bool aciddamage = damageratio > 0.0 && (damagedata>>7 & 1) == 1;
	if (ENABLE_DEBUG) {
		damageratio = debug_damage_amount;
		firedamage = damageratio > 0.0 && debug_fire;
		aciddamage = damageratio > 0.0 && debug_acid;
		physicaldamage = damageratio > 0.0 && debug_physical;
	}
	bool notDamaged = (!(physicaldamage || firedamage || aciddamage) || (damageratio < 0.001));

	if (notDamaged)
	{
		ALBEDO = basecol.rgb;
	}
	else
	{
		vec3[2] res = ApplyDamageShaders(uv, UV, damageratio, physicaldamage, firedamage, aciddamage, basecol.rgb, false);

		ALBEDO = res[0];
		EMISSION = res[1];

		float normscale = mix(0.0,0.5,damageratio);

		if (aciddamage || physicaldamage)
		{
			NORMAL_MAP = texture(_normalmap, 0.15*uv).rgb;
			NORMAL_MAP_DEPTH = normscale;
		}

		if (aciddamage)
		{
			SPECULAR = mix(0.0,1.0,damageratio);
			ROUGHNESS = texture(_roughness, 0.15*uv).r * normscale;
		}
	}

	if (_pulse_when_damaged)
	{
		float damageTick = fract(TIME*(1.0+3.0*damageratio));
		float pulseRatio = texture(_damage_pulse_curvetex, vec2(damageTick,0.0)).r;
		pulseRatio = mix(0, pulseRatio, damageratio);
		ALBEDO = mix(ALBEDO, _pulsecol,pulseRatio);
		EMISSION += pulseRatio*0.2;
	}
}