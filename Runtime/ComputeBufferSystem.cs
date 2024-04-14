using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{

    public enum ComputeBufferSystemBufferID
    {
        LightDataBuffer,
        LightIndicesBuffer,

        AdditionalLightShadowParamsStructuredBuffer,
        AdditionalLightShadowSliceMatricesStructuredBuffer,
        /// <summary>SSR Compute Buffer.</summary>
        SSRDispatchIndirectBuffer,
        SSRTileListBuffer,
        /// <summary>GPU Lights Compute Buffer.</summary>
        GPULightsConvexBoundsBuffer,
        GPULightsLightVolumeDataBuffer,
        GPULightsAABBBounds,
        GPULightsCoarseLightList,
        GPULightsPerVoxelLightLists,
        GPULightsPerVoxelOffset,
        GPULightsPerTileLogBaseTweak,
        GPULightsGlobalLightLIstAtomic,
        GPULightsData,
    }


    /// <summary>
    /// ComputeBufferSystem used for managing compute buffers in DanbaidongRP.
    /// Each camera uses one system, and the compute buffer will be set to the maximum size across cameras.
    /// 
    /// !!!IMPORTANT!!!
    /// Please note that buffers may not be reliable if you intend to access the buffer from the previous frame.
    /// </summary>
    class ComputeBufferSystem : IDisposable
    {
        static Dictionary<int, ComputeBuffer> s_ComputeBuffers = new Dictionary<int, ComputeBuffer>();
        bool m_DisposedValue = false;

        static ComputeBufferSystem m_Instance = null;

        ComputeBufferSystem()
        {
        }

        internal static ComputeBufferSystem instance
        {
            get
            {
                if (m_Instance == null)
                    m_Instance = new ComputeBufferSystem();

                return m_Instance;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bufferId"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
        internal ComputeBuffer GetComputeBuffer(ComputeBufferSystemBufferID bufferId, ComputeBufferDesc desc)
        {
            int id = (int)bufferId;
            if (!s_ComputeBuffers.ContainsKey(id))
            {
                Debug.Log("ComputeBufferSystem: create buffer " + bufferId);

                var buffer = new ComputeBuffer(desc.count, desc.stride, desc.type);
                s_ComputeBuffers.Add(id, buffer);
                return buffer;
            }

            ComputeBuffer mbuffer;
            s_ComputeBuffers.TryGetValue(id, out mbuffer);
            // Update reference
            if (GetOrUpdateBuffer(ref mbuffer, desc))
            {
                s_ComputeBuffers.Remove(id);
                s_ComputeBuffers.Add(id, mbuffer);
            }


            return mbuffer;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bufferId"></param>
        /// <param name="size"></param>
        /// <param name="bufferType"></param>
        /// <returns></returns>
        internal ComputeBuffer GetComputeBuffer<T>(ComputeBufferSystemBufferID bufferId, int size, ComputeBufferType bufferType = ComputeBufferType.Default) where T : struct
        {
            var desc = new ComputeBufferDesc(size, Marshal.SizeOf<T>(), bufferType);

            return GetComputeBuffer(bufferId, desc);
        }

        /// <summary>
        /// Note that we should ensure that the buffer stride is not changed.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="desc"></param>
        /// <returns>if reference updated or not</returns>
        bool GetOrUpdateBuffer(ref ComputeBuffer buffer, ComputeBufferDesc desc)
        {
            bool bufUpdated = false;
            if (buffer == null)
            {
                buffer = new ComputeBuffer(desc.count, desc.stride, desc.type);
                bufUpdated = true;
            }
            else if (desc.count > buffer.count)
            {
                buffer.Release();
                buffer = new ComputeBuffer(desc.count, desc.stride, desc.type);
                bufUpdated = true;
            }

            return bufUpdated;
        }

        void DisposeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                CoreUtils.SafeRelease(buffer);
                buffer = null;
            }
        }

        public static void ClearAll()
        {
            if (m_Instance != null)
                m_Instance.Dispose();

            m_Instance = null;
        }

        /// <summary>
        /// Dispose implementation
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (!m_DisposedValue)
            {
                if (disposing)
                {
                    ReleaseAll();
                }

                m_DisposedValue = true;
            }
        }

        /// <summary>
        /// Deallocate and clear all compute buffers.
        /// </summary>
        public void ReleaseAll()
        {
            foreach (var item in s_ComputeBuffers)
            {
                var buffer = item.Value;
                DisposeBuffer(ref buffer);
            }
            s_ComputeBuffers.Clear();
        }
    }
}
