shader_type spatial;
render_mode ensure_correct_normals;
uniform sampler2DArray albedo_texture : filter_nearest;

varying vec3 local_vert;
varying vec3 local_norm;
varying vec2 local_uv;

void vertex() {
	local_vert = VERTEX;
	local_norm = NORMAL;
	local_uv = UV;
}

bool equals(vec3 v1, vec3 v2) {
	return abs(dot(v1,v2) - 1.0) < 0.001;
}

void fragment() {
	vec3 normal = local_norm;
	vec3 position = local_vert;

	int face = int(UV.x);
	// we are shading voxels
	if (equals(normal,vec3(0,-1,0))) {face = 0;}
	else if (equals(normal,vec3(0,1,0))) {face = 1;}
	if (equals(normal,vec3(-1,0,0))) {face = 2;}
	else if (equals(normal,vec3(1,0,0))) {face = 3;}
	if (equals(normal,vec3(0,0,-1))) {face = 4;}
	else if (equals(normal,vec3(0,0,1))) {face = 5;}

	float texture_frame = UV.y;
	vec2 sample_uv = position.xz;
	if (face == 0 || face == 1) sample_uv = position.xz;
	if (face == 2 || face == 3) sample_uv = position.zy;
	if (face == 4 || face == 5) sample_uv = position.xy;

	//sample_uv *= inv_voxel_size;
	sample_uv = fract(sample_uv);

	ALBEDO = texture(albedo_texture,vec3(sample_uv.x,-sample_uv.y,texture_frame)).rgb;
}