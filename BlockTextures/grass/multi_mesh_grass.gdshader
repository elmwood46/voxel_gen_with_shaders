shader_type spatial;
render_mode cull_disabled;

const vec3 UP = vec3(0,1,0);
const vec3 RIGHT = vec3(1,0,0);
const float DEG2RAD = (PI/180.0);

uniform vec4 col_top:source_color = vec4(1,1,1,1);
uniform vec4 col_bottom:source_color  = vec4(0,0,0,0);
uniform float roughness_factor: hint_range(0.0, 5.0) = 0.8;

uniform float deg_sway_pitch_offset = 80.0;
uniform float deg_sway_yaw_offset = 45.0;

uniform float wind_scale = 4.0;
uniform float wind_speed = 1.0;
uniform vec3 wind_direction = vec3(0,0,-1);
varying float wind;

// used in voroni noise function
vec2 random2(vec2 p) {
	float offset = 231.23;
	return fract(sin(vec2(
		dot(p,vec2(127.32,231.4)),
		dot(p, vec2(12.3,146.3))
	))*offset);
}

// a type of simple voroni noise
float worley2(vec2 p) {
	float dist = 1.0;
	vec2 p_i = floor(p);
	vec2 p_f = fract(p);
	for (int y=-1;y<=1;y++) {
		for (int x=-1;x<=1;x++) {
			vec2 n= vec2(float(x),float(y));
			vec2 diff = n + random2(p_i+n) - p_f;
			dist = min(dist,length(diff));
		}
	}
	return dist;
}

// a transform that represents a rotation of an angle around an axis
mat3 mat3_from_axis_angle(float angle, vec3 axis) {
	float s = sin(angle), c = cos(angle);
	float t = 1.0 - c;
	float x = axis.x, y = axis.y, z=axis.z;
	return mat3(
		vec3(t*x*x+c,t*x*y-s*z,t*x*z+s*y),
		vec3(t*x*y+s*z,t*y*y+c,t*y*z-s*x),
		vec3(t*x*z-s*y,t*y*z+s*z,t*z*z+c)
	);
}

void vertex() {
	NORMAL = UP;
	vec3 v = VERTEX;
	vec3 wind_direction_normalized = normalize(wind_direction);
	float time = TIME*wind_speed;
	vec2 uv = (MODEL_MATRIX * vec4(v,-1.0)).xz * wind_scale; // get world uv of vertex

	v.xz *= INSTANCE_CUSTOM.x;
	v.y *= INSTANCE_CUSTOM.y;
	uv += wind_direction_normalized.xz*time;
	wind = pow(worley2(uv),2.0) * UV.y;

	mat3 to_model = inverse(mat3(MODEL_MATRIX));
	vec3 wind_forward = to_model * wind_direction_normalized;
	vec3 wind_right = normalize(cross(wind_forward,UP));

	float sway_pitch = deg_sway_pitch_offset * DEG2RAD * wind + INSTANCE_CUSTOM.w;
	float sway_yaw = sin(time) * deg_sway_yaw_offset * DEG2RAD * wind + INSTANCE_CUSTOM.z;

	mat3 rot_right = mat3_from_axis_angle(sway_pitch,wind_right);
	mat3 rot_forward = mat3_from_axis_angle(sway_yaw,wind_forward);

	VERTEX = rot_right * rot_forward * v;
	COLOR = mix(col_bottom,col_top,UV.y);
}

void fragment() {
	if (!FRONT_FACING) {NORMAL=-NORMAL;}
	ALBEDO = COLOR.rgb;
	SPECULAR = 0.5;
	ROUGHNESS = clamp(1.0- (roughness_factor*wind),0.5,1.0);
}