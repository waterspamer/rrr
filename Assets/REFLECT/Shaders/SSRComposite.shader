Shader "Hidden/RRR/SSRComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SSR Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma multi_compile_fragment _ _SSR_DEFERRED_INPUT
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_X(_BlitTexture);
            TEXTURE2D_X(_RrrSsrTexture);

            TEXTURE2D_X_HALF(_GBuffer0);
            TEXTURE2D_X_HALF(_GBuffer1);
            TEXTURE2D_X_HALF(_GBuffer2);

            float _DebugReflectionOnly;

            // MaterialFlags (URP): bit3 = SpecularSetup
            static const uint kMaterialFlagSpecularSetup = 8u;

            static const float kDielectricF0 = 0.04;
            static const float kOneMinusDielectricF0 = 1.0 - kDielectricF0;

            float MetallicFromReflectivity(float reflectivity)
            {
                // URP: metallic = (reflectivity - 0.04) / (1 - 0.04)
                return saturate((reflectivity - kDielectricF0) / kOneMinusDielectricF0);
            }

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionHCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }

            bool IsInvalidDepth(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth <= 0.00001;
            #else
                return rawDepth >= 0.99999;
            #endif
            }

            float3 DecodeGBufferNormalWS(float3 packedNormal)
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                half2 remapped = half2(Unpack888ToFloat2(packedNormal));
                half2 oct = remapped * 2.0h - 1.0h;
                return normalize(half3(UnpackNormalOctQuadEncode(oct)));
            #else
                return normalize(packedNormal);
            #endif
            }

            void ReadSurfaceFromGBuffer(float2 uv, out SurfaceData surf, out InputData inputData, out uint flags)
            {
                ZERO_INITIALIZE(SurfaceData, surf);
                ZERO_INITIALIZE(InputData, inputData);
                flags = 0u;

                float rawDepth = SampleSceneDepth(uv);
                if (IsInvalidDepth(rawDepth))
                    return;

                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                half4 g0 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, sampler_LinearClamp, uv, 0); // albedo + flags
                half4 g1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, sampler_LinearClamp, uv, 0); // spec/reflectivity + occlusion
                half4 g2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_LinearClamp, uv, 0); // normal + smoothness

                flags = (uint)(saturate(g0.a) * 255.0 + 0.5);

                float3 albedo = saturate(g0.rgb);
                float occlusion = saturate(g1.a);
                float smoothness = saturate(g2.a);

                float3 normalWS = DecodeGBufferNormalWS(g2.xyz);

                surf.albedo = albedo;
                surf.occlusion = occlusion;
                surf.smoothness = smoothness;

                bool isSpecular = (flags & kMaterialFlagSpecularSetup) != 0u;
                if (isSpecular)
                {
                    surf.specular = saturate(g1.rgb); // F0
                    surf.metallic = 0.0;
                }
                else
                {
                    float reflectivity = saturate(g1.r); // URP: reflectivity stored in 8bit channel
                    surf.metallic = MetallicFromReflectivity(reflectivity);
                    surf.specular = 0.0.xxx;
                }

                inputData.positionWS = positionWS;
                inputData.normalWS = normalize(normalWS);
                inputData.viewDirectionWS = SafeNormalize(GetCameraPositionWS() - positionWS);
                inputData.shadowCoord = 0;
                inputData.fogCoord = 0;
                inputData.vertexLighting = 0;
                inputData.bakedGI = 0;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                float3 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                float4 ssr = SAMPLE_TEXTURE2D_X_LOD(_RrrSsrTexture, sampler_LinearClamp, uv, 0);

                float3 ssrRadiance = ssr.rgb;  // SSRTrace должен писать сюда radiance
                float w = saturate(ssr.a);     // SSRTrace должен писать сюда confidence/weight

                if (_DebugReflectionOnly > 0.5)
                    return float4(ssrRadiance, 1.0);

            #if !defined(_SSR_DEFERRED_INPUT)
                // fallback: без gbuffer честно заменить envSpec нельзя
                return float4(lerp(src, ssrRadiance, w), 1.0);
            #else
                SurfaceData surf;
                InputData inputData;
                uint flags;
                ReadSurfaceFromGBuffer(uv, surf, inputData, flags);

                if (dot(inputData.normalWS, inputData.normalWS) < 1e-4)
                    return float4(src, 1.0);

                BRDFData brdf;
                InitializeBRDFData(surf, brdf);

                float NoV = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
                float3 R = reflect(-inputData.viewDirectionWS, inputData.normalWS);
                float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surf.smoothness);

                // env radiance (skybox/probes)
                half3 envRadiance = GlossyEnvironmentReflection(R, perceptualRoughness, 1.0h);

                // URP indirect specular = EnvBRDF * envRadiance
                float3 envSpec = EnvironmentBRDFSpecular(brdf, envRadiance, NoV);

                // SSR specular = EnvBRDF * ssrRadiance
                float3 ssrSpec = EnvironmentBRDFSpecular(brdf, ssrRadiance, NoV);
                ssrSpec *= surf.occlusion;

                // Замена envSpec на SSR по w (w — только надежность SSR)
                float3 specIndirect = lerp(envSpec, ssrSpec, w);

                // src уже содержит envSpec, поэтому делаем замену:
                float3 outColor = src + (specIndirect - envSpec);

                return float4(outColor, 1.0);
            #endif
            }
            ENDHLSL
        }
    }
}