Shader "Hidden/RRR/SSRTrace"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SSR Trace"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            TEXTURE2D_X(_BlitTexture);
            TEXTURE2D_X_HALF(_GBuffer0); // albedo.rgb + materialFlags.a
            TEXTURE2D_X_HALF(_GBuffer1); // metallic/specular + occlusion
            TEXTURE2D_X_HALF(_GBuffer2); // normal + smoothness.a

            float _MaxDistance;
            float _RayStep;
            float _MaxSteps;
            float _BinarySteps;
            float _Thickness;
            float _MissFade;
            float _TraceQuality;
            float _SurfaceBias;
            float _FadeDistance;
            float _FresnelFade;
            float _ResolveRadius;
            float _DebugReflectionOnly;
            float _DebugMode;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            bool IsInvalidDepth(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth <= 0.00001;
            #else
                return rawDepth >= 0.99999;
            #endif
            }

            bool IsOutsideScreen(float2 uv)
            {
                return uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0;
            }

            float3 ViewPosFromDepth(float2 uv, float rawDepth)
            {
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                return TransformWorldToView(worldPos);
            }

            float2 ProjectToUv(float3 viewPos)
            {
                float4 clip = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                float2 uv = clip.xy / max(clip.w, 0.00001) * 0.5 + 0.5;
                if (_ProjectionParams.x < 0.0)
                    uv.y = 1.0 - uv.y;
                return uv;
            }

            float3 ReconstructNormalVS(float2 uv, float3 centerVS)
            {
                float2 texel = 1.0 / _ScreenSize.xy;
                float2 uvL = clamp(uv + float2(-texel.x, 0.0), 0.0, 1.0);
                float2 uvR = clamp(uv + float2(texel.x, 0.0), 0.0, 1.0);
                float2 uvD = clamp(uv + float2(0.0, -texel.y), 0.0, 1.0);
                float2 uvU = clamp(uv + float2(0.0, texel.y), 0.0, 1.0);

                float rawL = SampleSceneDepth(uvL);
                float rawR = SampleSceneDepth(uvR);
                float rawD = SampleSceneDepth(uvD);
                float rawU = SampleSceneDepth(uvU);

                float3 posL = IsInvalidDepth(rawL) ? centerVS : ViewPosFromDepth(uvL, rawL);
                float3 posR = IsInvalidDepth(rawR) ? centerVS : ViewPosFromDepth(uvR, rawR);
                float3 posD = IsInvalidDepth(rawD) ? centerVS : ViewPosFromDepth(uvD, rawD);
                float3 posU = IsInvalidDepth(rawU) ? centerVS : ViewPosFromDepth(uvU, rawU);

                float3 dx = abs(posR.z - centerVS.z) < abs(centerVS.z - posL.z) ? (posR - centerVS) : (centerVS - posL);
                float3 dy = abs(posU.z - centerVS.z) < abs(centerVS.z - posD.z) ? (posU - centerVS) : (centerVS - posD);

                float3 normal = normalize(cross(dx, dy));
                if (any(isnan(normal)))
                    normal = float3(0.0, 1.0, 0.0);
                return normal;
            }

            float3 GetSurfaceNormalVS(float2 uv, float3 centerVS)
            {
                float2 texel = 1.0 / _ScreenSize.xy;
                float3 n0 = SampleSceneNormals(uv);
                float3 n1 = SampleSceneNormals(clamp(uv + float2(texel.x, 0.0), 0.0, 1.0));
                float3 n2 = SampleSceneNormals(clamp(uv + float2(-texel.x, 0.0), 0.0, 1.0));
                float3 n3 = SampleSceneNormals(clamp(uv + float2(0.0, texel.y), 0.0, 1.0));
                float3 n4 = SampleSceneNormals(clamp(uv + float2(0.0, -texel.y), 0.0, 1.0));
                float3 normalWS = normalize(n0 * 2.0 + n1 + n2 + n3 + n4);

                // Fallback when normal texture is unavailable/invalid.
                if (dot(normalWS, normalWS) < 1e-4 || any(isnan(normalWS)))
                    return ReconstructNormalVS(uv, centerVS);

                return normalize(TransformWorldToViewDir(normalWS, true));
            }

            float ReadSmoothness(float2 uv)
            {
                float4 n = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_LinearClamp, uv);
                float smoothFromNormals = saturate(n.a);
                half4 g2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_LinearClamp, uv, 0);
                float smoothFromGBuffer = saturate(g2.a);
                return max(smoothFromNormals, smoothFromGBuffer);
            }

            float3 ReadAlbedo(float2 uv)
            {
                half4 g0 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, sampler_LinearClamp, uv, 0);
                return saturate(g0.rgb);
            }

            float ReadMetallic(float2 uv)
            {
                half4 g1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0);
                return saturate(g1.r);
            }

            float3 EnvBRDFApprox(float3 f0, float roughness, float nDotV)
            {
                float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
                float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
                float4 r = roughness * c0 + c1;
                float a004 = min(r.x * r.x, exp2(-9.28 * nDotV)) * r.x + r.y;
                float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
                return f0 * ab.x + ab.y;
            }

            float InterleavedGradientNoise(float2 uv)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(uv, magic.xy)));
            }

            float Bayer4x4(float2 uv)
            {
                int2 p = int2(floor(uv * _ScreenSize.xy)) & 3;
                int idx = p.x + p.y * 4;
                // 0..15 / 16
                const float bayer[16] =
                {
                    0.0, 8.0, 2.0, 10.0,
                    12.0, 4.0, 14.0, 6.0,
                    3.0, 11.0, 1.0, 9.0,
                    15.0, 7.0, 13.0, 5.0
                };
                return (bayer[idx] + 0.5) / 16.0;
            }

            float3 SampleReflectionFiltered(float2 hitUv, float radiusPx, float roughness)
            {
                float2 texel = 1.0 / _ScreenSize.xy;
                float2 o = texel * max(0.0, radiusPx);
                float lod = saturate(roughness) * 3.0;

                float centerRaw = SampleSceneDepth(hitUv);
                float centerLin = 0.0;
                if (!IsInvalidDepth(centerRaw))
                {
                    float3 centerVS = ViewPosFromDepth(hitUv, centerRaw);
                    centerLin = -centerVS.z;
                }

                float2 offsets[9] =
                {
                    float2( 0.0,  0.0),
                    float2( 1.0,  0.0),
                    float2(-1.0,  0.0),
                    float2( 0.0,  1.0),
                    float2( 0.0, -1.0),
                    float2( 1.0,  1.0),
                    float2(-1.0,  1.0),
                    float2( 1.0, -1.0),
                    float2(-1.0, -1.0)
                };

                float weights[9] = { 4.0, 2.0, 2.0, 2.0, 2.0, 1.0, 1.0, 1.0, 1.0 };

                float3 accum = 0.0;
                float wsum = 0.0;

                UNITY_UNROLL
                for (int i = 0; i < 9; i++)
                {
                    float2 suv = hitUv + offsets[i] * o;
                    float w = weights[i];

                    // Depth-aware bilateral term to reduce noisy shimmering near edges.
                    if (centerLin > 0.0)
                    {
                        float raw = SampleSceneDepth(suv);
                        if (!IsInvalidDepth(raw))
                        {
                            float3 vs = ViewPosFromDepth(suv, raw);
                            float lin = -vs.z;
                            float dz = abs(lin - centerLin);
                            w *= exp2(-dz * 12.0);
                        }
                    }

                    float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, suv, lod).rgb;
                    accum += c * w;
                    wsum += w;
                }

                return accum / max(0.0001, wsum);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float3 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                float rawDepth = SampleSceneDepth(uv);
                if (IsInvalidDepth(rawDepth))
                    return float4(src, 1.0);

                float3 viewPos = ViewPosFromDepth(uv, rawDepth);
                float3 viewDirVS = normalize(viewPos);
                float3 normalVS = GetSurfaceNormalVS(uv, viewPos);
                if (dot(normalVS, -viewDirVS) < 0.0)
                    normalVS = -normalVS;
                float smoothness = ReadSmoothness(uv);
                float smoothMask = smoothness * smoothness;

                float3 rayDirVS = normalize(reflect(viewDirVS, normalVS));
                if (rayDirVS.z >= -0.0001 || smoothMask <= 0.0001)
                    return float4(src, 1.0);

                int debugMode = (int)_DebugMode;
                if (debugMode == 1)
                    return float4(normalVS * 0.5 + 0.5, 1.0);
                if (debugMode == 2)
                    return float4((-viewDirVS) * 0.5 + 0.5, 1.0);
                if (debugMode == 3)
                    return float4(rayDirVS * 0.5 + 0.5, 1.0);

                float3 startPosVS = viewPos + normalVS * _SurfaceBias;
                float3 prevPosVS = startPosVS;
                float3 currPosVS = prevPosVS;
                float hitTravel = 0.0;
                float2 hitUv = float2(0.0, 0.0);
                bool hit = false;
                bool usedSoftFallback = false;
                float softFallbackWeight = 1.0;
                bool hasClosest = false;
                float closestDelta = 1e6;
                float2 closestUv = uv;
                float closestTravel = 0.0;
                float lastDepthDelta = 0.0;
                float prevSignedDelta = 1e6;
                float dither = lerp(Bayer4x4(uv), InterleavedGradientNoise(uv + _Time.yy), 0.5);
                float quality = saturate(_TraceQuality);
                float jitterAmp = lerp(1.6, 0.2, quality);
                float stepJitter = lerp(0.65, 0.1, quality);
                float travel = _RayStep * dither * jitterAmp;

                UNITY_LOOP
                for (int i = 0; i < (int)_MaxSteps; i++)
                {
                    float perStepNoise = InterleavedGradientNoise(uv * (i + 1.0) + float2(i * 0.37, i * 0.71));
                    float stepScale = 1.0 + (perStepNoise - 0.5) * stepJitter;
                    float stepSize = _RayStep * (1.0 + i * 0.02) * stepScale;
                    travel += stepSize;
                    currPosVS = startPosVS + rayDirVS * travel;
                    float linearDepth = -currPosVS.z;
                    if (linearDepth <= 0.0)
                        break;

                    if (linearDepth > _MaxDistance)
                        break;

                    float2 rayUv = ProjectToUv(currPosVS);
                    if (IsOutsideScreen(rayUv))
                        break;

                    float sceneRawDepth = SampleSceneDepth(rayUv);
                    if (IsInvalidDepth(sceneRawDepth))
                    {
                        prevPosVS = currPosVS;
                        continue;
                    }

                    float3 sceneVS = ViewPosFromDepth(rayUv, sceneRawDepth);
                    float sceneLinearDepth = -sceneVS.z;
                    float signedDelta = sceneLinearDepth - linearDepth;
                    lastDepthDelta = signedDelta;
                    float thickness = _Thickness;
                    float absDelta = abs(signedDelta);
                    if (absDelta < closestDelta)
                    {
                        closestDelta = absDelta;
                        closestUv = rayUv;
                        closestTravel = distance(startPosVS, currPosVS);
                        hasClosest = true;
                    }

                    // Hit only when ray crosses scene depth from front to behind.
                    bool crossedSurface = prevSignedDelta > 0.0 && signedDelta <= 0.0;
                    if (crossedSurface && abs(signedDelta) <= thickness)
                    {
                        float3 low = prevPosVS;
                        float3 high = currPosVS;

                        UNITY_LOOP
                        for (int j = 0; j < (int)_BinarySteps; j++)
                        {
                            float3 mid = (low + high) * 0.5;
                            float2 midUv = ProjectToUv(mid);

                            float midRaw = SampleSceneDepth(midUv);
                            if (IsInvalidDepth(midRaw))
                            {
                                low = mid;
                                continue;
                            }

                            float3 midSceneVS = ViewPosFromDepth(midUv, midRaw);
                            float midLinear = -mid.z;
                            float midSignedDelta = (-midSceneVS.z) - midLinear;

                            if (midSignedDelta > 0.0)
                                low = mid;
                            else
                                high = mid;
                        }

                        float3 refined = (low + high) * 0.5;
                        hitUv = ProjectToUv(refined);
                        hitTravel = distance(startPosVS, refined);
                        hit = !IsOutsideScreen(hitUv);
                        break;
                    }

                    prevSignedDelta = signedDelta;
                    prevPosVS = currPosVS;
                }

                if (debugMode == 6)
                {
                    float d = saturate(lastDepthDelta * 2.0 + 0.5);
                    return float4(d, 1.0 - d, 0.0, 1.0);
                }

                if (!hit)
                {
                    float missSoft = 0.0;
                    if (hasClosest)
                    {
                        float softRange = max(0.0001, _Thickness * max(0.001, _MissFade));
                        missSoft = smoothstep(0.0, 1.0, saturate(1.0 - (closestDelta / softRange)));
                    }

                    if (debugMode == 5)
                        return float4(missSoft.xxx, 1.0);
                    if (missSoft <= 0.0001)
                        return float4(_DebugReflectionOnly > 0.5 ? 0.0.xxx : src, 1.0);

                    hitUv = closestUv;
                    hitTravel = closestTravel;
                    hit = true;
                    usedSoftFallback = true;
                    // Preserve reflection visibility while keeping soft boundary.
                    softFallbackWeight = sqrt(missSoft);
                }

                if (debugMode == 4)
                    return float4(hitUv, 0.0, 1.0);
                if (debugMode == 5)
                    return float4(1.0, 1.0, 1.0, 1.0);

                float rough = 1.0 - smoothness;
                float filterRadius = _ResolveRadius * (0.5 + rough * 1.5);
                float3 reflected = SampleReflectionFiltered(hitUv, filterRadius, rough);
                float3 albedo = ReadAlbedo(uv);
                float metallic = ReadMetallic(uv);
                float3 f0 = lerp(0.04.xxx, albedo, metallic);
                float nDotV = saturate(dot(normalVS, -viewDirVS));
                float3 envBrdf = EnvBRDFApprox(f0, max(0.02, rough), nDotV);
                float3 reflectedPbr = reflected * envBrdf;
                float fresnelTerm = pow(saturate(1.0 - nDotV), 5.0);
                float fresnelAtten = lerp(1.0, fresnelTerm, saturate(_FresnelFade));

                float fadeStart = saturate(_FadeDistance / max(0.001, _MaxDistance)) * _MaxDistance;
                float distRange = max(0.001, _MaxDistance - fadeStart);
                float travelAtten = 1.0 - saturate((hitTravel - fadeStart) / distRange);

                float border = min(min(hitUv.x, 1.0 - hitUv.x), min(hitUv.y, 1.0 - hitUv.y));
                float edgeWidth = lerp(0.01, 0.14, saturate(_FresnelFade));
                float edgeAtten = saturate(border / edgeWidth);
                float alpha = saturate(edgeAtten * travelAtten * fresnelAtten * smoothMask);
                if (usedSoftFallback)
                    alpha *= softFallbackWeight;

                float3 outColor = _DebugReflectionOnly > 0.5 ? reflectedPbr * alpha : src + reflectedPbr * alpha;
                return float4(outColor, 1.0);
            }
            ENDHLSL
        }
    }
}
