// must be included below the uniforms of a shader which has the chunk uv shader uniforms
vec3[2] ApplyDamageShaders(
	vec2 uv,
	vec2 base_uv,
	float damageratio,
	bool physicaldamage,
	bool firedamage,
	bool aciddamage,
	vec3 base_albedo,
	bool broken_block_mode)
{
	// for some reason the curve textures are fucky with high input values of damageratio
	damageratio = min(damageratio,0.999);

	vec3 finalcol = base_albedo;
	vec3 finalemission = vec3(0);

	// acid damage modifies uvs to make them wavy
	// which is why the check for it comes first
	if (aciddamage) {
		float acid_ratio = texture(_acidcurvetex, vec2(damageratio,0.0)).r;
		float uvoffset = acid_ratio*0.01*sin(10.0*base_uv.y+10.0*TIME);
		//uv += uvoffset;
		//finalcol = mix(finalcol,finalcol+vec3(0.00,0.4,0),damageratio);
		float noisediscard = texture(_spot_noise,0.5*(uv+uvoffset)).r;
	    vec2 noisecoord = vec2(32,32);
	    //noisecoord += TIME*0.05;
	    float noise = texture(_noise,noisecoord).r;
	    noisediscard += noise*0.15;

		if (noisediscard < acid_ratio) {
			float noiseval = texture(_spot_noise,uv).r;
			noiseval += 0.05*sin(TIME);
			noiseval = pow(noiseval,2);
			finalcol = _acidcol*noiseval;
			finalemission *= noiseval;
		}
		else {
			float borderdist = smoothstep(acid_ratio,acid_ratio+0.1,noisediscard);

			finalcol = mix(vec4(_acidedge,1.0), vec4(finalcol, 1.0), borderdist).rgb;
			finalemission += (mix(vec4(_acidedge,1.0)*7.0, vec4(0.0), borderdist)).rgb;

			finalcol = mix(vec4(0.00,0.01,0,1.0), vec4(finalcol, 1.0), borderdist).rgb;
			finalemission += (mix(vec4(0.00,0.1,0,1.0)*7.0, vec4(0.0), borderdist)).rgb;

			noise += 0.05*sin(TIME);
			noise = pow(noise,2);
			finalcol = mix(vec4(finalcol, 1.0),vec4(finalcol + 2.0*_acidedge*noise,1.0),acid_ratio).rgb;
			finalemission *= noise;
		}
	}

	if (firedamage) {
		float fire_ratio = texture(_firecurvetex, vec2(damageratio,0.0)).r;

		float noisediscard = texture(_spot_noise,uv).r;
	    vec2 noisecoord = base_uv;
	    noisecoord += TIME*0.05;
	    float noise = texture(_noise,noisecoord).r;
	    noisediscard += noise*0.05;

		if (noisediscard < fire_ratio) {
			float noiseval = texture(_spot_noise,uv).r;
			noiseval += 0.05*sin(TIME);
			noiseval = pow(noiseval,2);
			finalcol = _burncol*noiseval;
			finalemission *= noiseval;
		}
		else {
			float borderdist = smoothstep(fire_ratio,fire_ratio+0.1,noisediscard);

			finalcol = mix(_bordercol, vec4(finalcol, 1.0), borderdist).rgb;
			finalemission += (mix(_emissioncol*7.0, vec4(0.0), borderdist)).rgb;
		}
	}

	if (physicaldamage) {
		// we have "broken block mode" which sets the scale of cracks
		// it's scaled up by 6x for chunk blocks, because they are
		// textured per-face (each face the UV goes 0.0-1.0)
		// while broken blocks have one texture which is spread across the 6 faces

		vec2 uvscaled = uv;
		if (!broken_block_mode) uvscaled *= 0.16666667f;
		float cracks = texture(_cracks_texture, uvscaled).r;
		float cracksExtra = 1.0;
		if (damageratio > 0.5) {
			cracksExtra = texture(_cracks_texture, vec2(0.95,-1.0)+uvscaled).r;
			cracksExtra = mix(1.0,cracksExtra,(damageratio-0.5)/0.5);
		}
		cracks = pow(cracks*cracksExtra, mix(0.0,20,damageratio));
		finalcol -= vec3(1.0-cracks);//*cracksExtra;
		finalcol *= (1.0-damageratio*0.5);
	}

	return {finalcol,finalemission};
}