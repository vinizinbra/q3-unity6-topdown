using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class CartoonOutlineFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public Color outlineColor = Color.black;
        [Range(0.0001f, 0.05f)] public float depthThreshold = 0.003f;
        [Range(0f, 2f)] public float normalThreshold = 0.4f;
        [Range(0.5f, 4f)] public float edgeThickness = 1f;
        [Range(0f, 2f)] public float edgeSoftness = 0.3f;
        [Range(0f, 1f)] public float grazingAngleBias = 0.2f;
        [Range(1f, 10f)] public float grazingAngleBiasScale = 5f;
    }

    public Settings settings = new();
    public Shader outlineShader;

    private Material _material;
    private CartoonOutlinePass _pass;

    public override void Create()
    {
        if (outlineShader == null)
        {
            outlineShader = Shader.Find("Hidden/Custom/CartoonOutline");
        }

        _pass = new CartoonOutlinePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
        {
            return;
        }

        if (outlineShader == null)
        {
            return;
        }

        if (_material == null)
        {
            _material = CoreUtils.CreateEngineMaterial(outlineShader);
        }

        _pass.Setup(_material, settings);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
    }

    private class CartoonOutlinePass : ScriptableRenderPass
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
        private static readonly int NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
        private static readonly int EdgeThicknessId = Shader.PropertyToID("_EdgeThickness");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int GrazingAngleBiasId = Shader.PropertyToID("_GrazingAngleBias");
        private static readonly int GrazingAngleBiasScaleId = Shader.PropertyToID("_GrazingAngleBiasScale");
        private static readonly int SourceTexelSizeId = Shader.PropertyToID("_SourceTexelSize");

        private Material _material;
        private Settings _settings;

        public CartoonOutlinePass()
        {
            profilingSampler = new ProfilingSampler("Cartoon Outline");
            ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
        }

        public void Setup(Material material, Settings settings)
        {
            _material = material;
            _settings = settings;
            renderPassEvent = settings.renderPassEvent;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            var source = resourceData.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_CartoonOutlineTarget";
            desc.clearBuffer = false;
            desc.depthBufferBits = 0;
            var destination = renderGraph.CreateTexture(desc);

            _material.SetColor(OutlineColorId, _settings.outlineColor);
            _material.SetFloat(DepthThresholdId, _settings.depthThreshold);
            _material.SetFloat(NormalThresholdId, _settings.normalThreshold);
            _material.SetFloat(EdgeThicknessId, _settings.edgeThickness);
            _material.SetFloat(EdgeSoftnessId, _settings.edgeSoftness);
            _material.SetFloat(GrazingAngleBiasId, _settings.grazingAngleBias);
            _material.SetFloat(GrazingAngleBiasScaleId, _settings.grazingAngleBiasScale);
            _material.SetVector(SourceTexelSizeId, new Vector4(1f / desc.width, 1f / desc.height, desc.width, desc.height));

            var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0);
            renderGraph.AddBlitPass(blitParams, "Cartoon Outline Edge Detection");

            resourceData.cameraColor = destination;
        }
    }
}
