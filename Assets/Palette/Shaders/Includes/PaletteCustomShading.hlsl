
void GetShadingMask_float(in float diffuse, in float coverage, in float sharpness, in bool specular, out float mask)
{
    diffuse = saturate(diffuse);
    diffuse = specular ? diffuse : (1.0 - diffuse);

    float w = (1.0 - sharpness) * 0.25;

    mask = 1.0 - smoothstep(coverage - w, coverage + w, diffuse);
}
void GetShadingMask_half(in half diffuse, in half coverage, in half sharpness, in bool specular, out half mask)
{
    diffuse = saturate(diffuse);
    diffuse = specular ? diffuse : (1.0 - diffuse);

    half w = (1.0 - sharpness) * 0.25;

    mask = 1.0 - smoothstep(coverage - w, coverage + w, diffuse);
}


void ApplyShadows_float(in float3 color, in float attenuation, in float4 tint, in float opacity, in float4 replacementColor, out float3 result)
{
    // shadowOpacity is from -1 to 1. shadowTint is the target color of the shadow. shadowTint is added to the color.
    float3 shadowColor = tint.rgb;

    float mask = 1.0 - attenuation;
    result = color * lerp(1.0, shadowColor, mask * tint.a * opacity);
    result = lerp(result, replacementColor.rgb, mask * abs(opacity) * replacementColor.a);
}
void ApplyShadows_half(in half3 color, in half attenuation, in half4 tint, in half opacity, in half4 replacementColor, out half3 result)
{
    // shadowOpacity is from -1 to 1. shadowTint is the target color of the shadow. shadowTint is added to the color.
    half3 shadowColor = tint.rgb;

    half mask = 1.0 - attenuation;
    result = color * lerp(1.0, shadowColor, mask * tint.a * opacity);
    result = lerp(result, replacementColor.rgb, mask * abs(opacity) * replacementColor.a);
    result = replacementColor.rgb;
}


float4 _Fog_Color;
float _Fog_Density;
float _Fog_HeightFogOpacity;
float _Fog_HeightFogHeight;

float3 _Sky_GroundColor;
float3 _Sky_HorizonColor;
float3 _Sky_ZenithColor;
float3 _Sky_SunColor;
float _Sky_SunSize;
float4 _Sky_NighttimeTint;

// Very simple sky: ground/horizon/zenith gradient plus a flat sun disc.
// viewDir is expected to be the surface-to-camera vector (e.g. Shader
// Graph's View Vector node) - negate it to get the camera's look direction.
void GetSky_float(in float3 viewDir, in float3 sunDir, in float3 cameraPos, out float3 result)
{
    float3 dir = -normalize(viewDir);

    // Both terms are 0 on the opposite side of the horizon, so they compose
    // into a single ground -> horizon -> zenith gradient with no branching.
    float horizonToZenith = pow(saturate(dir.y), 0.45);
    float groundToHorizon = pow(saturate(-dir.y), 0.05);

    float3 horizonColor = _Sky_HorizonColor;
    float3 groundColor = _Sky_GroundColor;

    // horizonColor = lerp(horizonColor, _Fog_Color.rgb, _Fog_Density * 1.0);
    // groundColor = lerp(groundColor, _Fog_Color.rgb, saturate(exp(_Fog_Density * 1.0)));
    // groundColor = lerp(groundColor, _Fog_Color.rgb, _Fog_HeightFogOpacity * 1.0 * pow(saturate(-dir.y), 0.2));
    // horizonColor = lerp(horizonColor, _Fog_Color.rgb, _Fog_HeightFogOpacity * 1.0 * pow(saturate(-dir.y), 0.2));

    result = lerp(horizonColor, _Sky_ZenithColor, horizonToZenith);
    result = lerp(result, groundColor, groundToHorizon);

    // Sun disc only visible when looking upward (dir.y > 0), not on the ground
    if (dir.y > 0.0)
    {
        float sunDot = dot(dir, normalize(sunDir));
        float sunDisc = smoothstep(1.0 - _Sky_SunSize, 1.0, sunDot);
        result = lerp(result, _Sky_SunColor, sunDisc);
    }

    // Tint toward nighttime color as the sun sets (sun looking down = 0, reaches 1 at horizon, stays 1 below)
    float nightFactor = saturate(1.0 - normalize(sunDir).y);
    result = lerp(result, _Sky_NighttimeTint.rgb, nightFactor * _Sky_NighttimeTint.a);

    float heightFogFactor = 1.0 - saturate((cameraPos.y) / _Fog_HeightFogHeight);
    heightFogFactor = exp(log(heightFogFactor) * 10.0);
    heightFogFactor = saturate(heightFogFactor);
    heightFogFactor *= _Fog_HeightFogOpacity;
    // dim the heightFogFactor using the view direction so if we are looking up at the sky, we don't get a huge fog factor. This is a hacky way to do it, but it works for now.
    heightFogFactor *= saturate(1.0-dir.y);

    float fogFactor = 0.0;
    fogFactor = max(fogFactor, groundToHorizon * smoothstep(0.0, 0.18, _Fog_HeightFogOpacity));
    fogFactor = max(fogFactor, heightFogFactor);
    fogFactor = max(fogFactor, 1.0 - exp(-80.0 * _Fog_Density * 0.1));
    result = lerp(result, _Fog_Color.rgb, fogFactor);

}
void GetSky_half(in half3 viewDir, in half3 sunDir, in half3 cameraPos, out half3 result)
{
    half3 dir = -normalize(viewDir);

    half horizonToZenith = pow(saturate(dir.y), 0.45h);
    half groundToHorizon = pow(saturate(-dir.y), 0.05h);

    result = lerp((half3)_Sky_HorizonColor, (half3)_Sky_ZenithColor, horizonToZenith);
    result = lerp(result, (half3)_Sky_GroundColor, groundToHorizon);

    if (dir.y > 0.0h)
    {
        half sunDot = dot(dir, normalize(sunDir));
        half sunDisc = smoothstep(1.0 - (half)_Sky_SunSize, 1.0, sunDot);
        result = lerp(result, (half3)_Sky_SunColor, sunDisc);
    }

    // Tint toward nighttime color as the sun sets (sun looking down = 0, reaches 1 at horizon, stays 1 below)
    half nightFactor = saturate(1.0h - normalize(sunDir).y);
    result = lerp(result, (half3)_Sky_NighttimeTint, nightFactor * (half)_Sky_NighttimeTint.a);
}


void ApplyFog_float(in float3 color, in float3 cameraPos, in float3 worldPos, out float3 result)
{
    result = color;

    //  Apply height fog
    if (_Fog_HeightFogOpacity > 0.0)
    {
        float heightFogFactor = 1.0 - saturate((worldPos.y) / _Fog_HeightFogHeight);
        heightFogFactor = exp(log(heightFogFactor) * 10.0);
        heightFogFactor = saturate(heightFogFactor);

        result = lerp(result, _Fog_Color.rgb, heightFogFactor * _Fog_HeightFogOpacity);
    }

     // Apply distance fog
    if (_Fog_Density > 0.0)
    {
        float distance = length(worldPos - cameraPos);
        float fogFactor = 1.0 - exp(-distance * _Fog_Density * 0.1);

        result = lerp(result, _Fog_Color.rgb, fogFactor);
    }
}
void ApplyFog_half(in half3 color, in half3 cameraPos, in half3 worldPos, out half3 result)
{
    result = color;

    if (_Fog_HeightFogOpacity > 0.0h)
    {
        half heightFogFactor = 1.0h - saturate(worldPos.y / (half)_Fog_HeightFogHeight);
        heightFogFactor = exp(log(heightFogFactor) * 10.0h);
        heightFogFactor = saturate(heightFogFactor);

        result = lerp(result, (half3)_Fog_Color.rgb, heightFogFactor * (half)_Fog_HeightFogOpacity);
    }

    if (_Fog_Density > 0.0h)
    {
        half distance = length(worldPos - cameraPos);
        half fogFactor = 1.0h - exp(-distance * (half)_Fog_Density * 0.1h);

        result = lerp(result, (half3)_Fog_Color.rgb, fogFactor);
    }
}