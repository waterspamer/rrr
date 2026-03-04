using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using System.Text;
using UnityEngine.Experimental.Rendering;

public class ReflectRenderPass : ScriptableRendererFeature
{
    public RenderPassEvent renderPass = RenderPassEvent.AfterRenderingSkybox;
    private const string TraceShaderName = "Hidden/RRR/SSRTrace";
    private const string HiZPyramidShaderName = "Hidden/RRR/SSRHiZPyramid";
    private Material traceMaterial;
    private Material hiZPyramidMaterial;
    private SsrPass ssrPass;

    public override void Create()
    {
        if (traceMaterial == null)
        {
            Shader traceShader = Shader.Find(TraceShaderName);
            if (traceShader != null)
                traceMaterial = CoreUtils.CreateEngineMaterial(traceShader);
        }
        if (hiZPyramidMaterial == null)
        {
            Shader hiZShader = Shader.Find(HiZPyramidShaderName);
            if (hiZShader != null)
                hiZPyramidMaterial = CoreUtils.CreateEngineMaterial(hiZShader);
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

        if (settings.hierarchicalTraversal.value && hiZPyramidMaterial == null)
            Debug.LogWarning("ReflectRenderPass: Hierarchical traversal is enabled but Hidden/RRR/SSRHiZPyramid is missing. Falling back to linear SSR trace.");

        ssrPass.Setup(traceMaterial, hiZPyramidMaterial, settings);
        renderer.EnqueuePass(ssrPass);
    }

    protected override void Dispose(bool disposing)
    {
        ssrPass?.Dispose();
        CoreUtils.Destroy(traceMaterial);
        CoreUtils.Destroy(hiZPyramidMaterial);
        traceMaterial = null;
        hiZPyramidMaterial = null;
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
        private static readonly int DepthCrossToleranceId = Shader.PropertyToID("_DepthCrossTolerance");
        private static readonly int MinHitDistanceId = Shader.PropertyToID("_MinHitDistance");
        private static readonly int UseHierarchicalTraversalId = Shader.PropertyToID("_UseHierarchicalTraversal");
        private static readonly int UseDualLayerThicknessId = Shader.PropertyToID("_UseDualLayerThickness");
        private static readonly int DualLayerRadiusId = Shader.PropertyToID("_DualLayerRadius");
        private static readonly int MissFadeId = Shader.PropertyToID("_MissFade");
        private static readonly int TraceQualityId = Shader.PropertyToID("_TraceQuality");
        private static readonly int SurfaceBiasId = Shader.PropertyToID("_SurfaceBias");
        private static readonly int FadeDistanceId = Shader.PropertyToID("_FadeDistance");
        private static readonly int FresnelFadeId = Shader.PropertyToID("_FresnelFade");
        private static readonly int ReflectionIntensityId = Shader.PropertyToID("_ReflectionIntensity");
        private static readonly int ResolveRadiusId = Shader.PropertyToID("_ResolveRadius");
        private static readonly int DebugReflectionOnlyId = Shader.PropertyToID("_DebugReflectionOnly");
        private static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");
        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int GBuffer2Id = Shader.PropertyToID("_GBuffer2");
        private static readonly int HiZPyramidId = Shader.PropertyToID("_HiZPyramid");
        private static readonly int HiZMipCountId = Shader.PropertyToID("_HiZMipCount");
        private static readonly int DeferredInputActiveId = Shader.PropertyToID("_DeferredInputActive");
        private const string DeferredInputKeyword = "_SSR_DEFERRED_INPUT";
        private static readonly MaterialPropertyBlock SharedPropertyBlock = new MaterialPropertyBlock();
        private static int s_LastLogFrame = -1000000;
        private Material traceMaterial;
        private Material hiZPyramidMaterial;
        private ScreenSpaceReflections settings;
#if URP_COMPATIBILITY_MODE
        private RTHandle reflectionSource;
#endif

        public void Setup(Material trace, Material hiZPyramid, ScreenSpaceReflections volumeSettings)
        {
            traceMaterial = trace;
            hiZPyramidMaterial = hiZPyramid;
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
            float thickness = Mathf.Max(0.02f, settings.thickness.value);
            float missFade = Mathf.Max(0.5f, settings.missFade.value);

            traceMaterial.SetFloat(MaxDistanceId, settings.maxDistance.value);
            traceMaterial.SetFloat(RayStepId, rayStep);
            traceMaterial.SetFloat(MaxStepsId, maxSteps);
            traceMaterial.SetFloat(BinaryStepsId, settings.refinementSamples.value);
            traceMaterial.SetFloat(ThicknessId, thickness);
            traceMaterial.SetFloat(DepthCrossToleranceId, settings.depthCrossTolerance.value);
            traceMaterial.SetFloat(MinHitDistanceId, settings.minHitDistance.value);
            traceMaterial.SetFloat(UseHierarchicalTraversalId, settings.hierarchicalTraversal.value ? 1.0f : 0.0f);
            traceMaterial.SetFloat(UseDualLayerThicknessId, settings.dualLayerThickness.value ? 1.0f : 0.0f);
            traceMaterial.SetFloat(DualLayerRadiusId, settings.dualLayerRadius.value);
            traceMaterial.SetFloat(MissFadeId, missFade);
            traceMaterial.SetFloat(TraceQualityId, quality);
            traceMaterial.SetFloat(SurfaceBiasId, Mathf.Max(0.002f, thickness * 0.125f));
            traceMaterial.SetFloat(FadeDistanceId, settings.fadeDistance.value);
            traceMaterial.SetFloat(FresnelFadeId, settings.fresnelFade.value);
            traceMaterial.SetFloat(ReflectionIntensityId, settings.reflectionIntensity.value);
            traceMaterial.SetFloat(ResolveRadiusId, resolveRadius);
            traceMaterial.SetFloat(DebugReflectionOnlyId, settings.debugReflectionOnly.value ? 1.0f : 0.0f);
            traceMaterial.SetFloat(DebugModeId, settings.debugMode.value);
        }

        private void SetDeferredInputKeyword(bool enabled)
        {
            if (traceMaterial == null)
                return;

            if (enabled)
                traceMaterial.EnableKeyword(DeferredInputKeyword);
            else
                traceMaterial.DisableKeyword(DeferredInputKeyword);

            traceMaterial.SetFloat(DeferredInputActiveId, enabled ? 1.0f : 0.0f);
        }

        private static void AppendTextureInfo(StringBuilder sb, RenderGraph renderGraph, string name, TextureHandle handle)
        {
            if (!handle.IsValid())
            {
                sb.Append(name).Append(": invalid").AppendLine();
                return;
            }

            TextureDesc desc = renderGraph.GetTextureDesc(handle);
            sb.Append(name)
              .Append(": valid ")
              .Append(desc.width).Append('x').Append(desc.height)
              .Append(" msaa=").Append(desc.msaaSamples)
              .Append(" format=").Append(desc.format)
              .Append(" clear=").Append(desc.clearBuffer)
              .Append(" name=").Append(desc.name)
              .AppendLine();
        }

        private void LogResourcesIfNeeded(
            RenderGraph renderGraph,
            UniversalResourceData resources,
            UniversalRenderingData renderingData,
            bool hasDeferredGBuffer)
        {
            if (settings == null || !settings.debugLogResources.value)
                return;

            int interval = Mathf.Max(1, settings.debugLogInterval.value);
            int frame = Time.frameCount;
            if (frame - s_LastLogFrame < interval)
                return;
            s_LastLogFrame = frame;

            StringBuilder sb = new StringBuilder(1024);
            sb.Append("[SSR] frame=").Append(frame)
              .Append(" mode=").Append(renderingData.renderingMode)
              .Append(" deferredInput=").Append(hasDeferredGBuffer)
              .AppendLine();

            AppendTextureInfo(sb, renderGraph, "activeColor", resources.activeColorTexture);
            AppendTextureInfo(sb, renderGraph, "activeDepth", resources.activeDepthTexture);
            AppendTextureInfo(sb, renderGraph, "cameraDepthTexture", resources.cameraDepthTexture);
            AppendTextureInfo(sb, renderGraph, "cameraNormalsTexture", resources.cameraNormalsTexture);

            if (resources.gBuffer == null)
            {
                sb.Append("gBuffer: null").AppendLine();
            }
            else
            {
                sb.Append("gBuffer.Length=").Append(resources.gBuffer.Length).AppendLine();
                for (int i = 0; i < resources.gBuffer.Length; i++)
                    AppendTextureInfo(sb, renderGraph, $"gBuffer[{i}]", resources.gBuffer[i]);
            }

            Debug.Log(sb.ToString());
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
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            if (!resources.activeColorTexture.IsValid() || cameraData.isPreviewCamera)
                return;

            ApplySettingsToMaterial();
            bool isDeferred = renderingData.renderingMode == RenderingMode.Deferred;
            bool hasDeferredGBuffer = isDeferred
                                      && resources.gBuffer != null
                                      && resources.gBuffer.Length > 2
                                      && resources.gBuffer[1].IsValid()
                                      && resources.gBuffer[2].IsValid();
            SetDeferredInputKeyword(hasDeferredGBuffer);
            LogResourcesIfNeeded(renderGraph, resources, renderingData, hasDeferredGBuffer);

            bool useHierarchicalTraversal = settings.hierarchicalTraversal.value && hiZPyramidMaterial != null && resources.cameraDepthTexture.IsValid();
            traceMaterial.SetFloat(UseHierarchicalTraversalId, useHierarchicalTraversal ? 1.0f : 0.0f);

            TextureHandle hiZPyramid = TextureHandle.nullHandle;
            int hiZMipCount = 1;
            if (useHierarchicalTraversal)
            {
                TextureDesc depthDesc = renderGraph.GetTextureDesc(resources.cameraDepthTexture);
                TextureDesc hiZDesc = new TextureDesc(depthDesc)
                {
                    name = "_RrrSsrHiZPyramid",
                    clearBuffer = false,
                    msaaSamples = MSAASamples.None,
                    format = GraphicsFormat.R32_SFloat,
                    useMipMap = true,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Point
                };

                int maxDim = Mathf.Max(hiZDesc.width, hiZDesc.height);
                hiZMipCount = Mathf.Max(1, (int)Mathf.Floor(Mathf.Log(maxDim, 2.0f)) + 1);
                hiZPyramid = renderGraph.CreateTexture(hiZDesc);

                var copyMip0 = new RenderGraphUtils.BlitMaterialParameters(resources.cameraDepthTexture, hiZPyramid, hiZPyramidMaterial, 0)
                {
                    sourceMip = 0,
                    destinationMip = 0,
                    numMips = 1
                };
                renderGraph.AddBlitPass(copyMip0, "SSR HiZ Mip0");

                for (int mip = 1; mip < hiZMipCount; mip++)
                {
                    var downsampleMip = new RenderGraphUtils.BlitMaterialParameters(hiZPyramid, hiZPyramid, hiZPyramidMaterial, 1)
                    {
                        sourceMip = mip - 1,
                        destinationMip = mip,
                        numMips = 1
                    };
                    renderGraph.AddBlitPass(downsampleMip, $"SSR HiZ Mip{mip}");
                }
            }

            TextureDesc sourceDesc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            sourceDesc.name = "_RrrSsrSourceRG";
            sourceDesc.clearBuffer = false;
            TextureHandle source = renderGraph.CreateTexture(sourceDesc);

            renderGraph.AddBlitPass(resources.activeColorTexture, source, Vector2.one, Vector2.zero, passName: "SSR Copy Source");
            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>("SSR Trace Composite", out PassData passData, ProfilingSampler))
            {
                passData.material = traceMaterial;
                passData.source = source;
                passData.gBuffer = resources.gBuffer;
                passData.hiZPyramid = hiZPyramid;
                passData.hiZMipCount = hiZMipCount;

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
                if (hiZPyramid.IsValid())
                    builder.UseTexture(hiZPyramid, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext rgContext) =>
                {
                    SharedPropertyBlock.Clear();
                    SharedPropertyBlock.SetTexture(BlitTextureId, data.source);
                    SharedPropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
                    if (data.gBuffer != null && data.gBuffer.Length > 2)
                    {
                        data.material.SetTexture(GBuffer0Id, data.gBuffer[0]);
                        data.material.SetTexture(GBuffer1Id, data.gBuffer[1]);
                        data.material.SetTexture(GBuffer2Id, data.gBuffer[2]);
                    }
                    if (data.hiZPyramid.IsValid())
                        data.material.SetTexture(HiZPyramidId, data.hiZPyramid);
                    data.material.SetFloat(HiZMipCountId, data.hiZMipCount);
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
            public TextureHandle[] gBuffer;
            public TextureHandle hiZPyramid;
            public int hiZMipCount;
        }
    }
}
