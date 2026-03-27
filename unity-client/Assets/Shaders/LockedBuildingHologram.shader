Shader "CastleDefender/LockedBuildingHologram"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.16, 0.78, 1.0, 0.16)
        _EdgeColor ("Edge Color", Color) = (0.80, 0.96, 1.0, 0.92)
        _ScanColor ("Scan Color", Color) = (0.92, 0.99, 1.0, 0.65)
        _Opacity ("Opacity", Range(0, 1)) = 0.14
        _EdgePower ("Edge Power", Range(0.5, 8.0)) = 3.8
        _EdgeIntensity ("Edge Intensity", Range(0.0, 3.0)) = 1.35
        _GridScale ("Grid Scale", Range(0.1, 12.0)) = 4.5
        _ScanTiling ("Scan Tiling", Range(1.0, 48.0)) = 18.0
        _ScanSpeed ("Scan Speed", Range(0.0, 4.0)) = 0.85
        _OutlineWidth ("Outline Width", Range(0.0, 0.08)) = 0.018
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
            Name "LockedFill"
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
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EdgeColor;
            float4 _ScanColor;
            float _Opacity;
            float _EdgePower;
            float _EdgeIntensity;
            float _GridScale;
            float _ScanTiling;
            float _ScanSpeed;
            float _OutlineWidth;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = SafeNormalize(input.normalWS);
                float3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float rim = pow(saturate(1.0 - dot(normalWS, viewDirWS)), max(0.01, _EdgePower));

                float scan = 0.5 + 0.5 * sin(
                    (input.positionWS.y + input.positionWS.x * 0.35 + input.positionWS.z * 0.35) * _ScanTiling
                    - _Time.y * (_ScanSpeed * 6.2831853));

                float2 gridUv = input.positionWS.xz * _GridScale;
                float2 gridDeriv = max(fwidth(gridUv), 0.0001);
                float2 gridDist = abs(frac(gridUv - 0.5) - 0.5) / gridDeriv;
                float grid = 1.0 - saturate(min(gridDist.x, gridDist.y));

                float edge = saturate(rim * _EdgeIntensity);
                float glow = saturate(edge + grid * 0.18 + scan * 0.14);
                float alpha = saturate(_Opacity + edge * _EdgeColor.a + grid * 0.08 + scan * 0.04);
                float3 color = _BaseColor.rgb + _EdgeColor.rgb * glow + _ScanColor.rgb * (scan * 0.16 + grid * 0.05);

                return half4(color, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "LockedOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex VertOutline
            #pragma fragment FragOutline

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EdgeColor;
            float4 _ScanColor;
            float _Opacity;
            float _EdgePower;
            float _EdgeIntensity;
            float _GridScale;
            float _ScanTiling;
            float _ScanSpeed;
            float _OutlineWidth;
            CBUFFER_END

            Varyings VertOutline(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = SafeNormalize(TransformObjectToWorldNormal(input.normalOS));
                positionWS += normalWS * _OutlineWidth;
                output.positionHCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 FragOutline(Varyings input) : SV_Target
            {
                return half4(_EdgeColor.rgb, _EdgeColor.a * 0.74);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
