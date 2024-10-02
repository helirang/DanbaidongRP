using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal class ScreenSpaceDirectionalShadowsPass : ScriptableRenderPass
    {
        // Public Variables

        // Private Variables
        private ComputeShader m_ScreenSpaceDirectionalShadowsCS;
        private int m_ClassifyTilesKernel;
        private int m_SSShadowsKernel;
        private int m_BilateralHKernel;
        private int m_BilateralVKernel;

        // Constants
        private const int c_screenSpaceShadowsTileSize = 16;

        // Statics


        public ScreenSpaceDirectionalShadowsPass(RenderPassEvent evt, ComputeShader ssDirectionalShadowsCS)
        {
            base.renderPassEvent = evt;
            m_ScreenSpaceDirectionalShadowsCS = ssDirectionalShadowsCS;

            m_ClassifyTilesKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("ShadowClassifyTiles");
            m_SSShadowsKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("ScreenSpaceShadowmap");
            m_BilateralHKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("BilateralFilterH");
            m_BilateralVKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("BilateralFilterV");
        }


        private class PassData
        {
            // Compute shader
            internal ComputeShader cs;
            internal int classifyTilesKernel;
            internal int shadowmapKernel;
            internal int bilateralHKernel;
            internal int bilateralVKernel;

            internal int numTilesX;
            internal int numTilesY;

            // Compute Buffers
            internal BufferHandle dispatchIndirectBuffer;
            internal BufferHandle tileListBuffer;

            // Texture
            internal TextureHandle dirShadowmapTex;
            internal TextureHandle screenSpaceShadowmapTex;
            internal Vector2Int screenSpaceShadowmapSize;
            internal TextureHandle normalGBuffer;

            internal int camHistoryFrameCount;

            // Ray Tracing
            internal bool requireRayTracing;
            internal RayTracingShader rtrtShader;
            internal RayTracingAccelerationStructure rtas;
            internal uint dispatchRaySizeX;
            internal uint dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal TextureHandle stencilHandle;
        }

        /// <summary>
        /// Initialize the shared pass data.
        /// </summary>
        /// <param name="passData"></param>
        private void InitPassData(RenderGraph renderGraph, PassData passData, UniversalCameraData cameraData, UniversalResourceData resourceData, int historyFramCount)
        {
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.colorFormat = RenderTextureFormat.RG32;
            desc.depthBufferBits = 0;
            desc.enableRandomWrite = true;

            passData.cs = m_ScreenSpaceDirectionalShadowsCS;
            passData.classifyTilesKernel = m_ClassifyTilesKernel;
            passData.shadowmapKernel = m_SSShadowsKernel;
            passData.bilateralHKernel = m_BilateralHKernel;
            passData.bilateralVKernel = m_BilateralVKernel;

            passData.camHistoryFrameCount = historyFramCount;

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            passData.numTilesX = RenderingUtils.DivRoundUp(width, c_screenSpaceShadowsTileSize);
            passData.numTilesY = RenderingUtils.DivRoundUp(height, c_screenSpaceShadowsTileSize);

            var bufferSystem = GraphicsBufferSystem.instance;
            var dispatchIndirectBuffer = bufferSystem.GetGraphicsBuffer<uint>(GraphicsBufferSystemBufferID.ScreenSpaceShadowIndirect, 3, "dispatchIndirectBuffer", GraphicsBuffer.Target.IndirectArguments);
            passData.dispatchIndirectBuffer = renderGraph.ImportBuffer(dispatchIndirectBuffer);
            var tileListBufferDesc = new BufferDesc(passData.numTilesX * passData.numTilesY, sizeof(uint), "tileListBuffer");
            passData.tileListBuffer = renderGraph.CreateBuffer(tileListBufferDesc);


            passData.dirShadowmapTex = resourceData.directionalShadowsTexture;
            passData.screenSpaceShadowmapTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceShadowmapTexture", true, Color.white);
            passData.screenSpaceShadowmapSize = new Vector2Int(desc.width, desc.height);

            passData.normalGBuffer = resourceData.gBuffer[2]; // Normal GBuffer
        }

        private void InitRayTracingPassData(RenderGraph renderGraph, PassData passData, UniversalCameraData cameraData, UniversalResourceData resourceData)
        {
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<Shadows>();
            if (volumeSettings == null)
            {
                passData.requireRayTracing = false;
                return;
            }

            passData.requireRayTracing &= volumeSettings.rayTracing.value;

            if (passData.requireRayTracing)
            {
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<UniversalRenderPipelineRuntimeShaders>();
                passData.rtrtShader = runtimeShaders.rayTracingShadows;
                passData.rtas = cameraData.rayTracingSystem.RequestAccelerationStructure();

                var width = cameraData.cameraTargetDescriptor.width;
                var height = cameraData.cameraTargetDescriptor.height;
                passData.dispatchRaySizeX = (uint)width;
                passData.dispatchRaySizeY = (uint)height;

                passData.stencilHandle = resourceData.activeDepthTexture;

                // RayTracing constant buffer
                {
                    var rayTracingSettings = stack.GetComponent<RayTracingSettings>();

                    passData.rayTracingCB = cameraData.rayTracingSystem.GetShaderVariablesRaytracingCB(new Vector2Int(width, height), rayTracingSettings);
                    passData.rayTracingCB._RaytracingRayMaxLength = Mathf.Min(volumeSettings.dirShadowsRayLength.value, rayTracingSettings.directionalShadowRayLength.value);
                    passData.rayTracingCB._RayTracingClampingFlag = 1;
                    passData.rayTracingCB._RaytracingIntensityClamp = 1.0f;
                    passData.rayTracingCB._RaytracingPreExposition = 0;
                    passData.rayTracingCB._RayTracingDiffuseLightingOnly = 0;
                    passData.rayTracingCB._RayTracingAPVRayMiss = 0;
                    passData.rayTracingCB._RayTracingRayMissFallbackHierarchy = 0;
                    passData.rayTracingCB._RayTracingRayMissUseAmbientProbeAsSky = 0;
                    passData.rayTracingCB._RayTracingLastBounceFallbackHierarchy = 0;
                    passData.rayTracingCB._RayTracingAmbientProbeDimmer = 1.0f;
                }

            }
        }

        private static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            // Ray Tracing Shadows
            if (data.requireRayTracing)
            {
                using (new ProfilingScope(cmd, new ProfilingSampler("RayTracingShadows")))
                {
                    // Define the shader pass to use for the reflection pass
                    cmd.SetRayTracingShaderPass(data.rtrtShader, "VisibilityDXR");

                    // Set the acceleration structure for the pass
                    cmd.SetRayTracingAccelerationStructure(data.rtrtShader, "_RaytracingAccelerationStructure", data.rtas);

                    // SetConstantBuffer
                    ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                    // SetTextures
                    cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._RayTracingShadowsTextureRW, data.screenSpaceShadowmapTex);
                    cmd.SetGlobalTexture(ShaderConstants._StencilTexture, data.stencilHandle, RenderTextureSubElement.Stencil);

                    cmd.DispatchRays(data.rtrtShader, "SingleRayGen", data.dispatchRaySizeX, data.dispatchRaySizeY, 1);
                }
            }
            else
            {
                cmd.SetComputeFloatParam(data.cs, ShaderConstants._CamHistoryFrameCount, data.camHistoryFrameCount);

                // BuildIndirect
                {
                    cmd.SetComputeBufferParam(data.cs, data.classifyTilesKernel, ShaderConstants.g_DispatchIndirectBuffer, data.dispatchIndirectBuffer);
                    cmd.SetComputeBufferParam(data.cs, data.classifyTilesKernel, ShaderConstants.g_TileList, data.tileListBuffer);

                    cmd.SetComputeTextureParam(data.cs, data.classifyTilesKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                    cmd.SetComputeTextureParam(data.cs, data.classifyTilesKernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);

                    cmd.DispatchCompute(data.cs, data.classifyTilesKernel, data.numTilesX, data.numTilesY, 1);
                }

                // PCSS ScreenSpaceShadowmap
                {
                    cmd.SetComputeTextureParam(data.cs, data.shadowmapKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                    cmd.SetComputeTextureParam(data.cs, data.shadowmapKernel, ShaderConstants._PCSSTexture, data.screenSpaceShadowmapTex);

                    // Indirect buffer & dispatch
                    cmd.SetComputeBufferParam(data.cs, data.shadowmapKernel, ShaderConstants.g_TileList, data.tileListBuffer);
                    cmd.DispatchCompute(data.cs, data.shadowmapKernel, data.dispatchIndirectBuffer, argsOffset: 0);
                }
            }

        }

        internal TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData)
        {
            int historyFramCount = 0;
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(frameData.Get<UniversalCameraData>().camera);
            if (historyRTSystem != null)
                historyFramCount = historyRTSystem.historyFrameCount;

            using (var builder = renderGraph.AddComputePass<PassData>("Render SS Shadow", out var passData, ProfilingSampler.Get(URPProfileId.RenderSSShadow)))
            {
                // Access resources
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();

                // Ray Tracing
                passData.requireRayTracing = cameraData.supportedRayTracing && cameraData.rayTracingSystem.GetRayTracingState();
                InitRayTracingPassData(renderGraph, passData, cameraData, resourceData);
                shadowData.rayTracingShadowsEnabled = passData.requireRayTracing;

                // Setup passData
                InitPassData(renderGraph, passData, cameraData, resourceData, historyFramCount);

                // Setup builder state
                builder.UseBuffer(passData.dispatchIndirectBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.tileListBuffer, AccessFlags.ReadWrite);
                builder.UseTexture(passData.dirShadowmapTex, AccessFlags.Read);
                builder.UseTexture(passData.screenSpaceShadowmapTex, AccessFlags.ReadWrite);
                builder.UseTexture(passData.normalGBuffer, AccessFlags.Read);
                if (passData.requireRayTracing)
                {
                    builder.UseTexture(passData.stencilHandle, AccessFlags.Read);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(passData.requireRayTracing);
                //builder.EnableAsyncCompute(true);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context);
                });

                return passData.screenSpaceShadowmapTex;
            }
        }

        static class ShaderConstants
        {
            public static readonly int g_DispatchIndirectBuffer = Shader.PropertyToID("g_DispatchIndirectBuffer");
            public static readonly int g_TileList = Shader.PropertyToID("g_TileList");

            public static readonly int _DirShadowmapTexture = Shader.PropertyToID("_DirShadowmapTexture");
            public static readonly int _SSDirShadowmapTexture = Shader.PropertyToID("_SSDirShadowmapTexture");
            public static readonly int _ScreenSpaceShadowmapTexture = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
            public static readonly int _PCSSTexture = Shader.PropertyToID("_PCSSTexture");
            public static readonly int _BilateralTexture = Shader.PropertyToID("_BilateralTexture");
            public static readonly int _CamHistoryFrameCount = Shader.PropertyToID("_CamHistoryFrameCount");

            public static readonly int _RayTracingShadowsTextureRW = Shader.PropertyToID("_RayTracingShadowsTextureRW");
            public static readonly int _StencilTexture = Shader.PropertyToID("_StencilTexture");
        }
    }
}
