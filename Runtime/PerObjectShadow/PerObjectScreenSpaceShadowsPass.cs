using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal class PerObjectScreenSpaceShadowsPass : ScriptableRenderPass
    {
        private static class PerObjectShadowProjectorConstant
        {
            public static int _PerObjectWorldToShadow;
            public static int _PerObjectUVScaleOffset;
            public static int _PerObjectShadowParams;
            public static int _PerObjectShadowScaledScreenParams;
            public static int _PerObjectScreenSpaceShadowmapTexture;
        }
        // Profiling tag
        private static string m_ProfilerTag = "PerObjectScreenSpaceShadows";
        private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        // Public Variables

        // Private Variables
        private PerObjectShadowSettings m_CurrentSettings;
        private RTHandle m_ScreenSpaceShadowTexture;
        private ObjectShadowDrawSystem m_DrawSystem;
        private ShaderTagId m_ShaderTagId;

        // Constants


        // Statics


        internal PerObjectScreenSpaceShadowsPass(ObjectShadowDrawSystem drawSystem)
        {
            m_DrawSystem = drawSystem;
            m_ShaderTagId = new ShaderTagId(PerObjectShadowShaderPassNames.PerObjectScreenSpaceShadow);

            PerObjectShadowProjectorConstant._PerObjectWorldToShadow = Shader.PropertyToID("_PerObjectWorldToShadow");
            PerObjectShadowProjectorConstant._PerObjectUVScaleOffset = Shader.PropertyToID("_PerObjectUVScaleOffset");
            PerObjectShadowProjectorConstant._PerObjectShadowParams = Shader.PropertyToID("_PerObjectShadowParams");
            PerObjectShadowProjectorConstant._PerObjectShadowScaledScreenParams = Shader.PropertyToID("_PerObjectShadowScaledScreenParams");
            PerObjectShadowProjectorConstant._PerObjectScreenSpaceShadowmapTexture = Shader.PropertyToID("_PerObjectScreenSpaceShadowmapTexture");
        }

        /// <summary>
        /// Cleans up resources used by the pass.
        /// </summary>
        public void Dispose()
        {
            m_ScreenSpaceShadowTexture?.Release();
        }

        internal bool Setup(PerObjectShadowSettings settings)
        {
            m_CurrentSettings = settings;

            ConfigureInput(ScriptableRenderPassInput.Depth);

            return true;
        }

        /// <inheritdoc/>
        //public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        //{
        //    var desc = renderingData.cameraData.cameraTargetDescriptor;
        //    int downSampleScale = m_CurrentSettings.GetScreenSpaceShadowTexScale();
        //    desc.width = desc.width >> downSampleScale;
        //    desc.height = desc.height >> downSampleScale;
        //    desc.useMipMap = false;
        //    desc.depthBufferBits = 0;
        //    desc.msaaSamples = 1;
        //    desc.graphicsFormat = GraphicsFormat.R8_UNorm;

        //    RenderingUtils.ReAllocateIfNeeded(ref m_ScreenSpaceShadowTexture, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_PerObjectScreenSpaceShadowmapTexture");
        //    cmd.SetGlobalTexture(m_ScreenSpaceShadowTexture.name, m_ScreenSpaceShadowTexture.nameID);

        //    ConfigureTarget(m_ScreenSpaceShadowTexture);
        //    ConfigureClear(ClearFlag.Color, Color.white);
        //}


        ///// <inheritdoc/>
        //[Obsolete(DeprecationMessage.CompatibilityScriptingAPIObsolete, false)]
        //public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        //{
        //    ContextContainer frameData = renderingData.frameData;
        //    UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
        //    var cmd = universalRenderingData.commandBuffer;
        //    using (new ProfilingScope(cmd, m_ProfilingSampler))
        //    {
        //        // Params
        //        float softShadowQuality = (float)m_CurrentSettings.GetSoftShadowQuality();
        //        float shadowStrength = 1.0f;
        //        int downSampleScale = m_CurrentSettings.GetScreenSpaceShadowTexScale();
        //        cmd.SetGlobalVector(PerObjectShadowProjectorConstant._PerObjectShadowParams, new Vector4(softShadowQuality, shadowStrength, downSampleScale, 0));

        //        cmd.SetKeyword(ShaderGlobalKeywords.PerObjectScreenSpaceShadow, true);

        //        // Draw System
        //        Vector2 rtSize = new Vector2(m_ScreenSpaceShadowTexture.rt.width, m_ScreenSpaceShadowTexture.rt.height);
        //        m_DrawSystem.Execute(cmd, rtSize);

        //        CullingResults cullingResults = renderingData.cullResults;
        //        SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
        //        DrawingSettings drawingSettings = CreateDrawingSettings(m_ShaderTagId, ref renderingData, sortingCriteria);
        //        FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1);

        //        context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
        //    }
        //}

        /// <summary>
        /// Clear Keyword.
        /// </summary>
        /// <param name="cmd"></param>
        public void ClearRenderingState(CommandBuffer cmd)
        {
            cmd.SetKeyword(ShaderGlobalKeywords.PerObjectScreenSpaceShadow, false);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.SetKeyword(ShaderGlobalKeywords.PerObjectScreenSpaceShadow, false);
        }
        private class PassData
        {
            internal RendererListHandle rendererListHandle;
            internal ObjectShadowDrawSystem drawSystem;
            internal Vector2 rtSize;
            internal Vector4 perObjectShadowParams;
        }

        private void InitRendererLists(ContextContainer frameData, ref PassData passData, RenderGraph renderGraph)
        {
            // Access the relevant frame data from the Universal Render Pipeline
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var sortFlags = cameraData.defaultOpaqueSortFlags;
            RenderQueueRange renderQueueRange = RenderQueueRange.opaque;
            FilteringSettings filterSettings = new FilteringSettings(renderQueueRange);
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagId, universalRenderingData, cameraData, lightData, sortFlags);

            var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, filterSettings);
            passData.rendererListHandle = renderGraph.CreateRendererList(param);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            var desc = cameraData.cameraTargetDescriptor;
            int downSampleScale = m_CurrentSettings.GetScreenSpaceShadowTexScale();
            desc.width = desc.width >> downSampleScale;
            desc.height = desc.height >> downSampleScale;
            desc.useMipMap = false;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R8_UNorm;

            // Create RenderGraphTexture, will clear before render this pass.
            var screenSpaceShadowMapTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, 
                "_PerObjectScreenSpaceShadowmapTexture", true, Color.white, FilterMode.Point, TextureWrapMode.Clamp);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("PerObject ScreenSpace Shadows", out var passData, m_ProfilingSampler))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                
                // Params
                float softShadowQuality = (float)m_CurrentSettings.GetSoftShadowQuality();
                float shadowStrength = 1.0f;
                passData.perObjectShadowParams = new Vector4(softShadowQuality, shadowStrength, downSampleScale, 0);
                passData.drawSystem = m_DrawSystem;
                passData.rtSize = new Vector2(desc.width, desc.height);

                InitRendererLists(frameData, ref passData, renderGraph);

                // Shader keyword changes are considered as global state modifications
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                if (frameData.Contains<PerObjectShadowCasterPass.PerObjectShadowMapRefData>())
                {
                    var shadowMapRefData = frameData.Get<PerObjectShadowCasterPass.PerObjectShadowMapRefData>();
                    builder.UseTexture(shadowMapRefData.perObjectShadowMap);
                }

                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture);

                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderAttachment(screenSpaceShadowMapTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                if (screenSpaceShadowMapTexture.IsValid())
                    builder.SetGlobalTextureAfterPass(screenSpaceShadowMapTexture, PerObjectShadowProjectorConstant._PerObjectScreenSpaceShadowmapTexture);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    // Draw System
                    data.drawSystem?.Execute(context.cmd, data.rtSize);

                    context.cmd.SetGlobalVector(PerObjectShadowProjectorConstant._PerObjectShadowParams, data.perObjectShadowParams);
                    context.cmd.SetKeyword(ShaderGlobalKeywords.PerObjectScreenSpaceShadow, true);

                    context.cmd.DrawRendererList(data.rendererListHandle);
                });
            }

        }


    }

}