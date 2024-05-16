using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;


namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Generates the gaussian pyramid of source into destination.
    /// We can't do it in place as the color pyramid has to be read while writing to the color buffer in some cases (e.g. refraction, distortion)
    /// </summary>
    public class ColorPyramidPass : ScriptableRenderPass
    {
        private Vector2Int m_PyramidSize;
        private ComputeShader m_Shader;
        private int m_GaussianKernel;
        private int m_DownsampleKernel;
        private Material m_Material;
        private MaterialPropertyBlock m_PropertyBlock;

        private RTHandle m_ColorPyramidTexture;
        private RTHandle m_SourceColorTexture;
        private RTHandle[] m_TempColorTargets;
        private RTHandle[] m_TempDownsamplePyramid;

        private bool m_useCompute = true;

        // TODO: if useTesArray for XR rendering?
        private const int s_TargetCount = 1;

        static readonly int s_BlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int s_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        static readonly int s_BlitMipLevel = Shader.PropertyToID("_BlitMipLevel");

        static readonly int s_Source = Shader.PropertyToID("_Source");
        static readonly int s_SrcScaleBias = Shader.PropertyToID("_SrcScaleBias");
        static readonly int s_SrcUvLimits = Shader.PropertyToID("_SrcUvLimits");
        static readonly int s_SourceMip = Shader.PropertyToID("_SourceMip");

        static readonly int s_Mip0 = Shader.PropertyToID("_Mip0");
        static readonly int s_Size = Shader.PropertyToID("_Size");
        static readonly int s_Destination = Shader.PropertyToID("_Destination");

        /// <summary>
        /// Generate color pyramid with pixel shader
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="colorPyramidMat"></param>
        public ColorPyramidPass(RenderPassEvent evt, Material colorPyramidMat)
        {
            base.profilingSampler = new ProfilingSampler("ColorPyramid");
            renderPassEvent = evt;
            
            m_Material = colorPyramidMat;

            m_TempColorTargets = new RTHandle[s_TargetCount];
            m_TempDownsamplePyramid = new RTHandle[s_TargetCount];
            m_PropertyBlock = new MaterialPropertyBlock();

            m_useCompute = false;
        }

        /// <summary>
        /// Generate color pyramid with compute shader
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="computeShader"></param>
        public ColorPyramidPass(RenderPassEvent evt, ComputeShader computeShader)
        {
            base.profilingSampler = new ProfilingSampler("ColorPyramid");
            renderPassEvent = evt;

            m_Shader = computeShader;
            if (m_Shader != null)
            {
                m_GaussianKernel = m_Shader.FindKernel("KColorGaussian");
                m_DownsampleKernel = m_Shader.FindKernel("KColorDownsample");
            }

            m_TempColorTargets = new RTHandle[s_TargetCount];
            m_TempDownsamplePyramid = new RTHandle[s_TargetCount];
            m_PropertyBlock = new MaterialPropertyBlock();

            m_useCompute = true;
        }

        /// <summary>
        /// Render colorpyramid with pixel shader
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="size"></param>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <returns>The number of mips</returns>
        int RenderColorGaussianPyramid(CommandBuffer cmd, Vector2Int size, RTHandle source, RTHandle destination)
        {
            int rtIndex = 0;
            // Check if format has changed since last time we generated mips
            if (m_TempColorTargets[rtIndex] != null && m_TempColorTargets[rtIndex].rt.graphicsFormat != destination.rt.graphicsFormat)
            {
                RTHandles.Release(m_TempColorTargets[rtIndex]);
                m_TempColorTargets[rtIndex] = null;
            }

            // Only create the temporary target on-demand in case the game doesn't actually need it
            if (m_TempColorTargets[rtIndex] == null)
            {
                m_TempColorTargets[rtIndex] = RTHandles.Alloc(
                    Vector2.one * 0.5f,
                    slices: 1,
                    dimension: source.rt.dimension,
                    filterMode: FilterMode.Bilinear,
                    colorFormat: destination.rt.graphicsFormat,
                    enableRandomWrite: true,
                    useMipMap: false,
                    useDynamicScale: true,
                    name: "Temp Gaussian Pyramid Target"
                );
            }

            int srcMipLevel = 0;
            int srcMipWidth = size.x;
            int srcMipHeight = size.y;

            // Check if format has changed since last time we generated mips
            if (m_TempDownsamplePyramid[rtIndex] != null && m_TempDownsamplePyramid[rtIndex].rt.graphicsFormat != destination.rt.graphicsFormat)
            {
                RTHandles.Release(m_TempDownsamplePyramid[rtIndex]);
                m_TempDownsamplePyramid[rtIndex] = null;
            }

            if (m_TempDownsamplePyramid[rtIndex] == null)
            {
                m_TempDownsamplePyramid[rtIndex] = RTHandles.Alloc(
                    Vector2.one * 0.5f,
                    slices: 1,
                    dimension: source.rt.dimension,
                    filterMode: FilterMode.Bilinear,
                    colorFormat: destination.rt.graphicsFormat,
                    enableRandomWrite: false,
                    useMipMap: false,
                    useDynamicScale: true,
                    name: "Temporary Downsampled Pyramid"
                );

                cmd.SetRenderTarget(m_TempDownsamplePyramid[rtIndex]);
                cmd.ClearRenderTarget(false, true, Color.black);
            }

            float sourceScaleX = (float)size.x / (float)source.rt.width;
            float sourceScaleY = (float)size.y / (float)source.rt.height;

            Material blitMaterial = Blitter.GetBlitMaterial(source.rt.dimension);

            // Copies src mip0 to dst mip0
            m_PropertyBlock.SetTexture(s_BlitTexture, source);
            m_PropertyBlock.SetVector(s_BlitScaleBias, new Vector4(sourceScaleX, sourceScaleY, 0f, 0f));
            m_PropertyBlock.SetFloat(s_BlitMipLevel, 0f);
            cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
            cmd.SetViewport(new Rect(0, 0, srcMipWidth, srcMipHeight));
            cmd.DrawProcedural(Matrix4x4.identity, blitMaterial, 0, MeshTopology.Triangles, 3, 1, m_PropertyBlock);

            var finalTargetSize = new Vector2Int(destination.rt.width, destination.rt.height);

            // Note: smaller mips are excluded as we don't need them and the gaussian compute works
            // on 8x8 blocks
            while (srcMipWidth >= 8 || srcMipHeight >= 8)
            {
                int dstMipWidth = Mathf.Max(1, srcMipWidth >> 1);
                int dstMipHeight = Mathf.Max(1, srcMipHeight >> 1);

                // Scale for downsample
                float scaleX = ((float)srcMipWidth / finalTargetSize.x);
                float scaleY = ((float)srcMipHeight / finalTargetSize.y);

                // Downsample.
                m_PropertyBlock.SetTexture(s_BlitTexture, destination);
                m_PropertyBlock.SetVector(s_BlitScaleBias, new Vector4(scaleX, scaleY, 0f, 0f));
                m_PropertyBlock.SetFloat(s_BlitMipLevel, srcMipLevel);
                cmd.SetRenderTarget(m_TempDownsamplePyramid[rtIndex], 0, CubemapFace.Unknown, -1);
                cmd.SetViewport(new Rect(0, 0, dstMipWidth, dstMipHeight));
                cmd.DrawProcedural(Matrix4x4.identity, blitMaterial, 1, MeshTopology.Triangles, 3, 1, m_PropertyBlock);

                // In this mip generation process, source viewport can be smaller than the source render target itself because of the RTHandle system
                // We are not using the scale provided by the RTHandle system for two reasons:
                // - Source might be a planar probe which will not be scaled by the system (since it's actually the final target of probe rendering at the exact size)
                // - When computing mip size, depending on even/odd sizes, the scale computed for mip 0 might miss a texel at the border.
                //   This can result in a shift in the mip map downscale that depends on the render target size rather than the actual viewport
                //   (Two rendering at the same viewport size but with different RTHandle reference size would yield different results which can break automated testing)
                // So in the end we compute a specific scale for downscale and blur passes at each mip level.

                // Scales for Blur
                // Same size as m_TempColorTargets which is the source for vertical blur
                var hardwareBlurSourceTextureSize = new Vector2Int(m_TempDownsamplePyramid[rtIndex].rt.width, m_TempDownsamplePyramid[rtIndex].rt.height);
                //if (isHardwareDrsOn)
                //    hardwareBlurSourceTextureSize = DynamicResolutionHandler.instance.ApplyScalesOnSize(hardwareBlurSourceTextureSize);

                float blurSourceTextureWidth = (float)hardwareBlurSourceTextureSize.x;
                float blurSourceTextureHeight = (float)hardwareBlurSourceTextureSize.y;

                scaleX = ((float)dstMipWidth / blurSourceTextureWidth);
                scaleY = ((float)dstMipHeight / blurSourceTextureHeight);

                // Blur horizontal.
                m_PropertyBlock.SetTexture(s_Source, m_TempDownsamplePyramid[rtIndex]);
                m_PropertyBlock.SetVector(s_SrcScaleBias, new Vector4(scaleX, scaleY, 0f, 0f));
                m_PropertyBlock.SetVector(s_SrcUvLimits, new Vector4((dstMipWidth - 0.5f) / blurSourceTextureWidth, (dstMipHeight - 0.5f) / blurSourceTextureHeight, 1.0f / blurSourceTextureWidth, 0f));
                m_PropertyBlock.SetFloat(s_SourceMip, 0);
                cmd.SetRenderTarget(m_TempColorTargets[rtIndex], 0, CubemapFace.Unknown, -1);
                cmd.SetViewport(new Rect(0, 0, dstMipWidth, dstMipHeight));
                cmd.DrawProcedural(Matrix4x4.identity, m_Material, rtIndex, MeshTopology.Triangles, 3, 1, m_PropertyBlock);

                // Blur vertical.
                m_PropertyBlock.SetTexture(s_Source, m_TempColorTargets[rtIndex]);
                m_PropertyBlock.SetVector(s_SrcScaleBias, new Vector4(scaleX, scaleY, 0f, 0f));
                m_PropertyBlock.SetVector(s_SrcUvLimits, new Vector4((dstMipWidth - 0.5f) / blurSourceTextureWidth, (dstMipHeight - 0.5f) / blurSourceTextureHeight, 0f, 1.0f / blurSourceTextureHeight));
                m_PropertyBlock.SetFloat(s_SourceMip, 0);
                cmd.SetRenderTarget(destination, srcMipLevel + 1, CubemapFace.Unknown, -1);
                cmd.SetViewport(new Rect(0, 0, dstMipWidth, dstMipHeight));
                cmd.DrawProcedural(Matrix4x4.identity, m_Material, rtIndex, MeshTopology.Triangles, 3, 1, m_PropertyBlock);

                srcMipLevel++;
                srcMipWidth = srcMipWidth >> 1;
                srcMipHeight = srcMipHeight >> 1;

                finalTargetSize.x = finalTargetSize.x >> 1;
                finalTargetSize.y = finalTargetSize.y >> 1;
            }

            return srcMipLevel + 1;
        }

        /// <summary>
        /// Render colorpyramid with compute shader
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="size"></param>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <returns></returns>
        public int ComputeColorGaussianPyramid(CommandBuffer cmd, Vector2Int size, RTHandle source, RTHandle destination)
        {
            var cs = m_Shader;
            int rtIndex = 0;
            // Check if format has changed since last time we generated mips
            if (m_TempColorTargets[rtIndex] != null && m_TempColorTargets[rtIndex].rt.graphicsFormat != destination.rt.graphicsFormat)
            {
                RTHandles.Release(m_TempColorTargets[rtIndex]);
                m_TempColorTargets[rtIndex] = null;
            }

            // Only create the temporary target on-demand in case the game doesn't actually need it
            if (m_TempColorTargets[rtIndex] == null)
            {
                m_TempColorTargets[rtIndex] = RTHandles.Alloc(
                    Vector2.one * 0.5f,
                    slices: 1,
                    dimension: source.rt.dimension,
                    filterMode: FilterMode.Bilinear,
                    colorFormat: destination.rt.graphicsFormat,
                    enableRandomWrite: true,
                    useMipMap: false,
                    useDynamicScale: true,
                    name: "Temp Gaussian Pyramid Target"
                );
            }

            int srcMipLevel = 0;
            int srcMipWidth = size.x;
            int srcMipHeight = size.y;

            // Check if format has changed since last time we generated mips
            if (m_TempDownsamplePyramid[rtIndex] != null && m_TempDownsamplePyramid[rtIndex].rt.graphicsFormat != destination.rt.graphicsFormat)
            {
                RTHandles.Release(m_TempDownsamplePyramid[rtIndex]);
                m_TempDownsamplePyramid[rtIndex] = null;
            }

            if (m_TempDownsamplePyramid[rtIndex] == null)
            {
                m_TempDownsamplePyramid[rtIndex] = RTHandles.Alloc(
                    Vector2.one * 0.5f,
                    slices: 1,
                    dimension: source.rt.dimension,
                    filterMode: FilterMode.Bilinear,
                    colorFormat: destination.rt.graphicsFormat,
                    enableRandomWrite: true,
                    useMipMap: false,
                    useDynamicScale: true,
                    name: "Temporary Downsampled Pyramid"
                );

                cmd.SetRenderTarget(m_TempDownsamplePyramid[rtIndex]);
                cmd.ClearRenderTarget(false, true, Color.black);
            }

            Vector2Int targetSize = new Vector2Int(m_TempDownsamplePyramid[rtIndex].rt.width, m_TempDownsamplePyramid[rtIndex].rt.height);

            cmd.EnableShaderKeyword("COPY_MIP_0");
            cmd.SetComputeVectorParam(cs, s_Size, new Vector4(srcMipWidth, srcMipHeight, 0, 0));
            cmd.SetComputeTextureParam(cs, m_DownsampleKernel, s_Source, source);
            cmd.SetComputeTextureParam(cs, m_DownsampleKernel, s_Mip0, destination, 0);
            cmd.SetComputeTextureParam(cs, m_DownsampleKernel, s_Destination, m_TempDownsamplePyramid[rtIndex]);
            cmd.DispatchCompute(cs, m_DownsampleKernel, RenderingUtils.DivRoundUp(targetSize.x, 8), RenderingUtils.DivRoundUp(targetSize.y, 8), source.rt.volumeDepth);
            cmd.DisableShaderKeyword("COPY_MIP_0");

            // Note: smaller mips are excluded as we don't need them and the gaussian compute works
            // on 8x8 blocks
            while (srcMipWidth >= 8 || srcMipHeight >= 8)
            {
                int dstMipWidth = Mathf.Max(1, srcMipWidth >> 1);
                int dstMipHeight = Mathf.Max(1, srcMipHeight >> 1);

                if (srcMipLevel != 0)
                {
                    cmd.SetComputeVectorParam(cs, s_Size, new Vector4(srcMipWidth, srcMipHeight, 0, 0));
                    cmd.SetComputeTextureParam(cs, m_DownsampleKernel, s_Source, destination, srcMipLevel);
                    cmd.SetComputeTextureParam(cs, m_DownsampleKernel, s_Destination, m_TempDownsamplePyramid[rtIndex]);
                    cmd.DispatchCompute(cs, m_DownsampleKernel, RenderingUtils.DivRoundUp(dstMipWidth, 8), RenderingUtils.DivRoundUp(dstMipHeight, 8), source.rt.volumeDepth);
                }

                cmd.SetComputeVectorParam(cs, s_Size, new Vector4(dstMipWidth, dstMipHeight, 0, 0));
                cmd.SetComputeTextureParam(cs, m_GaussianKernel, s_Source, m_TempDownsamplePyramid[rtIndex]);
                cmd.SetComputeTextureParam(cs, m_GaussianKernel, s_Destination, destination, srcMipLevel + 1);
                cmd.DispatchCompute(cs, m_GaussianKernel, RenderingUtils.DivRoundUp(dstMipWidth, 8), RenderingUtils.DivRoundUp(dstMipHeight, 8), source.rt.volumeDepth);

                srcMipLevel++;
                srcMipWidth >>= 1;
                srcMipHeight >>= 1;
            }

            return srcMipLevel + 1;
        }

        private class PassData
        {
            internal TextureHandle source;
            internal TextureHandle destination;
            internal ColorPyramidPass pass;
            internal Vector2Int pyramidSize;
        }

        internal void Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>("Color Pyramid", out var passData, base.profilingSampler))
            {
                // Access resources.
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                var camHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
                if (camHistoryRTSystem == null)
                {
                    return;
                }

                // Set passData
                int actualWidth = cameraData.cameraTargetDescriptor.width;
                int actualHeight = cameraData.cameraTargetDescriptor.height;
                Vector2Int pyramidSize = new Vector2Int(actualWidth, actualHeight);
                if (camHistoryRTSystem.GetNumFramesAllocated(HistoryFrameType.ColorBufferMipChain) == 0)
                {
                    camHistoryRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ColorBufferMipChain, cameraData.camera.name,
                                                                        HistoryBufferAllocatorFunction, cameraData.cameraTargetDescriptor.graphicsFormat, 2);
                }

                RTHandle colorpyramidRTHandle = camHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.ColorBufferMipChain);

                passData.source = source;
                passData.destination = renderGraph.ImportTexture(colorpyramidRTHandle);
                passData.pass = this;
                passData.pyramidSize = pyramidSize;

                // Declare input/output textures
                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseTexture(passData.destination, AccessFlags.Write);
                //builder.SetRenderAttachment(destination, 0);

                // Setup builder state
                builder.AllowPassCulling(false);
                //builder.AllowGlobalStateModification(true); // Shader keyword changes are considered as global state modifications
                //builder.SetGlobalTextureAfterPass(destination, ShaderConstants._CustomGlobalTexture); // Setup global texture if needed.

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    passData.pass.ComputeColorGaussianPyramid(unsafeCmd, data.pyramidSize, data.source, data.destination);

                });

            }
        }

        public void Dispose()
        {
            for (int i = 0; i < s_TargetCount; i++)
            {
                m_TempColorTargets[i]?.Release();
                m_TempColorTargets[i] = null;

                m_TempDownsamplePyramid[i]?.Release();
                m_TempDownsamplePyramid[i] = null;
            }
        }

        // BufferedRTHandleSystem API expects an allocator function. We define it here.
        /// <summary>
        /// Allocator for cameraColorBufferMipChain.
        /// TODO: dimension configured
        /// </summary>
        static RTHandle HistoryBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one, TextureXR.slices, colorFormat: graphicsFormat,
                enableRandomWrite: true, useMipMap: true, autoGenerateMips: false, useDynamicScale: true,
                name: string.Format("{0}_CameraColorBufferMipChain{1}", viewName, frameIndex));
        }
    }
}
