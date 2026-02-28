using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

    public class ReflectRenderPass : ScriptableRendererFeature
{
    public RenderPassEvent renderPass = RenderPassEvent.BeforeRenderingPostProcessing;
    private const string TraceShaderName = "Hidden/RRR/SSRTrace";
    private Material traceMaterial;
    private SsrPass ssrPass;

    public override void Create()
    {
        if (traceMaterial == null)
        {
            Shader traceShader = Shader.Find(TraceShaderName);
            if (traceShader != null)
                traceMaterial = CoreUtils.CreateEngineMaterial(traceShader);
        }

        ssrPass ??= new SsrPass();
        ssrPass.renderPassEvent = renderPass;
        ssrPass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (traceMaterial == null)
        {
            Debug.LogWarning("ReflectRenderPass: SSR shader is missing. Expected Hidden/RRR/SSRTrace.");
            return;
        }

        if (!renderingData.postProcessingEnabled)
            return;

        ScreenSpaceReflections settings = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflections>();
        if (settings == null || !settings.IsActive())
            return;

        ssrPass.Setup(traceMaterial, settings);
        renderer.EnqueuePass(ssrPass);
    }

    protected override void Dispose(bool disposing)
    {
        ssrPass?.Dispose();
        CoreUtils.Destroy(traceMaterial);
        traceMaterial = null;
    }

    private sealed class SsrPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("SSR Trace+Composite");
        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int MaxDistanceId = Shader.PropertyToID("_MaxDistance");
        private static readonly int RayStepId = Shader.PropertyToID("_RayStep");
        private static readonly int MaxStepsId = Shader.PropertyToID("_MaxSteps");
        private static readonly int BinaryStepsId = Shader.PropertyToID("_BinarySteps");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int MissFadeId = Shader.PropertyToID("_MissFade");
        private static readonly int TraceQualityId = Shader.PropertyToID("_TraceQuality");
        private static readonly int SurfaceBiasId = Shader.PropertyToID("_SurfaceBias");
        private static readonly int FadeDistanceId = Shader.PropertyToID("_FadeDistance");
        private static readonly int FresnelFadeId = Shader.PropertyToID("_FresnelFade");
        private static readonly int ResolveRadiusId = Shader.PropertyToID("_ResolveRadius");
        private static readonly int DebugReflectionOnlyId = Shader.PropertyToID("_DebugReflectionOnly");
        private static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");
        private static readonly MaterialPropertyBlock SharedPropertyBlock = new MaterialPropertyBlock();
        private Material traceMaterial;
        private ScreenSpaceReflections settings;
#if URP_COMPATIBILITY_MODE
        private RTHandle reflectionSource;
#endif

        public void Setup(Material trace, ScreenSpaceReflections volumeSettings)
        {
            traceMaterial = trace;
            settings = volumeSettings;
        }

        private void ApplySettingsToMaterial()
        {
            if (traceMaterial == null || settings == null)
                return;

            float quality = Mathf.Clamp01(settings.traceQuality.value);
            float maxSteps = Mathf.Round(Mathf.Lerp(48.0f, 192.0f, quality));
            float rayStep = Mathf.Lerp(0.45f, 0.08f, quality);
            float resolveRadius = Mathf.Lerp(1.8f, 0.45f, quality);

            traceMaterial.SetFloat(MaxDistanceId, settings.maxDistance.value);
            traceMaterial.SetFloat(RayStepId, rayStep);
            traceMaterial.SetFloat(MaxStepsId, maxSteps);
            traceMaterial.SetFloat(BinaryStepsId, settings.refinementSamples.value);
            traceMaterial.SetFloat(ThicknessId, settings.thickness.value);
            traceMaterial.SetFloat(MissFadeId, settings.missFade.value);
            traceMaterial.SetFloat(TraceQualityId, quality);
            traceMaterial.SetFloat(SurfaceBiasId, Mathf.Max(0.002f, settings.thickness.value * 0.125f));
            traceMaterial.SetFloat(FadeDistanceId, settings.fadeDistance.value);
            traceMaterial.SetFloat(FresnelFadeId, settings.fresnelFade.value);
            traceMaterial.SetFloat(ResolveRadiusId, resolveRadius);
            traceMaterial.SetFloat(DebugReflectionOnlyId, settings.debugReflectionOnly.value ? 1.0f : 0.0f);
            traceMaterial.SetFloat(DebugModeId, settings.debugMode.value);
        }

#if URP_COMPATIBILITY_MODE
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor sourceDesc = renderingData.cameraData.cameraTargetDescriptor;
            sourceDesc.depthBufferBits = 0;
            sourceDesc.msaaSamples = 1;
            sourceDesc.colorFormat = RenderTextureFormat.DefaultHDR;
            RenderingUtils.ReAllocateIfNeeded(ref reflectionSource, sourceDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_RrrSsrSource");
        }
#endif

#if URP_COMPATIBILITY_MODE
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (traceMaterial == null || settings == null)
                return;
            if (renderingData.cameraData.isPreviewCamera)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                RTHandle colorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

                Blitter.BlitCameraTexture(cmd, colorTarget, reflectionSource);
                ApplySettingsToMaterial();
                Blitter.BlitCameraTexture(cmd, reflectionSource, colorTarget, traceMaterial, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#endif

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (traceMaterial == null || settings == null)
                return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (!resources.activeColorTexture.IsValid() || cameraData.isPreviewCamera)
                return;

            ApplySettingsToMaterial();

            TextureDesc sourceDesc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            sourceDesc.name = "_RrrSsrSourceRG";
            sourceDesc.clearBuffer = false;
            TextureHandle source = renderGraph.CreateTexture(sourceDesc);

            renderGraph.AddBlitPass(resources.activeColorTexture, source, Vector2.one, Vector2.zero, passName: "SSR Copy Source");
            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>("SSR Trace Composite", out PassData passData, ProfilingSampler))
            {
                passData.material = traceMaterial;
                passData.source = source;

                builder.UseTexture(source, AccessFlags.Read);
                if (resources.cameraDepthTexture.IsValid())
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                if (resources.cameraNormalsTexture.IsValid())
                    builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                if (resources.gBuffer != null)
                {
                    if (resources.gBuffer.Length > 0 && resources.gBuffer[0].IsValid())
                        builder.UseTexture(resources.gBuffer[0], AccessFlags.Read);
                    if (resources.gBuffer.Length > 1 && resources.gBuffer[1].IsValid())
                        builder.UseTexture(resources.gBuffer[1], AccessFlags.Read);
                    if (resources.gBuffer.Length > 2 && resources.gBuffer[2].IsValid())
                        builder.UseTexture(resources.gBuffer[2], AccessFlags.Read);
                }
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext rgContext) =>
                {
                    SharedPropertyBlock.Clear();
                    SharedPropertyBlock.SetTexture(BlitTextureId, data.source);
                    SharedPropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
                    rgContext.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, SharedPropertyBlock);
                });
            }
        }

        public void Dispose()
        {
#if URP_COMPATIBILITY_MODE
            reflectionSource?.Release();
#endif
        }

        private sealed class PassData
        {
            public Material material;
            public TextureHandle source;
        }
    }
}
