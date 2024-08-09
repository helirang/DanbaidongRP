Shader "Hidden/ScreenSpaceShadowScatter"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }


        // 0: Shadow estimation
        Pass
        {
            Name "ShadowScatter"

            // -------------------------------------
            // Render State Commands
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM


            // -------------------------------------
            // Shader Stages
            #pragma vertex Vert
            #pragma fragment FragShadowScatter

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Lighting.hlsl"

            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"

            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/ScreenSpaceLighting/ScreenSpaceLighting.hlsl"





    #pragma editor_sync_compilation
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch




#define SCREENSPACE_SHADOWS_TILE_SIZE   (16)
#define SCREENSPACE_SHADOWS_GROUP_SIZE  (SCREENSPACE_SHADOWS_TILE_SIZE / 2)
#define SCREENSPACE_SHADOWS_THREADS     (64)


// Scatter use big width and small count
#define PREFILTER_SAMPLE_COUNT          (8)
#define BLOCKER_SAMPLE_COUNT            (8)
#define PCSS_SAMPLE_COUNT               (16)
// World space filter size.
#define FILTER_SIZE_PREFILTER           (0.4)
#define FILTER_SIZE_BLOCKER             (0.2)
#define DIR_LIGHT_PENUMBRA_WIDTH        _DirLightShadowPenumbraParams.y * 3



// Limitation:
// Note that in cascade shadows, all occluders behind the near plane will get clamped to the near plane
// This will lead to the closest blocker sometimes being reported as much closer to the receiver than it really is
#if UNITY_REVERSED_Z
#define Z_OFFSET_DIRECTION 1
#else
#define Z_OFFSET_DIRECTION (-1)
#endif


#define DISK_SAMPLE_COUNT 64
// Fibonacci Spiral Disk Sampling Pattern
// https://people.irisa.fr/Ricardo.Marques/articles/2013/SF_CGF.pdf
//
// Normalized direction vector portion of fibonacci spiral can be baked into a LUT, regardless of sampleCount.
// This allows us to treat the directions as a progressive sequence, using any sampleCount in range [0, n <= LUT_LENGTH]
// the radius portion of spiral construction is coupled to sample count, but is fairly cheap to compute at runtime per sample.
// Generated (in javascript) with:
// var res = "";
// for (var i = 0; i < 64; ++i)
// {
//     var a = Math.PI * (3.0 - Math.sqrt(5.0));
//     var b = a / (2.0 * Math.PI);
//     var c = i * b;
//     var theta = (c - Math.floor(c)) * 2.0 * Math.PI;
//     res += "float2 (" + Math.cos(theta) + ", " + Math.sin(theta) + "),\n";
// }

static const float2 fibonacciSpiralDirection[DISK_SAMPLE_COUNT] =
{
    float2 (1, 0),
    float2 (-0.7373688780783197, 0.6754902942615238),
    float2 (0.08742572471695988, -0.9961710408648278),
    float2 (0.6084388609788625, 0.793600751291696),
    float2 (-0.9847134853154288, -0.174181950379311),
    float2 (0.8437552948123969, -0.5367280526263233),
    float2 (-0.25960430490148884, 0.9657150743757782),
    float2 (-0.46090702471337114, -0.8874484292452536),
    float2 (0.9393212963241182, 0.3430386308741014),
    float2 (-0.924345556137805, 0.3815564084749356),
    float2 (0.423845995047909, -0.9057342725556143),
    float2 (0.29928386444487326, 0.9541641203078969),
    float2 (-0.8652112097532296, -0.501407581232427),
    float2 (0.9766757736281757, -0.21471942904125949),
    float2 (-0.5751294291397363, 0.8180624302199686),
    float2 (-0.12851068979899202, -0.9917081236973847),
    float2 (0.764648995456044, 0.6444469828838233),
    float2 (-0.9991460540072823, 0.04131782619737919),
    float2 (0.7088294143034162, -0.7053799411794157),
    float2 (-0.04619144594036213, 0.9989326054954552),
    float2 (-0.6407091449636957, -0.7677836880006569),
    float2 (0.9910694127331615, 0.1333469877603031),
    float2 (-0.8208583369658855, 0.5711318504807807),
    float2 (0.21948136924637865, -0.9756166914079191),
    float2 (0.4971808749652937, 0.8676469198750981),
    float2 (-0.952692777196691, -0.30393498034490235),
    float2 (0.9077911335843911, -0.4194225289437443),
    float2 (-0.38606108220444624, 0.9224732195609431),
    float2 (-0.338452279474802, -0.9409835569861519),
    float2 (0.8851894374032159, 0.4652307598491077),
    float2 (-0.9669700052147743, 0.25489019011123065),
    float2 (0.5408377383579945, -0.8411269468800827),
    float2 (0.16937617250387435, 0.9855514761735877),
    float2 (-0.7906231749427578, -0.6123030256690173),
    float2 (0.9965856744766464, -0.08256508601054027),
    float2 (-0.6790793464527829, 0.7340648753490806),
    float2 (0.0048782771634473775, -0.9999881011351668),
    float2 (0.6718851669348499, 0.7406553331023337),
    float2 (-0.9957327006438772, -0.09228428288961682),
    float2 (0.7965594417444921, -0.6045602168251754),
    float2 (-0.17898358311978044, 0.9838520605119474),
    float2 (-0.5326055939855515, -0.8463635632843003),
    float2 (0.9644371617105072, 0.26431224169867934),
    float2 (-0.8896863018294744, 0.4565723210368687),
    float2 (0.34761681873279826, -0.9376366819478048),
    float2 (0.3770426545691533, 0.9261958953890079),
    float2 (-0.9036558571074695, -0.4282593745796637),
    float2 (0.9556127564793071, -0.2946256262683552),
    float2 (-0.50562235513749, 0.8627549095688868),
    float2 (-0.2099523790012021, -0.9777116131824024),
    float2 (0.8152470554454873, 0.5791133210240138),
    float2 (-0.9923232342597708, 0.12367133357503751),
    float2 (0.6481694844288681, -0.7614961060013474),
    float2 (0.036443223183926, 0.9993357251114194),
    float2 (-0.7019136816142636, -0.7122620188966349),
    float2 (0.998695384655528, 0.05106396643179117),
    float2 (-0.7709001090366207, 0.6369560596205411),
    float2 (0.13818011236605823, -0.9904071165669719),
    float2 (0.5671206801804437, 0.8236347091470047),
    float2 (-0.9745343917253847, -0.22423808629319533),
    float2 (0.8700619819701214, -0.49294233692210304),
    float2 (-0.30857886328244405, 0.9511987621603146),
    float2 (-0.4149890815356195, -0.9098263912451776),
    float2 (0.9205789302157817, 0.3905565685566777)
};

float2 ComputeFibonacciSpiralDiskSample(const in int sampleIndex, const in float diskRadius, const in float sampleCountInverse, const in float sampleCountBias)
{
    float sampleRadius = diskRadius * sqrt((float)sampleIndex * sampleCountInverse + sampleCountBias);
    float2 sampleDirection = fibonacciSpiralDirection[sampleIndex];
    return sampleDirection * sampleRadius;
}

// Samples non-uniformly spread across the disk kernel
float2 ComputeFibonacciSpiralDiskSampleClumped_Directional(const in int sampleIndex, const in float sampleCountInverse, const in float clumpExponent, out float sampleDistNorm)
{
    // Samples biased away from the center, so that sample 0 doesn't fall at (0, 0), or it will not be affected by sample jitter and create a visible edge.
    sampleDistNorm = (float)sampleIndex * sampleCountInverse;

    // non-uniform distribution when clumpExponent != 0.5
    // More samples in the middle
    sampleDistNorm = PositivePow(sampleDistNorm, clumpExponent);

    return fibonacciSpiralDirection[sampleIndex] * sampleDistNorm;
}

// Samples uniformly spread across the disk kernel
float2 ComputeFibonacciSpiralDiskSampleUniform_Directional(const in int sampleIndex, const in float sampleCountInverse, const in float sampleBias, out float sampleDistNorm)
{
    // Samples biased away from the center, so that sample 0 doesn't fall at (0, 0), or it will not be affected by sample jitter and create a visible edge.
    sampleDistNorm = (float)sampleIndex * sampleCountInverse + sampleBias;

    // sqrt results in uniform distribution
    sampleDistNorm = sqrt(sampleDistNorm);

    return fibonacciSpiralDirection[sampleIndex] * sampleDistNorm;
}

// PreFilter finds the border of shadows. 
float PreFilterSearch(float sampleCount, float filterSize, float3 shadowCoord, float cascadeIndex)
{
    float numBlockers = 0.0;

    float radial2ShadowmapDepth = _PerCascadePCSSData[cascadeIndex].x;
    float texelSizeWS = _PerCascadePCSSData[cascadeIndex].y;
    float farToNear = _PerCascadePCSSData[cascadeIndex].z;
    float blockerInvTangent = _PerCascadePCSSData[cascadeIndex].w;

    float2 minCoord = _DirLightShadowUVMinMax.xy;
    float2 maxCoord = _DirLightShadowUVMinMax.zw;
    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;

    // kernel my be too large, and there can be no valid depth compare result.
    // we must calculate it again in later PCSS.
    float coordOutOfBoundCount = 0;

    for (int i = 1; i < sampleCount && i < DISK_SAMPLE_COUNT; i++)
    {
        float sampleRadius = sqrt((float)i * sampleCountInverse + sampleCountBias);
        float2 offset = fibonacciSpiralDirection[i] * sampleRadius;
        offset *= filterSize;
        offset *= _MainLightShadowmapSize.x; // coord to uv


        float2 sampleCoord = shadowCoord.xy + offset;

        float radialOffset = filterSize * sampleRadius * texelSizeWS;
        float zoffset = radialOffset / farToNear * blockerInvTangent;

        float depthLS = shadowCoord.z + (Z_OFFSET_DIRECTION) * zoffset;

        float shadowMapDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_DirectionalLightsShadowmapTexture, sampler_PointClamp, sampleCoord, cascadeIndex, 0).x;

        bool isOutOfCoord = any(sampleCoord < minCoord) || any(sampleCoord > maxCoord);
        if (!isOutOfCoord && COMPARE_DEVICE_DEPTH_CLOSER(shadowMapDepth, depthLS))
        {
            numBlockers += 1.0;
        }

        if (isOutOfCoord)
        {
            coordOutOfBoundCount++;
        }
    }

    // Out of bound, we must calculate it again in later PCSS.
    if (coordOutOfBoundCount > 0)
    {
        numBlockers = 1.0;
    }

    // We must cover zero offset.
    float shadowMapDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_DirectionalLightsShadowmapTexture, sampler_PointClamp, shadowCoord.xy, cascadeIndex, 0).x;
    if (!(any(shadowCoord.xy < minCoord) || any(shadowCoord.xy > maxCoord)) && 
        COMPARE_DEVICE_DEPTH_CLOSER(shadowMapDepth, shadowCoord.z))
    {
        numBlockers += 1.0;
    }

    return numBlockers;
}

// Return x:avgerage blocker depth, y:num blockers
float2 BlockerSearch(float sampleCount, float filterSize, float3 shadowCoord, float2 random, float cascadeIndex)
{
    float avgBlockerDepth = 0.0;
    float depthSum = 0.0;
    float numBlockers = 0.0;

    float radial2ShadowmapDepth = _PerCascadePCSSData[cascadeIndex].x;
    float texelSizeWS = _PerCascadePCSSData[cascadeIndex].y;
    float farToNear = _PerCascadePCSSData[cascadeIndex].z;
    float blockerInvTangent = _PerCascadePCSSData[cascadeIndex].w;

    float2 minCoord = _DirLightShadowUVMinMax.xy;
    float2 maxCoord = _DirLightShadowUVMinMax.zw;
    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;
    
    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; i++)
    {
        float sampleDistNorm;
        float2 offset = 0.0;
        offset = ComputeFibonacciSpiralDiskSampleUniform_Directional(i, sampleCountInverse, sampleCountBias, sampleDistNorm);
        offset = float2(offset.x *  random.y + offset.y * random.x,
                    offset.x * -random.x + offset.y * random.y);
        offset *= filterSize;
        offset *= _MainLightShadowmapSize.x; // coord to uv

        float2 sampleCoord = shadowCoord.xy + offset;

        float radialOffset = filterSize * sampleDistNorm * texelSizeWS;
        float zoffset = radialOffset / farToNear * blockerInvTangent;

        float depthLS = shadowCoord.z + (Z_OFFSET_DIRECTION) * zoffset;

        float shadowMapDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_DirectionalLightsShadowmapTexture, sampler_PointClamp, sampleCoord, cascadeIndex, 0).x;
        if (!(any(sampleCoord < minCoord) || any(sampleCoord > maxCoord)) && 
            COMPARE_DEVICE_DEPTH_CLOSER(shadowMapDepth, depthLS))
        {
            depthSum += shadowMapDepth;
            numBlockers += 1.0;
        }
    }

    if (numBlockers > 0.0)
    {
        avgBlockerDepth = depthSum / numBlockers;
    }

    return float2(avgBlockerDepth, numBlockers);
}

float PCSSFilter(float sampleCount, float filterSize, float3 shadowCoord, float2 random, float cascadeIndex, float maxPCSSoffset)
{
    float numBlockers = 0.0;
    float totalSamples = 0.0;

    float radial2ShadowmapDepth = _PerCascadePCSSData[cascadeIndex].x;
    float texelSizeWS = _PerCascadePCSSData[cascadeIndex].y;
    float farToNear = _PerCascadePCSSData[cascadeIndex].z;
    float blockerInvTangent = _PerCascadePCSSData[cascadeIndex].w;

    float2 minCoord = _DirLightShadowUVMinMax.xy;
    float2 maxCoord = _DirLightShadowUVMinMax.zw;
    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;

    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; i++)
    {
        float sampleDistNorm;
        float2 offset = 0.0;
        offset = ComputeFibonacciSpiralDiskSampleUniform_Directional(i, sampleCountInverse, sampleCountBias, sampleDistNorm);
        offset = float2(offset.x *  random.y + offset.y * random.x,
                    offset.x * -random.x + offset.y * random.y);
        offset *= filterSize;
        offset *= _MainLightShadowmapSize.x; // coord to uv

        float2 sampleCoord = shadowCoord.xy + offset;

        float radialOffset = filterSize * sampleDistNorm * texelSizeWS;
        float zoffset = radialOffset / farToNear * blockerInvTangent;

        float depthLS = shadowCoord.z + (Z_OFFSET_DIRECTION) * min(zoffset, maxPCSSoffset);

        if (!(any(sampleCoord < minCoord) || any(sampleCoord > maxCoord)))
        {
            float shadowSample = SAMPLE_TEXTURE2D_ARRAY_SHADOW(_DirectionalLightsShadowmapTexture, sampler_LinearClampCompare, float3(sampleCoord, depthLS), cascadeIndex).x;
            numBlockers += shadowSample;
            totalSamples += 1.0;
        }
    }

    return totalSamples > 0 ? numBlockers / totalSamples : 1.0;
}



















            float4 FragShadowScatter(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 screenUV = i.texcoord.xy;

                float depth = SampleSceneDepth(screenUV);
                if (depth == UNITY_RAW_FAR_CLIP_VALUE)
                    return 1.0;

                PositionInputs posInput = GetPositionInput(screenUV * _ScreenSize.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V, uint2(0, 0));

                float3 positionWS = posInput.positionWS;
                float cascadeIndex = ComputeCascadeIndex(positionWS);
                float4 shadowCoord = mul(_MainLightWorldToShadow[cascadeIndex], float4(positionWS, 1.0));


                float shadowMapDepth = SAMPLE_TEXTURE2D_ARRAY_LOD(_DirectionalLightsShadowmapTexture, sampler_PointClamp, shadowCoord.xy, cascadeIndex, 0).x;

                float radial2ShadowmapDepth = _PerCascadePCSSData[cascadeIndex].x;
                float texelSizeWS           = _PerCascadePCSSData[cascadeIndex].y;
                float farToNear             = _PerCascadePCSSData[cascadeIndex].z;
                float blockerInvTangent     = _PerCascadePCSSData[cascadeIndex].w;

                bool needPenumbra = false;

                // PreFilter Search
                float preFilterSize = FILTER_SIZE_PREFILTER / texelSizeWS; // texel count
                preFilterSize = max(preFilterSize, 1.0);
                float preFilterRet = PreFilterSearch(PREFILTER_SAMPLE_COUNT, 2.0 * preFilterSize, shadowCoord.xyz, cascadeIndex);
                bool isOcclusion = preFilterRet > 0;
                if (isOcclusion && preFilterRet < PREFILTER_SAMPLE_COUNT)
                {
                    needPenumbra = true;
                }


                // Sample Noise: Use Jitter instead.
                float2 noiseJitter = 0;
                noiseJitter.xy = InterleavedGradientNoise(screenUV * _ScreenSize.xy, _STBNIndex);
                noiseJitter *= TWO_PI;
                noiseJitter.x = sin(noiseJitter.x);
                noiseJitter.y = cos(noiseJitter.y);

                // Blocker Search
                float filterSize = FILTER_SIZE_BLOCKER / texelSizeWS; // texel count
                filterSize = max(filterSize, 1.0);
                float2 avgDepthAndCount = BlockerSearch(BLOCKER_SAMPLE_COUNT, filterSize, shadowCoord.xyz, noiseJitter, cascadeIndex);
                if (avgDepthAndCount.y == 0) // No Blocker
                {
                    return 1.0;
                }

                // Penumbra Estimation
                float blockerDistance = abs(avgDepthAndCount.x - shadowCoord.z);
                blockerDistance *= farToNear;
                blockerDistance = min(blockerDistance, 10.0);

                float maxPCSSoffset = blockerDistance / farToNear * 0.25;

                float pcssFilterSize = _DirLightShadowPenumbraParams.y * blockerDistance * 0.01 / texelSizeWS;
                pcssFilterSize = max(pcssFilterSize, 0.01);


                // PCSS Filter
                float pcssResult = PCSSFilter(PCSS_SAMPLE_COUNT, pcssFilterSize, shadowCoord.xyz, noiseJitter, cascadeIndex, maxPCSSoffset);

                return pcssResult;
            }
            ENDHLSL
        }
    }
}
