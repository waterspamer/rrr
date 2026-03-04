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
            TEXTURE2D_X_HALF(_GBuffer0); // baseColor + materialFlags  (RT0) :contentReference[oaicite:5]{index=5}
            TEXTURE2D_X_HALF(_GBuffer1); // specular/reflectivity + occlusion (RT1) :contentReference[oaicite:6]{index=6}
            TEXTURE2D_X_HALF(_GBuffer2); // normal + smoothness (RT2) :contentReference[oaicite:7]{index=7}
            TEXTURE2D_X(_HiZPyramid);

            float _MaxDistance;
            float _RayStep;
            float _MaxSteps;
            float _BinarySteps;
            float _Thickness;
            float _DepthCrossTolerance;
            float _MinHitDistance;
            float _UseHierarchicalTraversal;
            float _UseDualLayerThickness;
            float _DualLayerRadius;
            float _MissFade;
            float _TraceQuality;
            float _SurfaceBias;
            float _FadeDistance;
            float _FresnelFade;
            float _ReflectionIntensity;
            float _ResolveRadius;
            float _DebugReflectionOnly;
            float _DebugMode;
            float _DeferredInputActive;
            float _HiZMipCount;

            // URP MaterialFlags bits (Unity docs): :contentReference[oaicite:8]{index=8}
            static const uint kMaterialFlagReceiveShadowsOff      = 1u;  // bit0
            static const uint kMaterialFlagSpecularHighlightsOff  = 2u;  // bit1
            static const uint kMaterialFlagSubtractiveMixedLight  = 4u;  // bit2
            static const uint kMaterialFlagSpecularSetup          = 8u;  // bit3 (Specular workflow)

            static const float kDielectricF0 = 0.04;
            static const float kOneMinusDielectricF0 = 1.0 - kDielectricF0;

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


            float Max3(float3 v)
            {
                return max(v.x, max(v.y, v.z));
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

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            bool LinearDepthAtUv(float2 uv, out float linearDepth)
            {
                float raw = SampleSceneDepth(uv);
                if (IsInvalidDepth(raw))
                {
                    linearDepth = 0.0;
                    return false;
                }

                float3 vs = ViewPosFromDepth(uv, raw);
                linearDepth = -vs.z;
                return true;
            }

            bool HiZLinearDepthAtUv(float2 uv, float mip, out float linearDepth)
            {
                float raw = SAMPLE_TEXTURE2D_X_LOD(_HiZPyramid, sampler_PointClamp, uv, mip).r;
                if (IsInvalidDepth(raw))
                {
                    linearDepth = 0.0;
                    return false;
                }

                float3 vs = ViewPosFromDepth(uv, raw);
                linearDepth = -vs.z;
                return true;
            }

            bool ComputeDepthBand(float2 uv, float radiusPx, out float nearDepth, out float farDepth)
            {
                float2 texel = 1.0 / _ScreenSize.xy;
                float2 o = texel * max(0.5, radiusPx);

                float2 taps[5] =
                {
                    float2(0.0, 0.0),
                    float2(1.0, 0.0),
                    float2(-1.0, 0.0),
                    float2(0.0, 1.0),
                    float2(0.0, -1.0)
                };

                nearDepth = 1e20;
                farDepth = 0.0;
                bool anyValid = false;

                UNITY_UNROLL
                for (int i = 0; i < 5; i++)
                {
                    float2 suv = clamp(uv + taps[i] * o, 0.001, 0.999);
                    float d;
                    if (!LinearDepthAtUv(suv, d))
                        continue;
                    anyValid = true;
                    nearDepth = min(nearDepth, d);
                    farDepth = max(farDepth, d);
                }

                return anyValid;
            }

            // --- PBR material decode (URP-consistent) ---
            // URP GBuffer layout + flags described in docs. :contentReference[oaicite:9]{index=9}
            void ReadMaterialPBR(float2 uv,
                                 out float3 baseColor,
                                 out float3 F0,
                                 out float  smoothness,
                                 out float  occlusion,
                                 out float  reflectivity,
                                 out uint   materialFlags,
                                 out float  smoothnessG,
                                 out float  smoothnessN)
            {
                // Normals texture alpha in URP часто содержит smoothness для forward-only / depth-normals prepass.
                smoothnessN = saturate(SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_LinearClamp, uv).a);

            #if defined(_SSR_DEFERRED_INPUT)
                half4 g0 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, sampler_LinearClamp, uv, 0);
                half4 g1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0);
                half4 g2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_LinearClamp, uv, 0);

                baseColor = saturate(g0.rgb);
                materialFlags = (uint)(saturate(g0.a) * 255.0 + 0.5);
                occlusion = saturate(g1.a);
                smoothnessG = saturate(g2.a);

                // Deferred path: authoritative smoothness is from GBuffer2.a.
                // Normals alpha may contain unrelated data depending on path/feature setup.
                smoothness = smoothnessG;

                bool isSpecularWorkflow = (materialFlags & kMaterialFlagSpecularSetup) != 0u;

                if (isSpecularWorkflow)
                {
                    // Specular workflow: g1.rgb = specular color (F0). :contentReference[oaicite:10]{index=10}
                    F0 = saturate(g1.rgb);
                    reflectivity = Max3(F0);
                }
                else
                {
                    // Metallic workflow: в specular поле хранится reflectivity в 8 бит (используем R),
                    // metallic восстанавливается как MetallicFromReflectivity (URP BRDF). :contentReference[oaicite:11]{index=11}
                    reflectivity = saturate(g1.r);
                    float metallic = saturate((reflectivity - kDielectricF0) / kOneMinusDielectricF0);
                    F0 = lerp(kDielectricF0.xxx, baseColor, metallic);
                }
            #else
                // Forward fallback: без отдельного material buffer невозможно восстановить metallic/specular корректно.
                baseColor = 0.0.xxx;
                materialFlags = 0u;
                occlusion = 1.0;
                smoothnessG = 0.0;
                smoothness = smoothnessN;
                reflectivity = kDielectricF0;
                F0 = kDielectricF0.xxx;
            #endif
            }

            // URP-style environment specular BRDF factor (см. EnvironmentBRDFSpecular). :contentReference[oaicite:12]{index=12}
            float3 EnvironmentBRDFSpecularFactor(float3 F0, float smoothness, float reflectivity, float NoV)
            {
                float perceptualRoughness = saturate(1.0 - smoothness);
                float roughness = max(perceptualRoughness * perceptualRoughness, 1e-4);
                float roughness2 = max(roughness * roughness, 1e-8);

                float surfaceReduction = 1.0 / (roughness2 + 1.0);
                float grazingTerm = saturate(smoothness + reflectivity);
                float fresnelTerm = Pow4(1.0 - NoV); // URP GI fresnel term uses Pow4 approximation.

                float3 noFresnel = surfaceReduction * F0;
                float3 full = surfaceReduction * lerp(F0, grazingTerm.xxx, fresnelTerm);

                // _FresnelFade теперь становится честным "0 = без fresnel, 1 = как URP"
                return lerp(noFresnel, full, saturate(_FresnelFade));
            }

            float3 SampleReflectionFiltered(float2 hitUv, float radiusPx, float perceptualRoughness)
            {
                float2 texel = 1.0 / _ScreenSize.xy;
                float2 o = texel * max(0.0, radiusPx);

                // LOD — это не “идеальный prefilter”, но это хотя бы монотонная функция roughness.
                float lod = saturate(perceptualRoughness) * 2.0;

                float centerRaw = SampleSceneDepth(hitUv);
                float centerLin = 0.0;
                if (!IsInvalidDepth(centerRaw))
                {
                    float3 centerVS = ViewPosFromDepth(hitUv, centerRaw);
                    centerLin = -centerVS.z;
                }

                float3 accum = 0.0;
                float wsum = 0.0;
                float2 pix = hitUv * _ScreenSize.xy;
                float noise = Hash12(pix + _Time.yy * 60.0);
                float angle = noise * 6.2831853;
                float s = sin(angle);
                float c = cos(angle);

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
                    float2 r = offsets[i];
                    float2 rotated = float2(r.x * c - r.y * s, r.x * s + r.y * c);
                    float2 suv = clamp(hitUv + rotated * o, 0.001, 0.999);
                    float3 sampleColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, suv, lod).rgb;
                    float w = weights[i];

                    // depth-aware weighting чтобы не смешивать разные поверхности
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

                    accum += sampleColor * w;
                    wsum += w;
                }

                return accum / max(0.0001, wsum);
            }

            half4 SampleGBuffer0Raw(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, sampler_LinearClamp, uv, 0); }
            half4 SampleGBuffer1Raw(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0); }
            half4 SampleGBuffer2Raw(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_LinearClamp, uv, 0); }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float3 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                int debugMode = (int)_DebugMode;

                // ---- Debug: raw buffers ----
                if (debugMode == 15) return float4(SampleGBuffer0Raw(uv).rgb, 1.0);
                if (debugMode == 16) return float4(SampleGBuffer0Raw(uv).aaa, 1.0);
                if (debugMode == 17) return float4(SampleGBuffer1Raw(uv).rgb, 1.0);
                if (debugMode == 18) return float4(SampleGBuffer1Raw(uv).aaa, 1.0);
                if (debugMode == 19) return float4(SampleGBuffer2Raw(uv).rgb, 1.0);
                if (debugMode == 20) return float4(SampleGBuffer2Raw(uv).aaa, 1.0);
                if (debugMode == 21) { float3 n = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_LinearClamp, uv).rgb; return float4(n, 1.0); }
                if (debugMode == 22) { float a = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_LinearClamp, uv).a; return float4(a.xxx, 1.0); }
                if (debugMode == 26) return _DeferredInputActive > 0.5 ? float4(0.0, 1.0, 0.0, 1.0) : float4(1.0, 0.0, 0.0, 1.0);

                float rawDepth = SampleSceneDepth(uv);
                if (debugMode == 23) return float4(rawDepth.xxx, 1.0);
                if (debugMode == 32)
                {
                    float d0;
                    if (HiZLinearDepthAtUv(uv, 0.0, d0))
                        return float4(saturate(d0 / max(0.001, _MaxDistance)).xxx, 1.0);
                    return float4(0.0, 0.0, 0.0, 1.0);
                }
                if (debugMode == 33)
                {
                    float coarseMip = max(0.0, _HiZMipCount - 1.0);
                    float dc;
                    if (HiZLinearDepthAtUv(uv, coarseMip, dc))
                        return float4(saturate(dc / max(0.001, _MaxDistance)).xxx, 1.0);
                    return float4(0.0, 0.0, 0.0, 1.0);
                }
                if (IsInvalidDepth(rawDepth)) return float4(src, 1.0);

                float3 viewPos = ViewPosFromDepth(uv, rawDepth);
                if (debugMode == 24)
                {
                    float linearDepthDbg = -viewPos.z;
                    float scaled = saturate(linearDepthDbg / max(0.001, _MaxDistance));
                    return float4(scaled.xxx, 1.0);
                }

                float3 viewDirVS = normalize(-viewPos);
                float3 normalVS = ReadSurfaceNormalVS(uv, viewPos, viewDirVS);

                // ---- PBR material decode ----
                float3 baseColor;
                float3 F0;
                float smoothness;
                float occlusion;
                float reflectivity;
                uint materialFlags;
                float smoothnessG, smoothnessN;
                ReadMaterialPBR(uv, baseColor, F0, smoothness, occlusion, reflectivity, materialFlags, smoothnessG, smoothnessN);

                bool specularOff = (materialFlags & kMaterialFlagSpecularHighlightsOff) != 0u;
                if (specularOff)
                    return float4(src, 1.0);

                if (debugMode == 1) return float4(normalVS * 0.5 + 0.5, 1.0);
                if (debugMode == 2) return float4(viewDirVS * 0.5 + 0.5, 1.0);
                if (debugMode == 7) return float4(smoothness.xxx, 1.0);
                if (debugMode == 8) return float4(reflectivity.xxx, 1.0);
                if (debugMode == 9) return float4(F0, 1.0);
                if (debugMode == 10) { float grazing = saturate(smoothness + reflectivity); return float4(grazing.xxx, 1.0); }
                if (debugMode == 11) return float4(smoothnessN.xxx, 1.0);
                if (debugMode == 12) return float4(smoothnessG.xxx, 1.0);
                if (debugMode == 13) return float4(abs(smoothnessG - smoothnessN).xxx, 1.0);
                if (debugMode == 14) { float pr = 1.0 - smoothness; return float4(pr.xxx, 1.0); }

                float3 rayDirVS = normalize(reflect(-viewDirVS, normalVS));
                if (debugMode == 3) return float4(rayDirVS * 0.5 + 0.5, 1.0);

                // SSR имеет смысл только если луч “идёт в экран” и есть хоть какая-то гладкость
                if (rayDirVS.z >= -1e-4 || smoothness <= 1e-4)
                    return float4(src, 1.0);

                float quality = saturate(_TraceQuality);
                float stepBase = _RayStep * lerp(1.7, 0.85, quality);
                float pixelNoise = Hash12(uv * _ScreenSize.xy + _Time.yy * 60.0);
                float travel = stepBase * (0.2 + pixelNoise * 0.8);
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
                    float seq = frac(pixelNoise + (float)i * 0.61803398875);
                    step *= lerp(0.85, 1.15, seq);
                    if (_UseHierarchicalTraversal > 0.5 && _HiZMipCount > 1.0)
                    {
                        float maxMip = max(0.0, _HiZMipCount - 1.0);
                        float targetMip = saturate((float)i / max(1.0, _MaxSteps)) * maxMip;

                        float2 probeUv = ProjectToUv(startPosVS + rayDirVS * (travel + step));
                        float hizLinear;
                        if (!IsOutsideScreen(probeUv) && HiZLinearDepthAtUv(probeUv, targetMip, hizLinear))
                        {
                            float probeLinear = -(startPosVS + rayDirVS * (travel + step)).z;
                            float depthGap = hizLinear - probeLinear;
                            float farFromSurface = saturate(depthGap / max(0.001, _Thickness * 2.0));
                            float nearSurface = saturate(1.0 - abs(depthGap) / max(0.001, _Thickness * 3.0));

                            // Conservative hierarchy usage: accelerate in empty space but avoid overskipping.
                            step *= lerp(0.8, 2.0, farFromSurface);
                            step *= lerp(1.0, 0.75, nearSurface);
                        }
                    }
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

                    // NOTE: do not reject candidates by coarse Hi-Z depth here.
                    // Conservative depth pyramids can be too coarse near silhouettes and cause false misses.

                    float thickness = _Thickness * lerp(1.0, 1.6, saturate(linearDepth / max(0.001, _MaxDistance)));
                    bool crossed = (prevSignedDelta > 0.0 && signedDelta <= 0.0);
                    float crossTol = max(0.5, _DepthCrossTolerance);
                    bool validCross = crossed && (abs(prevSignedDelta) <= thickness * (crossTol * 3.0) || abs(signedDelta) <= thickness * crossTol);
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
                            if (midDelta > 0.0) low = mid;
                            else high = mid;
                        }

                        float3 refined = (low + high) * 0.5;
                        hitUv = ProjectToUv(refined);
                        hit = !IsOutsideScreen(hitUv);
                        hitTravel = distance(startPosVS, refined);
                        if (hitTravel < max(max(_SurfaceBias * 12.0, _RayStep * 1.5), _MinHitDistance))
                            hit = false;

                        if (hit && _UseDualLayerThickness > 0.5)
                        {
                            float nearBand, farBand;
                            if (ComputeDepthBand(hitUv, _DualLayerRadius, nearBand, farBand))
                            {
                                float refinedLinear = -refined.z;
                                float frontDist = abs(refinedLinear - nearBand);
                                float backDist = abs(refinedLinear - farBand);
                                float layerTol = thickness * (crossTol * 1.5);
                                bool nearLayerHit = frontDist <= layerTol;
                                bool backLayerHit = backDist <= layerTol && (farBand - nearBand) > layerTol;
                                hit = nearLayerHit || backLayerHit;
                            }
                            else
                            {
                                hit = false;
                            }
                        }
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

                if (debugMode == 4) return float4(hitUv, 0.0, 1.0);
                if (debugMode == 5) return float4(1.0, 1.0, 1.0, 1.0);

                float perceptualRoughness = saturate(1.0 - smoothness);
                float roughness = max(perceptualRoughness * perceptualRoughness, 1e-4);
                float3 ssrRadiance = SampleReflectionFiltered(hitUv, _ResolveRadius * perceptualRoughness * 1.8, perceptualRoughness);

                float NoV = saturate(dot(normalVS, viewDirVS));
                float3 envSpec = EnvironmentBRDFSpecularFactor(F0, smoothness, reflectivity, NoV);

                // Optional: match URP indirect occlusion on specular (gbuffer1.a). :contentReference[oaicite:13]{index=13}
                float3 ssrSpec = ssrRadiance * envSpec * occlusion;

                float fadeStart = saturate(_FadeDistance / max(0.001, _MaxDistance)) * _MaxDistance;
                float distRange = max(0.001, _MaxDistance - fadeStart);
                float travelAtten = 1.0 - saturate((hitTravel - fadeStart) / distRange);

                float border = min(min(hitUv.x, 1.0 - hitUv.x), min(hitUv.y, 1.0 - hitUv.y));
                float edge = saturate(border / lerp(0.02, 0.12, perceptualRoughness));
                float confidence = 1.0 - saturate(abs(lastDepthDelta) / max(0.001, _Thickness * max(0.5, _MissFade)));

                // Reject hits on backfacing geometry at the reflection sample to reduce streak artifacts.
                float hitRaw = SampleSceneDepth(hitUv);
                if (!IsInvalidDepth(hitRaw))
                {
                    float3 hitVS = ViewPosFromDepth(hitUv, hitRaw);
                    float3 hitViewDirVS = normalize(-hitVS);
                    float3 hitNormalVS = ReadSurfaceNormalVS(hitUv, hitVS, hitViewDirVS);
                    if (dot(hitNormalVS, -rayDirVS) <= 0.0)
                        confidence = 0.0;
                }

                // Base SSR validity (screen-space confidence).
                float alpha = edge;
                alpha *= saturate(0.3 + 0.7 * travelAtten);
                alpha *= saturate(0.35 + 0.65 * confidence);

                // Material response weight (physically-motivated):
                // stronger for high F0, grazing angles and smoother surfaces.
                float F0Max = Max3(F0);
                float fresnel = Pow4(1.0 - NoV);
                float fresnelWeight = saturate(lerp(F0Max, 1.0, fresnel));
                float smoothWeight = saturate(1.0 - roughness);
                float materialWeight = saturate(fresnelWeight * lerp(0.2, 1.0, smoothWeight));

                alpha *= materialWeight;
                alpha = saturate(alpha);

                if (debugMode == 27) return float4(envSpec, 1.0);
                if (debugMode == 28) return float4(Pow4(1.0 - NoV).xxx, 1.0);
                if (debugMode == 29) return float4(occlusion.xxx, 1.0);
                if (debugMode == 30) return float4(alpha.xxx, 1.0);
                if (debugMode == 31) return float4(((materialFlags & kMaterialFlagSpecularSetup) != 0u ? 1.0 : 0.0).xxx, 1.0);

                if (alpha < 0.005)
                    return float4(_DebugReflectionOnly > 0.5 ? 0.0.xxx : src, 1.0);

                // Energy-aware composite:
                // replace an approximation of existing indirect spec in src with SSR, not pure additive boost.
                float3 approxIndirectSpecInSrc = src * (fresnelWeight * occlusion * lerp(0.3, 0.8, smoothWeight));
                float3 ssrDelta = ssrSpec - approxIndirectSpecInSrc;

                float3 outColor = (_DebugReflectionOnly > 0.5)
                    ? (ssrSpec * alpha * _ReflectionIntensity)
                    : max(0.0, src + ssrDelta * alpha * _ReflectionIntensity);

                return float4(outColor, 1.0);
            }
            ENDHLSL
        }
    }
}
