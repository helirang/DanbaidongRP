using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    internal class TileClusterDebugPass : ScriptableRenderPass
    {
        // Profiling tag

        // Public Variables

        // Private Variables
        private Material m_Material;
        private RTHandle m_RenderTarget;
        private ShaderVariablesLightList m_LightCBuffer;
        private DebugTileClusterMode m_DebugMode;
        private int m_ClusterDebugID;
        // Constants

        // Statics


        public TileClusterDebugPass(Material mat)
        {
            base.profilingSampler = new ProfilingSampler(nameof(TileClusterDebugPass));
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_Material = mat;
            m_DebugMode = DebugTileClusterMode.None;
            m_ClusterDebugID = 0;
        }



        public void Setup(ref CameraData cameraData, DebugTileClusterMode debugMode, int clusterDebugID, ShaderVariablesLightList lightCBuffer)
        {
            m_DebugMode = debugMode;
            m_LightCBuffer = lightCBuffer;
            m_ClusterDebugID = clusterDebugID;
        }

        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = renderingData.commandBuffer;
            var sourceTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Vector2 viewportScale = sourceTexture.useScaling ? new Vector2(sourceTexture.rtHandleProperties.rtHandleScale.x, sourceTexture.rtHandleProperties.rtHandleScale.y) : Vector2.one;

            using (new ProfilingScope(cmd, new ProfilingSampler("TileClusterDebug")))
            {
                m_Material.SetFloat("_DebugTileClusterMode", (float)m_DebugMode);
                m_Material.SetInteger("_ClusterDebugID", m_ClusterDebugID);
                ConstantBuffer.Push(cmd, m_LightCBuffer, m_Material, Shader.PropertyToID("ShaderVariablesLightList"));
                
                Blitter.BlitTexture(cmd, viewportScale, m_Material, 0);
            }
        }


        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {

        }


        internal class ShaderConstants
        {
            public static readonly int _DebugHDRModeId = Shader.PropertyToID("_DebugHDRMode");
        }

    }
}
