using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Generates an in-place depth pyramid 
    /// TODO: Mip-mapping depth is problematic for precision at lower mips, generate a packed atlas instead
    /// </summary>
    public class DepthPyramidPass : ScriptableRenderPass
    {
        private RTHandle m_DepthMipChainTexture { get; set; }
        private RenderingUtils.PackedMipChainInfo m_PackedMipChainInfo;
        private bool m_Mip0AlreadyComputed;

        private ComputeShader m_Shader;
        private int m_DepthDownsampleKernel;

        private int[] m_SrcOffset;
        private int[] m_DstOffset;


        /// <summary>
        /// 
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="computeShader"></param>
        public DepthPyramidPass(RenderPassEvent evt, ComputeShader computeShader)
        {
            base.profilingSampler = new ProfilingSampler("DepthPyramid");
            renderPassEvent = evt;

            m_Shader = computeShader;
            m_DepthDownsampleKernel = m_Shader.FindKernel("KDepthDownsample8DualUav");

            m_SrcOffset = new int[4];
            m_DstOffset = new int[4];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="depthMipChainTexture"></param>
        internal void Setup(RTHandle depthMipChainTexture, RenderingUtils.PackedMipChainInfo info, bool mip0AlreadyComputed = false)
        {
            this.m_DepthMipChainTexture = depthMipChainTexture;
            this.m_PackedMipChainInfo = info;
            this.m_Mip0AlreadyComputed = mip0AlreadyComputed;
        }

        private class PassData
        {
            internal TextureHandle depthTexture;
            internal RenderingUtils.PackedMipChainInfo mipChainInfo;
            internal DepthPyramidPass pass;

            internal bool mip0AlreadyComputed;
        }


        internal void Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle depthMipChainTexture, RenderingUtils.PackedMipChainInfo mipChainInfo, bool mip0AlreadyComputed = false)
        {

            using (var builder = renderGraph.AddComputePass<PassData>("Depth Pyramid", out var passData, base.profilingSampler))
            {
                passData.depthTexture = depthMipChainTexture;
                passData.mipChainInfo = mipChainInfo;
                passData.pass = this;
                passData.mip0AlreadyComputed = mip0AlreadyComputed;

                builder.UseTexture(passData.depthTexture, AccessFlags.ReadWrite);
                // TODO: Set true.
                builder.AllowPassCulling(false);
                builder.EnableAsyncCompute(true);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    data.pass.RenderMinDepthPyramid(context.cmd, data.depthTexture, data.mipChainInfo, data.mip0AlreadyComputed);
                });
            }
        }

        internal void RenderMinDepthPyramid(ComputeCommandBuffer cmd, TextureHandle texture, RenderingUtils.PackedMipChainInfo info, bool mip0AlreadyComputed)
        {
            var cs = m_Shader;
            int kernel = m_DepthDownsampleKernel;

            // TODO: Do it 1x MIP at a time for now. In the future, do 4x MIPs per pass, or even use a single pass.
            // Note: Gather() doesn't take a LOD parameter and we cannot bind an SRV of a MIP level,
            // and we don't support Min samplers either. So we are forced to perform 4x loads.
            for (int i = 1; i < info.mipLevelCount; i++)
            {
                if (mip0AlreadyComputed && i == 1) continue;

                Vector2Int dstSize = info.mipLevelSizes[i];
                Vector2Int dstOffset = info.mipLevelOffsets[i];
                Vector2Int srcSize = info.mipLevelSizes[i - 1];
                Vector2Int srcOffset = info.mipLevelOffsets[i - 1];
                Vector2Int srcLimit = srcOffset + srcSize - Vector2Int.one;

                m_SrcOffset[0] = srcOffset.x;
                m_SrcOffset[1] = srcOffset.y;
                m_SrcOffset[2] = srcLimit.x;
                m_SrcOffset[3] = srcLimit.y;

                m_DstOffset[0] = dstOffset.x;
                m_DstOffset[1] = dstOffset.y;
                m_DstOffset[2] = 0;
                m_DstOffset[3] = 0;

                cmd.SetComputeIntParams(cs, ShaderConstants._SrcOffsetAndLimit, m_SrcOffset);
                cmd.SetComputeIntParams(cs, ShaderConstants._DstOffset, m_DstOffset);
                cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._DepthMipChain, texture);
                cmd.DispatchCompute(cs, kernel, RenderingUtils.DivRoundUp(dstSize.x, 8), RenderingUtils.DivRoundUp(dstSize.y, 8), ((RTHandle)texture).rt.volumeDepth);
            }
        }

        private static class ShaderConstants
        {
            public static readonly int _SrcOffsetAndLimit = Shader.PropertyToID("_SrcOffsetAndLimit");
            public static readonly int _DstOffset = Shader.PropertyToID("_DstOffset");
            public static readonly int _DepthMipChain = Shader.PropertyToID("_DepthMipChain");
        }
    }

}
