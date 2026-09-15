Shader "Custom/PixelatedIntermediate"
{
    Properties
    {
        _PixelSize ("Pixel Block Size (Screen Pixels)", Range(1, 64)) = 4
        _DepthEdgeThreshold ("Depth Edge Threshold", Range(0.0001, 1.0)) = 0.08
        _DepthEdgeStrength ("Depth Edge Strength", Range(0, 1)) = 0.75
        _EdgeColor ("Depth Edge Color", Color) = (0.02, 0.02, 0.02, 1)
        _EdgeWidth ("Depth Edge Width", Range(0.5, 3.0)) = 1.0
        _DebugDepth ("Debug Depth", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "PixelatedIntermediate"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            // Core.hlsl must come first: it defines TEXTURE2D_X and the other XR-safe
            // texture macros used by Blit.hlsl.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PixelSize;
                float _DepthEdgeThreshold;
                float _DepthEdgeStrength;
                float4 _EdgeColor;
                float _EdgeWidth;
                float _DebugDepth;
            CBUFFER_END

            float2 PixelatedUV(float2 uv)
            {
                float2 screenSize = _ScreenParams.xy;
                float2 block = max(_PixelSize, 1.0).xx;
                float2 pixel = uv * screenSize;
                float2 cell = floor(pixel / block);
                float2 centerPixel = (cell + 0.5) * block;
                return centerPixel / screenSize;
            }

            float LinearDepth(float2 uv)
            {
                float raw = SampleSceneDepth(saturate(uv));
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            float DepthEdge(float2 uv)
            {
                float2 texel = 1.0 / _ScreenParams.xy;
                float2 stepUV = texel * max(_EdgeWidth, 0.5);

                float c = LinearDepth(uv);
                float l = LinearDepth(uv + float2(-stepUV.x, 0));
                float r = LinearDepth(uv + float2(stepUV.x, 0));
                float u = LinearDepth(uv + float2(0, stepUV.y));
                float d = LinearDepth(uv + float2(0, -stepUV.y));

                float gx = abs(r - l);
                float gy = abs(u - d);
                float gradient = max(gx, gy) / max(c, 1e-3);

                return smoothstep(
                    _DepthEdgeThreshold,
                    _DepthEdgeThreshold * 2.0,
                    gradient
                );
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = saturate(input.texcoord);
                float2 pixelatedUV = PixelatedUV(uv);

                // One representative source sample is reused for the whole screen-space block.
                float2 sourcePixel = floor(pixelatedUV * _ScreenParams.xy);
                float2 snappedUV = (sourcePixel + 0.5) / _ScreenParams.xy;

                half4 color = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_PointClamp,
                    snappedUV,
                    0
                );

                if (_DebugDepth > 0.5)
                {
                    float depth = LinearDepth(pixelatedUV);
                    float normalized = saturate(1.0 / max(depth, 1e-3));
                    return half4(normalized.xxx, 1);
                }

                float edge = DepthEdge(pixelatedUV);
                color.rgb = lerp(
                    color.rgb,
                    _EdgeColor.rgb,
                    edge * saturate(_DepthEdgeStrength)
                );

                return color;
            }
            ENDHLSL
        }
    }
}
