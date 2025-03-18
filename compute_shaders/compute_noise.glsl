#ifndef NOISE_SIMPLEX_FUNC
#define NOISE_SIMPLEX_FUNC
/*

Description:
	Array- and textureless CgFx/HLSL 2D, 3D and 4D simplex noise functions.
	a.k.a. simplified and optimized Perlin noise.

	The functions have very good performance
	and no dependencies on external data.

	2D - Very fast, very compact code.
	3D - Fast, compact code.
	4D - Reasonably fast, reasonably compact code.

------------------------------------------------------------------

Ported by:
	Lex-DRL
	I've ported the code from GLSL to CgFx/HLSL for Unity,
	added a couple more optimisations (to speed it up even further)
	and slightly reformatted the code to make it more readable.

Original GLSL functions:
	https://github.com/ashima/webgl-noise
	Credits from original glsl file are at the end of this cginc.

------------------------------------------------------------------

Usage:

	float ns = snoise(v);
	// v is any of: vec2, vec3, vec4

	Return type is float.
	To generate 2 or more components of noise (colorful noise),
	call these functions several times with different
	constant offsets for the arguments.
	E.g.:

	vec3 colorNs = vec3(
		snoise(v),
		snoise(v + 17.0),
		snoise(v - 43.0),
	);


Remark about those offsets from the original author:

	People have different opinions on whether these offsets should be integers
	for the classic noise functions to match the spacing of the zeroes,
	so we have left that for you to decide for yourself.
	For most applications, the exact offsets don't really matter as long
	as they are not too small or too close to the noise lattice period
	(289 in this implementation).

*/

// 1 / 289
#define NOISE_SIMPLEX_1_DIV_289 0.00346020761245674740484429065744f

float mod289(float x) {
	return x - floor(x * NOISE_SIMPLEX_1_DIV_289) * 289.0;
}

vec2 mod289(vec2 x) {
	return x - floor(x * NOISE_SIMPLEX_1_DIV_289) * 289.0;
}

vec3 mod289(vec3 x) {
	return x - floor(x * NOISE_SIMPLEX_1_DIV_289) * 289.0;
}

vec4 mod289(vec4 x) {
	return x - floor(x * NOISE_SIMPLEX_1_DIV_289) * 289.0;
}

// ( x*34.0 + 1.0 )*x = 
// x*x*34.0 + x
float permute(float x) {
	return mod289(
		x * x * 34.0 + x
	);
}

vec3 permute(vec3 x) {
	return mod289(
		x * x * 34.0 + x
	);
}

vec4 permute(vec4 x) {
	return mod289(
		x * x * 34.0 + x
	);
}

vec4 grad4(float j, vec4 ip)
{
	const vec4 ones = vec4(1.0, 1.0, 1.0, -1.0);
	vec4 p, s;
	p.xyz = floor(fract(j * ip.xyz) * 7.0) * ip.z - 1.0;
	p.w = 1.5 - dot(abs(p.xyz), ones.xyz);

	// GLSL: lessThan(x, y) = x < y
	// HLSL: 1 - step(y, x) = x < y
	p.xyz -= sign(p.xyz) * float(p.w < 0);

	return p;
}



// ----------------------------------- 2D -------------------------------------

float snoise(vec2 v)
{
	const vec4 C = vec4(
		0.211324865405187, // (3.0-sqrt(3.0))/6.0
		0.366025403784439, // 0.5*(sqrt(3.0)-1.0)
		-0.577350269189626, // -1.0 + 2.0 * C.x
		0.024390243902439  // 1.0 / 41.0
		);

	// First corner
	vec2 i = floor(v + dot(v, C.yy));
	vec2 x0 = v - i + dot(i, C.xx);

	// Other corners
		// vec2 i1 = (x0.x > x0.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
		// Lex-DRL: afaik, step() in GPU is faster than if(), so:
		// step(x, y) = x <= y

		// Actually, a simple conditional without branching is faster than that madness :)
	vec2 i1 = (x0.x > x0.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
	vec4 x12 = x0.xyxy + C.xxzz;
	x12.xy -= i1;

	// Permutations
	i = mod289(i); // Avoid truncation effects in permutation
	vec3 p = permute(
		permute(
			i.y + vec3(0.0, i1.y, 1.0)
		) + i.x + vec3(0.0, i1.x, 1.0)
	);

	vec3 m = max(
		0.5 - vec3(
			dot(x0, x0),
			dot(x12.xy, x12.xy),
			dot(x12.zw, x12.zw)
			),
		0.0
	);
	m = m * m;
	m = m * m;

	// Gradients: 41 points uniformly over a line, mapped onto a diamond.
	// The ring size 17*17 = 289 is close to a multiple of 41 (41*7 = 287)

	vec3 x = 2.0 * fract(p * C.www) - 1.0;
	vec3 h = abs(x) - 0.5;
	vec3 ox = floor(x + 0.5);
	vec3 a0 = x - ox;

	// Normalise gradients implicitly by scaling m
	// Approximation of: m *= inversesqrt( a0*a0 + h*h );
	m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);

	// Compute final noise value at P
	vec3 g;
	g.x = a0.x * x0.x + h.x * x0.y;
	g.yz = a0.yz * x12.xz + h.yz * x12.yw;
	return 130.0 * dot(m, g);
}

// ----------------------------------- 3D -------------------------------------

float snoise(vec3 v)
{
	const vec2 C = vec2(
		0.166666666666666667, // 1/6
		0.333333333333333333  // 1/3
		);
	const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);

	// First corner
	vec3 i = floor(v + dot(v, C.yyy));
	vec3 x0 = v - i + dot(i, C.xxx);

	// Other corners
	vec3 g = step(x0.yzx, x0.xyz);
	vec3 l = 1 - g;
	vec3 i1 = min(g.xyz, l.zxy);
	vec3 i2 = max(g.xyz, l.zxy);

	vec3 x1 = x0 - i1 + C.xxx;
	vec3 x2 = x0 - i2 + C.yyy; // 2.0*C.x = 1/3 = C.y
	vec3 x3 = x0 - D.yyy;      // -1.0+3.0*C.x = -0.5 = -D.y

// Permutations
	i = mod289(i);
	vec4 p = permute(
		permute(
			permute(
				i.z + vec4(0.0, i1.z, i2.z, 1.0)
			) + i.y + vec4(0.0, i1.y, i2.y, 1.0)
		) + i.x + vec4(0.0, i1.x, i2.x, 1.0)
	);

	// Gradients: 7x7 points over a square, mapped onto an octahedron.
	// The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
	float n_ = 0.142857142857; // 1/7
	vec3 ns = n_ * D.wyz - D.xzx;

	vec4 j = p - 49.0 * floor(p * ns.z * ns.z); // mod(p,7*7)

	vec4 x_ = floor(j * ns.z);
	vec4 y_ = floor(j - 7.0 * x_); // mod(j,N)

	vec4 x = x_ * ns.x + ns.yyyy;
	vec4 y = y_ * ns.x + ns.yyyy;
	vec4 h = 1.0 - abs(x) - abs(y);

	vec4 b0 = vec4(x.xy, y.xy);
	vec4 b1 = vec4(x.zw, y.zw);

	//vec4 s0 = vec4(lessThan(b0,0.0))*2.0 - 1.0;
	//vec4 s1 = vec4(lessThan(b1,0.0))*2.0 - 1.0;
	vec4 s0 = floor(b0) * 2.0 + 1.0;
	vec4 s1 = floor(b1) * 2.0 + 1.0;
	vec4 sh = -step(0.0,h);

	vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
	vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

	vec3 p0 = vec3(a0.xy, h.x);
	vec3 p1 = vec3(a0.zw, h.y);
	vec3 p2 = vec3(a1.xy, h.z);
	vec3 p3 = vec3(a1.zw, h.w);

	//Normalise gradients
	vec4 norm = inversesqrt(vec4(
		dot(p0, p0),
		dot(p1, p1),
		dot(p2, p2),
		dot(p3, p3)
		));
	p0 *= norm.x;
	p1 *= norm.y;
	p2 *= norm.z;
	p3 *= norm.w;

	// Mix final noise value
	vec4 m = max(
		0.6 - vec4(
			dot(x0, x0),
			dot(x1, x1),
			dot(x2, x2),
			dot(x3, x3)
			),
		0.0
	);
	m = m * m;
	return 42.0 * dot(
		m * m,
		vec4(
			dot(p0, x0),
			dot(p1, x1),
			dot(p2, x2),
			dot(p3, x3)
			)
	);
}

// ----------------------------------- 4D -------------------------------------

float snoise(vec4 v)
{
	const vec4 C = vec4(
		0.138196601125011, // (5 - sqrt(5))/20 G4
		0.276393202250021, // 2 * G4
		0.414589803375032, // 3 * G4
		-0.447213595499958  // -1 + 4 * G4
		);

	// First corner
	vec4 i = floor(
		v + v*0.309016994374947451 // (sqrt(5) - 1) / 4
	);
	vec4 x0 = v - i + dot(i, C.xxxx);

	// Other corners

	// Rank sorting originally contributed by Bill Licea-Kane, AMD (formerly ATI)
	vec4 i0;
	vec3 isX = step(x0.yzw, x0.xxx);
	vec3 isYZ = step(x0.zww, x0.yyz);
	i0.x = isX.x + isX.y + isX.z;
	i0.yzw = 1.0 - isX;
	i0.y += isYZ.x + isYZ.y;
	i0.zw += 1.0 - isYZ.xy;
	i0.z += isYZ.z;
	i0.w += 1.0 - isYZ.z;

	// i0 now contains the unique values 0,1,2,3 in each channel
	vec4 i3 = clamp(i0,0.0,1.0);
	vec4 i2 = clamp(i0 - 1.0,0.0,1.0);
	vec4 i1 = clamp(i0 - 2.0,0.0,1.0);

	//	x0 = x0 - 0.0 + 0.0 * C.xxxx
	//	x1 = x0 - i1  + 1.0 * C.xxxx
	//	x2 = x0 - i2  + 2.0 * C.xxxx
	//	x3 = x0 - i3  + 3.0 * C.xxxx
	//	x4 = x0 - 1.0 + 4.0 * C.xxxx
	vec4 x1 = x0 - i1 + C.xxxx;
	vec4 x2 = x0 - i2 + C.yyyy;
	vec4 x3 = x0 - i3 + C.zzzz;
	vec4 x4 = x0 + C.wwww;

	// Permutations
	i = mod289(i);
	float j0 = permute(
		permute(
			permute(
				permute(i.w) + i.z
			) + i.y
		) + i.x
	);
	vec4 j1 = permute(
		permute(
			permute(
				permute(
					i.w + vec4(i1.w, i2.w, i3.w, 1.0)
				) + i.z + vec4(i1.z, i2.z, i3.z, 1.0)
			) + i.y + vec4(i1.y, i2.y, i3.y, 1.0)
		) + i.x + vec4(i1.x, i2.x, i3.x, 1.0)
	);

	// Gradients: 7x7x6 points over a cube, mapped onto a 4-cross polytope
	// 7*7*6 = 294, which is close to the ring size 17*17 = 289.
	const vec4 ip = vec4(
		0.003401360544217687075, // 1/294
		0.020408163265306122449, // 1/49
		0.142857142857142857143, // 1/7
		0.0
		);

	vec4 p0 = grad4(j0, ip);
	vec4 p1 = grad4(j1.x, ip);
	vec4 p2 = grad4(j1.y, ip);
	vec4 p3 = grad4(j1.z, ip);
	vec4 p4 = grad4(j1.w, ip);

	// Normalise gradients
	vec4 norm = inversesqrt(vec4(
		dot(p0, p0),
		dot(p1, p1),
		dot(p2, p2),
		dot(p3, p3)
		));
	p0 *= norm.x;
	p1 *= norm.y;
	p2 *= norm.z;
	p3 *= norm.w;
	p4 *= inversesqrt(dot(p4, p4));

	// Mix contributions from the five corners
	vec3 m0 = max(
		0.6 - vec3(
			dot(x0, x0),
			dot(x1, x1),
			dot(x2, x2)
			),
		0.0
	);
	vec2 m1 = max(
		0.6 - vec2(
			dot(x3, x3),
			dot(x4, x4)
			),
		0.0
	);
	m0 = m0 * m0;
	m1 = m1 * m1;

	return 49.0 * (
		dot(
			m0 * m0,
			vec3(
				dot(p0, x0),
				dot(p1, x1),
				dot(p2, x2)
				)
		) + dot(
			m1 * m1,
			vec2(
				dot(p3, x3),
				dot(p4, x4)
				)
		)
		);
}



//                 Credits from source glsl file:
//
// Description : Array and textureless GLSL 2D/3D/4D simplex 
//               noise functions.
//      Author : Ian McEwan, Ashima Arts.
//  Maintainer : ijm
//     Lastmod : 20110822 (ijm)
//     License : Copyright (C) 2011 Ashima Arts. All rights reserved.
//               Distributed under the MIT License. See LICENSE file.
//               https://github.com/ashima/webgl-noise
//
//
//           The text from LICENSE file:
//
//
// Copyright (C) 2011 by Ashima Arts (Simplex noise)
// Copyright (C) 2011 by Stefan Gustavson (Classic noise)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
#endif