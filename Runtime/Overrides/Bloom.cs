using System;

namespace UnityEngine.Rendering.Universal
{
    public enum BloomMode
    {
        None,
        BloomURP,
        BloomDanbaidong,
    }
    [Serializable]
    public sealed class BloomModeParameter : VolumeParameter<BloomMode>
    {
        public BloomModeParameter(BloomMode value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }
    /// <summary>
    /// This controls the size of the bloom texture.
    /// </summary>
    public enum BloomDownscaleMode
    {
        /// <summary>
        /// Use this to select half size as the starting resolution.
        /// </summary>
        Half,

        /// <summary>
        /// Use this to select quarter size as the starting resolution.
        /// </summary>
        Quarter,
    }

    /// <summary>
    /// A volume component that holds settings for the Bloom effect.
    /// </summary>
    [Serializable, VolumeComponentMenu("Post-processing/Bloom")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("post-processing-bloom")]
    public sealed partial class Bloom : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Select a Bloom Mode.")]
        public BloomModeParameter mode = new BloomModeParameter(BloomMode.None);
        /// <summary>
        /// Set the level of brightness to filter out pixels under this level.
        /// This value is expressed in gamma-space.
        /// A value above 0 will disregard energy conservation rules.
        /// </summary>
        [Header("Bloom")]
        [Tooltip("Filters out pixels under this level of brightness. Value is in gamma-space.")]
        public MinFloatParameter threshold = new MinFloatParameter(0.7f, 0f);

        /// <summary>
        /// Controls the strength of the bloom filter.
        /// </summary>
        [Tooltip("Strength of the bloom filter.")]
        public MinFloatParameter intensity = new MinFloatParameter(0.75f, 0f);

        [Tooltip("lumRnageScale of the bloom filter. We need this to anti-flicker.")]
        public ClampedFloatParameter lumRnageScale = new ClampedFloatParameter(0.2f, 0f, 1f);

        [Tooltip("preFilterScale of the bloom filter.")]
        public ClampedFloatParameter preFilterScale = new ClampedFloatParameter(2.5f, 0f, 5.0f);

        [Tooltip("preFilterScale of the bloom filter.")]
        public Vector4Parameter blurCompositeWeight = new Vector4Parameter(new Vector4(0.3f, 0.3f, 0.26f, 0.15f));

        /// <summary>
        /// Controls the extent of the veiling effect.
        /// </summary>
        [Tooltip("Set the radius of the bloom effect.")]
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0f, 1f);

        /// <summary>
        /// Set the maximum intensity that Unity uses to calculate Bloom.
        /// If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.
        /// </summary>
        [Tooltip("Set the maximum intensity that Unity uses to calculate Bloom. If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.")]
        public MinFloatParameter clamp = new MinFloatParameter(65472f, 0f);

        /// <summary>
        /// Specifies the tint of the bloom filter.
        /// </summary>
        [Tooltip("Use the color picker to select a color for the Bloom effect to tint to.")]
        public ColorParameter tint = new ColorParameter(new Color(1f, 1f, 1f, 0f), false, true, true);

        /// <summary>
        /// Controls whether to use bicubic sampling instead of bilinear sampling for the upsampling passes.
        /// This is slightly more expensive but helps getting smoother visuals.
        /// </summary>
        [Tooltip("Use bicubic sampling instead of bilinear sampling for the upsampling passes. This is slightly more expensive but helps getting smoother visuals.")]
        public BoolParameter highQualityFiltering = new BoolParameter(false);

        /// <summary>
        /// Controls the starting resolution that this effect begins processing.
        /// </summary>
        [Tooltip("The starting resolution that this effect begins processing."), AdditionalProperty]
        public DownscaleParameter downscale = new DownscaleParameter(BloomDownscaleMode.Half);

        /// <summary>
        /// Controls the maximum number of iterations in the effect processing sequence.
        /// </summary>
        [Tooltip("The maximum number of iterations in the effect processing sequence."), AdditionalProperty]
        public ClampedIntParameter maxIterations = new ClampedIntParameter(6, 2, 8);

        /// <summary>
        /// Specifies a Texture to add smudges or dust to the bloom effect.
        /// </summary>
        [Header("Lens Dirt")]
        [Tooltip("Dirtiness texture to add smudges or dust to the bloom effect.")]
        public TextureParameter dirtTexture = new TextureParameter(null);

        /// <summary>
        /// Controls the strength of the lens dirt.
        /// </summary>
        [Tooltip("Amount of dirtiness.")]
        public MinFloatParameter dirtIntensity = new MinFloatParameter(0f, 0f);

        /// <inheritdoc/>
        public bool IsActive() => intensity.value > 0f && mode.value != BloomMode.None;

        /// <inheritdoc/>
        [Obsolete("Unused #from(2023.1)", false)]
        public bool IsTileCompatible() => false;
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="BloomDownscaleMode"/> value.
    /// </summary>
    [Serializable]
    public sealed class DownscaleParameter : VolumeParameter<BloomDownscaleMode>
    {
        /// <summary>
        /// Creates a new <see cref="DownscaleParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public DownscaleParameter(BloomDownscaleMode value, bool overrideState = false) : base(value, overrideState) { }
    }
}
