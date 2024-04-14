using System;
using System.Runtime.InteropServices;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Use it as compute buffer system.(Danbaidong, 20240317)
    /// Change computeBuffer Dispose() to CoreUtils.SafeRelease(). (Danbaidong, 20240403)
    /// !!!!!!Obsolete!!!!!! Use ComputeBufferSystem instead
    /// </summary>
    class ShaderData : IDisposable
    {
        static ShaderData m_Instance = null;
        ComputeBuffer m_LightDataBuffer = null;
        ComputeBuffer m_LightIndicesBuffer = null;

        ComputeBuffer m_AdditionalLightShadowParamsStructuredBuffer = null;
        ComputeBuffer m_AdditionalLightShadowSliceMatricesStructuredBuffer = null;

        // SSR Compute Buffer
        ComputeBuffer m_SSRDispatchIndirectBuffer = null;
        ComputeBuffer m_SSRTileListBuffer = null;

        // GPU Lights Compute Buffer
        ComputeBuffer m_GPULightsConvexBoundsBuffer = null;
        ComputeBuffer m_GPULightsLightVolumeDataBuffer = null;

        ShaderData()
        {
        }

        internal static ShaderData instance
        {
            get
            {
                if (m_Instance == null)
                    m_Instance = new ShaderData();

                return m_Instance;
            }
        }

        public void Dispose()
        {
            DisposeBuffer(ref m_LightDataBuffer);
            DisposeBuffer(ref m_LightIndicesBuffer);
            DisposeBuffer(ref m_AdditionalLightShadowParamsStructuredBuffer);
            DisposeBuffer(ref m_AdditionalLightShadowSliceMatricesStructuredBuffer);
            DisposeBuffer(ref m_SSRDispatchIndirectBuffer);
            DisposeBuffer(ref m_SSRTileListBuffer);

            DisposeBuffer(ref m_GPULightsConvexBoundsBuffer);
            DisposeBuffer(ref m_GPULightsLightVolumeDataBuffer);
        }

        internal ComputeBuffer GetLightDataBuffer(int size)
        {
            return GetOrUpdateBuffer<ShaderInput.LightData>(ref m_LightDataBuffer, size);
        }

        internal ComputeBuffer GetLightIndicesBuffer(int size)
        {
            return GetOrUpdateBuffer<int>(ref m_LightIndicesBuffer, size);
        }

        internal ComputeBuffer GetAdditionalLightShadowParamsStructuredBuffer(int size)
        {
            return GetOrUpdateBuffer<Vector4>(ref m_AdditionalLightShadowParamsStructuredBuffer, size);
        }

        internal ComputeBuffer GetAdditionalLightShadowSliceMatricesStructuredBuffer(int size)
        {
            return GetOrUpdateBuffer<Matrix4x4>(ref m_AdditionalLightShadowSliceMatricesStructuredBuffer, size);
        }

        internal ComputeBuffer GetSSRDispatchIndirectBuffer(int size)
        {
            return GetOrUpdateBuffer<uint>(ref m_SSRDispatchIndirectBuffer, size, ComputeBufferType.IndirectArguments);
        }

        internal ComputeBuffer GetSSRTileListBuffer(int size)
        {
            return GetOrUpdateBuffer<uint>(ref m_SSRTileListBuffer, size);
        }

        internal ComputeBuffer GetGPULightsBoundsBuffer(int size)
        {
            return GetOrUpdateBuffer<SFiniteLightBound>(ref m_GPULightsConvexBoundsBuffer, size);
        }

        internal ComputeBuffer GetGPULightsVolumeDataBuffer(int size)
        {
            return GetOrUpdateBuffer<LightVolumeData>(ref m_GPULightsLightVolumeDataBuffer, size);
        }

        ComputeBuffer GetOrUpdateBuffer<T>(ref ComputeBuffer buffer, int size, ComputeBufferType bufferType = ComputeBufferType.Default) where T : struct
        {
            if (buffer == null)
            {
                buffer = new ComputeBuffer(size, Marshal.SizeOf<T>(), bufferType);
            }
            else if (size > buffer.count)
            {
                CoreUtils.SafeRelease(buffer);
                buffer = new ComputeBuffer(size, Marshal.SizeOf<T>(), bufferType);
            }

            return buffer;
        }

        void DisposeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                CoreUtils.SafeRelease(buffer);
                buffer = null;
            }
        }
    }
}
