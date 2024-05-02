using System;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal.Internal
{
    //-----------------------------------------------------------------------------
    // light extension
    //-----------------------------------------------------------------------------
    static class VisibleLightExtensionMethods
    {
        public struct VisibleLightAxisAndPosition
        {
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Up;
            public Vector3 Right;
        }

        public static Vector3 GetPosition(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(3);
        }

        public static Vector3 GetForward(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(2);
        }

        public static Vector3 GetUp(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(1);
        }

        public static Vector3 GetRight(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(0);
        }

        public static VisibleLightAxisAndPosition GetAxisAndPosition(this VisibleLight value)
        {
            var matrix = value.localToWorldMatrix;
            VisibleLightAxisAndPosition output;
            output.Position = matrix.GetColumn(3);
            output.Forward = matrix.GetColumn(2);
            output.Up = matrix.GetColumn(1);
            output.Right = matrix.GetColumn(0);
            return output;
        }
    }

    //-----------------------------------------------------------------------------
    // structure definition
    //-----------------------------------------------------------------------------

    [GenerateHLSL]
    internal enum LightVolumeType
    {
        Cone,
        Sphere,
        Box,
        Count
    }

    [GenerateHLSL]
    internal enum LightCategory
    {
        Punctual,
        Area,
        Env,
        Decal,
        Count
    }

    [GenerateHLSL]
    internal enum LightFeatureFlags
    {
        // Light bit mask must match LightDefinitions.s_LightFeatureMaskFlags value
        Punctual = 1 << 12,
        Area = 1 << 13,
        Directional = 1 << 14,
        Env = 1 << 15,
        Sky = 1 << 16,
        SSRefraction = 1 << 17,
        SSReflection = 1 << 18,
        // If adding more light be sure to not overflow LightDefinitions.s_LightFeatureMaskFlags
    }

    // Caution: Order is important and is use for optimization in light loop
    [GenerateHLSL]
    internal enum GPULightType
    {
        Directional,
        Point,
        Spot,
        ProjectorPyramid,
        ProjectorBox,

        // AreaLight
        //Tube, // Keep Line lights before Rectangle. This is needed because of a compiler bug (see LightLoop.hlsl)
        //Rectangle,
        // Currently not supported in real time (just use for reference)
        //Disc,
        // Sphere,
    };

    //Do not change these numbers!!
    //Its not a full power of 2 because the last light slot is reserved.
    internal enum FPTLMaxLightSizes
    {
        Low = 31,
        High = 63
    }

    [GenerateHLSL]
    class LightDefinitions
    {
        public static int s_MaxNrBigTileLightsPlusOne = 512;      // may be overkill but the footprint is 2 bits per pixel using uint16.
        public static float s_ViewportScaleZ = 1.0f;
        public static int s_UseLeftHandCameraSpace = 1;

        public static int s_TileSizeFptl = 16;
        public static int s_TileSizeClustered = 32;
        public static int s_TileSizeBigTile = 64;

        // Tile indexing constants for indirect dispatch deferred pass : [2 bits for eye index | 15 bits for tileX | 15 bits for tileY]
        public static int s_TileIndexMask = 0x7FFF;
        public static int s_TileIndexShiftX = 0;
        public static int s_TileIndexShiftY = 15;
        public static int s_TileIndexShiftEye = 30;

        // feature variants
        public static int s_NumFeatureVariants = 29;

        // light list limits
        public static int s_LightListMaxCoarseEntries = 64;
        public static int s_LightClusterMaxCoarseEntries = 128;

        // We have room for ShaderConfig.FPTLMaxLightCount lights, plus 1 implicit value for length.
        // We allocate only 16 bits per light index & length, thus we divide by 2, and store in a word buffer.
        /// <summary>
        /// Maximum number of lights for a fine pruned light tile. This number can only be the prespecified possibilities in FPTLMaxLightSizes
        /// Lower count will mean some memory savings.
        /// Note: For any rendering bigger than 4k (in native) it is recommended to use Low count per tile, to avoid possible artifacts.
        /// </summary>
        public static int s_LightDwordPerFptlTile = (((int)FPTLMaxLightSizes.High + 1)) / 2;
        public static int s_LightClusterPackingCountBits = (int)Mathf.Ceil(Mathf.Log(Mathf.NextPowerOfTwo((int)FPTLMaxLightSizes.High), 2));
        public static int s_LightClusterPackingCountMask = (1 << s_LightClusterPackingCountBits) - 1;
        public static int s_LightClusterPackingOffsetBits = 32 - s_LightClusterPackingCountBits;
        public static int s_LightClusterPackingOffsetMask = (1 << s_LightClusterPackingOffsetBits) - 1;

        // Following define the maximum number of bits use in each feature category.
        public static uint s_LightFeatureMaskFlags = 0xFFF000;
        public static uint s_LightFeatureMaskFlagsOpaque = 0xFFF000 & ~((uint)LightFeatureFlags.SSRefraction); // Opaque don't support screen space refraction
        public static uint s_LightFeatureMaskFlagsTransparent = 0xFFF000 & ~((uint)LightFeatureFlags.SSReflection); // Transparent don't support screen space reflection
        public static uint s_MaterialFeatureMaskFlags = 0x000FFF;   // don't use all bits just to be safe from signed and/or float conversions :/

        // Screen space shadow flags
        public static uint s_RayTracedScreenSpaceShadowFlag = 0x1000;
        public static uint s_ScreenSpaceColorShadowFlag = 0x100;
        public static uint s_InvalidScreenSpaceShadow = 0xff;
        public static uint s_ScreenSpaceShadowIndexMask = 0xff;

        //Contact shadow bit definitions
        public static int s_ContactShadowFadeBits = 8;
        public static int s_ContactShadowMaskBits = 32 - s_ContactShadowFadeBits;
        public static int s_ContactShadowFadeMask = (1 << s_ContactShadowFadeBits) - 1;
        public static int s_ContactShadowMaskMask = (1 << s_ContactShadowMaskBits) - 1;

    }

    [GenerateHLSL]
    struct SFiniteLightBound
    {
        public Vector3 boxAxisX; // Scaled by the extents (half-size)
        public Vector3 boxAxisY; // Scaled by the extents (half-size)
        public Vector3 boxAxisZ; // Scaled by the extents (half-size)
        public Vector3 center;   // Center of the bounds (box) in camera space
        public float scaleXY;  // Scale applied to the top of the box to turn it into a truncated pyramid (X = Y)
        public float radius;     // Circumscribed sphere for the bounds (box)
    };

    [GenerateHLSL]
    struct LightVolumeData
    {
        public Vector3 lightPos;     // Of light's "origin"
        public uint lightVolume;     // Type index

        public Vector3 lightAxisX;   // Normalized
        public uint lightCategory;   // Category index

        public Vector3 lightAxisY;   // Normalized
        public float radiusSq;       // Cone and sphere: light range squared

        public Vector3 lightAxisZ;   // Normalized
        public float cotan;          // Cone: cotan of the aperture (half-angle)

        public Vector3 boxInnerDist; // Box: extents (half-size) of the inner box
        public uint featureFlags;

        public Vector3 boxInvRange;  // Box: 1 / (OuterBoxExtents - InnerBoxExtents)
        public float unused2;
    };

    /// <summary>
    /// unsafe for array
    /// </summary>
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    //unsafe struct ShaderVariablesLightList
    struct ShaderVariablesLightList
    {
        public Matrix4x4 g_mInvScrProjectionArr;
        public Matrix4x4 g_mScrProjectionArr;
        public Matrix4x4 g_mInvProjectionArr;
        public Matrix4x4 g_mProjectionArr;

        public Vector4 g_screenSize;

        public Vector2Int g_viDimensions;
        public int g_iNrVisibLights;
        public uint g_isOrthographic;

        public uint g_BaseFeatureFlags;
        public int g_iNumSamplesMSAA;
        public uint _EnvLightIndexShift;
        public uint _DecalIndexShift;

        // From HDRP ShaderVariablesGlobal
        // Tile/Cluster
        public uint _NumTileFtplX;
        public uint _NumTileFtplY;
        public float g_fClustScale;
        public float g_fClustBase;

        public float g_fNearPlane;
        public float g_fFarPlane;
        public int g_iLog2NumClusters; // We need to always define these to keep constant buffer layouts compatible
        public uint g_isLogBaseBufferEnabled;

        public uint _NumTileClusteredX;
        public uint _NumTileClusteredY;
        public uint _DirectionalLightCount;
        public int _EnvSliceSize; // Unused

        //public uint _EnableDecalLayers;
    }


    [GenerateHLSL(PackingRules.Exact, false)]
    struct GPULightData
    {
        // Packing order depends on chronological access to avoid cache misses
        // Make sure to respect the 16-byte alignment
        public Vector3 lightPosWS;
        public uint lightLayerMask;

        public Vector3 lightColor;
        public int lightFlags;

        public Vector4 lightAttenuation;

        public Vector3 lightDirection;
        public int shadowLightIndex;

        public Vector4 lightOcclusionProbInfo;
        
        public int cookieLightIndex;
        public int shadowType;
        public float minRoughness;
        public float __unused2__;

    };

    [GenerateHLSL(PackingRules.Exact, false)]
    struct DirectionalLightData
    {
        // Packing order depends on chronological access to avoid cache misses
        // Make sure to respect the 16-byte alignment
        public Vector3 lightPosWS;
        public uint lightLayerMask;

        public Vector3 lightColor;
        public int lightFlags;

        public Vector4 lightAttenuation;

        public Vector3 lightDirection;
        public int shadowlightIndex;

        public float minRoughness;
        public float lightDimmer;       //TODO: make it used
        public float diffuseDimmer;     //TODO: make it used
        public float specularDimmer;    //TODO: make it used
    };

    //-----------------------------------------------------------------------------
    // render pass
    //-----------------------------------------------------------------------------

    /// <summary>
    /// Main Class
    /// </summary>
    public class GPULights : ScriptableRenderPass
    {
        // Public Variables
        internal ShaderVariablesLightList lightCBuffer;

        private int m_MaxDirectionalLightsOnScreen = 16;
        private int m_MaxPunctualLightsOnScreen = 512;
        // TODO: change to m_MaxDirectionalLightsOnScreen + m_MaxPunctualLightsOnScreen(512) + m_MaxAreaLightsOnScreen + m_MaxEnvLightsOnScreen
        private int m_MaxLightOnScreen = 16 + 512;

        // Private Variables
        private ComputeShader m_gpulightsCS_ClearLists;
        private ComputeShader m_gpuLightsCS_CoarseCulling;
        private ComputeShader m_gpuLightsCS_FPTL;
        private ComputeShader m_gpuLightsCS_Cluster;
        private int m_ClearKernel;
        private int m_ScreenSpaceAABBKernel;
        private int m_CoarseCullingLightsKernel;
        private int m_ClusterCullingLightsKernel;

        private ComputeBuffer m_LightBoundsBuffer;
        private ComputeBuffer m_LightVolumeDataBuffer;
        private ComputeBuffer m_AABBBoundsBuffer;
        private ComputeBuffer m_CoarseLightList;

        // ClusterBuffer
        private ComputeBuffer m_PerVoxelOffset;
        private ComputeBuffer m_PerVoxelLightLists;
        private ComputeBuffer m_PerTileLogBaseTweak;
        private ComputeBuffer m_GlobalLightListAtomic;

        // LightsDataBuffer
        private ComputeBuffer m_GPULightsData;
        private ComputeBuffer m_DirectionalLightsData;
        //private ComputeBuffer m_EnvLightsData;

        private GPULightsDataBuildSystem m_GPULightsDataBuildSystem;

        private int m_nrBigTilesX;
        private int m_nrBigTilesY;

        private int m_nrClustersX;
        private int m_nrClustersY;

        // Constants
        private const int k_Log2NumClusters = 6; // accepted range is from 0 to 6 (NR_THREADS is set to 64). NumClusters is 1<<g_iLog2NumClusters
        private const float k_ClustLogBase = 1.02f;     // each slice 2% bigger than the previous

        // Statics
        // Left-handed to right-handed
        static readonly Matrix4x4 s_FlipMatrixLHSRHS = Matrix4x4.Scale(new Vector3(1, 1, -1));

        public GPULights(UniversalRenderPipelineRuntimeResources.ShaderResources shaderResources, RenderPassEvent passEvent)
        {
            m_gpulightsCS_ClearLists        = shaderResources.gpuLightsClearLists;
            m_gpuLightsCS_CoarseCulling     = shaderResources.gpuLightsCoarseCullingCS;
            m_gpuLightsCS_FPTL              = shaderResources.gpuLightsFPTL;
            m_gpuLightsCS_Cluster           = shaderResources.gpuLightsCluster;

            m_ClearKernel                   = m_gpulightsCS_ClearLists.FindKernel("ClearList");
            m_ScreenSpaceAABBKernel         = m_gpuLightsCS_CoarseCulling.FindKernel("ScreenSpaceAABB");
            m_CoarseCullingLightsKernel     = m_gpuLightsCS_CoarseCulling.FindKernel("CoarseCullingLights");
            m_ClusterCullingLightsKernel = m_gpuLightsCS_Cluster.FindKernel("ClusterCullingLights");

            renderPassEvent = passEvent;
            lightCBuffer = new ShaderVariablesLightList();
        }

        /// <summary>
        /// Compute and upload light CBUFFER
        /// </summary>
        /// <param name="context"></param>
        /// <param name="renderingData"></param>
        /// <returns></returns>
        internal bool Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            return true;
        }

        internal void PreSetup(ref RenderingData renderingData, GPULightsDataBuildSystem gpuLightsDataBuildSystem)
        {
            m_GPULightsDataBuildSystem = gpuLightsDataBuildSystem;

            var lightData = renderingData.lightData;

            var cameraData = renderingData.cameraData;
            int width = cameraData.camera.pixelWidth;
            int height = cameraData.camera.pixelHeight;

            var temp = new Matrix4x4();
            temp.SetRow(0, new Vector4(0.5f * width, 0.0f, 0.0f, 0.5f * width));
            temp.SetRow(1, new Vector4(0.0f, 0.5f * height, 0.0f, 0.5f * height));
            temp.SetRow(2, new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
            temp.SetRow(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            var temp2 = new Matrix4x4();
            temp2.SetRow(0, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            temp2.SetRow(1, new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
            temp2.SetRow(2, new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
            temp2.SetRow(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            // camera to screen matrix (and it's inverse)
            {
                //Matrix4x4 gpuProjectionMatrix = cameraData.GetGPUProjectionMatrix(cameraData.IsCameraProjectionMatrixFlipped());
                Matrix4x4 projMatrix = cameraData.GetProjectionMatrix();

                projMatrix *= s_FlipMatrixLHSRHS;
                
                lightCBuffer.g_mScrProjectionArr = temp * projMatrix;
                lightCBuffer.g_mInvScrProjectionArr = lightCBuffer.g_mScrProjectionArr.inverse;
                
                lightCBuffer.g_mProjectionArr = temp2 * projMatrix;
                lightCBuffer.g_mInvProjectionArr = lightCBuffer.g_mProjectionArr.inverse;

            }

            var scaledCameraWidth = (float)cameraData.cameraTargetDescriptor.width;
            var scaledCameraHeight = (float)cameraData.cameraTargetDescriptor.height;

            if (cameraData.camera.allowDynamicResolution)
            {
                scaledCameraWidth *= ScalableBufferManager.widthScaleFactor;
                scaledCameraHeight *= ScalableBufferManager.heightScaleFactor;
            }

            lightCBuffer.g_iNrVisibLights = lightData.additionalLightsCount;
            lightCBuffer._DirectionalLightCount = (uint)lightData.directionalLightsCount;

            /// <see cref="ScriptableRenderer"/> cmd.SetGlobalVector(ShaderPropertyId.screenSize...
            lightCBuffer.g_screenSize = new Vector4(scaledCameraWidth, scaledCameraHeight, 1.0f / scaledCameraWidth, 1.0f / scaledCameraHeight);
            lightCBuffer.g_viDimensions = new Vector2Int((int)scaledCameraWidth, (int)scaledCameraHeight);
            lightCBuffer.g_isOrthographic = cameraData.camera.orthographic ? 1u : 0u;
            //lightCBuffer.g_BaseFeatureFlags = 0; // Filled for each individual pass.
            //lightCBuffer.g_iNumSamplesMSAA = msaaSamples;
            lightCBuffer._EnvLightIndexShift = (uint)lightData.additionalLightsCount;
            lightCBuffer._DecalIndexShift = 0;// (uint)(additionalLightsCount + envLights);

            const float C = (float)(1 << k_Log2NumClusters);
            var geomSeries = (1.0 - Mathf.Pow(k_ClustLogBase, C)) / (1 - k_ClustLogBase); // geometric series: sum_k=0^{C-1} base^k

            // Tile/Cluster
            lightCBuffer._NumTileFtplX = (uint)RenderingUtils.DivRoundUp(width, LightDefinitions.s_TileSizeFptl);
            lightCBuffer._NumTileFtplY = (uint)RenderingUtils.DivRoundUp(height, LightDefinitions.s_TileSizeFptl);
            lightCBuffer.g_fClustScale = (float)(geomSeries / (cameraData.camera.farClipPlane - cameraData.camera.nearClipPlane)); ;
            lightCBuffer.g_fClustBase = k_ClustLogBase;
            lightCBuffer.g_fNearPlane = cameraData.camera.nearClipPlane;
            lightCBuffer.g_fFarPlane = cameraData.camera.farClipPlane;
            lightCBuffer.g_iLog2NumClusters = k_Log2NumClusters;
            lightCBuffer.g_isLogBaseBufferEnabled = 1;// Need depth
            lightCBuffer._NumTileClusteredX = (uint)RenderingUtils.DivRoundUp(width, LightDefinitions.s_TileSizeClustered);
            lightCBuffer._NumTileClusteredY = (uint)RenderingUtils.DivRoundUp(height, LightDefinitions.s_TileSizeClustered);
        }

        /// <inheritdoc />
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var width = renderingData.cameraData.cameraTargetDescriptor.width;
            var height = renderingData.cameraData.cameraTargetDescriptor.height;
            m_nrBigTilesX = RenderingUtils.DivRoundUp(width, 64);
            m_nrBigTilesY = RenderingUtils.DivRoundUp(height, 64);

            var bufferSystem = ComputeBufferSystem.instance;
            m_LightBoundsBuffer = bufferSystem.GetComputeBuffer<SFiniteLightBound>(ComputeBufferSystemBufferID.GPULightsConvexBoundsBuffer, m_MaxLightOnScreen);
            m_LightVolumeDataBuffer = bufferSystem.GetComputeBuffer<LightVolumeData>(ComputeBufferSystemBufferID.GPULightsLightVolumeDataBuffer, m_MaxLightOnScreen);

            m_AABBBoundsBuffer = bufferSystem.GetComputeBuffer<float4>(ComputeBufferSystemBufferID.GPULightsAABBBounds, m_MaxLightOnScreen);

            m_CoarseLightList = bufferSystem.GetComputeBuffer<uint>(ComputeBufferSystemBufferID.GPULightsCoarseLightList, LightDefinitions.s_MaxNrBigTileLightsPlusOne * m_nrBigTilesX * m_nrBigTilesY);

            // Cluster buffers
            m_nrClustersX = (width + LightDefinitions.s_TileSizeClustered - 1) / LightDefinitions.s_TileSizeClustered;
            m_nrClustersY = (height + LightDefinitions.s_TileSizeClustered - 1) / LightDefinitions.s_TileSizeClustered;
            var nrClusterTiles = m_nrClustersX * m_nrClustersY;

            m_GlobalLightListAtomic = bufferSystem.GetComputeBuffer<uint>(ComputeBufferSystemBufferID.GPULightsGlobalLightLIstAtomic, 1);
            m_PerVoxelLightLists = bufferSystem.GetComputeBuffer<uint>(ComputeBufferSystemBufferID.GPULightsPerVoxelLightLists, 32 * (1 << k_Log2NumClusters) * nrClusterTiles);
            m_PerVoxelOffset = bufferSystem.GetComputeBuffer<uint>(ComputeBufferSystemBufferID.GPULightsPerVoxelOffset, (int)LightCategory.Count * (1 << k_Log2NumClusters) * nrClusterTiles);
            m_PerTileLogBaseTweak = bufferSystem.GetComputeBuffer<float>(ComputeBufferSystemBufferID.GPULightsPerTileLogBaseTweak, nrClusterTiles);

            // LightsData buffers
            m_GPULightsData = bufferSystem.GetComputeBuffer<GPULightData>(ComputeBufferSystemBufferID.GPULightsData, m_MaxPunctualLightsOnScreen);
            m_DirectionalLightsData = bufferSystem.GetComputeBuffer<DirectionalLightData>(ComputeBufferSystemBufferID.DirectionalLightsData, m_MaxDirectionalLightsOnScreen);
        }

        /// <summary>
        /// Clear one compute buffer.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="bufferToClear"></param>
        private void ClearLightList(CommandBuffer cmd, ComputeBuffer bufferToClear)
        {
            Vector2 countAndOffset = new Vector2Int(bufferToClear.count, 0);
            int totalNumberOfGroupsNeeded = RenderingUtils.DivRoundUp(bufferToClear.count, 64);
            const int maxAllowedGroups = 65535;// On higher resolutions we might end up with more than 65535 group which is not allowed, so we need to to have multiple dispatches.

            cmd.SetComputeBufferParam(m_gpulightsCS_ClearLists, m_ClearKernel, "_LightListToClear", bufferToClear);

            int i = 0;
            while (totalNumberOfGroupsNeeded > 0)
            {
                countAndOffset.y = maxAllowedGroups * i;
                cmd.SetComputeVectorParam(m_gpulightsCS_ClearLists, "_LightListEntriesAndOffset", countAndOffset);

                int currGroupCount = Math.Min(maxAllowedGroups, totalNumberOfGroupsNeeded);

                cmd.DispatchCompute(m_gpulightsCS_ClearLists, m_ClearKernel, currGroupCount, 1, 1);

                totalNumberOfGroupsNeeded -= currGroupCount;
                i++;
            }
        }

        /// <summary>
        /// Clear all light lists compute buffer.
        /// </summary>
        /// <param name="cmd"></param>
        internal void ClearAllLightLists(CommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.GPULights)))
            {
                if (m_CoarseLightList != null)
                {
                    ClearLightList(cmd, m_CoarseLightList);
                    ClearLightList(cmd, m_PerVoxelOffset);
                }
            }
        }

        internal void ResolveGPULightsData(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_GPULightsDataBuildSystem.ReBuildGPULightsDataBuffer(renderingData);
            m_GPULightsData.SetData(m_GPULightsDataBuildSystem.gpuLightsData, 0, 0, m_GPULightsDataBuildSystem.lightsCount);
            m_DirectionalLightsData.SetData(m_GPULightsDataBuildSystem.directionalLightsData, 0, 0, m_GPULightsDataBuildSystem.directionalLightCount);

            ConstantBuffer.PushGlobal(cmd, lightCBuffer, ShaderConstants.ShaderVariablesLightList);

            cmd.SetGlobalBuffer(ShaderConstants.g_CoarseLightList, m_CoarseLightList);
            cmd.SetGlobalBuffer(ShaderConstants.g_GPULightDatas, m_GPULightsData);
            cmd.SetGlobalBuffer(ShaderConstants.g_DirectionalLightDatas, m_DirectionalLightsData);

            cmd.SetGlobalBuffer(ShaderConstants.g_vLightListCluster, m_PerVoxelLightLists);
            cmd.SetGlobalBuffer(ShaderConstants.g_vLayeredOffsetsBuffer, m_PerVoxelOffset);
            cmd.SetGlobalBuffer(ShaderConstants.g_logBaseBuffer, m_PerTileLogBaseTweak);

            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.GPULightsCluster, true);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // TODO: We should add envLights(probe) and decals as HDRP.
            int totalLightCount = renderingData.lightData.additionalLightsCount;
            if (totalLightCount == 0)
            {
                ClearAllLightLists(renderingData.commandBuffer);
                ResolveGPULightsData(renderingData.commandBuffer, ref renderingData);
                return;
            }


            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.GPULights)))
            {
                m_LightBoundsBuffer.SetData(m_GPULightsDataBuildSystem.lightBounds, 0, 0, m_GPULightsDataBuildSystem.boundsCount);
                m_LightVolumeDataBuffer.SetData(m_GPULightsDataBuildSystem.lightVolumes, 0, 0, m_GPULightsDataBuildSystem.boundsCount);

                // GenerateLightsScreenSpaceAABBs
                {
                    cmd.SetComputeBufferParam(m_gpuLightsCS_CoarseCulling, m_ScreenSpaceAABBKernel, ShaderConstants.g_LightBounds, m_LightBoundsBuffer);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_CoarseCulling, m_ScreenSpaceAABBKernel, ShaderConstants.g_vBoundsBuffer, m_AABBBoundsBuffer);

                    ConstantBuffer.Push(cmd, lightCBuffer, m_gpuLightsCS_CoarseCulling, ShaderConstants.ShaderVariablesLightList);

                    const int threadsPerLight = 4;  // Shader: THREADS_PER_LIGHT (4)
                    const int threadsPerGroup = 64; // Shader: THREADS_PER_GROUP (64)

                    int groupCount = RenderingUtils.DivRoundUp(totalLightCount * threadsPerLight, threadsPerGroup);

                    cmd.DispatchCompute(m_gpuLightsCS_CoarseCulling, m_ScreenSpaceAABBKernel, groupCount, 1, 1);
                }

                // CoarseCullingLights
                {
                    cmd.SetComputeBufferParam(m_gpuLightsCS_CoarseCulling, m_CoarseCullingLightsKernel, ShaderConstants.g_vBoundsBuffer, m_AABBBoundsBuffer);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_CoarseCulling, m_CoarseCullingLightsKernel, ShaderConstants.g_LightVolumeData, m_LightVolumeDataBuffer);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_CoarseCulling, m_CoarseCullingLightsKernel, ShaderConstants.g_LightBounds, m_LightBoundsBuffer);

                    cmd.SetComputeBufferParam(m_gpuLightsCS_CoarseCulling, m_CoarseCullingLightsKernel, ShaderConstants.g_vLightList, m_CoarseLightList);

                    ConstantBuffer.Push(cmd, lightCBuffer, m_gpuLightsCS_CoarseCulling, ShaderConstants.ShaderVariablesLightList);

                    cmd.DispatchCompute(m_gpuLightsCS_CoarseCulling, m_CoarseCullingLightsKernel, m_nrBigTilesX, m_nrBigTilesY, 1);
                }

                // FPTLLights
                {
                    // no implementation

                }

                // ClusterLights
                {
                    cmd.SetComputeBufferParam(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, ShaderConstants.g_vBoundsBuffer, m_AABBBoundsBuffer);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, ShaderConstants.g_LightVolumeData, m_LightVolumeDataBuffer);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, ShaderConstants.g_LightBounds, m_LightBoundsBuffer);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, ShaderConstants.g_CoarseLightList, m_CoarseLightList);


                    cmd.SetComputeBufferParam(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, ShaderConstants.g_vLayeredLightList, m_PerVoxelLightLists);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, ShaderConstants.g_LayeredOffset, m_PerVoxelOffset);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, ShaderConstants.g_LayeredSingleIdxBuffer, m_GlobalLightListAtomic);
                    cmd.SetComputeBufferParam(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, ShaderConstants.g_logBaseBuffer, m_PerTileLogBaseTweak);

                    ConstantBuffer.Push(cmd, lightCBuffer, m_gpuLightsCS_Cluster, ShaderConstants.ShaderVariablesLightList);

                    cmd.DispatchCompute(m_gpuLightsCS_Cluster, m_ClusterCullingLightsKernel, m_nrClustersX, m_nrClustersY, 1);
                }

                // Resolve
                {
                    ResolveGPULightsData(cmd, ref renderingData);
                }

            }
        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            // Clean Keyword if need
            //CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.GPULightsCluster, false);
        }

        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {
            //m_RenderTarget?.Release();
        }

        static class ShaderConstants
        {
            public static readonly int g_LightBounds            = Shader.PropertyToID("g_LightBounds");
            public static readonly int g_vBoundsBuffer          = Shader.PropertyToID("g_vBoundsBuffer");
            public static readonly int g_LightVolumeData        = Shader.PropertyToID("g_LightVolumeData");
            public static readonly int g_vLightList             = Shader.PropertyToID("g_vLightList");
            public static readonly int ShaderVariablesLightList = Shader.PropertyToID("ShaderVariablesLightList");
            public static readonly int g_CoarseLightList        = Shader.PropertyToID("g_CoarseLightList");
            public static readonly int g_vLayeredLightList      = Shader.PropertyToID("g_vLayeredLightList");
            public static readonly int g_LayeredOffset          = Shader.PropertyToID("g_LayeredOffset");
            public static readonly int g_LayeredSingleIdxBuffer = Shader.PropertyToID("g_LayeredSingleIdxBuffer");
            public static readonly int g_vLightListCluster      = Shader.PropertyToID("g_vLightListCluster");
            public static readonly int g_vLayeredOffsetsBuffer  = Shader.PropertyToID("g_vLayeredOffsetsBuffer");
            public static readonly int g_logBaseBuffer          = Shader.PropertyToID("g_logBaseBuffer");
            public static readonly int g_GPULightDatas          = Shader.PropertyToID("g_GPULightDatas");
            public static readonly int g_DirectionalLightDatas  = Shader.PropertyToID("g_DirectionalLightDatas");
        }
    }

}