using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Sky/HDRI Sky", typeof(UniversalRenderPipeline))]
    [SkyUniqueID((int)SkyType.HDRI)]
    public sealed class HDRISky : SkySettings
    {
        /// <summary>Cubemap used to render the HDRI sky.</summary>
        [Tooltip("Specify the cubemap uses to render the sky.")]
        public CubemapParameter hdriSky = new CubemapParameter(null);

        /// <summary>
        /// Returns the hash code of the HDRI sky parameters.
        /// </summary>
        /// <returns>The hash code of the HDRI sky parameters.</returns>
        public override int GetHashCode()
        {
            int hash = base.GetHashCode();

            unchecked
            {
#if UNITY_2019_3 // In 2019.3, when we call GetHashCode on a VolumeParameter it generate garbage (due to the boxing of the generic parameter)
                hash = hdriSky.value != null ? hash * 23 + hdriSky.value.GetHashCode() : hash;   
#else
                hash = hash * 23 + hdriSky.GetHashCode();
#endif
            }

            return hash;
        }

        /// <summary>
        /// Returns HDRISky Renderer type.
        /// </summary>
        /// <returns></returns>
        public override Type GetSkyRendererType()
        {
            return typeof(HDRISkyRenderer);
        }
    }
}