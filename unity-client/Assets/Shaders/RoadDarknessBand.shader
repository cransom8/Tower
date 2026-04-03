Shader "CastleDefender/RoadDarknessBand"
{
    Properties
    {
        _Color ("Color", Color) = (0.03, 0.04, 0.065, 1.0)
        _InnerAlpha ("Inner Alpha", Range(0.0, 1.0)) = 0.22
        _OuterAlpha ("Outer Alpha", Range(0.0, 1.0)) = 0.38
        _NoiseStrength ("Noise Strength", Range(0.0, 0.4)) = 0.12
        _EndFade ("End Fade", Range(0.01, 0.2)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RoadDarknessBand"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _InnerAlpha;
            float _OuterAlpha;
            float _NoiseStrength;
            float _EndFade;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(234.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 smoothF = f * f * (3.0 - 2.0 * f);

                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));

                return lerp(lerp(a, b, smoothF.x), lerp(c, d, smoothF.x), smoothF.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float outward = smoothstep(0.0, 1.0, saturate(input.uv.y));
                float alpha = lerp(_InnerAlpha, _OuterAlpha, pow(outward, 1.15));

                float endFade = smoothstep(0.0, _EndFade, input.uv.x) *
                    smoothstep(0.0, _EndFade, 1.0 - input.uv.x);

                float noise = ValueNoise(input.positionWS.xz * 0.06);
                float noiseScale = lerp(1.0 - _NoiseStrength, 1.0, noise);

                alpha *= endFade;
                alpha *= noiseScale;

                return half4(_Color.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
