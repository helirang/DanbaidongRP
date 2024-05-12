using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Sky/ProceduralToon Sky", typeof(UniversalRenderPipeline))]
    [SkyUniqueID((int)SkyType.ProceduralToon)]
    public sealed class ProceduralToonSky : SkySettings
    {


        /// <summary>
        /// Returns ProceduralToonSky Renderer type.
        /// </summary>
        /// <returns></returns>
        public override Type GetSkyRendererType()
        {
            return typeof(ProceduralToonSkyRenderer);
        }
    }
}