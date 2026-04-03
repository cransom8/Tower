Shader "CastleDefender/LanternRoadGlow"
{
    Properties
    {
        _Color ("Color", Color) = (1.0, 0.63, 0.28, 0.86)
        _EdgePower ("Edge Power", Range(0.5, 4.0)) = 2.35
        _CenterBoost ("Center Boost", Range(0.5, 2.0)) = 1.2
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
            Name "RoadGlow"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
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
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _EdgePower;
            float _CenterBoost;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = (input.uv - 0.5) * 2.0;
                centered.x *= 1.1;
                centered.y *= 0.72;

                float radial = saturate(1.0 - dot(centered, centered));
                float alpha = pow(radial, max(0.001, _EdgePower));
                float centerPulse = pow(radial, 0.5) * (_CenterBoost - 1.0);
                float3 color = _Color.rgb * saturate(alpha + centerPulse);

                return half4(color, _Color.a * alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
