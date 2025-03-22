#ifndef VOXEL_VALUES
#define VOXEL_VALUES

// local block position in local padded chunk space
vec3 local_block_pos()
{
    return (vec3(float(int(gl_GlobalInvocationID.x) % Params.CSP), float(gl_GlobalInvocationID.y), float(gl_GlobalInvocationID.z)));
}

// local block position in local padded chunk space, as an unsigned int
uvec3 u_local_block_pos()
{
    return (uvec3(gl_GlobalInvocationID.x % uint(Params.CSP), gl_GlobalInvocationID.y, gl_GlobalInvocationID.z));
}

// global block position in world global space
vec3 global_block_pos()
{
    return Uniform.Position*float(Params.CSP-2) + local_block_pos() - VEC3_ONE; // offset by one because chunk padding
}

// convert the current global invocation to a position in the voxel array
int glob_invocation_to_vox_array_index()
{
    return int(gl_GlobalInvocationID.x)
        + (int(gl_GlobalInvocationID.y) * Params.CSP * Params.CSP)
        + (int(gl_GlobalInvocationID.z) * Params.CSP);
}

const vec3 CUBEVERTICES[8] =
{
        vec3(0, 0, 0), //0
        vec3(1, 0, 0), //1
        vec3(0, 1, 0), //2
        vec3(1, 1, 0), //3

        vec3(0, 0, 1), //4
        vec3(1, 0, 1), //5
        vec3(0, 1, 1), //6
        vec3(1, 1, 1), //7
};

const vec3 CUBEFACECHECKS[6] =
{
        vec3(0, 0, -1), //back
        vec3(0, 0, 1),  //front
        vec3(-1, 0, 0), //left
        vec3(1, 0, 0),  //right
        vec3(0, -1, 0), //bottom
        vec3(0, 1, 0)   //top
};

const int CUBEVERTEXINDEX[6][4] =
{
    { 0, 1, 2, 3 },
    { 4, 5, 6, 7 },
    { 4, 0, 6, 2 },
    { 5, 1, 7, 3 },
    { 0, 1, 4, 5 },
    { 2, 3, 6, 7 },
};

const vec2 CUBEUVS[4] =
{
        vec2(0, 0),
        vec2(0, 1),
        vec2(1, 0),
        vec2(1, 1)
};

const int CUBETRIS[6][6] =
{
    { 0, 2, 3, 0, 3, 1 },
    { 0, 1, 2, 1, 3, 2 },
    { 0, 2, 3, 0, 3, 1 },
    { 0, 1, 2, 1, 3, 2 },
    { 0, 1, 2, 1, 3, 2 },
    { 0, 2, 3, 0, 3, 1 },
};

#endif