Shader "HeroDoom/Weather/FogLayer"
{
    Properties
    {
        _NoiseTex ("Noise Map", 2D) = "gray" {}
        _FogColor ("Fog Color", Color) = (0.72, 0.78, 0.80, 1)
        _Density ("Density", Range(0, 1)) = 0
        _NoiseScale ("Noise Scale", Range(0.01, 1)) = 0.11
        _DetailScale ("Detail Scale", Range(0.2, 4)) = 1.85
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.38
        _Contrast ("Contrast", Range(0.25, 4)) = 1.55
        _Softness ("Patch Softness", Range(0.01, 0.5)) = 0.16
        _MoveSpeed ("Move Speed XY", Vector) = (0.012, 0.007, 0, 0)
        _DetailMoveSpeed ("Detail Move Speed XY", Vector) = (-0.006, 0.011, 0, 0)
        _EdgeFade ("Edge Fade", Range(0.02, 0.5)) = 0.18
        _Opacity ("Max Opacity", Range(0, 1)) = 0.58
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+40"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "FogLayer"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _FogColor;
            float _Density;
            float _NoiseScale;
            float _DetailScale;
            float _DetailStrength;
            float _Contrast;
            float _Softness;
            float4 _MoveSpeed;
            float4 _DetailMoveSpeed;
            float _EdgeFade;
            float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                output.positionWS =
                    TransformObjectToWorld(
                        input.positionOS.xyz);

                output.positionCS =
                    TransformWorldToHClip(
                        output.positionWS);

                output.uv = input.uv;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float time = _Time.y;

                float2 worldUV =
                    input.positionWS.xz *
                    _NoiseScale;

                float2 uvA =
                    worldUV +
                    _MoveSpeed.xy *
                    time;

                float2 uvB =
                    worldUV *
                    _DetailScale +
                    _DetailMoveSpeed.xy *
                    time;

                float noiseA =
                    SAMPLE_TEXTURE2D(
                        _NoiseTex,
                        sampler_NoiseTex,
                        uvA).r;

                float noiseB =
                    SAMPLE_TEXTURE2D(
                        _NoiseTex,
                        sampler_NoiseTex,
                        uvB).r;

                float noise =
                    lerp(
                        noiseA,
                        noiseA * noiseB,
                        _DetailStrength);

                noise =
                    pow(
                        saturate(noise),
                        _Contrast);

                float threshold =
                    lerp(
                        0.78,
                        0.34,
                        saturate(_Density));

                float patch =
                    smoothstep(
                        threshold - _Softness,
                        threshold + _Softness,
                        noise);

                float2 centered =
                    abs(input.uv - 0.5) *
                    2.0;

                float edgeDistance =
                    max(
                        centered.x,
                        centered.y);

                float edgeFade =
                    1.0 -
                    smoothstep(
                        1.0 - _EdgeFade,
                        1.0,
                        edgeDistance);

                float alpha =
                    patch *
                    edgeFade *
                    _Opacity *
                    saturate(
                        _Density * 1.35);

                return half4(
                    _FogColor.rgb,
                    alpha);
            }

            ENDHLSL
        }
    }
}
