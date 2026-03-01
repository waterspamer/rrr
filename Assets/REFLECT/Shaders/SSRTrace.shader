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
            #pragma multi_compile_fragment _ _SSR_DEFERRED_INPUT
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            TEXTURE2D_X(_BlitTexture);
            TEXTURE2D_X_HALF(_GBuffer0);
            TEXTURE2D_X_HALF(_GBuffer1);
            TEXTURE2D_X_HALF(_GBuffer2);

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
            float _DeferredInputActive;

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
                float4 clipPos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                float2 uv = clipPos.xy / max(0.00001, clipPos.w);
                uv = uv * 0.5 + 0.5;
                if (_ProjectionParams.x < 0.0)
                    uv.y = 1.0 - uv.y;
                return uv;
            }

            float3 DecodeGBufferNormalWS(float3 packedNormal)
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                half2 remappedOctNormalWS = half2(Unpack888ToFloat2(packedNormal));
                half2 octNormalWS = remappedOctNormalWS * 2.0h - 1.0h;
                return normalize(half3(UnpackNormalOctQuadEncode(octNormalWS)));
            #else
                return normalize(packedNormal);
            #endif
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

                float3 n = normalize(cross(dy, dx));
                if (any(isnan(n)))
                    n = float3(0.0, 1.0, 0.0);
                return n;
            }

            float3 ReadSurfaceNormalVS(float2 uv, float3 centerVS, float3 viewDirVS)
            {
                float3 geom = ReconstructNormalVS(uv, centerVS);
                if (dot(geom, viewDirVS) < 0.0)
                    geom = -geom;

                float3 nWorld = 0.0;
            #if defined(_SSR_DEFERRED_INPUT)
                half4 g2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_LinearClamp, uv, 0);
                nWorld = DecodeGBufferNormalWS(g2.xyz);
            #else
                nWorld = SampleSceneNormals(uv);
            #endif

                float3 nView = normalize(TransformWorldToViewDir(nWorld, true));
                bool valid = dot(nView, nView) > 1e-4 && !any(isnan(nView));
                if (!valid)
                    return geom;

                if (dot(nView, viewDirVS) < 0.0)
                    nView = -nView;

                return dot(nView, geom) < -0.6 ? geom : nView;
            }

            float ReadGBufferSmoothness(float2 uv)
            {
            #if defined(_SSR_DEFERRED_INPUT)
                half4 g2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_LinearClamp, uv, 0);
                return saturate(g2.a);
            #else
                return 0.0;
            #endif
            }

            float ReadCameraNormalsSmoothness(float2 uv)
            {
                return saturate(SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_LinearClamp, uv).a);
            }

            float ReadSmoothness(float2 uv, float3 src)
            {
            #if defined(_SSR_DEFERRED_INPUT)
                float smoothGBuffer = ReadGBufferSmoothness(uv);
                float smoothNormals = ReadCameraNormalsSmoothness(uv);
                return max(smoothGBuffer, smoothNormals);
            #else
                float luma = dot(src, float3(0.299, 0.587, 0.114));
                return saturate(0.2 + luma * 0.25);
            #endif
            }

            float ReadSpecularStrength(float2 uv)
            {
            #if defined(_SSR_DEFERRED_INPUT)
                half4 g1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0);
                float spec = max(g1.r, max(g1.g, g1.b));
                // Keep a physically plausible baseline reflectance for dielectrics.
                return saturate(max(spec, 0.04));
            #else
                return 0.2;
            #endif
            }

            float ReadSpecularStrengthRaw(float2 uv)
            {
            #if defined(_SSR_DEFERRED_INPUT)
                half4 g1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0);
                return saturate(max(g1.r, max(g1.g, g1.b)));
            #else
                return 0.0;
            #endif
            }

            float3 ReadSpecularMaskDebug(float2 uv)
            {
            #if defined(_SSR_DEFERRED_INPUT)
                half4 g1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0);
                float specR = saturate(g1.r);
                float specG = saturate(g1.g);
                float specB = saturate(g1.b);
                return float3(specR, specG, specB);
            #else
                return 0.0.xxx;
            #endif
            }

            float3 ReadSpecularTint(float2 uv)
            {
            #if defined(_SSR_DEFERRED_INPUT)
                half4 g0 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, sampler_LinearClamp, uv, 0);
                half4 g1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0);
                float metallic = saturate(g1.r);
                // Avoid fully tinting reflections to near-black on dark metallic paints.
                return lerp(1.0.xxx, saturate(g0.rgb), metallic * 0.35);
            #else
                return 1.0.xxx;
            #endif
            }

            half4 SampleGBuffer0Raw(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, sampler_LinearClamp, uv, 0);
            }

            half4 SampleGBuffer1Raw(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0);
            }

            half4 SampleGBuffer2Raw(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_LinearClamp, uv, 0);
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
                float lod = saturate(roughness) * 2.0;

                float centerRaw = SampleSceneDepth(hitUv);
                float centerLin = 0.0;
                if (!IsInvalidDepth(centerRaw))
                {
                    float3 centerVS = ViewPosFromDepth(hitUv, centerRaw);
                    centerLin = -centerVS.z;
                }

                float3 accum = 0.0;
                float wsum = 0.0;

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

                UNITY_UNROLL
                for (int i = 0; i < 9; i++)
                {
                    float2 suv = clamp(hitUv + offsets[i] * o, 0.001, 0.999);
                    float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, suv, lod).rgb;
                    float w = weights[i];
                    if (centerLin > 0.0)
                    {
                        float raw = SampleSceneDepth(suv);
                        if (!IsInvalidDepth(raw))
                        {
                            float3 vs = ViewPosFromDepth(suv, raw);
                            float lin = -vs.z;
                            float dz = abs(lin - centerLin);
                            w *= exp2(-dz * 10.0);
                        }
                    }
                    accum += c * w;
                    wsum += w;
                }

                return accum / max(0.0001, wsum);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float3 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                int debugMode = (int)_DebugMode;

                if (debugMode == 15)
                    return float4(SampleGBuffer0Raw(uv).rgb, 1.0);
                if (debugMode == 16)
                    return float4(SampleGBuffer0Raw(uv).aaa, 1.0);
                if (debugMode == 17)
                    return float4(SampleGBuffer1Raw(uv).rgb, 1.0);
                if (debugMode == 18)
                    return float4(SampleGBuffer1Raw(uv).aaa, 1.0);
                if (debugMode == 19)
                    return float4(SampleGBuffer2Raw(uv).rgb, 1.0);
                if (debugMode == 20)
                    return float4(SampleGBuffer2Raw(uv).aaa, 1.0);
                if (debugMode == 21)
                {
                    float3 n = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_LinearClamp, uv).rgb;
                    return float4(n, 1.0);
                }
                if (debugMode == 22)
                {
                    float a = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_LinearClamp, uv).a;
                    return float4(a.xxx, 1.0);
                }
                if (debugMode == 26)
                    return _DeferredInputActive > 0.5 ? float4(0.0, 1.0, 0.0, 1.0) : float4(1.0, 0.0, 0.0, 1.0);

                float rawDepth = SampleSceneDepth(uv);
                if (debugMode == 23)
                    return float4(rawDepth.xxx, 1.0);
                if (IsInvalidDepth(rawDepth))
                    return float4(src, 1.0);

                float3 viewPos = ViewPosFromDepth(uv, rawDepth);
                if (debugMode == 24)
                {
                    float linearDepthDbg = -viewPos.z;
                    float scaled = saturate(linearDepthDbg / max(0.001, _MaxDistance));
                    return float4(scaled.xxx, 1.0);
                }
                float3 viewDirVS = normalize(-viewPos);
                float3 normalVS = ReadSurfaceNormalVS(uv, viewPos, viewDirVS);
                float smoothness = ReadSmoothness(uv, src);
                // Buffer smoothness in this project is visibly lower than material slider values.
                // Remap to keep SSR perceptually responsive on glossy paints.
                float smoothResponse = saturate(smoothness * 1.35 - 0.2);
                smoothResponse = max(smoothResponse, smoothness);
                float specStrengthRaw = ReadSpecularStrengthRaw(uv);
                float specStrength = ReadSpecularStrength(uv);
                // Boost low specular values (common for dielectric materials) while preserving range.
                float specResponse = saturate(sqrt(max(specStrength, 0.04)));
                float reflectionResponse = saturate(smoothResponse * lerp(0.25, 1.0, specResponse));
                float rawReflectMask = saturate(smoothness * specStrengthRaw);
                float gbufferSmoothness = ReadGBufferSmoothness(uv);
                float cameraNormalSmoothness = ReadCameraNormalsSmoothness(uv);
                float materialReflectivity = saturate(max(specResponse, max(smoothResponse * 0.25, cameraNormalSmoothness * 0.25)));

                if (debugMode == 1)
                    return float4(normalVS * 0.5 + 0.5, 1.0);
                if (debugMode == 2)
                    return float4(viewDirVS * 0.5 + 0.5, 1.0);
                if (debugMode == 7)
                    return float4(smoothness.xxx, 1.0);
                if (debugMode == 8)
                    return float4(specStrength.xxx, 1.0);
                if (debugMode == 9)
                {
                    float3 specMask = ReadSpecularMaskDebug(uv);
                    return float4(specMask, 1.0);
                }
                if (debugMode == 10)
                    return float4(materialReflectivity.xxx, 1.0);
                if (debugMode == 11)
                    return float4(cameraNormalSmoothness.xxx, 1.0);
                if (debugMode == 12)
                    return float4(gbufferSmoothness.xxx, 1.0);
                if (debugMode == 13)
                    return float4(abs(gbufferSmoothness - cameraNormalSmoothness).xxx, 1.0);
                if (debugMode == 14)
                    return float4(smoothness.xxx, 1.0);
                if (debugMode == 27)
                    return float4(reflectionResponse.xxx, 1.0);
                if (debugMode == 28)
                    return float4(specResponse.xxx, 1.0);
                if (debugMode == 29)
                    return float4(rawReflectMask.xxx, 1.0);
                if (debugMode == 30)
                    return float4(pow(rawReflectMask.xxx, 1.0 / 2.2), 1.0);
                if (debugMode == 31)
                    return float4(specStrengthRaw.xxx, 1.0);
                if (debugMode == 25)
                {
                    float2 localUv = frac(uv * 2.0);
                    if (uv.x < 0.5 && uv.y >= 0.5)
                        return float4(SampleGBuffer0Raw(localUv).rgb, 1.0);
                    if (uv.x >= 0.5 && uv.y >= 0.5)
                        return float4(SampleGBuffer1Raw(localUv).rgb, 1.0);
                    if (uv.x < 0.5 && uv.y < 0.5)
                        return float4(SampleGBuffer2Raw(localUv).aaa, 1.0);
                    return float4(SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_LinearClamp, localUv).aaa, 1.0);
                }

                float3 rayDirVS = normalize(reflect(-viewDirVS, normalVS));
                if (debugMode == 3)
                    return float4(rayDirVS * 0.5 + 0.5, 1.0);

                if (rayDirVS.z >= -1e-4 || smoothResponse <= 1e-4)
                    return float4(src, 1.0);

                float quality = saturate(_TraceQuality);
                float stepBase = _RayStep * lerp(1.7, 0.85, quality);
                float travel = stepBase * (0.35 + Bayer4x4(uv) * 0.3);
                float3 startPosVS = viewPos + normalVS * _SurfaceBias;

                float3 prevPosVS = startPosVS;
                float3 currPosVS = startPosVS;
                float prevSignedDelta = 1e6;
                float lastDepthDelta = 1e6;
                float hitTravel = 0.0;
                float2 hitUv = 0.0;
                bool hit = false;

                UNITY_LOOP
                for (int i = 0; i < (int)_MaxSteps; i++)
                {
                    float step = stepBase * (1.0 + i * 0.02);
                    travel += step;
                    currPosVS = startPosVS + rayDirVS * travel;

                    float linearDepth = -currPosVS.z;
                    if (linearDepth <= 0.0 || linearDepth > _MaxDistance)
                        break;

                    float2 rayUv = ProjectToUv(currPosVS);
                    if (IsOutsideScreen(rayUv))
                        break;

                    float sceneRaw = SampleSceneDepth(rayUv);
                    if (IsInvalidDepth(sceneRaw))
                    {
                        prevPosVS = currPosVS;
                        continue;
                    }

                    float3 sceneVS = ViewPosFromDepth(rayUv, sceneRaw);
                    float signedDelta = currPosVS.z - sceneVS.z;
                    lastDepthDelta = signedDelta;

                    float thickness = _Thickness * lerp(1.0, 1.6, saturate(linearDepth / max(0.001, _MaxDistance)));
                    bool crossed = (prevSignedDelta > 0.0 && signedDelta <= 0.0);
                    bool validCross = crossed && (abs(prevSignedDelta) <= thickness * 8.0 || abs(signedDelta) <= thickness * 4.0);
                    if (validCross)
                    {
                        float3 low = prevPosVS;
                        float3 high = currPosVS;

                        UNITY_LOOP
                        for (int j = 0; j < (int)_BinarySteps; j++)
                        {
                            float3 mid = (low + high) * 0.5;
                            float2 midUv = ProjectToUv(mid);
                            if (IsOutsideScreen(midUv))
                            {
                                high = mid;
                                continue;
                            }
                            float midRaw = SampleSceneDepth(midUv);
                            if (IsInvalidDepth(midRaw))
                            {
                                low = mid;
                                continue;
                            }

                            float3 midSceneVS = ViewPosFromDepth(midUv, midRaw);
                            float midDelta = mid.z - midSceneVS.z;
                            if (midDelta > 0.0)
                                low = mid;
                            else
                                high = mid;
                        }

                        float3 refined = (low + high) * 0.5;
                        hitUv = ProjectToUv(refined);
                        hit = !IsOutsideScreen(hitUv);
                        hitTravel = distance(startPosVS, refined);
                        // Reject near-origin intersections that typically produce "streak" artifacts.
                        if (hitTravel < max(_SurfaceBias * 12.0, _RayStep * 1.5))
                            hit = false;
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
                    if (debugMode == 5)
                        return float4(0.0, 0.0, 0.0, 1.0);
                    return float4(_DebugReflectionOnly > 0.5 ? 0.0.xxx : src, 1.0);
                }

                if (debugMode == 4)
                    return float4(hitUv, 0.0, 1.0);
                if (debugMode == 5)
                    return float4(1.0, 1.0, 1.0, 1.0);

                float rough = saturate(1.0 - smoothness);
                float3 reflected = SampleReflectionFiltered(hitUv, _ResolveRadius * rough * 1.8, rough);
                float3 specTint = ReadSpecularTint(uv);
                reflected *= specTint;

                float nDotV = saturate(dot(normalVS, viewDirVS));
                float fresnel = lerp(1.0, 0.06 + 0.94 * pow(1.0 - nDotV, 5.0), saturate(_FresnelFade));

                float fadeStart = saturate(_FadeDistance / max(0.001, _MaxDistance)) * _MaxDistance;
                float distRange = max(0.001, _MaxDistance - fadeStart);
                float travelAtten = 1.0 - saturate((hitTravel - fadeStart) / distRange);

                float border = min(min(hitUv.x, 1.0 - hitUv.x), min(hitUv.y, 1.0 - hitUv.y));
                float edge = saturate(border / lerp(0.02, 0.12, rough));
                float confidence = 1.0 - saturate(abs(lastDepthDelta) / max(0.001, _Thickness * max(0.5, _MissFade)));

                // Reject only strongly backfacing hits; avoid aggressive cutouts on valid grazing reflections.
                float hitRaw = SampleSceneDepth(hitUv);
                if (!IsInvalidDepth(hitRaw))
                {
                    float3 hitVS = ViewPosFromDepth(hitUv, hitRaw);
                    float3 hitViewDirVS = normalize(-hitVS);
                    float3 hitNormalVS = ReadSurfaceNormalVS(hitUv, hitVS, hitViewDirVS);
                    if (dot(hitNormalVS, -rayDirVS) <= -0.2)
                        confidence = 0.0;
                }

                float alpha = smoothResponse;
                alpha *= fresnel;
                alpha *= edge;
                alpha *= saturate(0.3 + 0.7 * travelAtten);
                alpha *= saturate(0.35 + 0.65 * confidence);
                alpha *= saturate(lerp(0.3, 1.0, reflectionResponse));
                alpha *= saturate(lerp(0.6, 1.0, materialReflectivity));
                alpha = saturate(alpha);
                if (alpha < 0.005)
                    return float4(_DebugReflectionOnly > 0.5 ? 0.0.xxx : src, 1.0);

                float reflectionGain = lerp(1.0, 3.8, saturate(pow(reflectionResponse, 0.6)));
                reflectionGain *= lerp(0.8, 1.4, specResponse);
                reflectionGain *= lerp(0.9, 1.2, materialReflectivity);
                float3 reflectedFinal = reflected * reflectionGain;

                float3 outColor = _DebugReflectionOnly > 0.5 ? reflectedFinal * alpha : src + reflectedFinal * alpha;
                return float4(outColor, 1.0);
            }
            ENDHLSL
        }
    }
}
