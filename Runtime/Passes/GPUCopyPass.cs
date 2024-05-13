using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Copy the given depth buffer into the given destination depth buffer.
    ///
    /// You can use this pass to copy a depth buffer to a destination,
    /// so you can use it later in rendering. If the source texture has MSAA
    /// enabled, the pass uses a custom MSAA resolve. If the source texture
    /// does not have MSAA enabled, the pass uses a Blit or a Copy Texture
    /// operation, depending on what the current platform supports.
    /// </summary>
    public class GPUCopyPass : ScriptableRenderPass
    {
        private RTHandle m_Source { get; set; }
        private RTHandle m_Destination { get; set; }

        internal bool m_ShouldClear;

        private ComputeShader m_GPUCopy;
        private int k_SampleKernel_xyzw2x_8;
        private int k_SampleKernel_xyzw2x_1;

        static readonly int s_RectOffset = Shader.PropertyToID("_RectOffset");
        static readonly int s_Result = Shader.PropertyToID("_Result");
        static readonly int s_Source = Shader.PropertyToID("_Source");
        static int[] s_IntParams = new int[2];

        /// <summary>
        /// Creates a new <c>GPUCopyPass</c> instance.
        /// </summary>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <param name="shouldClear">Controls whether it should do a clear before copying the depth.</param>
        /// <seealso cref="RenderPassEvent"/>
        public GPUCopyPass(RenderPassEvent evt, ComputeShader computeShader, bool shouldClear = false)
        {
            base.profilingSampler = new ProfilingSampler("GPUCopyDepth");
            renderPassEvent = evt;
            m_ShouldClear = shouldClear;

            m_GPUCopy = computeShader;
            k_SampleKernel_xyzw2x_8 = m_GPUCopy.FindKernel("KSampleCopy4_1_x_8");
            k_SampleKernel_xyzw2x_1 = m_GPUCopy.FindKernel("KSampleCopy4_1_x_1");
        }

        /// <summary>
        /// Configure the pass with the source and destination to execute on.
        /// </summary>
        /// <param name="source">Source Render Target</param>
        /// <param name="destination">Destination Render Target</param>
        public void Setup(RTHandle source, RTHandle destination)
        {
            this.m_Source = source;
            this.m_Destination = destination;
        }

        void SampleCopyChannel(
            ComputeCommandBuffer cmd,
            RectInt rect,
            int sourceID,
            TextureHandle source,
            int targetID,
            TextureHandle target,
            int slices,
            int kernel8,
            int kernel1)
        {
            RectInt main, topRow, rightCol, topRight;
            unsafe
            {
                RectInt* dispatch1Rects = stackalloc RectInt[3];
                int dispatch1RectCount = 0;
                RectInt dispatch8Rect = new RectInt(0, 0, 0, 0);

                if (TileLayoutUtils.TryLayoutByTiles(
                    rect,
                    8,
                    out main,
                    out topRow,
                    out rightCol,
                    out topRight))
                {
                    if (topRow.width > 0 && topRow.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = topRow;
                        ++dispatch1RectCount;
                    }
                    if (rightCol.width > 0 && rightCol.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = rightCol;
                        ++dispatch1RectCount;
                    }
                    if (topRight.width > 0 && topRight.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = topRight;
                        ++dispatch1RectCount;
                    }
                    dispatch8Rect = main;
                }
                else if (rect.width > 0 && rect.height > 0)
                {
                    dispatch1Rects[dispatch1RectCount] = rect;
                    ++dispatch1RectCount;
                }

                cmd.SetComputeTextureParam(m_GPUCopy, kernel8, sourceID, source);
                cmd.SetComputeTextureParam(m_GPUCopy, kernel1, sourceID, source);
                cmd.SetComputeTextureParam(m_GPUCopy, kernel8, targetID, target);
                cmd.SetComputeTextureParam(m_GPUCopy, kernel1, targetID, target);

                if (dispatch8Rect.width > 0 && dispatch8Rect.height > 0)
                {
                    var r = dispatch8Rect;
                    // Use intermediate array to avoid garbage
                    s_IntParams[0] = r.x;
                    s_IntParams[1] = r.y;
                    cmd.SetComputeIntParams(m_GPUCopy, s_RectOffset, s_IntParams);
                    cmd.DispatchCompute(m_GPUCopy, kernel8, (int)Mathf.Max(r.width / 8, 1), (int)Mathf.Max(r.height / 8, 1), slices);
                }

                for (int i = 0, c = dispatch1RectCount; i < c; ++i)
                {
                    var r = dispatch1Rects[i];
                    // Use intermediate array to avoid garbage
                    s_IntParams[0] = r.x;
                    s_IntParams[1] = r.y;
                    cmd.SetComputeIntParams(m_GPUCopy, s_RectOffset, s_IntParams);
                    cmd.DispatchCompute(m_GPUCopy, kernel1, (int)Mathf.Max(r.width, 1), (int)Mathf.Max(r.height, 1), slices);
                }
            }
        }

        public void SampleCopyChannel_xyzw2x(ComputeCommandBuffer cmd, TextureHandle source, TextureHandle target, RectInt rect)
        {
            RTHandle s = (RTHandle)source;
            RTHandle t = (RTHandle)source;
            Debug.Assert(s.rt.volumeDepth == t.rt.volumeDepth);
            SampleCopyChannel(cmd, rect, s_Source, source, s_Result, target, s.rt.volumeDepth, k_SampleKernel_xyzw2x_8, k_SampleKernel_xyzw2x_1);
        }

        private class PassData
        {
            internal TextureHandle source;
            internal TextureHandle destination;
            internal GPUCopyPass gpuCopyPass;
            internal int width;
            internal int height;
        }

        //RenderGraph
        internal void Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle destination, TextureHandle source, bool setGlobal = false)
        {
            if (m_GPUCopy == null)
                return;

            using (var builder = renderGraph.AddComputePass<PassData>("GPU Copy Depth", out var passData, base.profilingSampler))
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                passData.source = source;
                passData.destination = destination;
                passData.gpuCopyPass = this;
                passData.width = cameraData.pixelWidth;
                passData.height = cameraData.pixelHeight;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(destination, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                if (setGlobal)
                    builder.SetGlobalTextureAfterPass(destination, Shader.PropertyToID("_CameraDepthTexture"));

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    data.gpuCopyPass.SampleCopyChannel_xyzw2x(context.cmd, source, destination, new RectInt(0, 0, data.width, data.height));
                });
            }
        }
    }

}
