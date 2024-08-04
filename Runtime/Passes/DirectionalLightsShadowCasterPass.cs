using System;
using UnityEngine.Rendering.RenderGraphModule;


namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Renders a shadow map for the directional Light.
    /// </summary>
    public class DirectionalLightsShadowCasterPass : ScriptableRenderPass
    {
        private static class DirectionalLightsShadowConstantBuffer
        {
            public static int _WorldToShadow;
            public static int _ShadowParams;
            public static int _CascadeShadowSplitSpheres0;
            public static int _CascadeShadowSplitSpheres1;
            public static int _CascadeShadowSplitSpheres2;
            public static int _CascadeShadowSplitSpheres3;
            public static int _CascadeShadowSplitSphereRadii;
            public static int _ShadowOffset0;
            public static int _ShadowOffset1;
            public static int _ShadowUVMinMax;
            public static int _PerCascadePCSSData;
            public static int _ShadowmapSize;
        }

        const int k_MaxCascades = 4;
        const int k_ShadowmapBufferBits = 16;
        float m_CascadeBorder;
        float m_MaxShadowDistanceSq;
        int m_ShadowCasterCascadesCount;

        int m_MainLightShadowmapID;
        private RTHandle m_EmptyLightShadowmapTexture;
        private const int k_EmptyShadowMapDimensions = 1;
        private const string k_EmptyShadowMapName = "_EmptyLightShadowmapTexture";
        private static readonly Vector4 s_EmptyShadowParams = new Vector4(1, 0, 1, 0);
        private static readonly Vector4 s_EmptyShadowmapSize = s_EmptyShadowmapSize = new Vector4(k_EmptyShadowMapDimensions, 1f / k_EmptyShadowMapDimensions, k_EmptyShadowMapDimensions, k_EmptyShadowMapDimensions);

        Matrix4x4[] m_MainLightShadowMatrices;
        ShadowSliceData[] m_CascadeSlices;
        Vector4[] m_CascadeSplitDistances;
        Vector4[] m_PerCascadePCSSData;

        bool m_CreateEmptyShadowmap;
        bool m_EmptyShadowmapNeedsClear = false;

        int renderTargetWidth;
        int renderTargetHeight;

        ProfilingSampler m_ProfilingSetupSampler = new ProfilingSampler("Setup Main Shadowmap");
        private PassData m_PassData;
        /// <summary>
        /// Creates a new <c>MainLightShadowCasterPass</c> instance.
        /// </summary>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <seealso cref="RenderPassEvent"/>
        public DirectionalLightsShadowCasterPass(RenderPassEvent evt)
        {
            base.profilingSampler = new ProfilingSampler(nameof(MainLightShadowCasterPass));
            renderPassEvent = evt;

            m_PassData = new PassData();
            m_MainLightShadowMatrices = new Matrix4x4[k_MaxCascades + 1];
            m_CascadeSlices = new ShadowSliceData[k_MaxCascades];
            m_CascadeSplitDistances = new Vector4[k_MaxCascades];
            m_PerCascadePCSSData = new Vector4[k_MaxCascades];

            DirectionalLightsShadowConstantBuffer._WorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
            DirectionalLightsShadowConstantBuffer._ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
            DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSpheres0 = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
            DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSpheres1 = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
            DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSpheres2 = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
            DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSpheres3 = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
            DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSphereRadii = Shader.PropertyToID("_CascadeShadowSplitSphereRadii");
            DirectionalLightsShadowConstantBuffer._ShadowOffset0 = Shader.PropertyToID("_MainLightShadowOffset0");
            DirectionalLightsShadowConstantBuffer._ShadowOffset1 = Shader.PropertyToID("_MainLightShadowOffset1");
            DirectionalLightsShadowConstantBuffer._ShadowUVMinMax = Shader.PropertyToID("_DirLightShadowUVMinMax");
            DirectionalLightsShadowConstantBuffer._PerCascadePCSSData = Shader.PropertyToID("_PerCascadePCSSData");
            DirectionalLightsShadowConstantBuffer._ShadowmapSize = Shader.PropertyToID("_MainLightShadowmapSize");

            m_MainLightShadowmapID = Shader.PropertyToID("_MainLightShadowmapTexture");

            m_EmptyLightShadowmapTexture = ShadowUtils.AllocShadowRT(k_EmptyShadowMapDimensions, k_EmptyShadowMapDimensions, k_ShadowmapBufferBits, 1, 0, name: k_EmptyShadowMapName);
            m_EmptyShadowmapNeedsClear = true;
        }

        /// <summary>
        /// Cleans up resources used by the pass.
        /// </summary>
        public void Dispose()
        {
            m_EmptyLightShadowmapTexture?.Release();
        }

        /// <summary>
        /// Sets up the pass.
        /// </summary>
        /// <param name="renderingData">Data containing rendering settings.</param>
        /// <param name="cameraData">Data containing camera settings.</param>
        /// <param name="lightData">Data containing light settings.</param>
        /// <param name="shadowData">Data containing shadow settings.</param>
        /// <returns>True if the pass should be enqueued, otherwise false.</returns>
        /// <seealso cref="RenderingData"/>
        public bool Setup(UniversalRenderingData renderingData, UniversalCameraData cameraData, UniversalLightData lightData, UniversalShadowData shadowData)
        {
            if (!shadowData.mainLightShadowsEnabled)
                return false;

            using var profScope = new ProfilingScope(m_ProfilingSetupSampler);

            if (!shadowData.supportsMainLightShadows)
                return SetupForEmptyRendering(cameraData.renderer.stripShadowsOffVariants);

            Clear();
            int shadowLightIndex = lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return SetupForEmptyRendering(cameraData.renderer.stripShadowsOffVariants);

            VisibleLight shadowLight = lightData.visibleLights[shadowLightIndex];
            Light light = shadowLight.light;
            if (light.shadows == LightShadows.None)
                return SetupForEmptyRendering(cameraData.renderer.stripShadowsOffVariants);

            if (shadowLight.lightType != LightType.Directional)
            {
                Debug.LogWarning("Only directional lights are supported as main light.");
            }

            Bounds bounds;
            if (!renderingData.cullResults.GetShadowCasterBounds(shadowLightIndex, out bounds))
                return SetupForEmptyRendering(cameraData.renderer.stripShadowsOffVariants);

            m_ShadowCasterCascadesCount = shadowData.mainLightShadowCascadesCount;
            renderTargetWidth = shadowData.mainLightRenderTargetWidth;
            renderTargetHeight = shadowData.mainLightRenderTargetHeight;

            ref readonly URPLightShadowCullingInfos shadowCullingInfos = ref shadowData.visibleLightsShadowCullingInfos.UnsafeElementAt(shadowLightIndex);

            for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
            {
                ref readonly ShadowSliceData sliceData = ref shadowCullingInfos.slices.UnsafeElementAt(cascadeIndex);
                m_CascadeSplitDistances[cascadeIndex] = sliceData.splitData.cullingSphere;
                m_CascadeSlices[cascadeIndex] = sliceData;

                if (!shadowCullingInfos.IsSliceValid(cascadeIndex))
                    return SetupForEmptyRendering(cameraData.renderer.stripShadowsOffVariants);
            }

            m_MaxShadowDistanceSq = cameraData.maxShadowDistance * cameraData.maxShadowDistance;
            m_CascadeBorder = shadowData.mainLightShadowCascadeBorder;
            m_CreateEmptyShadowmap = false;
            useNativeRenderPass = true;

            return true;
        }

        bool SetupForEmptyRendering(bool stripShadowsOffVariants)
        {
            if (!stripShadowsOffVariants)
                return false;

            m_CreateEmptyShadowmap = true;
            useNativeRenderPass = false;

            // Required for scene view camera(URP renderer not initialized)
            if (ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_EmptyLightShadowmapTexture, k_EmptyShadowMapDimensions, k_EmptyShadowMapDimensions, k_ShadowmapBufferBits, name: k_EmptyShadowMapName))
                m_EmptyShadowmapNeedsClear = true;

            return true;
        }


        void Clear()
        {
            for (int i = 0; i < m_MainLightShadowMatrices.Length; ++i)
                m_MainLightShadowMatrices[i] = Matrix4x4.identity;

            for (int i = 0; i < m_CascadeSplitDistances.Length; ++i)
                m_CascadeSplitDistances[i] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);

            for (int i = 0; i < m_CascadeSlices.Length; ++i)
                m_CascadeSlices[i].Clear();

            for (int i = 0; i < m_CascadeSplitDistances.Length; i++)
                m_PerCascadePCSSData[i] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        }

        void SetEmptyMainLightCascadeShadowmap(RasterCommandBuffer cmd)
        {
            cmd.EnableKeyword(ShaderGlobalKeywords.MainLightShadows);
            SetEmptyMainLightShadowParams(cmd);
        }

        internal static void SetEmptyMainLightShadowParams(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._ShadowParams, s_EmptyShadowParams);
            cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._ShadowmapSize, s_EmptyShadowmapSize);
        }

        void RenderMainLightCascadeShadowmap(UnsafeCommandBuffer uncmd, ref PassData data)
        {
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(uncmd);
            var rasCmd = CommandBufferHelpers.GetRasterCommandBuffer(nativeCmd);
            
            var lightData = data.lightData;

            int shadowLightIndex = lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return;

            VisibleLight visMainLight = lightData.visibleLights[shadowLightIndex];

            using (new ProfilingScope(rasCmd, ProfilingSampler.Get(URPProfileId.DirectionalLightsShadow)))
            {
                // Need to start by setting the Camera position and worldToCamera Matrix as that is not set for passes executed before normal rendering
                ShadowUtils.SetCameraPosition(rasCmd, data.cameraData.worldSpaceCameraPos);

                for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
                {
                    Vector4 shadowBias = ShadowUtils.GetShadowBias(ref visMainLight, shadowLightIndex, data.shadowData, m_CascadeSlices[cascadeIndex].projectionMatrix, m_CascadeSlices[cascadeIndex].resolution);
                    ShadowUtils.SetupShadowCasterConstantBuffer(rasCmd, ref visMainLight, shadowBias);
                    rasCmd.SetKeyword(ShaderGlobalKeywords.CastingPunctualLightShadow, false);
                    RendererList shadowRendererList = data.shadowRendererListsHandle[cascadeIndex];

                    nativeCmd.SetRenderTarget(data.shadowmapTexture, data.shadowmapTexture, 0, CubemapFace.Unknown, cascadeIndex);
                    ShadowUtils.RenderShadowSliceNoOffset(rasCmd, ref m_CascadeSlices[cascadeIndex], ref shadowRendererList, m_CascadeSlices[cascadeIndex].projectionMatrix, m_CascadeSlices[cascadeIndex].viewMatrix);
                }

                data.shadowData.isKeywordSoftShadowsEnabled = visMainLight.light.shadows == LightShadows.Soft && data.shadowData.supportsSoftShadows;
                rasCmd.SetKeyword(ShaderGlobalKeywords.MainLightShadows, data.shadowData.mainLightShadowCascadesCount == 1);
                rasCmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowCascades, data.shadowData.mainLightShadowCascadesCount > 1);
                ShadowUtils.SetSoftShadowQualityShaderKeywords(rasCmd, data.shadowData);

                SetupDirLightsShadowReceiverConstants(rasCmd, ref visMainLight, data.shadowData);
            }
        }

        void DebugMatrix(Matrix4x4 view, Matrix4x4 proj)
        {
            // 投影矩阵的逆矩阵
            Matrix4x4 inverseProjectionMatrix = proj.inverse;
            // 视图矩阵的逆矩阵
            Matrix4x4 inverseViewMatrix = view.inverse;

            // NDC空间的四个角点 (左下角, 右下角, 左上角, 右上角)
            Vector4[] ndcCorners = new Vector4[]
            {
            new Vector4(-1, -1, -1, 1),
            new Vector4( 1, -1, -1, 1),
            new Vector4(-1,  1, -1, 1),
            new Vector4( 1,  1, -1, 1),
            new Vector4( 1,  1, +1, 1),
            };

            Vector3[] worldCorners = new Vector3[5];

            for (int i = 0; i < 5; i++)
            {
                // 从NDC空间到相机空间
                Vector4 cameraSpaceCorner = inverseProjectionMatrix * ndcCorners[i];
                // 进行透视除法
                cameraSpaceCorner /= cameraSpaceCorner.w;
                // 从相机空间到世界空间
                Vector4 worldSpaceCorner = inverseViewMatrix * cameraSpaceCorner;
                worldCorners[i] = worldSpaceCorner;
            }


            // 计算视口在世界空间中的宽度和高度
            float width = Vector3.Distance(worldCorners[0], worldCorners[1]);
            float height = Vector3.Distance(worldCorners[0], worldCorners[2]);

            Debug.Log("Width: " + width);
            Debug.Log("Height: " + height);



            // 获取视口深度
            //float nearClip = proj.m32 / (proj.m22 - 1);
            //float farClip = proj.m32 / (proj.m22 + 1);
            //float depth = farClip - nearClip;
            float depth = Vector3.Distance(worldCorners[3], worldCorners[4]);

            Debug.Log("Depth: "  + depth  + ", "+ worldCorners[4] + ", " + worldCorners[3]);
        }

        void SetupDirLightsShadowReceiverConstants(RasterCommandBuffer cmd, ref VisibleLight shadowLight, UniversalShadowData shadowData)
        {
            Light light = shadowLight.light;
            bool softShadows = shadowLight.light.shadows == LightShadows.Soft && shadowData.supportsSoftShadows;

            int cascadeCount = m_ShadowCasterCascadesCount;
            for (int i = 0; i < cascadeCount; ++i)
                m_MainLightShadowMatrices[i] = m_CascadeSlices[i].shadowTransform;

            // We setup and additional a no-op WorldToShadow matrix in the last index
            // because the ComputeCascadeIndex function in Shadows.hlsl can return an index
            // out of bounds. (position not inside any cascade) and we want to avoid branching
            Matrix4x4 noOpShadowMatrix = Matrix4x4.zero;
            noOpShadowMatrix.m22 = (SystemInfo.usesReversedZBuffer) ? 1.0f : 0.0f;
            for (int i = cascadeCount; i <= k_MaxCascades; ++i)
                m_MainLightShadowMatrices[i] = noOpShadowMatrix;

            //string debugstr = "";
            //for (int i = 0; i < 4; i++)
            //{
            //    debugstr += m_CascadeSlices[0].projectionMatrix.GetRow(i) + "\n";
            //}
            //Debug.Log(debugstr);
            //Debug.Log("Size: " + 1.0f / m_CascadeSlices[0].projectionMatrix.m11 * 2.0f + ", Far-Near: " + (2.0f / m_CascadeSlices[0].projectionMatrix.m22));
            //Debug.Log("Sphere: " + m_CascadeSplitDistances[0]);
            //DebugMatrix(m_CascadeSlices[0].viewMatrix, m_CascadeSlices[0].projectionMatrix);
            //Debug.Log("Proj Matrix Far-+Near: " + 1.0f / m_CascadeSlices[0].projectionMatrix.m23);

            float blockerAngularDiameter = 12.0f;
            if (light.TryGetComponent(out UniversalAdditionalLightData additionalLightData))
            {
                blockerAngularDiameter = Mathf.Max(blockerAngularDiameter, additionalLightData.angularDiameter);
            }
            //float halfBlockerSearchAngularDiameterTangent = Mathf.Tan(0.5f * Mathf.Deg2Rad * blockerAngularDiameter);
            Vector4 dir = -light.transform.localToWorldMatrix.GetColumn(2);
            float halfBlockerSearchAngularDiameterTangent = dir.y / MathF.Sqrt(1 - dir.y * dir.y + 0.0001f);

            for (int i = 0; i < cascadeCount; ++i)
            {
                // Far-Near/viewPortSizeWS
                // viewPortSizeWS is 1.0f / m_CascadeSlices[0].projectionMatrix.m11 * 2.0f;
                // Far-Near is 2.0f / m_CascadeSlices[0].projectionMatrix.m22;
                float farToNear = MathF.Abs(2.0f / m_CascadeSlices[i].projectionMatrix.m22);
                float viewPortSizeWS = 1.0f / m_CascadeSlices[i].projectionMatrix.m11 * 2.0f;
                float radial2ShadowmapDepth = Mathf.Abs(m_CascadeSlices[i].projectionMatrix.m00 / m_CascadeSlices[i].projectionMatrix.m22);
                float texelSizeWS = viewPortSizeWS / renderTargetWidth;
                

                m_PerCascadePCSSData[i] = new Vector4(1.0f / (radial2ShadowmapDepth), texelSizeWS, farToNear, 1.0f / halfBlockerSearchAngularDiameterTangent);
            }

            cmd.SetGlobalVectorArray(DirectionalLightsShadowConstantBuffer._PerCascadePCSSData, m_PerCascadePCSSData);

            float invShadowAtlasWidth = 1.0f / renderTargetWidth;
            float invShadowAtlasHeight = 1.0f / renderTargetHeight;
            float invHalfShadowAtlasWidth = 0.5f * invShadowAtlasWidth;
            float invHalfShadowAtlasHeight = 0.5f * invShadowAtlasHeight;
            float softShadowsProp = ShadowUtils.SoftShadowQualityToShaderProperty(light, softShadows);

            ShadowUtils.GetScaleAndBiasForLinearDistanceFade(m_MaxShadowDistanceSq, m_CascadeBorder, out float shadowFadeScale, out float shadowFadeBias);

            cmd.SetGlobalMatrixArray(DirectionalLightsShadowConstantBuffer._WorldToShadow, m_MainLightShadowMatrices);
            cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._ShadowParams,
                new Vector4(light.shadowStrength, softShadowsProp, shadowFadeScale, shadowFadeBias));

            if (m_ShadowCasterCascadesCount > 1)
            {
                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSpheres0,
                    m_CascadeSplitDistances[0]);
                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSpheres1,
                    m_CascadeSplitDistances[1]);
                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSpheres2,
                    m_CascadeSplitDistances[2]);
                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSpheres3,
                    m_CascadeSplitDistances[3]);
                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._CascadeShadowSplitSphereRadii, new Vector4(
                    m_CascadeSplitDistances[0].w * m_CascadeSplitDistances[0].w,
                    m_CascadeSplitDistances[1].w * m_CascadeSplitDistances[1].w,
                    m_CascadeSplitDistances[2].w * m_CascadeSplitDistances[2].w,
                    m_CascadeSplitDistances[3].w * m_CascadeSplitDistances[3].w));
            }

            // Inside shader soft shadows are controlled through global keyword.
            // If any additional light has soft shadows it will force soft shadows on main light too.
            // As it is not trivial finding out which additional light has soft shadows, we will pass main light properties if soft shadows are supported.
            // This workaround will be removed once we will support soft shadows per light.
            
            // DanbaidongRP always soft shadows properties.

            //if (shadowData.supportsSoftShadows)
            {
                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._ShadowOffset0,
                    new Vector4(-invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight,
                        invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight));
                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._ShadowOffset1,
                    new Vector4(-invHalfShadowAtlasWidth, invHalfShadowAtlasHeight,
                        invHalfShadowAtlasWidth, invHalfShadowAtlasHeight));

                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._ShadowUVMinMax,
                    new Vector4(invHalfShadowAtlasWidth, invHalfShadowAtlasHeight,
                        1.0f - invHalfShadowAtlasWidth, 1.0f - invHalfShadowAtlasHeight));

                cmd.SetGlobalVector(DirectionalLightsShadowConstantBuffer._ShadowmapSize, new Vector4(invShadowAtlasWidth,
                    invShadowAtlasHeight,
                    renderTargetWidth, renderTargetHeight));
            }
        }

        private class PassData
        {
            internal UniversalRenderingData renderingData;
            internal UniversalCameraData cameraData;
            internal UniversalLightData lightData;
            internal UniversalShadowData shadowData;

            internal DirectionalLightsShadowCasterPass pass;

            internal TextureHandle shadowmapTexture;
            internal int shadowmapID;
            internal bool emptyShadowmap;

            internal RendererListHandle[] shadowRendererListsHandle = new RendererListHandle[k_MaxCascades];
            internal RendererList[] shadowRendererLists = new RendererList[k_MaxCascades];
        }

        private void InitPassData(
            ref PassData passData,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalLightData lightData,
            UniversalShadowData shadowData)
        {
            passData.pass = this;

            passData.emptyShadowmap = m_CreateEmptyShadowmap;
            passData.shadowmapID = m_MainLightShadowmapID;
            passData.renderingData = renderingData;
            passData.cameraData = cameraData;
            passData.lightData = lightData;
            passData.shadowData = shadowData;
        }

        void InitEmptyPassData(
            ref PassData passData,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalLightData lightData,
            UniversalShadowData shadowData)
        {
            passData.pass = this;

            passData.emptyShadowmap = m_CreateEmptyShadowmap;
            passData.shadowmapID = m_MainLightShadowmapID;
            passData.renderingData = renderingData;
            passData.cameraData = cameraData;
            passData.lightData = lightData;
            passData.shadowData = shadowData;
        }

        private void InitRendererLists(ref PassData passData, ScriptableRenderContext context, RenderGraph renderGraph, bool useRenderGraph)
        {
            int shadowLightIndex = passData.lightData.mainLightIndex;
            if (!m_CreateEmptyShadowmap && shadowLightIndex != -1)
            {
                var settings = new ShadowDrawingSettings(passData.renderingData.cullResults, shadowLightIndex);
                settings.useRenderingLayerMaskTest = UniversalRenderPipeline.asset.useRenderingLayers;
                for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
                {
                    if (useRenderGraph)
                        passData.shadowRendererListsHandle[cascadeIndex] = renderGraph.CreateShadowRendererList(ref settings);
                    else
                        passData.shadowRendererLists[cascadeIndex] = context.CreateShadowRendererList(ref settings);
                }
            }
        }

        internal TextureHandle Render(RenderGraph graph, ContextContainer frameData)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();

            TextureHandle shadowTexture;

            using (var builder = graph.AddUnsafePass<PassData>("Directional Lights Shadowmap", out var passData, base.profilingSampler))
            {
                InitPassData(ref passData, renderingData, cameraData, lightData, shadowData);
                InitRendererLists(ref passData, default(ScriptableRenderContext), graph, true);

                if (!m_CreateEmptyShadowmap)
                {
                    for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
                    {
                        builder.UseRendererList(passData.shadowRendererListsHandle[cascadeIndex]);
                    }

                    // Directional Lights ShadowMaps use texture2D array.
                    var shadowDescriptor = ShadowUtils.GetTemporaryShadowTextureDescriptor(shadowData.mainLightRenderTargetWidth, shadowData.mainLightRenderTargetHeight, 16);
                    shadowDescriptor.dimension = TextureDimension.Tex2DArray;
                    shadowDescriptor.volumeDepth = shadowData.mainLightShadowCascadesCount;

                    shadowTexture = UniversalRenderer.CreateRenderGraphTexture(graph, shadowDescriptor, "_DirectionalLightsShadowmapTexture", true, ShadowUtils.m_ForceShadowPointSampling ? FilterMode.Point : FilterMode.Bilinear);
                }
                else
                {
                    shadowTexture = graph.defaultResources.defaultShadowArrayTexture;
                }

                passData.shadowmapTexture = shadowTexture;

                builder.UseTexture(shadowTexture, AccessFlags.Write);
                // Need this as shadowmap is only used as Global Texture and not a buffer, so would get culled by RG
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                if (shadowTexture.IsValid())
                    builder.SetGlobalTextureAfterPass(shadowTexture, m_MainLightShadowmapID);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    if (!data.emptyShadowmap)
                    {
                        data.pass.RenderMainLightCascadeShadowmap(context.cmd, ref data);
                    }
                    else
                    {
                        //data.pass.SetEmptyMainLightCascadeShadowmap(context.cmd);
                    }
                });
            }

            return shadowTexture;
        }
    };
}
