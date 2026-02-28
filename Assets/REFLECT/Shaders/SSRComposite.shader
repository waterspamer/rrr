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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_LinearClamp);
            TEXTURE2D_X(_RrrSsrTexture);

            float _DebugReflectionOnly;

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

            float4 Frag(Varyings input) : SV_Target
            {
                float3 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.uv, 0).rgb;
                float4 reflection = SAMPLE_TEXTURE2D_X_LOD(_RrrSsrTexture, sampler_LinearClamp, input.uv, 0);
                float alpha = saturate(reflection.a);

                if (_DebugReflectionOnly > 0.5)
                    return float4(reflection.rgb, 1.0);

                float3 color = lerp(src, reflection.rgb, alpha);
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
