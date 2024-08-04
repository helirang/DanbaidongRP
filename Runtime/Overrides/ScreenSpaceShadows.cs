using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Lighting/Screen Space Shadows")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed partial class ScreenSpaceShadows : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Penumbra controls shadows soften width.")]
        public ClampedFloatParameter penumbra = new ClampedFloatParameter(1.0f, 0.0001f, 3.0f);

        [Tooltip("Shadow intensity.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [Tooltip("Use shadow ramp texture or not.")]
        public BoolParameter enableShadowRamp = new BoolParameter(true);

        [Tooltip("Shadow ramp texture.")]
        public NoInterpTextureParameter shadowRampTex = new NoInterpTextureParameter(null);

        /// <inheritdoc/>
        public bool IsActive() => true; // Always enable screenSpaceShadows.

        /// <inheritdoc/>
        [Obsolete("Unused #from(2023.1)", false)]
        public bool IsTileCompatible() => false;
    }
}