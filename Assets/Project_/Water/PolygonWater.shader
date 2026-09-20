Shader "HeroDoom/Water/PolygonWater"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor ("Shallow Color", Color) = (0.08, 0.58, 0.72, 0.82)
        _DeepColor ("Deep Color", Color) = (0.015, 0.18, 0.32, 0.94)
        _FoamColor ("Crest Color", Color) = (0.72, 0.95, 1, 1)
        [Header(World Space Waves)]
        _WaveScale ("Wave Scale", Range(0.05, 4)) = 0.65
        _WaveStrength ("Wave Strength", Range(0, 1)) = 0.22
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 0.55
        _SecondaryScale ("Secondary Scale", Range(0.05, 6)) = 1.35
        _SecondarySpeed ("Secondary Speed", Range(0, 3)) = 0.28
        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.78
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.2
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.35
        _CrestThreshold ("Crest Threshold", Range(0, 1)) = 0.72
        _CrestSoftness ("Crest Softness", Range(0.01, 0.4)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor, _DeepColor, _FoamColor;
            float _WaveScale, _WaveStrength, _WaveSpeed, _SecondaryScale, _SecondarySpeed;
            float _Smoothness, _FresnelPower, _FresnelStrength, _CrestThreshold, _CrestSoftness;
            CBUFFER_END

            float _HeroDoomRainIntensity;

            float WaveField(float2 p)
            {
                float t = _Time.y;
                float a = sin((p.x + p.y * 0.63) * _WaveScale + t * _WaveSpeed);
                float b = sin((p.x * -0.42 + p.y) * _SecondaryScale - t * _SecondarySpeed);
                float c = sin((p.x * 0.71 - p.y * 0.36) * (_WaveScale * 0.55) + t * (_WaveSpeed * 0.42));
                return a * 0.5 + b * 0.3 + c * 0.2;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float RainRipple(float2 worldXZ)
            {
                float2 p = worldXZ * 0.85;
                float2 cell = floor(p);
                float2 local = frac(p) - 0.5;
                float randomPhase = Hash21(cell);
                float age = frac(_Time.y * 1.4 + randomPhase);
                float radius = age * 0.48;
                float ring = 1.0 - smoothstep(0.025, 0.07, abs(length(local) - radius));
                return ring * (1.0 - age) * _HeroDoomRainIntensity;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 ws = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = ws;
                OUT.positionCS = TransformWorldToHClip(ws);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float eps = 0.08;
                float h = WaveField(IN.positionWS.xz);
                float hx = WaveField(IN.positionWS.xz + float2(eps, 0));
                float hz = WaveField(IN.positionWS.xz + float2(0, eps));
                float3 normalWS = normalize(float3((h - hx) * _WaveStrength / eps, 1.0, (h - hz) * _WaveStrength / eps));

                float3 viewDir = SafeNormalize(GetWorldSpaceViewDir(IN.positionWS));
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower) * _FresnelStrength;
                float wave01 = saturate(h * 0.5 + 0.5);
                half4 water = lerp(_DeepColor, _ShallowColor, saturate(wave01 * 0.55 + fresnel));
                float crest = smoothstep(_CrestThreshold - _CrestSoftness, _CrestThreshold + _CrestSoftness, wave01);

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(normalWS, mainLight.direction));
                float3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(normalWS, halfDir)), lerp(8.0, 128.0, _Smoothness));

                float ripple = RainRipple(IN.positionWS.xz);
                half3 rgb = water.rgb * (0.72 + ndl * 0.28);
                rgb += mainLight.color * spec * (0.18 + fresnel * 0.5);
                rgb = lerp(rgb, _FoamColor.rgb, crest * 0.16 + ripple * 0.65);
                return half4(rgb, water.a);
            }
            ENDHLSL
        }
    }
}
