#[compute]
#version 450

layout(local_size_x = 100, local_size_y = 1, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict buffer input_array {
    float data[];
} FloatInput;

layout(set = 0, binding = 1, std430) restrict buffer readonly test_buffer {
    uint test;
} TestBuffer;

void main() {
    FloatInput.data[gl_GlobalInvocationID.x] *= 2.0;
}