// Global resources belong to register space1 and they are set using SetGlobal* functions.

TextureCube<float4>	g_EnvTexture			: register(t0, space1);
SamplerState		sampler_g_EnvTexture	: register(s0, space1);
