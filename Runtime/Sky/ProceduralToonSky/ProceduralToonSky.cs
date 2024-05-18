using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Sky/ProceduralToon Sky")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
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