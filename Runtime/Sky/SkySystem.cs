using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// SkySystem used for rendering sky in the main view.
    /// TODO: SkySystem also used for lighting the scene(ambient probe and sky reflection).
    /// </summary>
    internal class SkySystem : IDisposable
    {
        Material m_StandardSkyboxMat;

        SphericalHarmonicsL2 m_BlackAmbientProbe = new SphericalHarmonicsL2();

        bool m_UpdateRequired = false;
        bool m_StaticSkyUpdateRequired = false;
        int m_Resolution, m_LowResolution;

        // Sky used for static lighting. It will be used for ambient lighting if Ambient Mode is set to Static (even when realtime GI is enabled)
        // It will also be used for lightmap and light probe baking
        //SkyUpdateContext m_StaticLightingSky = new SkyUpdateContext();

        // This interpolation volume stack is used to interpolate the lighting override separately from the visual sky.
        // If a sky setting is present in this volume then it will be used for lighting override.
        public VolumeStack lightingOverrideVolumeStack { get; private set; }

        //public LayerMask lightingOverrideLayerMask { get; private set; } = -1;

        static Dictionary<int, Type> m_SkyTypesDict = null;
        //public static Dictionary<int, Type> skyTypesDict { get { if (m_SkyTypesDict == null) UpdateSkyTypes(); return m_SkyTypesDict; } }

        //static Dictionary<int, Type> m_CloudTypesDict = null;
        //public static Dictionary<int, Type> cloudTypesDict { get { if (m_CloudTypesDict == null) UpdateCloudTypes(); return m_CloudTypesDict; } }

        // This list will hold the static lighting sky that should be used for baking ambient probe.
        // In practice we will always use the last one registered but we use a list to be able to roll back to the previous one once the user deletes the superfluous instances.
        //private static List<StaticLightingSky> m_StaticLightingSkies = new List<StaticLightingSky>();

        CubemapArray m_BlackCubemapArray;
        ComputeBuffer m_BlackAmbientProbeBuffer;

        // SkyUpdate
        private SkySettings m_SkySettings;
        private SkyRenderer m_SkyRenderer;
        private float m_LastUpdateTime = -1.0f;
        private int m_SkyParametersHash = -1;

        // SkyRendering
        private SphericalHarmonicsL2 m_AmbientProbe;
        private RTHandle m_SkyboxCubemapRT;
        private CubemapArray m_SkyboxBSDFCubemapArray;

        public SkyRenderer skyRenderer
        {
            get { return m_SkyRenderer; }
            set { m_SkyRenderer = value; }
        }
        public SkySettings skySettings
        {
            get { return m_SkySettings; }
            set 
            {
                // We cleanup the renderer first here because in some cases, after scene unload, the skySettings field will be "null" because the object got destroyed.
                // In this case, the renderer might stay allocated until a non null value is set. To avoid a lingering allocation, we cleanup first before anything else.
                // So next frame after scene unload, renderer will be freed.
                if (skyRenderer != null && (value == null || value.GetSkyRendererType() != skyRenderer.GetType()))
                {
                    skyRenderer.Cleanup();
                    skyRenderer = null;
                }

                //if (m_SkySettings == null)

                if (m_SkySettings == value)
                    return;

                m_SkyParametersHash = -1;
                m_SkySettings = value;
                m_LastUpdateTime = -1.0f;

                if (m_SkySettings != null && skyRenderer == null)
                {
                    var rendererType = m_SkySettings.GetSkyRendererType();
                    skyRenderer = (SkyRenderer)Activator.CreateInstance(rendererType);
                    skyRenderer.Build();
                }
            }
        }

        static SkySystem m_Instance = null;

        internal static SkySystem instance
        {
            get
            {
                if (m_Instance == null)
                    m_Instance = new SkySystem();

                return m_Instance;
            }
        }

        SkySystem()
        {
        }

        internal SkySettings GetSkySettings(VolumeStack volumeStack)
        {
            var visualSky = volumeStack.GetComponent<VisualSky>();
            SkyType typeEnum = (SkyType)visualSky.skyType.value;
            SkySettings skySettings = null;
            Type skyType = null;
            switch (typeEnum)
            {
                case SkyType.ProceduralToon:
                    skyType = typeof(ProceduralToonSky);
                    break;
                case SkyType.Gradient:
                    skyType = typeof(GradientSky);
                    break;
                case SkyType.HDRI:
                default:
                    skyType = typeof(HDRISky);
                    break;
            }

            skySettings = (SkySettings)volumeStack.GetComponent(skyType);

            return skySettings;
        }

        internal void UpdateCurrentSky()
        {
            var volumeStack = VolumeManager.instance.stack;

            skySettings = GetSkySettings(volumeStack);

        }

        internal void SetGlobalSkyData(CommandBuffer cmd, SkySettings skySettings)
        {

        }

        /// <summary>
        /// Build when create Pipeline.
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="runtimeResources"></param>
        public void Build(UniversalRenderPipelineAsset asset, UniversalRenderPipelineRuntimeResources runtimeResources)
        {
            m_LowResolution = 16;


            //lightingOverrideVolumeStack = VolumeManager.instance.CreateStack();


            InitializeBlackCubemapArray(cubemapCount: 1);

            //// Initialize black ambient probe buffer
            //if (m_BlackAmbientProbeBuffer == null)
            //{
            //    // 27 SH Coeffs in 7 float4
            //    m_BlackAmbientProbeBuffer = new ComputeBuffer(7, 16);
            //    float[] blackValues = new float[28];
            //    for (int i = 0; i < 28; ++i)
            //        blackValues[i] = 0.0f;
            //    m_BlackAmbientProbeBuffer.SetData(blackValues);
            //}

        }

        private void InitializeBlackCubemapArray(int cubemapCount = 1)
        {
            if (m_BlackCubemapArray == null)
            {
                m_BlackCubemapArray = new CubemapArray(1, cubemapCount, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Repeat,
                    wrapModeV = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 0,
                    name = "BlackCubemapArray"
                };

                Color32[] black = { new Color32(0, 0, 0, 0) };

                for (int element = 0; element < cubemapCount; ++element)
                {
                    for (int i = 0; i < 6; i++)
                        m_BlackCubemapArray.SetPixels32(black, (CubemapFace)i, element);
                }

                m_BlackCubemapArray.Apply();
            }
        }

        public bool IsValid()
        {
            // We need to check m_SkySettings because it can be "nulled" when destroying the volume containing the settings (as it's a ScriptableObject) without the context knowing about it.
            return m_SkySettings != null;
        }

        internal Texture GetSkyCubemap()
        {
            if (IsValid())
            {
                return m_SkyboxCubemapRT;
            }
            else
            {
                return CoreUtils.blackCubeTexture;
            }
        }

        internal Texture GetReflectionTexture()
        {
            if (IsValid())
            {
                return m_SkyboxBSDFCubemapArray;
            }
            else
            {
                return m_BlackCubemapArray;
            }
        }

        internal void RenderSkyToCubemap(CommandBuffer cmd, ref RenderingData renderingData)
        {

        }

        int GetSunLightHashCode(Light light)
        {
            unchecked
            {
                // Sun could influence the sky (like for procedural sky). We need to handle this possibility. If sun property change, then we need to update the sky
                int hash = 13;
                hash = hash * 23 + light.transform.position.GetHashCode();
                hash = hash * 23 + light.transform.rotation.GetHashCode();
                hash = hash * 23 + light.color.GetHashCode();
                hash = hash * 23 + light.colorTemperature.GetHashCode();
                hash = hash * 23 + light.intensity.GetHashCode();

                return hash;
            }
        }

        int ComputeSkyHash(Light sunLight, SkyAmbientMode ambientMode, bool staticSky = false)
        {
            int sunHash = 0;
            if (sunLight != null && skyRenderer.SupportDynamicSunLight)
            {
                sunHash = GetSunLightHashCode(sunLight);
            }

            // No check for camera

            int skyHash = sunHash * 23 + skySettings.GetHashCode();

            skyHash = skyHash * 23 + (staticSky ? 1 : 0);
            skyHash = skyHash * 23 + (ambientMode == SkyAmbientMode.Static ? 1 : 0);

            return skyHash;
        }

        /// <summary>
        /// SkySystem is global sky environment.
        /// This use skyHash or realTime to ensure update only once in one frame.
        /// </summary>
        internal void UpdateEnvironment(
            CommandBuffer cmd, 
            ref RenderingData renderingData, 
            bool updateRequired,
            bool updateAmbientProbe, 
            bool staticSky, 
            SkyAmbientMode ambientMode)
        {
            if (IsValid())
            {
                Light sunLight = null;
                int mainLightIndex = renderingData.lightData.mainLightIndex;
                if (mainLightIndex < 0)
                {
                    sunLight = null;
                }
                else
                {
                    sunLight = renderingData.lightData.visibleLights[mainLightIndex].light;
                }

                var timeSinceLastUpdate = Time.realtimeSinceStartup - m_LastUpdateTime;

                // When update is not requested and the context is already valid (ie: already computed at least once),
                // we need to early out in two cases:
                // - updateMode is "OnDemand" in which case we never update unless explicitly requested
                // - updateMode is "Realtime" in which case we only update if the time threshold for realtime update is passed.
                // TODO: Check ambient and cubemap texture valid, exist: we need set it for shader, not exist: render.
                if (!updateRequired)
                {
                    if (skySettings.updateMode.value == EnvironmentUpdateMode.OnDemand)
                        return;
                    else if (skySettings.updateMode.value == EnvironmentUpdateMode.Realtime && timeSinceLastUpdate < skySettings.updatePeriod.value)
                        return;
                }

                int skyHash = ComputeSkyHash(sunLight, ambientMode, staticSky);
                bool forceUpdate = updateRequired;

                // TODO: need AcquireSkyRenderingContext, if the context was invalid or the hash has changed, this will request for an update.?
                //forceUpdate |= AcquireSkyRenderingContext;

                forceUpdate |= skyRenderer.DoUpdate(Time.frameCount);

                forceUpdate |= skySettings.updateMode.value == EnvironmentUpdateMode.OnChanged && skyHash != m_SkyParametersHash;
                forceUpdate |= skySettings.updateMode.value == EnvironmentUpdateMode.Realtime && timeSinceLastUpdate > skySettings.updatePeriod.value;


                if (forceUpdate)
                {
                    Debug.Log("SkySystem update " + Time.frameCount);

                    // Render sky to cubemap for indirect diffuse(SH, SphericalHarmonics) and specular(convolution cubemap)
                    //var skyCubemap = GenerateSkyCubemap();

                    // Compute ambient for shader SH use.
                    if (updateAmbientProbe)
                    {
                        //UpdateAmbientProbe();
                    }

                    // Render skyboxBSDFCubemapArray for shader use.
                    // TODO: need we check other params?
                    bool supportsConvolution = !staticSky;
                    if (supportsConvolution)
                    {
                        //RenderCubemapGGXConvolution();
                    }

                    m_SkyParametersHash = skyHash;
                    m_LastUpdateTime = Time.realtimeSinceStartup;
                }

            }


        }

        /// <summary>
        /// Call this in renderPass
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="renderingData"></param>
        internal void RenderSky(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (camera.clearFlags != CameraClearFlags.Skybox)
                return;

            if (IsValid())
            {
                m_SkyRenderer.DoUpdate(Time.frameCount);
                m_SkyRenderer.RenderSky(cmd, ref renderingData, skySettings, renderForCubemap: false);
            }
        }



        internal void RenderOpaqueAtmosphericScattering(CommandBuffer cmd, ref RenderingData renderingData)
        {

        }

        public static void ClearAll()
        {
            if (m_Instance != null)
                m_Instance.Dispose();

            m_Instance = null;
        }

        public void Dispose()
        {
            if (skyRenderer != null)
                skyRenderer.Cleanup();
            skyRenderer = null;

            CoreUtils.Destroy(m_BlackCubemapArray);
            m_BlackAmbientProbeBuffer.Release();
        }
    }
}