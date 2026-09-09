using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>One MAX amount mask, then one multiply using a shared color and hatch style.</summary>
public sealed class BlobShadowRendererFeature : ScriptableRendererFeature
{
    public enum MaskResolution { Full = 1, Half = 2, Quarter = 4 }

    [SerializeField, Tooltip("Full evaluates MAX at each camera pixel. Half/Quarter approximate it and can bleed across depth edges.")]
    private MaskResolution resolution = MaskResolution.Full;
    [SerializeField, Tooltip("Explicit reference keeps the composite shader in player builds.")]
    private Shader compositeShader;
    [SerializeField, Tooltip("Shared _ShadowColor and hatch settings for every blob. SpriteRenderer alpha still controls each contribution; RGB tint is not used.")]
    private Material shadowStyle;

    private Material compositeMaterial;
    private BlobShadowPass shadowPass;

    public override void Create()
    {
        CoreUtils.Destroy(compositeMaterial);
        if (compositeShader == null)
            compositeShader = Shader.Find("Hidden/BlobShadowComposite");
        compositeMaterial = compositeShader != null ? CoreUtils.CreateEngineMaterial(compositeShader) : null;
        shadowPass = new BlobShadowPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // UI/overlay cameras must not multiply the already shadowed base camera again.
        if (compositeMaterial == null || renderingData.cameraData.renderType == CameraRenderType.Overlay ||
            renderingData.cameraData.cameraType == CameraType.Preview)
            return;
        // Keep the existing material as the single place to tune shadow color and hatching.
        compositeMaterial.SetColor("_ShadowColor", shadowStyle != null ? shadowStyle.GetColor("_ShadowColor") : new Color(.35f, .35f, .4f, 1));
        compositeMaterial.SetTexture("_HatchMap", shadowStyle != null ? shadowStyle.GetTexture("_HatchMap") : Texture2D.whiteTexture);
        compositeMaterial.SetFloat("_HatchScale", shadowStyle != null ? shadowStyle.GetFloat("_HatchScale") : .5f);
        compositeMaterial.SetFloat("_HatchStrength", shadowStyle != null ? shadowStyle.GetFloat("_HatchStrength") : 0);
        shadowPass.Setup(compositeMaterial, (int)resolution);
        renderer.EnqueuePass(shadowPass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(compositeMaterial);

    // Fallback changes storage only. Never fall back to accumulating blend or stencil.
    public static GraphicsFormat SelectMaskFormat()
    {
        var usages = GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample | GraphicsFormatUsage.Blend;
        if (SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, usages))
            return GraphicsFormat.R8_UNorm;
        if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, usages))
            return GraphicsFormat.R16_SFloat;
        if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, usages))
            return GraphicsFormat.R8G8B8A8_UNorm;
        return GraphicsFormat.None;
    }

    private sealed class BlobShadowPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId MaskTag = new ShaderTagId("BlobShadowMask");
        private static readonly int SizeId = Shader.PropertyToID("_BlobShadowMaskSize");
        private readonly GraphicsFormat maskFormat = SelectMaskFormat();
        private Material composite;
        private int divisor;
        private bool warned;

        public BlobShadowPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            // No scene-color input or CameraOpaqueTexture. Resolved depth keeps hidden blobs
            // off opaque foreground geometry and supports a single-sample mask with camera MSAA.
            ConfigureInput(ScriptableRenderPassInput.Depth);
            // Use one orientation for scene depth, offscreen sprite geometry, and composite.
            requiresIntermediateTexture = true;
        }

        public void Setup(Material material, int resolutionDivisor)
        {
            composite = material;
            divisor = Mathf.Clamp(resolutionDivisor, 1, 4);
        }

        private sealed class DrawData
        {
            public RendererListHandle renderers;
            public Vector4 size;
        }

        private sealed class CompositeData
        {
            public TextureHandle amount;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (maskFormat == GraphicsFormat.None)
            {
                if (!warned)
                {
                    Debug.LogError("Blob shadows require a blendable R8, R16F or RGBA8 target. No accumulating fallback is used.");
                    warned = true;
                }
                return;
            }

            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var lights = frameData.Get<UniversalLightData>();
            var cameraDescriptor = camera.cameraTargetDescriptor;
            int width = Mathf.Max(1, (cameraDescriptor.width + divisor - 1) / divisor);
            int height = Mathf.Max(1, (cameraDescriptor.height + divisor - 1) / divisor);
            var size = new Vector4(width, height, 1f / width, 1f / height);
            var descriptor = new TextureDesc(width, height)
            {
                name = "Blob Shadow MAX Amount",
                colorFormat = maskFormat,
                dimension = cameraDescriptor.dimension,
                slices = cameraDescriptor.volumeDepth,
                useDynamicScale = cameraDescriptor.useDynamicScale,
                msaaSamples = MSAASamples.None,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.clear
            };
            var amount = renderGraph.CreateTexture(descriptor);

            var maskDrawing = RenderingUtils.CreateDrawingSettings(MaskTag, rendering, camera, lights, SortingCriteria.CommonTransparent);
            var filtering = new FilteringSettings(RenderQueueRange.transparent, camera.camera.cullingMask);
            var maskList = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, maskDrawing, filtering));

            using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Blob Shadows: MAX Amount", out var data))
            {
                data.renderers = maskList;
                data.size = size;
                builder.UseRendererList(maskList);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(amount, 0, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((DrawData pass, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalVector(SizeId, pass.size);
                    context.cmd.DrawRendererList(pass.renderers);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Blob Shadows: Multiply Once", out var data))
            {
                data.amount = amount;
                data.material = composite;
                builder.UseTexture(amount, AccessFlags.Read);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                // Fixed-function multiply reads destination color. Preserve the attachment;
                // do not copy/sample the scene color or overwrite unshadowed pixels.
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc((CompositeData pass, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, pass.amount, new Vector4(1, 1, 0, 0), pass.material, 0));
            }
        }
    }
}
