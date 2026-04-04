Shader "CastleDefender/Hero Foot Glow"
{
    Properties
    {
        [HDR]_GlowColor("Glow Color", Color) = (1.0, 0.82, 0.34, 0.9)
        _OuterRadius("Outer Radius", Range(0.1, 1.0)) = 0.88
        _InnerRadius("Inner Radius", Range(0.0, 0.9)) = 0.18
        _EdgeSoftness("Edge Softness", Range(0.01, 0.8)) = 0.28
        _PulseSpeed("Pulse Speed", Range(0.0, 4.0)) = 1.25
        _PulseAmount("Pulse Amount", Range(0.0, 0.5)) = 0.06
        _Alpha("Alpha", Range(0.0, 2.0)) = 0.80
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

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
                half4 _GlowColor;
                half _OuterRadius;
                half _InnerRadius;
                half _EdgeSoftness;
                half _PulseSpeed;
                half _PulseAmount;
                half _Alpha;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half2 centeredUv = input.uv * 2.0h - 1.0h;
                half radius = length(centeredUv);

                half pulse = 1.0h + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                half outerRadius = saturate(_OuterRadius * pulse);
                half outerFadeStart = max(0.0h, outerRadius - _EdgeSoftness);
                half softDisc = 1.0h - smoothstep(outerFadeStart, outerRadius, radius);

                half coreRadius = saturate(_InnerRadius * pulse);
                half core = 1.0h - smoothstep(coreRadius, coreRadius + (_EdgeSoftness * 0.6h), radius);

                half intensity = saturate((softDisc * 0.70h) + (core * 0.35h));
                half alpha = intensity * _Alpha * _GlowColor.a;
                half3 color = _GlowColor.rgb * alpha;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            Offset -1, -1

            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _GlowColor;
            half _OuterRadius;
            half _InnerRadius;
            half _EdgeSoftness;
            half _PulseSpeed;
            half _PulseAmount;
            half _Alpha;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                half2 centeredUv = input.uv * 2.0h - 1.0h;
                half radius = length(centeredUv);

                half pulse = 1.0h + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                half outerRadius = saturate(_OuterRadius * pulse);
                half outerFadeStart = max(0.0h, outerRadius - _EdgeSoftness);
                half softDisc = 1.0h - smoothstep(outerFadeStart, outerRadius, radius);

                half coreRadius = saturate(_InnerRadius * pulse);
                half core = 1.0h - smoothstep(coreRadius, coreRadius + (_EdgeSoftness * 0.6h), radius);

                half intensity = saturate((softDisc * 0.70h) + (core * 0.35h));
                half alpha = intensity * _Alpha * _GlowColor.a;
                half3 color = _GlowColor.rgb * alpha;
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
