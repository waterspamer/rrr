Shader "Custom/VehicleDamageVoxelURP"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.5

        _DamageTex("Damage Texture", 2D) = "black" {}
        _VehicleSize("Vehicle Size", Vector) = (1,1,1,0)
        _BoundsMin("Bounds Min", Vector) = (0,0,0,0)
        _BoundsSize("Bounds Size", Vector) = (1,1,1,0)
        _TexResolution("Texture Resolution", Vector) = (16,8,0,0)
        _DamageAmplitude("Damage Amplitude", Range(0,0.5)) = 0.08
        _DamageDirection("Damage Direction", Range(-1,1)) = -1
        _DamageSinFrequency("Damage Sine Frequency", Range(0,40)) = 10
        _DamageSinStrength("Damage Sine Strength", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            TEXTURE2D(_DamageTex);
            SAMPLER(sampler_DamageTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float4 _VehicleSize;
                float4 _BoundsMin;
                float4 _BoundsSize;
                float4 _TexResolution;
                float _DamageAmplitude;
                float _DamageDirection;
                float _DamageSinFrequency;
                float _DamageSinStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            float2 GetVehicleUV(float3 posOS, float3 boundsMin, float3 boundsSize)
            {
                float2 uv;
                uv.x = (posOS.x - boundsMin.x) / max(boundsSize.x, 0.0001f);
                uv.y = (posOS.z - boundsMin.z) / max(boundsSize.z, 0.0001f);
                return uv;
            }

            float GetDamageValue(float3 posOS, float3 boundsMin, float3 boundsSize, float2 texResolution)
            {
                float2 uvVehicle = GetVehicleUV(posOS, boundsMin, boundsSize);
                float2 cellIndex = floor(uvVehicle * texResolution);
                float2 cellUV = (cellIndex + 0.5f) / texResolution;
                float3 sampled = SAMPLE_TEXTURE2D_LOD(_DamageTex, sampler_DamageTex, cellUV, 0).rgb;

                float height01 = (posOS.y - boundsMin.y) / max(boundsSize.y, 0.0001f);
                float layerIndex = floor(height01 * 3.0f);
                float3 channelMask = float3(step(2.0f, layerIndex), step(1.0f, layerIndex) - step(2.0f, layerIndex), 1.0f - step(1.0f, layerIndex));
                return dot(sampled, channelMask);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 boundsMin = _BoundsMin.xyz;
                float3 boundsSize = _BoundsSize.xyz;
                float2 texResolution = max(_TexResolution.xy, float2(1.0f, 1.0f));
                float3 positionOS = input.positionOS.xyz;

                float damageValue = GetDamageValue(positionOS, boundsMin, boundsSize, texResolution);
                float dist = length(positionOS);
                float wave = sin(dist * _DamageSinFrequency) * _DamageSinStrength;
                float deform = damageValue * _DamageAmplitude * (1.0f + wave);
                float3 dir = normalize(positionOS + float3(0.0001f, 0.0001f, 0.0001f));
                positionOS += dir * deform * _DamageDirection;

                VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = input.uv;
                output.positionOS = positionOS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 baseSample = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
                half3 baseColor = baseSample.rgb * _BaseColor.rgb;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseColor;
                surfaceData.alpha = 1.0h;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1.0h;
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = 0.0f;
                inputData.vertexLighting = 0.0f;
                inputData.bakedGI = 0.0f;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = 1.0f;

                return UniversalFragmentPBR(inputData, surfaceData);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
