Shader "HeroDoom/Weather/FogLayer"
{
    Properties
    {
        _ShapeNoiseTex ("Shape Noise Map", 2D) = "gray" {}
        _DetailNoiseTex ("Detail Noise Map", 2D) = "gray" {}

        _FogColor ("Fog Color", Color) = (0.72, 0.78, 0.80, 1)
        _Density ("Density", Range(0, 1)) = 0
        _FogOpacity ("Fog Opacity", Range(0, 1)) = 0.52
        _PatchSize ("Fog Patch Size", Range(1, 80)) = 24
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.45)) = 0.2
        _FogContrast ("Fog Contrast", Range(0.4, 3)) = 1.25

        _EdgeDetailScale ("Edge Detail Scale", Range(1, 8)) = 2.4
        _EdgeDetailStrength ("Edge Detail Strength", Range(0, 0.75)) = 0.28
        _MicroDetailScale ("Micro Detail Scale", Range(2, 16)) = 5.5
        _MicroDetailStrength ("Micro Detail Strength", Range(0, 0.3)) = 0.08

        _WindSpeed ("Wind Speed XZ", Vector) = (0.006, 0.0025, 0, 0)
        _MorphSpeed ("Morph Speed", Range(0, 0.15)) = 0.018
        _MorphStrength ("Morph Strength", Range(0, 1)) = 0.32
        _EdgeFade ("Layer Edge Fade", Range(0.02, 0.5)) = 0.16
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

            TEXTURE2D(_ShapeNoiseTex);
            SAMPLER(sampler_ShapeNoiseTex);

            TEXTURE2D(_DetailNoiseTex);
            SAMPLER(sampler_DetailNoiseTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _FogColor;
            float _Density;
            float _FogOpacity;
            float _PatchSize;
            float _EdgeSoftness;
            float _FogContrast;

            float _EdgeDetailScale;
            float _EdgeDetailStrength;
            float _MicroDetailScale;
            float _MicroDetailStrength;

            float4 _WindSpeed;
            float _MorphSpeed;
            float _MorphStrength;
            float _EdgeFade;
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
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float time = _Time.y;
                float2 windOffset = _WindSpeed.xy * time;

                float2 baseUV =
                    input.positionWS.xz /
                    max(1.0, _PatchSize);

                float morphTime = time * _MorphSpeed;

                float2 morphOffsetA =
                    float2(
                        sin(morphTime * 0.83),
                        cos(morphTime * 0.67))
                    * 0.11 * _MorphStrength;

                float2 morphOffsetB =
                    float2(
                        cos(morphTime * 0.51 + 1.7),
                        sin(morphTime * 0.74 + 0.9))
                    * 0.09 * _MorphStrength;

                float shapeA =
                    SAMPLE_TEXTURE2D(
                        _ShapeNoiseTex,
                        sampler_ShapeNoiseTex,
                        baseUV + windOffset + morphOffsetA).r;

                float shapeB =
                    SAMPLE_TEXTURE2D(
                        _ShapeNoiseTex,
                        sampler_ShapeNoiseTex,
                        baseUV * 1.17
                            - windOffset * 0.37
                            + float2(0.37, 0.19)
                            + morphOffsetB).r;

                float shape =
                    lerp(
                        shapeA,
                        (shapeA + shapeB) * 0.5,
                        _MorphStrength * 0.62);

                float detail =
                    SAMPLE_TEXTURE2D(
                        _DetailNoiseTex,
                        sampler_DetailNoiseTex,
                        baseUV * _EdgeDetailScale
                            + windOffset * 0.63
                            + float2(0.13, 0.41)).r;

                float micro =
                    SAMPLE_TEXTURE2D(
                        _DetailNoiseTex,
                        sampler_DetailNoiseTex,
                        baseUV * _MicroDetailScale
                            - windOffset * 0.31
                            + float2(0.61, 0.27)).r;

                float field =
                    shape
                    + (detail - 0.5) * _EdgeDetailStrength
                    + (micro - 0.5) * _MicroDetailStrength;

                field =
                    pow(
                        saturate(field),
                        _FogContrast);

                float threshold =
                    lerp(
                        0.78,
                        0.32,
                        saturate(_Density));

                float fogPatch =
                    smoothstep(
                        threshold - _EdgeSoftness,
                        threshold + _EdgeSoftness,
                        field);

                float2 centered =
                    abs(input.uv - 0.5) * 2.0;

                float edgeDistance =
                    max(centered.x, centered.y);

                float layerEdge =
                    1.0
                    - smoothstep(
                        1.0 - _EdgeFade,
                        1.0,
                        edgeDistance);

                float alpha =
                    fogPatch
                    * layerEdge
                    * _FogOpacity
                    * saturate(_Density * 1.28);

                return half4(_FogColor.rgb, alpha);
            }

            ENDHLSL
        }
    }
}
