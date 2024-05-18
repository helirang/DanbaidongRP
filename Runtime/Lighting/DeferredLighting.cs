
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    // Render all deferred shading models.
    internal class DeferredLighting : ScriptableRenderPass
    {
        DeferredLights m_DeferredLights;

        // Public Variables

        // Private Variables
        private ComputeShader m_DeferredLightingCS;
        private int m_DeferredClassifyTilesKernel;
        private int m_DeferredLightingKernel;


        // Constants
        private const int c_deferredLightingTileSize = 16;

        // Statics

        public DeferredLighting(RenderPassEvent evt, DeferredLights deferredLights, ComputeShader deferredLightingCS)
        {
            base.profilingSampler = new ProfilingSampler(nameof(DeferredLighting));
            base.renderPassEvent = evt;
            m_DeferredLights = deferredLights;

            m_DeferredLightingCS = deferredLightingCS;

            m_DeferredClassifyTilesKernel = m_DeferredLightingCS.FindKernel("DeferredClassifyTiles");
            m_DeferredLightingKernel = m_DeferredLightingCS.FindKernel("DeferredLighting0");
        }

        private class PassData
        {
            internal UniversalCameraData cameraData;
            internal UniversalLightData lightData;
            internal UniversalShadowData shadowData;

            internal TextureHandle lightingHandle;
            internal TextureHandle stencilHandle;
            internal TextureHandle[] gbuffer;
            internal DeferredLights deferredLights;

            // Compute shader
            internal ComputeShader deferredLightingCS;
            internal int deferredClassifyTilesKernel;
            internal int deferredLightingKernel;

            internal int numTilesX;
            internal int numTilesY;

            // Compute Buffers
            internal BufferHandle dispatchIndirectBuffer;
            internal BufferHandle tileListBuffer;

            // PreIntergratedFGD
            internal TextureHandle FGD_GGXAndDisneyDiffuse;

            // Sky Ambient
            internal BufferHandle ambientProbe;
            // Sky Reflect
            internal TextureHandle reflectProbe;
        }

        static void InitIndirectComputeBufferValue(GraphicsBuffer buffer)
        {
            uint[] array = new uint[buffer.count];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = (i % 3) == 0 ? 0u : 1u;
            }
            buffer.SetData(array);
        }

        static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            // BuildIndirect
            {
                InitIndirectComputeBufferValue(data.dispatchIndirectBuffer);
                cmd.SetComputeTextureParam(data.deferredLightingCS, data.deferredClassifyTilesKernel, "_StencilTexture", data.stencilHandle, 0, RenderTextureSubElement.Stencil);

                cmd.SetComputeBufferParam(data.deferredLightingCS, data.deferredClassifyTilesKernel, "g_DispatchIndirectBuffer", data.dispatchIndirectBuffer);
                cmd.SetComputeBufferParam(data.deferredLightingCS, data.deferredClassifyTilesKernel, "g_TileList", data.tileListBuffer);

                cmd.DispatchCompute(data.deferredLightingCS, data.deferredClassifyTilesKernel, data.numTilesX, data.numTilesY, 1);
            }

            // Lighting
            {
                // Bind PreIntegratedFGD before deferred shading.
                PreIntegratedFGD.instance.Bind(cmd, PreIntegratedFGD.FGDIndex.FGD_GGXAndDisneyDiffuse, data.FGD_GGXAndDisneyDiffuse);


                Matrix4x4 viewMatrix = data.cameraData.GetViewMatrix();
                // Jittered, non-gpu
                Matrix4x4 projectionMatrix = data.cameraData.GetProjectionMatrix();
                // Jittered, gpu
                Matrix4x4 gpuProjectionMatrix = data.cameraData.GetGPUProjectionMatrix(true);
                Matrix4x4 viewAndProjectionMatrix = gpuProjectionMatrix * viewMatrix;
                Matrix4x4 inverseViewMatrix = Matrix4x4.Inverse(viewMatrix);
                Matrix4x4 inverseProjectionMatrix = Matrix4x4.Inverse(gpuProjectionMatrix);
                Matrix4x4 inverseViewProjection = inverseViewMatrix * inverseProjectionMatrix;

                Matrix4x4 proj = data.cameraData.GetProjectionMatrix(0);
                Matrix4x4 view = data.cameraData.GetViewMatrix(0);
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(proj, true);
                var targetMat = Matrix4x4.Inverse(gpuProj * view);

                cmd.SetComputeMatrixParam(data.deferredLightingCS, "_MATRIX_TEST", targetMat);

                for (int modelIndex = 0; modelIndex < (int)ShadingModels.CurModelsNum; modelIndex++)
                {
                    var kernelIndex = data.deferredLightingKernel + modelIndex;
                    cmd.SetComputeIntParam(data.deferredLightingCS, "_ShadingModelIndex", modelIndex);
                    cmd.SetComputeVectorParam(data.deferredLightingCS, "_TilesNum", new Vector2(data.numTilesX, data.numTilesY));

                    cmd.SetComputeBufferParam(data.deferredLightingCS, kernelIndex, "g_TileList", data.tileListBuffer);
                    cmd.SetComputeTextureParam(data.deferredLightingCS, kernelIndex, "_LightingTexture", data.lightingHandle);
                    cmd.SetComputeTextureParam(data.deferredLightingCS, kernelIndex, "_StencilTexture", data.stencilHandle, 0, RenderTextureSubElement.Stencil);

                    cmd.SetComputeBufferParam(data.deferredLightingCS, kernelIndex, "_AmbientProbeData", data.ambientProbe);
                    cmd.SetComputeTextureParam(data.deferredLightingCS, kernelIndex, "_SkyTexture", data.reflectProbe);

                    cmd.DispatchCompute(data.deferredLightingCS, kernelIndex, data.dispatchIndirectBuffer, (uint)modelIndex * 3 * sizeof(uint));
                }
            }
        }

        internal void Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle color, TextureHandle depth, TextureHandle[] gbuffer)
        {


            using (var builder = renderGraph.AddComputePass<PassData>("Deferred Lighting", out var passData, base.profilingSampler))
            {
                // Access resources
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();

                // Setup passData
                passData.cameraData = cameraData;
                passData.lightData = lightData;
                passData.shadowData = shadowData;

                passData.lightingHandle = color;
                passData.stencilHandle = depth;
                passData.deferredLights = m_DeferredLights;

                passData.deferredLightingCS = m_DeferredLightingCS;
                passData.deferredClassifyTilesKernel = m_DeferredClassifyTilesKernel;
                passData.deferredLightingKernel = m_DeferredLightingKernel;

                var width = cameraData.cameraTargetDescriptor.width;
                var height = cameraData.cameraTargetDescriptor.height;
                passData.numTilesX = RenderingUtils.DivRoundUp(width, c_deferredLightingTileSize);
                passData.numTilesY = RenderingUtils.DivRoundUp(height, c_deferredLightingTileSize);

                var indirectBufferDesc = new BufferDesc((int)ShadingModels.CurModelsNum * 3, sizeof(uint), "dispatchIndirectBuffer", GraphicsBuffer.Target.IndirectArguments);
                passData.dispatchIndirectBuffer = renderGraph.CreateBuffer(indirectBufferDesc);
                var tileListBufferDesc = new BufferDesc((int)ShadingModels.CurModelsNum * passData.numTilesX * passData.numTilesY, sizeof(uint), "tileListBuffer");
                passData.tileListBuffer = renderGraph.CreateBuffer(tileListBufferDesc);

                passData.FGD_GGXAndDisneyDiffuse = PreIntegratedFGD.instance.ImportToRenderGraph(renderGraph, PreIntegratedFGD.FGDIndex.FGD_GGXAndDisneyDiffuse);

                // Sky Environment
                passData.ambientProbe = resourceData.skyAmbientProbe;
                passData.reflectProbe = resourceData.skyReflectionProbe;

                // Declare input/output
                builder.UseTexture(passData.lightingHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.stencilHandle, AccessFlags.Read);
                builder.UseBuffer(passData.dispatchIndirectBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.tileListBuffer, AccessFlags.ReadWrite);
                builder.UseTexture(passData.FGD_GGXAndDisneyDiffuse, AccessFlags.Read);

                builder.UseBuffer(passData.ambientProbe, AccessFlags.Read);
                builder.UseTexture(passData.reflectProbe, AccessFlags.Read);

                // TODO: Delete
                builder.UseTexture(resourceData.cameraDepthPyramidTexture, AccessFlags.Read);

                for (int i = 0; i < gbuffer.Length; ++i)
                {
                    if (i != m_DeferredLights.GBufferLightingIndex)
                        builder.UseTexture(gbuffer[i], AccessFlags.Read);
                }
                
                // Setup builder state
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context);
                });
            }
        }

        // ScriptableRenderPass
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            m_DeferredLights.OnCameraCleanup(cmd);
        }
    }
}
