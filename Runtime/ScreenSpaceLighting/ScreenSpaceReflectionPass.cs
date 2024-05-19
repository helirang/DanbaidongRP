using System;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using static PlasticGui.PlasticTableColumn;
using static UnityEngine.Rendering.Universal.Internal.DrawObjectsPass;

namespace UnityEngine.Rendering.Universal
{
    internal class ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        // Profiling tag
        private static string m_SSRClassifyTilesProfilerTag = "SSRClassifyTiles";
        private static string m_SSRTracingProfilerTag = "SSRTracing";
        private static string m_SSRResolveProfilerTag = "SSRResolve";
        private static string m_SSRAccumulateProfilerTag = "SSRAccumulate";
        private static ProfilingSampler m_SSRClassifyTilesProfilingSampler = new ProfilingSampler(m_SSRClassifyTilesProfilerTag);
        private static ProfilingSampler m_SSRTracingProfilingSampler = new ProfilingSampler(m_SSRTracingProfilerTag);
        private static ProfilingSampler m_SSRResolveProfilingSampler = new ProfilingSampler(m_SSRResolveProfilerTag);
        private static ProfilingSampler m_SSRAccumulateProfilingSampler = new ProfilingSampler(m_SSRAccumulateProfilerTag);

        // Public Variables

        // Private Variables
        private ComputeShader m_Compute;

        private int m_SSRClassifyTilesKernel;
        private int m_SSRTracingKernel;
        private int m_SSRResolveKernel;
        private int m_SSRAccumulateKernel;

        private ScreenSpaceReflection m_volumeSettings;

        private bool traceDownSample = false;

        // Constants

        // Statics
        private static Vector2 s_accumulateTextureScaleFactor = Vector2.one;
        private static readonly int _ShaderVariablesScreenSpaceReflection = Shader.PropertyToID("ShaderVariablesScreenSpaceReflection");

        internal ScreenSpaceReflectionPass(RenderPassEvent evt, ComputeShader computeShader)
        {
            this.renderPassEvent = evt;

            m_Compute = computeShader;

            m_SSRClassifyTilesKernel = m_Compute.FindKernel("ScreenSpaceReflectionsClassifyTiles");
            m_SSRTracingKernel = m_Compute.FindKernel("ScreenSpaceReflectionsTracing");
            m_SSRResolveKernel = m_Compute.FindKernel("ScreenSpaceReflectionsResolve");
            m_SSRAccumulateKernel = m_Compute.FindKernel("ScreenSpaceReflectionsAccumulate");
        }

        /// <summary>
        /// Setup controls per frame shouldEnqueue this pass.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="renderingData"></param>
        /// <returns></returns>
        internal bool Setup()
        {
            var stack = VolumeManager.instance.stack;
            m_volumeSettings = stack.GetComponent<ScreenSpaceReflection>();

            return m_Compute != null && m_volumeSettings != null && m_volumeSettings.IsActive();
        }

        static RTHandle HistoryAccumulateTextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one * s_accumulateTextureScaleFactor, TextureXR.slices, colorFormat: graphicsFormat,
                 filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_SSRAccumTexture{1}", viewName, frameIndex));
        }

        internal void ReAllocatedAccumulateTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, out RTHandle currFrameRT, out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);

            if (curTexture == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceReflectionAccumulation, cameraData.camera.name
                                                            , HistoryAccumulateTextureAllocator, GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);
        }

        static RTHandle HistoryNumFramesAccumTextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one * s_accumulateTextureScaleFactor, TextureXR.slices, colorFormat: graphicsFormat,
                 filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_SSRNumFramesAccumTexture{1}", viewName, frameIndex));
        }

        internal void ReAllocatedNumFramesAccumTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, out RTHandle currFrameRT, out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);

            if (curTexture == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation, cameraData.camera.name
                                                            , HistoryNumFramesAccumTextureAllocator, GraphicsFormat.R8_UNorm, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);
        }

        internal class SSRPassData
        {
            internal ComputeShader cs;
            internal int classifyTilesKernel;
            internal int tracingKernel;
            internal int resolveKernel;
            internal int accumulateKernel;

            // Classify tiles
            internal BufferHandle dispatchIndirectBuffer;
            internal BufferHandle tileListBuffer;

            internal TextureHandle cameraDepthTexture;
            internal TextureHandle depthPyramidTexture;
            internal BufferHandle depthPyramidMipLevelOffsets;
            internal TextureHandle hitPointTexture;
            internal Vector2Int hitPointTextureSize;
            internal int camHistoryFrameCount;
            internal TextureHandle blueNoiseArray;
            internal TextureHandle ssrLightingTexture;
            internal TextureHandle rayInfoTexture;

            internal TextureHandle motionVectorTexture;
            internal TextureHandle prevColorPyramidTexture;
            internal TextureHandle avgRadianceTexture;
            internal TextureHandle hitDepthTexture;

            internal TextureHandle currAccumulateTexture;
            internal TextureHandle prevAccumulateTexture;
            internal TextureHandle currNumFramesAccumTexture;
            internal TextureHandle prevNumFramesAccumTexture;

            internal ShaderVariablesScreenSpaceReflection constantBuffer;

            internal ScreenSpaceReflection volumeSettings;
        }

        void InitResource(RenderGraph renderGraph, SSRPassData passData, UniversalResourceData resourceData, UniversalCameraData cameraData, HistoryFrameRTSystem historyRTSystem)
        {
            passData.cs = m_Compute;
            passData.classifyTilesKernel = m_SSRClassifyTilesKernel;
            passData.tracingKernel = m_SSRTracingKernel;
            passData.resolveKernel = m_SSRResolveKernel;
            passData.accumulateKernel = m_SSRAccumulateKernel;



            TextureDesc texDesc = new TextureDesc(cameraData.cameraTargetDescriptor);
            texDesc.depthBufferBits = 0;
            texDesc.msaaSamples = MSAASamples.None;
            texDesc.colorFormat = GraphicsFormat.R16G16_UNorm;
            texDesc.depthBufferBits = DepthBits.None;
            texDesc.enableRandomWrite = true;
            texDesc.filterMode = FilterMode.Point;
            texDesc.wrapMode = TextureWrapMode.Clamp;

            // As descripted in HDRP, but we only use DispatchIndirect.
            // DispatchIndirect: Buffer with arguments has to have three integer numbers at given argsOffset offset: number of work groups in X dimension, number of work groups in Y dimension, number of work groups in Z dimension.
            // DrawProceduralIndirect: Buffer with arguments has to have four integer numbers at given argsOffset offset: vertex count per instance, instance count, start vertex location, and start instance location
            // Use use max size of 4 unit for allocation
            var bufferSystem = GraphicsBufferSystem.instance;
            var dispatchIndirect = bufferSystem.GetGraphicsBuffer<uint>(GraphicsBufferSystemBufferID.SSRDispatchIndirectBuffer, 3, "SSRDispatIndirectBuffer", GraphicsBuffer.Target.IndirectArguments);
            passData.dispatchIndirectBuffer = renderGraph.ImportBuffer(dispatchIndirect);
            passData.tileListBuffer = renderGraph.CreateBuffer(new BufferDesc(RenderingUtils.DivRoundUp(texDesc.width, 8) * RenderingUtils.DivRoundUp(texDesc.height, 8), sizeof(uint), "SSRTileListBuffer"));

            passData.cameraDepthTexture = resourceData.cameraDepthTexture;
            passData.depthPyramidTexture = resourceData.cameraDepthPyramidTexture;
            passData.depthPyramidMipLevelOffsets = resourceData.cameraDepthPyramidMipLevelOffsets;

            var blueNoiseSystem = BlueNoiseSystem.TryGetInstance();
            if (blueNoiseSystem != null)
            {
                passData.camHistoryFrameCount = historyRTSystem.historyFrameCount;
                passData.blueNoiseArray = renderGraph.ImportTexture(blueNoiseSystem.textureHandle128RG);
            }

            var hitPointDesc = texDesc;
            if (traceDownSample)
            {
                hitPointDesc.width /= 2;
                hitPointDesc.height /= 2;
            }
            hitPointDesc.name = "_SSRHitPointTexture";
            passData.hitPointTexture = renderGraph.CreateTexture(hitPointDesc);
            passData.hitPointTextureSize = new Vector2Int(hitPointDesc.width, hitPointDesc.height);

            var lightingDesc = texDesc;
            lightingDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            lightingDesc.filterMode = FilterMode.Bilinear;
            lightingDesc.name = "_SSRLightingTexture";
            lightingDesc.clearBuffer = true;
            passData.ssrLightingTexture = renderGraph.CreateTexture(lightingDesc);

            var rayInfoDesc = texDesc;
            rayInfoDesc.colorFormat = GraphicsFormat.R16G16_SFloat;
            rayInfoDesc.name = "_SSRRayInfoTexture";
            passData.rayInfoTexture = renderGraph.CreateTexture(rayInfoDesc);

            var avgRadianceDesc = texDesc;
            avgRadianceDesc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
            avgRadianceDesc.width = RenderingUtils.DivRoundUp(texDesc.width, 8);
            avgRadianceDesc.height = RenderingUtils.DivRoundUp(texDesc.height, 8);
            avgRadianceDesc.filterMode = FilterMode.Bilinear;
            avgRadianceDesc.name = "_SSRAvgRadianceTexture";
            passData.avgRadianceTexture = renderGraph.CreateTexture(avgRadianceDesc);

            var hitDepthDesc = texDesc;
            hitDepthDesc.colorFormat = GraphicsFormat.R16_UNorm;
            hitDepthDesc.name = "_SSRHitDepthTexture";
            passData.hitDepthTexture = renderGraph.CreateTexture(hitDepthDesc);

            passData.motionVectorTexture = resourceData.motionVectorColor;
            passData.prevColorPyramidTexture = renderGraph.ImportTexture(historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ColorBufferMipChain));

            // Import history texture.
            RTHandle currAccumulateTexture, prevAccumulateTexture;
            RTHandle currNumFramesAccumTexture, prevNumFramesAccumTexture;
            ReAllocatedAccumulateTextureIfNeeded(historyRTSystem, cameraData, out currAccumulateTexture, out prevAccumulateTexture);
            ReAllocatedNumFramesAccumTextureIfNeeded(historyRTSystem, cameraData, out currNumFramesAccumTexture, out prevNumFramesAccumTexture);

            passData.currAccumulateTexture = renderGraph.ImportTexture(currAccumulateTexture);
            passData.prevAccumulateTexture = renderGraph.ImportTexture(prevAccumulateTexture);
            passData.currNumFramesAccumTexture = renderGraph.ImportTexture(currNumFramesAccumTexture);
            passData.prevNumFramesAccumTexture = renderGraph.ImportTexture(prevNumFramesAccumTexture);
        }

        void UpdateSSRConstantBuffer(SSRPassData passData,
            UniversalResourceData resourceData, 
            UniversalCameraData cameraData, 
            HistoryFrameRTSystem historyRTSystem, 
            int colorPyramidHistoryMipCount)
        {
            float n = cameraData.camera.nearClipPlane;
            float f = cameraData.camera.farClipPlane;
            float thickness = m_volumeSettings.depthBufferThickness.value;
            float thicknessScale = 1.0f / (1.0f + thickness);
            float thicknessBias = -n / (f - n) * (thickness * thicknessScale);

            float ssrRoughnessFadeEnd = 1 - m_volumeSettings.minSmoothness;
            float roughnessFadeStart = 1 - m_volumeSettings.smoothnessFadeStart;
            float roughnessFadeLength = ssrRoughnessFadeEnd - roughnessFadeStart;

            // ColorPyramidSize 
            Vector4 colorPyramidUvScaleAndLimitPrev = RenderingUtils.ComputeViewportScaleAndLimit(historyRTSystem.rtHandleProperties.previousViewportSize, historyRTSystem.rtHandleProperties.previousRenderTargetSize);

            
            // Constant Params
            {
                // Wtf? It seems that Unity has not setted this jittered matrix (or changed by something)?
                // We will transfer our own Temporal AA jittered matrices and use it in SSR compute.
                Matrix4x4 viewMatrix = cameraData.GetViewMatrix();
                // Jittered, non-gpu
                //Matrix4x4 projectionMatrix = renderingData.cameraData.GetProjectionMatrix();
                // Jittered, gpu
                Matrix4x4 gpuProjectionMatrix = cameraData.GetGPUProjectionMatrix(true);
                Matrix4x4 viewAndProjectionMatrix = gpuProjectionMatrix * viewMatrix;
                Matrix4x4 inverseViewMatrix = Matrix4x4.Inverse(viewMatrix);
                Matrix4x4 inverseProjectionMatrix = Matrix4x4.Inverse(gpuProjectionMatrix);
                Matrix4x4 inverseViewProjection = inverseViewMatrix * inverseProjectionMatrix;

                passData.constantBuffer._SSR_MATRIX_VP = viewAndProjectionMatrix;
                passData.constantBuffer._SSR_MATRIX_I_VP = inverseViewProjection;

                MotionVectorsPersistentData motionData = null;
                if (cameraData.camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalCameraData))
                    motionData = additionalCameraData.motionVectorsPersistentData;
                if (motionData != null)
                {
                    //constantBuffer._SSR_PREV_MATRIX_VP = motionData.previousViewProjectionJittered;
                    passData.constantBuffer._SSR_MATRIX_CLIP_TO_PREV_CLIP = motionData.previousViewProjection * Matrix4x4.Inverse(motionData.viewProjection);
                }

                passData.constantBuffer._SsrTraceScreenSize = new Vector4(passData.hitPointTextureSize.x, passData.hitPointTextureSize.y, 1.0f / passData.hitPointTextureSize.x, 1.0f / passData.hitPointTextureSize.y);
                passData.constantBuffer._SsrThicknessScale = thicknessScale;
                passData.constantBuffer._SsrThicknessBias = thicknessBias;
                passData.constantBuffer._SsrIterLimit = m_volumeSettings.rayMaxIterations;
                passData.constantBuffer._SsrFrameCount = Time.frameCount;

                passData.constantBuffer._SsrRoughnessFadeEnd = 1 - m_volumeSettings.minSmoothness;
                passData.constantBuffer._SsrRoughnessFadeRcpLength = (roughnessFadeLength != 0) ? (1.0f / roughnessFadeLength) : 0;
                passData.constantBuffer._SsrRoughnessFadeEndTimesRcpLength = ((roughnessFadeLength != 0) ? (ssrRoughnessFadeEnd * (1.0f / roughnessFadeLength)) : 1);
                passData.constantBuffer._SsrEdgeFadeRcpLength = Mathf.Min(1.0f / m_volumeSettings.screenFadeDistance.value, float.MaxValue);

                passData.constantBuffer._ColorPyramidUvScaleAndLimitPrevFrame = colorPyramidUvScaleAndLimitPrev;

                passData.constantBuffer._SsrDepthPyramidMaxMip = resourceData.cameraDepthPyramidInfo.mipLevelCount - 1;
                passData.constantBuffer._SsrColorPyramidMaxMip = colorPyramidHistoryMipCount - 1;
                passData.constantBuffer._SsrReflectsSky = m_volumeSettings.reflectSky.value ? 1 : 0;
                passData.constantBuffer._SsrAccumulationAmount = m_volumeSettings.accumulationFactor.value;


                var prevRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);
                Vector4 historyFrameRTSize = new Vector4(prevRT.rt.width, prevRT.rt.height, prevRT.rt.texelSize.x, prevRT.rt.texelSize.y);
                passData.constantBuffer._HistoryFrameRTSize = historyFrameRTSize;

                passData.constantBuffer._SsrPBRBias = m_volumeSettings.biasFactor.value;
            }
        }

        static void ExecutePass(SSRPassData data, ComputeCommandBuffer cmd)
        {
            // Default Approximation we use lighingTexture as SSRResolve handle.
            var SSRResolveHandle = data.ssrLightingTexture;
            var currAccumHandle = data.currAccumulateTexture;
            var prevAccumHandle = data.prevAccumulateTexture;

            if (data.volumeSettings.usedAlgorithm == ScreenSpaceReflectionAlgorithm.Approximation)
            {
                data.cs.EnableKeyword("SSR_APPROX");
            }
            else
            {
                // TODO: Clear accum and accum prev
                SSRResolveHandle = currAccumHandle;

                data.cs.DisableKeyword("SSR_APPROX");
            }

            // Push ConstantBuffer to compute shader
            ConstantBuffer.Push(cmd, data.constantBuffer, data.cs, _ShaderVariablesScreenSpaceReflection);

            // ClassifyTiles
            using (new ProfilingScope(cmd, m_SSRClassifyTilesProfilingSampler))
            {
                cmd.SetComputeBufferParam(data.cs, data.classifyTilesKernel, ShaderConstants.gDispatchIndirectBuffer, data.dispatchIndirectBuffer);
                cmd.SetComputeBufferParam(data.cs, data.classifyTilesKernel, ShaderConstants.gTileList, data.tileListBuffer);

                cmd.DispatchCompute(data.cs, data.classifyTilesKernel, RenderingUtils.DivRoundUp(data.hitPointTextureSize.x, 8), RenderingUtils.DivRoundUp(data.hitPointTextureSize.y, 8), 1);
            }

            // Tracing
            using (new ProfilingScope(cmd, m_SSRTracingProfilingSampler))
            {
                BlueNoiseSystem.BindSTBNParams(BlueNoiseTexFormat._128RG, cmd, data.cs, data.tracingKernel, data.blueNoiseArray, data.camHistoryFrameCount);
                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._CameraDepthPyramidTexture, data.depthPyramidTexture);
                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._SSRHitPointTexture, data.hitPointTexture);
                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._SSRRayInfoTexture, data.rayInfoTexture);

                cmd.SetComputeBufferParam(data.cs, data.tracingKernel, ShaderConstants._DepthPyramidMipLevelOffsets, data.depthPyramidMipLevelOffsets);
                cmd.SetComputeBufferParam(data.cs, data.tracingKernel, ShaderConstants.gTileList, data.tileListBuffer);

                cmd.DispatchCompute(data.cs, data.tracingKernel, data.dispatchIndirectBuffer, 0);
            }

            // Resolve
            using (new ProfilingScope(cmd, m_SSRResolveProfilingSampler))
            {
                cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._SSRHitPointTexture, data.hitPointTexture);
                cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._ColorPyramidTexture, data.prevColorPyramidTexture);
                cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._SSRAccumTexture, SSRResolveHandle);
                cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._SSRRayInfoTexture, data.rayInfoTexture);

                cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._SSRHitDepthTexture, data.hitDepthTexture);
                cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._SSRAvgRadianceTexture, data.avgRadianceTexture);

                cmd.SetComputeBufferParam(data.cs, data.resolveKernel, ShaderConstants.gTileList, data.tileListBuffer);
                cmd.DispatchCompute(data.cs, data.resolveKernel, data.dispatchIndirectBuffer, 0);
            }


            if (data.volumeSettings.usedAlgorithm == ScreenSpaceReflectionAlgorithm.PBRAccumulation)
            {
                using (new ProfilingScope(cmd, m_SSRAccumulateProfilingSampler))
                {
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRHitPointTexture, data.hitPointTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRAccumTexture, currAccumHandle);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SsrAccumPrev, prevAccumHandle);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SsrLightingTexture, data.ssrLightingTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRHitDepthTexture, data.hitDepthTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRAvgRadianceTexture, data.avgRadianceTexture);

                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRPrevNumFramesAccumTexture, data.prevNumFramesAccumTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRNumFramesAccumTexture, data.currNumFramesAccumTexture);

                    cmd.SetComputeBufferParam(data.cs, data.accumulateKernel, ShaderConstants.gTileList, data.tileListBuffer);
                    cmd.DispatchCompute(data.cs, data.accumulateKernel, data.dispatchIndirectBuffer, 0);
                }


            }

        }

        public TextureHandle RenderSSR(RenderGraph renderGraph, ContextContainer frameData, int colorPyramidHistoryMipCount)
        {
            // Early out
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(frameData.Get<UniversalCameraData>().camera);
            if (historyRTSystem == null ||
                historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ColorBufferMipChain) == null)
            {
                return TextureHandle.nullHandle;
            }

            using (var builder = renderGraph.AddComputePass("Render SSR", out SSRPassData passData, ProfilingSampler.Get(URPProfileId.RenderSSR)))
            {
                // Access resources.
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // Set passData
                InitResource(renderGraph, passData, resourceData, cameraData, historyRTSystem);
                UpdateSSRConstantBuffer(passData, resourceData, cameraData, historyRTSystem, colorPyramidHistoryMipCount);
                passData.volumeSettings = m_volumeSettings;

                // Declare input/output textures
                builder.UseBuffer(passData.dispatchIndirectBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.tileListBuffer, AccessFlags.ReadWrite);

                builder.UseTexture(passData.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.depthPyramidTexture, AccessFlags.Read);
                builder.UseTexture(resourceData.gBuffer[2], AccessFlags.Read); // Normal GBuffer
                builder.UseTexture(passData.blueNoiseArray, AccessFlags.Read);
                builder.UseTexture(passData.hitPointTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.rayInfoTexture, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.depthPyramidMipLevelOffsets, AccessFlags.Read);

                builder.UseTexture(passData.motionVectorTexture, AccessFlags.Read);
                builder.UseTexture(passData.prevColorPyramidTexture, AccessFlags.Read);
                builder.UseTexture(passData.hitDepthTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.avgRadianceTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ssrLightingTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.currAccumulateTexture, AccessFlags.ReadWrite);

                if (passData.volumeSettings.usedAlgorithm == ScreenSpaceReflectionAlgorithm.PBRAccumulation)
                {
                    builder.UseTexture(passData.prevAccumulateTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.prevNumFramesAccumTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.currNumFramesAccumTexture, AccessFlags.ReadWrite);
                }

                // Setup builder state
                builder.AllowPassCulling(false);
                //builder.EnableAsyncCompute(true);

                builder.SetRenderFunc((SSRPassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context.cmd);
                });

                return passData.ssrLightingTexture;
            }

        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            // Clean Keyword if need
            //CoreUtils.SetKeyword(cmd, ShaderKeywordStrings, false);
        }

        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {
        }


        static class ShaderConstants
        {
            public static readonly int gDispatchIndirectBuffer = Shader.PropertyToID("gDispatchIndirectBuffer");
            public static readonly int gTileList = Shader.PropertyToID("gTileList");
            public static readonly int _CameraDepthPyramidTexture = Shader.PropertyToID("_CameraDepthPyramidTexture");
            public static readonly int _DepthPyramidMipLevelOffsets = Shader.PropertyToID("_DepthPyramidMipLevelOffsets");
            public static readonly int _SSRHitPointTexture = Shader.PropertyToID("_SSRHitPointTexture");
            public static readonly int _SSRRayInfoTexture = Shader.PropertyToID("_SSRRayInfoTexture");
            public static readonly int _CameraMotionVectorsTexture = Shader.PropertyToID("_CameraMotionVectorsTexture");
            public static readonly int _ColorPyramidTexture = Shader.PropertyToID("_ColorPyramidTexture");
            public static readonly int _SsrAccumPrev = Shader.PropertyToID("_SsrAccumPrev");
            public static readonly int _SSRAccumTexture = Shader.PropertyToID("_SSRAccumTexture");
            public static readonly int _SSRHitDepthTexture = Shader.PropertyToID("_SSRHitDepthTexture");
            public static readonly int _SSRAvgRadianceTexture = Shader.PropertyToID("_SSRAvgRadianceTexture");
            public static readonly int _SsrLightingTexture = Shader.PropertyToID("_SsrLightingTexture");
            public static readonly int _SSRPrevNumFramesAccumTexture = Shader.PropertyToID("_SSRPrevNumFramesAccumTexture");
            public static readonly int _SSRNumFramesAccumTexture = Shader.PropertyToID("_SSRNumFramesAccumTexture");
        }
    }
}