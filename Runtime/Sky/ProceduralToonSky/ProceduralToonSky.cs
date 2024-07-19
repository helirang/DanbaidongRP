using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Sky/ProceduralToon Sky")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [SkyUniqueID((int)SkyType.ProceduralToon)]
    public sealed class ProceduralToonSky : SkySettings
    {
        internal static Material defaultMaterial { get; set; }


        public MaterialParameter material = new MaterialParameter(null);

        /// <summary>
        /// Returns the hash code of the Procedural Toon sky parameters.
        /// </summary>
        /// <returns>The hash code of the Procedural Toon sky parameters.</returns>
        public override int GetHashCode()
        {
            int hash = base.GetHashCode();

            unchecked
            {
                //hash = hash * 23 + material.GetHashCode();
            }

            return hash;
        }

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