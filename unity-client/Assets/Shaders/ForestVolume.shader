Shader "CastleDefender/ForestVolume"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.03, 0.055, 0.038, 0.94)
        _DepthColor ("Depth Color", Color) = (0.006, 0.012, 0.012, 0.99)
        _TopColor ("Top Color", Color) = (0.048, 0.072, 0.052, 0.9)
        _Darkness ("Darkness", Range(0.0, 1.0)) = 0.98
        _NoiseStrength ("Noise Strength", Range(0.0, 1.0)) = 0.62
        _FadeSoftness ("Fade Softness", Range(0.02, 0.45)) = 0.18
        _TopSilhouetteStrength ("Top Silhouette Strength", Range(0.0, 1.5)) = 1.08
        _NoiseScale ("Noise Scale", Range(0.005, 0.12)) = 0.045
        _CenterDensity ("Center Density", Range(0.0, 1.5)) = 1.12
        _ViewOcclusionBoost ("View Occlusion Boost", Range(0.0, 1.0)) = 0.4
        _EdgeFadeDistance ("Edge Fade Distance", Range(0.01, 0.18)) = 0.065
        _CanopyTex ("Canopy Texture", 2D) = "white" {}
        _CanopyTexScale ("Canopy Texture Scale", Range(0.002, 0.08)) = 0.0215
        _CanopyTexBlend ("Canopy Texture Blend", Range(0.0, 1.0)) = 0.9
        _CanopyTexClip ("Canopy Texture Clip", Range(0.0, 0.9)) = 0.24
        _CanopyTintStrength ("Canopy Tint Strength", Range(0.0, 1.5)) = 0.92
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
            Name "ForestVolume"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CanopyTex);
            SAMPLER(sampler_CanopyTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _DepthColor;
            float4 _TopColor;
            float _Darkness;
            float _NoiseStrength;
            float _FadeSoftness;
            float _TopSilhouetteStrength;
            float _NoiseScale;
            float _CenterDensity;
            float _ViewOcclusionBoost;
            float _EdgeFadeDistance;
            float _CanopyTexScale;
            float _CanopyTexBlend;
            float _CanopyTexClip;
            float _CanopyTintStrength;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
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

            float2 Rotate2D(float2 value, float degrees)
            {
                float angleRadians = radians(degrees);
                float s = sin(angleRadians);
                float c = cos(angleRadians);
                return float2(
                    (value.x * c) - (value.y * s),
                    (value.x * s) + (value.y * c));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalize(normalInputs.normalWS);
                output.positionOS = input.positionOS.xyz;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float darkness = saturate(_Darkness);
                float fadeSoftness = max(0.02, _FadeSoftness);
                float edgeFadeDistance = max(0.01, _EdgeFadeDistance);
                float3 normalWS = normalize(input.normalWS);
                float topMask = saturate(pow(saturate(abs(normalWS.y)), 4.0));
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float viewFacing = saturate(abs(dot(normalWS, viewDirWS)));

                float2 noiseUvA = input.positionWS.xz * _NoiseScale;
                float2 noiseUvB = (input.positionWS.xz + input.positionWS.yy * 0.55) * (_NoiseScale * 2.17);
                float noiseA = ValueNoise(noiseUvA);
                float noiseB = ValueNoise(noiseUvB);
                float canopyNoise = lerp(noiseA, noiseB, 0.42);
                float breakup = saturate(lerp(noiseA, canopyNoise, 0.72) + ((noiseB - 0.5) * _NoiseStrength * 0.35));

                float2 canopyUvA = input.positionWS.xz * _CanopyTexScale;
                float2 canopyUvB = Rotate2D((canopyUvA * 1.17) + float2(0.173, 0.381), 31.0);
                float4 canopyTexA = SAMPLE_TEXTURE2D(_CanopyTex, sampler_CanopyTex, canopyUvA);
                float4 canopyTexB = SAMPLE_TEXTURE2D(_CanopyTex, sampler_CanopyTex, canopyUvB);
                float canopyTexMask = saturate(lerp(canopyTexA.a, canopyTexB.a, 0.48));
                float canopyTexCoverage = smoothstep(_CanopyTexClip, 1.0, canopyTexMask);
                float3 canopyTexColor = lerp(canopyTexA.rgb, canopyTexB.rgb, 0.42);

                float vertical01 = saturate(input.positionOS.y + 0.5);
                float2 edgeUv = min(input.uv, 1.0 - input.uv);
                float edgeDistance = min(edgeUv.x, edgeUv.y);
                float linearCenter = 1.0 - abs((input.uv.x - 0.5) * 2.0);
                float radialCenter = 1.0 - saturate(length((input.uv - 0.5) * 2.0));
                float centerDensity = saturate(lerp(linearCenter, radialCenter, topMask));
                centerDensity = pow(centerDensity, 0.72);

                float topSilhouette = 0.68 + ((canopyNoise - 0.5) * _TopSilhouetteStrength * 0.34);
                float sideTopFade = 1.0 - smoothstep(
                    topSilhouette - fadeSoftness,
                    topSilhouette + fadeSoftness,
                    vertical01);
                float sideBottomFade = smoothstep(0.0, 0.045 + (fadeSoftness * 0.2), vertical01);
                float sideEdgeFade = smoothstep(
                    0.0,
                    edgeFadeDistance + (fadeSoftness * 0.18),
                    edgeDistance + ((noiseB - 0.5) * _NoiseStrength * 0.08));
                float sideAlpha = sideTopFade * sideBottomFade * sideEdgeFade;
                sideAlpha *= lerp(1.0, 1.0 + _CenterDensity, centerDensity);
                sideAlpha *= lerp(0.92, 1.0 + _ViewOcclusionBoost, viewFacing);

                float canopyCluster = smoothstep(0.16, 0.82, breakup + (noiseB * 0.18));
                float topEdgeFade = smoothstep(
                    0.0,
                    edgeFadeDistance + 0.028 + (fadeSoftness * 0.24),
                    edgeDistance + ((canopyNoise - 0.5) * _NoiseStrength * 0.12));
                float topAlpha = lerp(0.76, 0.98, canopyCluster) * topEdgeFade;
                topAlpha *= lerp(1.0, 1.0 + (_CenterDensity * 0.72), centerDensity);
                float canopyTexInfluence = topMask * _CanopyTexBlend;
                float canopyTexDensity = saturate((canopyTexCoverage * 0.86) + (canopyCluster * 0.32));
                topAlpha = lerp(topAlpha, topAlpha * canopyTexDensity, canopyTexInfluence);

                float3 sideColor = lerp(_BaseColor.rgb, _DepthColor.rgb, saturate(0.55 + ((1.0 - breakup) * 0.45)));
                sideColor *= lerp(0.84, 0.24, darkness);

                float3 topColor = lerp(_DepthColor.rgb, _TopColor.rgb, canopyCluster);
                topColor = lerp(topColor, _BaseColor.rgb, 0.28);
                topColor *= lerp(0.8, 0.34, darkness);
                float3 evergreenTop = saturate((canopyTexColor * _CanopyTintStrength) + (_DepthColor.rgb * 0.16));
                evergreenTop *= lerp(0.72, 1.05, canopyCluster);
                topColor = lerp(topColor, evergreenTop, canopyTexInfluence * canopyTexCoverage);

                float3 color = lerp(sideColor, topColor, topMask);
                color *= lerp(0.8, 1.02, breakup * _NoiseStrength);

                float alpha = lerp(sideAlpha, topAlpha, topMask);
                alpha *= lerp(0.92, 1.18, darkness);

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
