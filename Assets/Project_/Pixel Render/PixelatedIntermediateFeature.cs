using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Screen-space pixelation and depth-edge effect for Unity 6 URP.
/// Uses the camera color texture as input and applies pixelation together
/// with depth-based silhouette detection.
/// </summary>
public sealed class PixelatedIntermediateFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public sealed class Settings
    {
        [Tooltip("Pixel block size in physical screen pixels. 1 = native resolution.")]
        [Min(1f)]
        public float pixelSize = 4f;

        [Tooltip("Relative depth gradient at which an edge begins.")]
        [Range(0.0001f, 1f)]
        public float depthEdgeThreshold = 0.08f;

        [Range(0f, 1f)]
        public float depthEdgeStrength = 0.75f;

        public Color edgeColor = Color.black;

        [Range(0.5f, 3f)]
        public float edgeWidth = 1f;

        public bool debugDepth;

        [Tooltip(
            "Assign PixelatedIntermediate.shader here. " +
            "The shader must use the name Custom/PixelatedIntermediate."
        )]
        public Shader shader;
    }

    [SerializeField]
    private Settings settings = new Settings();

    private PixelatedIntermediatePass pass;

    public override void Create()
    {
        pass?.Dispose();

        pass = new PixelatedIntermediatePass(settings)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (pass == null)
            return;

        if (renderingData.cameraData.isPreviewCamera)
            return;

        if (settings.shader == null)
        {
            Debug.LogError(
                "PixelatedIntermediateFeature: No shader assigned. " +
                "Assign PixelatedIntermediate.shader to the Shader field " +
                "in the Renderer Feature Inspector."
            );

            return;
        }

        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
        pass = null;
    }

    private sealed class PixelatedIntermediatePass : ScriptableRenderPass
    {
        private static readonly int PixelSizeId =
            Shader.PropertyToID("_PixelSize");

        private static readonly int DepthEdgeThresholdId =
            Shader.PropertyToID("_DepthEdgeThreshold");

        private static readonly int DepthEdgeStrengthId =
            Shader.PropertyToID("_DepthEdgeStrength");

        private static readonly int EdgeColorId =
            Shader.PropertyToID("_EdgeColor");

        private static readonly int EdgeWidthId =
            Shader.PropertyToID("_EdgeWidth");

        private static readonly int DebugDepthId =
            Shader.PropertyToID("_DebugDepth");

        private readonly Settings settings;

        private Material material;

        public PixelatedIntermediatePass(Settings settings)
        {
            this.settings = settings;

            renderPassEvent =
                RenderPassEvent.BeforeRenderingPostProcessing;

            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Dispose()
        {
            if (material != null)
            {
                CoreUtils.Destroy(material);
                material = null;
            }
        }

        private void EnsureMaterial()
        {
            if (material != null)
                return;

            if (settings.shader == null)
                return;

            material = CoreUtils.CreateEngineMaterial(settings.shader);

            if (material == null)
            {
                Debug.LogError(
                    "PixelatedIntermediateFeature: Failed to create material " +
                    "from the assigned shader."
                );
            }
        }

        public override void RecordRenderGraph(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            EnsureMaterial();

            if (material == null)
                return;

            UniversalResourceData resourceData =
                frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogWarning(
                    "PixelatedIntermediateFeature skipped because the camera " +
                    "is rendering directly to the back buffer. " +
                    "Enable an intermediate Color Texture for the renderer."
                );

                return;
            }

            TextureHandle source =
                resourceData.activeColorTexture;

            if (!source.IsValid())
                return;

            var destinationDesc =
                renderGraph.GetTextureDesc(source);

            destinationDesc.name =
                "PixelatedIntermediateColor";

            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;

            TextureHandle destination =
                renderGraph.CreateTexture(destinationDesc);

            material.SetFloat(
                PixelSizeId,
                Mathf.Max(1f, settings.pixelSize)
            );

            material.SetFloat(
                DepthEdgeThresholdId,
                Mathf.Max(
                    0.0001f,
                    settings.depthEdgeThreshold
                )
            );

            material.SetFloat(
                DepthEdgeStrengthId,
                Mathf.Clamp01(
                    settings.depthEdgeStrength
                )
            );

            material.SetColor(
                EdgeColorId,
                settings.edgeColor
            );

            material.SetFloat(
                EdgeWidthId,
                Mathf.Clamp(
                    settings.edgeWidth,
                    0.5f,
                    3f
                )
            );

            material.SetFloat(
                DebugDepthId,
                settings.debugDepth ? 1f : 0f
            );

            RenderGraphUtils.BlitMaterialParameters parameters =
                new(
                    source,
                    destination,
                    material,
                    0
                );

            renderGraph.AddBlitPass(
                parameters,
                passName: "PixelatedIntermediate"
            );

            resourceData.cameraColor = destination;
        }
    }
}