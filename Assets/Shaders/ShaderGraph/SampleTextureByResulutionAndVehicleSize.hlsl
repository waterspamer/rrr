// Shader Graph custom function for sampling damage texture by resolution and vehicle size.
#ifndef SAMPLE_TEXTURE_BY_RESULUTION_AND_VEHICLE_SIZE_INCLUDED
#define SAMPLE_TEXTURE_BY_RESULUTION_AND_VEHICLE_SIZE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

void SampleTextureByResulutionAndVehicleSize_float(
    float3 PositionOS,
    float3 VehicleSize,
    float2 TexResolution,
    float VoxelScale,
    float VoxelHeightScale,
    UnityTexture2D DamageTex,
    out float3 DamageColor,
    out float VoxelMask,
    out float2 CellUV)
{
    float3 size = max(VehicleSize, float3(0.0001, 0.0001, 0.0001));
    float2 res = max(TexResolution, float2(1.0, 1.0));

    float2 uvVehicle;
    uvVehicle.x = (PositionOS.x + size.x * 0.5) / size.x;
    uvVehicle.y = (PositionOS.z + size.z * 0.5) / size.z;

    float2 inside = step(0.0, uvVehicle) * step(uvVehicle, 1.0);
    float insideMask = inside.x * inside.y;

    float2 cellIndex = floor(uvVehicle * res);
    CellUV = (cellIndex + 0.5) / res;
    DamageColor = DamageTex.SampleLevel(DamageTex.samplerstate, CellUV, 0).rgb;

    float2 local = frac(uvVehicle * res) - 0.5;
    float2 maskXZ = step(abs(local), 0.5 * VoxelScale);

    float layerHeight = max(size.y / 3.0, 0.0001);
    float layerIndex = floor(PositionOS.y / layerHeight);
    float layerMask = step(0.0, layerIndex) * step(layerIndex, 2.0);
    float localY = frac(PositionOS.y / layerHeight) - 0.5;
    float maskY = step(abs(localY), 0.5 * VoxelHeightScale);

    VoxelMask = maskXZ.x * maskXZ.y * maskY * layerMask * insideMask;
}

void SampleTextureByResulutionAndVehicleSize_half(
    half3 PositionOS,
    half3 VehicleSize,
    half2 TexResolution,
    half VoxelScale,
    half VoxelHeightScale,
    UnityTexture2D DamageTex,
    out half3 DamageColor,
    out half VoxelMask,
    out half2 CellUV)
{
    half3 size = max(VehicleSize, half3(0.0001, 0.0001, 0.0001));
    half2 res = max(TexResolution, half2(1.0, 1.0));

    half2 uvVehicle;
    uvVehicle.x = (PositionOS.x + size.x * 0.5h) / size.x;
    uvVehicle.y = (PositionOS.z + size.z * 0.5h) / size.z;

    half2 inside = step(0.0h, uvVehicle) * step(uvVehicle, 1.0h);
    half insideMask = inside.x * inside.y;

    half2 cellIndex = floor(uvVehicle * res);
    CellUV = (cellIndex + 0.5h) / res;
    DamageColor = DamageTex.SampleLevel(DamageTex.samplerstate, CellUV, 0).rgb;

    half2 local = frac(uvVehicle * res) - 0.5h;
    half2 maskXZ = step(abs(local), 0.5h * VoxelScale);

    half layerHeight = max(size.y / 3.0h, 0.0001h);
    half layerIndex = floor(PositionOS.y / layerHeight);
    half layerMask = step(0.0h, layerIndex) * step(layerIndex, 2.0h);
    half localY = frac(PositionOS.y / layerHeight) - 0.5h;
    half maskY = step(abs(localY), 0.5h * VoxelHeightScale);

    VoxelMask = maskXZ.x * maskXZ.y * maskY * layerMask * insideMask;
}

#endif
