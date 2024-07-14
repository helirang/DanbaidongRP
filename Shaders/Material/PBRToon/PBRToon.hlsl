#ifndef PBR_TOON_INCLUDED
#define PBR_TOON_INCLUDED

// These Flag encoded to GBuffer0.a
#define kToonFlagHairShadow     1 // Does not receive dynamic shadows
#define kToonFlagEyelash        2 // Does not receivce specular
#define kToonFlagHairMask       4 // The geometry uses subtractive mixed lighting
#define kToonFlagUnused1        8 // The geometry uses subtractive mixed lighting

float EncodeToonFlags(uint toonFlags)
{
    return toonFlags * (1.0h / 255.0h);
}

uint DecodeToonFlags(float encodedToonFlags)
{
    return uint((encodedToonFlags * 255.0h) + 0.5h);
}

float4 EncodeDepthToRGBA(float value) {
    uint intValue = asuint(value);
    float4 rgba;
    rgba.r = ((intValue >> 24) & 0xFF) / 255.0;
    rgba.g = ((intValue >> 16) & 0xFF) / 255.0;
    rgba.b = ((intValue >> 8) & 0xFF) / 255.0;
    rgba.a = (intValue & 0xFF) / 255.0;
    return rgba;
}

float DecodeRGBAToDepth(float4 rgba) {
    uint r = (uint)(rgba.r * 255.0);
    uint g = (uint)(rgba.g * 255.0);
    uint b = (uint)(rgba.b * 255.0);
    uint a = (uint)(rgba.a * 255.0);
    uint intValue = (r << 24) | (g << 16) | (b << 8) | a;
    return asfloat(intValue);
}


float4 SampleDirectShadowRamp(TEXTURE2D_PARAM(RampTex, RampSampler), float lightRange, float rampY = 0.125)
{
    return SAMPLE_TEXTURE2D(RampTex, RampSampler, float2(lightRange, rampY));
}

float4 SampleDirectSpecularRamp(TEXTURE2D_PARAM(RampTex, RampSampler), float specRange, float rampY = 0.375)
{
    return SAMPLE_TEXTURE2D(RampTex, RampSampler, float2(specRange, rampY));
}

float GetCharacterDirectRimLightArea(float3 normalVS, float2 screenUV, float d, float rimWidth)
{
    // RimLight
    float normalExtendLeftOffset = normalVS.x > 0 ? 1.0 : -1.0;
    normalExtendLeftOffset *= rimWidth * 0.0044;

    float eyeDepth = LinearEyeDepth(d, _ZBufferParams);

    float2 extendUV = screenUV;
    extendUV.x += normalExtendLeftOffset / (eyeDepth + 3.0);

    float extendedRawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_LinearClamp, extendUV, 0).x;
    float extendedEyeDepth = LinearEyeDepth(extendedRawDepth, _ZBufferParams);

    float depthOffset = extendedEyeDepth - eyeDepth;

    float rimArea = saturate(depthOffset * 5);

    return rimArea;
}

float GetCharacterPunctualRimLightArea(float3 lightDirVS, float2 screenUV, float d, float rimWidth)
{
    // RimLight
    float2 normalExtendDirVS = normalize(lightDirVS.xy);
    normalExtendDirVS *= rimWidth * 0.0044;

    float eyeDepth = LinearEyeDepth(d, _ZBufferParams);

    float2 extendUV = screenUV;
    extendUV.xy += normalExtendDirVS.xy / (eyeDepth + 3.0);

    float extendedRawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_LinearClamp, extendUV, 0).x;
    float extendedEyeDepth = LinearEyeDepth(extendedRawDepth, _ZBufferParams);

    float depthOffset = extendedEyeDepth - eyeDepth;

    float rimArea = saturate(depthOffset * 1);

    return rimArea;
}

float3 GetRimColor(float rimArea, float3 albedo, float3 normalVS, float3 lightDirVS, float shadow, float3 frontColor, float3 backColor)
{
    float NdotLVS = dot(normalVS, lightDirVS);

    float frontRim = max(NdotLVS, 0);
    float backRim = max(-NdotLVS, 0);

    float3 frontRimColor = frontRim * frontColor;
    float3 backRimColor = backRim * backColor;
    float3 albedoRimColor = saturate(albedo + 0.3);

    float3 rimColor = (frontRimColor + backRimColor) * albedoRimColor * saturate(shadow + 0.2);
    return rimColor * rimArea;
}

#endif /* PBR_TOON_INCLUDED */