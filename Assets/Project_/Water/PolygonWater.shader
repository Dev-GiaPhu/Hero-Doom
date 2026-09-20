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
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

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

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor;
            half4 _DeepColor;
            half4 _FoamColor;

            float _WaveScale;
            float _WaveStrength;
            float _WaveSpeed;
            float _SecondaryScale;
            float _SecondarySpeed;

            float _Smoothness;
            float _FresnelPower;
            float _FresnelStrength;
            float _CrestThreshold;
            float _CrestSoftness;
            CBUFFER_END

            float _HeroDoomRainIntensity;

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
                float b = Hash21(cell + float2(1, 0));
                float c = Hash21(cell + float2(0, 1));
                float d = Hash21(cell + float2(1, 1));

                return lerp(
                    lerp(a, b, smoothF.x),
                    lerp(c, d, smoothF.x),
                    smoothF.y);
            }

            float WaveField(float2 p)
            {
                float t = _Time.y;

                float largeNoise =
                    ValueNoise(p * 0.13 + float2(t * 0.025, -t * 0.018));

                float mediumNoise =
                    ValueNoise(p * 0.31 + float2(-t * 0.021, t * 0.029));

                float smallNoise =
                    ValueNoise(p * 0.67 + float2(t * 0.035, t * 0.013));

                float a = sin(
                    dot(p, float2(0.79, 0.61)) * _WaveScale
                    + largeNoise * 3.3
                    + t * _WaveSpeed);

                float b = sin(
                    dot(p, float2(-0.43, 0.90)) * _SecondaryScale
                    + mediumNoise * 2.7
                    - t * _SecondarySpeed);

                float c = sin(
                    dot(p, float2(0.31, -0.95)) * (_WaveScale * 0.73 + 0.11)
                    + smallNoise * 2.1
                    + t * (_WaveSpeed * 0.37));

                return
                    a * 0.34
                    + b * 0.24
                    + c * 0.17
                    + (largeNoise - 0.5) * 0.31
                    + (mediumNoise - 0.5) * 0.20;
            }

            float RainRippleLayer(
                float2 worldXZ,
                float scale,
                float timeScale,
                float seed)
            {
                float2 p = worldXZ * scale;
                float2 cell = floor(p);
                float2 local = frac(p);

                float phase = Hash21(cell + seed);
                float active = step(
                    0.38,
                    Hash21(cell + float2(seed * 2.17, seed * 0.71)));

                float2 center = float2(
                    Hash21(cell + float2(13.17 + seed, 2.31)),
                    Hash21(cell + float2(4.73, 19.41 + seed)));

                center = center * 0.62 + 0.19;

                float age = frac(_Time.y * timeScale + phase);
                float radius = age * 0.44;
                float distanceToCenter = length(local - center);

                float ring =
                    1.0
                    - smoothstep(
                        0.026,
                        0.064,
                        abs(distanceToCenter - radius));

                float fade = (1.0 - age) * (1.0 - age);
                return ring * fade * active;
            }

            float RainRipple(float2 worldXZ)
            {
                float r1 = RainRippleLayer(worldXZ, 0.47, 1.13, 3.7);
                float r2 = RainRippleLayer(worldXZ + 17.3, 0.73, 0.91, 11.2);
                float r3 = RainRippleLayer(worldXZ - 29.1, 0.31, 1.37, 23.9);

                return saturate(r1 * 0.75 + r2 * 0.58 + r3 * 0.45)
                    * _HeroDoomRainIntensity;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 worldPosition =
                    TransformObjectToWorld(input.positionOS.xyz);

                output.positionWS = worldPosition;
                output.positionCS = TransformWorldToHClip(worldPosition);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float epsilon = 0.09;

                float h = WaveField(input.positionWS.xz);
                float hx = WaveField(input.positionWS.xz + float2(epsilon, 0));
                float hz = WaveField(input.positionWS.xz + float2(0, epsilon));

                float3 normalWS = normalize(
                    float3(
                        (h - hx) * _WaveStrength / epsilon,
                        1.0,
                        (h - hz) * _WaveStrength / epsilon));

                float3 viewDirection =
                    SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                float fresnel =
                    pow(
                        1.0 - saturate(dot(normalWS, viewDirection)),
                        _FresnelPower)
                    * _FresnelStrength;

                float wave01 = saturate(h * 0.5 + 0.5);

                half4 water =
                    lerp(
                        _DeepColor,
                        _ShallowColor,
                        saturate(wave01 * 0.52 + fresnel));

                float crest =
                    smoothstep(
                        _CrestThreshold - _CrestSoftness,
                        _CrestThreshold + _CrestSoftness,
                        wave01);

                Light mainLight = GetMainLight();

                float ndl =
                    saturate(dot(normalWS, mainLight.direction));

                float3 halfDirection =
                    SafeNormalize(mainLight.direction + viewDirection);

                float specular =
                    pow(
                        saturate(dot(normalWS, halfDirection)),
                        lerp(8.0, 128.0, _Smoothness));

                float ripple = RainRipple(input.positionWS.xz);

                half3 rgb =
                    water.rgb * (0.72 + ndl * 0.28);

                rgb +=
                    mainLight.color
                    * specular
                    * (0.16 + fresnel * 0.45);

                rgb =
                    lerp(
                        rgb,
                        _FoamColor.rgb,
                        crest * 0.10 + ripple * 0.48);

                return half4(rgb, water.a);
            }

            ENDHLSL
        }
    }
}
