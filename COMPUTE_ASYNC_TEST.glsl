#[compute]
#version 450

layout(local_size_x = 2, local_size_y = 1, local_size_z = 1) in;

// input block information
layout(set=0, binding = 0, std430) restrict buffer FloatBuffer {
    float Floats[];
} float_buffer;

void main() {
	float_buffer.Floats[gl_GlobalInvocationID.x] *= 2.0;
}