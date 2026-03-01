Shader "Hidden/RRR/SSRHiZPyramid"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Copy Depth Mip0"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCopy
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            float _BlitMipLevel;

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

            float4 FragCopy(Varyings input) : SV_Target
            {
                float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.uv, 0).r;
                return float4(rawDepth, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Downsample Conservative"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsample
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            float _BlitMipLevel;
            float4 _BlitTexture_TexelSize;

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

            float4 FragDownsample(Varyings input) : SV_Target
            {
                float2 srcTexel = _BlitTexture_TexelSize.xy * exp2(_BlitMipLevel);
                float2 halfStep = srcTexel * 0.5;

                float d0 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.uv + float2(-halfStep.x, -halfStep.y), _BlitMipLevel).r;
                float d1 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.uv + float2( halfStep.x, -halfStep.y), _BlitMipLevel).r;
                float d2 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.uv + float2(-halfStep.x,  halfStep.y), _BlitMipLevel).r;
                float d3 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.uv + float2( halfStep.x,  halfStep.y), _BlitMipLevel).r;

            #if UNITY_REVERSED_Z
                float conservative = max(max(d0, d1), max(d2, d3));
            #else
                float conservative = min(min(d0, d1), min(d2, d3));
            #endif

                return float4(conservative, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}
