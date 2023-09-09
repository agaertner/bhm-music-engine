#if OPENGL
#define SV_POSITION POSITION
#define VS_SHADERMODEL vs_2_0
#define PS_SHADERMODEL ps_2_0
#else
#define VS_SHADERMODEL vs_4_0_level_9_1
#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float Radius = 0.215f;
float Opacity = 1.0f;
bool Smooth = true;

sampler s0 = sampler_state {
    AddressU = Mirror;
    AddressV = Mirror;
};

float4 CurvedBorder(float4 Position : SV_POSITION, float4 Color : COLOR0, float2 TexCoords : TEXCOORD0) : COLOR0 {
    // create alpha mask from vignette shape
    float vignette = TexCoords.x * TexCoords.y * (1.-TexCoords.x) * (1.-TexCoords.y);
    float alpha = smoothstep(pow(Radius,4), 0, vignette);

    float4 sam = tex2D(s0, TexCoords);
    
    // apply mask for curved border
    if (Smooth) {
    
      // anti-aliasing by alpha transition
      sam = lerp(sam, float4(0, 0, 0, 0), alpha);
      
    } else if (alpha > 0) {
    
      // sharp cut
      sam = float4(0, 0, 0, 0);
      
    }
    
    sam.a *= Opacity;
    return sam;
}

technique
{
    pass icon
    {
        PixelShader = compile PS_SHADERMODEL CurvedBorder();
    }
}
