using System;
using System.Reflection;
using UnityEditor.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Class containing shader resources used in URP.
    /// </summary>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: Runtime Shaders", Order = 1000), HideInInspector]
    public class UniversalRenderPipelineRuntimeShaders : IRenderPipelineResources
    {
        [SerializeField][HideInInspector] private int m_Version = 0;

        /// <summary>Version of the resource. </summary>
        public int version => m_Version;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Shaders/Utils/FallbackError.shader")]
        Shader m_FallbackErrorShader;

        /// <summary>
        /// Fallback error shader
        /// </summary>
        public Shader fallbackErrorShader
        {
            get => m_FallbackErrorShader;
            set => this.SetValueAndNotify(ref m_FallbackErrorShader, value, nameof(m_FallbackErrorShader));
        }


        [SerializeField]
        [ResourcePath("Shaders/Utils/BlitHDROverlay.shader")]
        internal Shader m_BlitHDROverlay;

        /// <summary>
        /// Blit HDR Overlay shader.
        /// </summary>
        public Shader blitHDROverlay
        {
            get => m_BlitHDROverlay;
            set => this.SetValueAndNotify(ref m_BlitHDROverlay, value, nameof(m_BlitHDROverlay));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/CoreBlit.shader")]
        internal Shader m_CoreBlitPS;

        /// <summary>
        /// Core Blit shader.
        /// </summary>
        public Shader coreBlitPS
        {
            get => m_CoreBlitPS;
            set => this.SetValueAndNotify(ref m_CoreBlitPS, value, nameof(m_CoreBlitPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/CoreBlitColorAndDepth.shader")]
        internal Shader m_CoreBlitColorAndDepthPS;

        /// <summary>
        /// Core Blit Color And Depth shader.
        /// </summary>
        public Shader coreBlitColorAndDepthPS
        {
            get => m_CoreBlitColorAndDepthPS;
            set => this.SetValueAndNotify(ref m_CoreBlitColorAndDepthPS, value, nameof(m_CoreBlitColorAndDepthPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/Sampling.shader")]
        private Shader m_SamplingPS;

        /// <summary>
        /// Sampling shader.
        /// </summary>
        public Shader samplingPS
        {
            get => m_SamplingPS;
            set => this.SetValueAndNotify(ref m_SamplingPS, value, nameof(m_SamplingPS));
        }


        [SerializeField, ResourcePath("Shaders/Utils/GPUCopy.compute")]
        private ComputeShader m_CopyChannelCS;

        /// <summary>
        /// GPUCopy compute shader.
        /// </summary>
        public ComputeShader copyChannelCS
        {
            get => m_CopyChannelCS;
            set => this.SetValueAndNotify(ref m_CopyChannelCS, value);
        }

        [SerializeField, ResourcePath("Shaders/Utils/DepthPyramid.compute")]
        private ComputeShader m_DepthPyramidCS;

        /// <summary>
        /// DepthPyramid computeshader
        /// </summary>
        public ComputeShader depthPyramidCS
        {
            get => m_DepthPyramidCS;
            set => this.SetValueAndNotify(ref m_DepthPyramidCS, value);
        }

        /// <summary>
        /// PreIntegratedFGD
        /// </summary>
        [SerializeField, ResourcePath("Shaders/PreIntegratedFGD/PreIntegratedFGD_GGXDisneyDiffuse.shader")]
        private Shader m_PreIntegratedFGD_GGXDisneyDiffusePS;

        public Shader preIntegratedFGD_GGXDisneyDiffusePS
        {
            get => m_PreIntegratedFGD_GGXDisneyDiffusePS;
            set => this.SetValueAndNotify(ref m_PreIntegratedFGD_GGXDisneyDiffusePS, value);
        }

        [SerializeField, ResourcePath("Shaders/PreIntegratedFGD/PreIntegratedFGD_CharlieFabricLambert.shader")]
        private Shader m_PreIntegratedFGD_CharlieFabricLambertPS;

        public Shader preIntegratedFGD_CharlieFabricLambertPS
        {
            get => m_PreIntegratedFGD_CharlieFabricLambertPS;
            set => this.SetValueAndNotify(ref m_PreIntegratedFGD_CharlieFabricLambertPS, value);
        }


        /// <summary>
        /// GPU lights list compute shader.
        /// </summary>
        [SerializeField, ResourcePath("Shaders/Lights/GPULightsClearLists.compute")]
        private ComputeShader m_GpuLightsClearLists;

        public ComputeShader gpuLightsClearLists
        {
            get => m_GpuLightsClearLists;
            set => this.SetValueAndNotify(ref m_GpuLightsClearLists, value);
        }

        [SerializeField, ResourcePath("Shaders/Lights/GPULightsCoarseCulling.compute")]
        private ComputeShader m_GpuLightsCoarseCullingCS;

        public ComputeShader gpuLightsCoarseCullingCS
        {
            get => m_GpuLightsCoarseCullingCS;
            set => this.SetValueAndNotify(ref m_GpuLightsCoarseCullingCS, value);
        }

        [SerializeField, ResourcePath("Shaders/Lights/GPULightsFPTL.compute")]
        private ComputeShader m_GpuLightsFPTL;

        public ComputeShader gpuLightsFPTL
        {
            get => m_GpuLightsFPTL;
            set => this.SetValueAndNotify(ref m_GpuLightsFPTL, value);
        }

        [SerializeField, ResourcePath("Shaders/Lights/GPULightsCluster.compute")]
        private ComputeShader m_GpuLightsCluster;

        public ComputeShader gpuLightsCluster
        {
            get => m_GpuLightsCluster;
            set => this.SetValueAndNotify(ref m_GpuLightsCluster, value);
        }


#if UNITY_EDITOR
        public void EnsureShadersCompiled()
        {
            void CheckComputeShaderMessages(ComputeShader computeShader)
            {
                foreach (var message in UnityEditor.ShaderUtil.GetComputeShaderMessages(computeShader))
                {
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    {
                        // Will be catched by the try in HDRenderPipelineAsset.CreatePipeline()
                        throw new System.Exception(System.String.Format(
                            "Compute Shader compilation error on platform {0} in file {1}:{2}: {3}{4}\n" +
                            "HDRP will not run until the error is fixed.\n",
                            message.platform, message.file, message.line, message.message, message.messageDetails
                        ));
                    }
                }
            }

            // We iterate over all compute shader to verify if they are all compiled, if it's not the case then
            // we throw an exception to avoid allocating resources and crashing later on by using a null compute kernel.
            this.ForEachFieldOfType<ComputeShader>(CheckComputeShaderMessages, BindingFlags.Public | BindingFlags.Instance);
        }
#endif

    }
}
