using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenuForRenderPipeline("GapperGames/Screen Space Reflections", typeof(UniversalRenderPipeline))]
    public sealed partial class ScreenSpaceReflections : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enabled = new BoolParameter(false);
        public ClampedFloatParameter maxDistance = new ClampedFloatParameter(80.0f, 5.0f, 400.0f);
        public ClampedFloatParameter traceQuality = new ClampedFloatParameter(0.75f, 0.0f, 1.0f);
        public ClampedFloatParameter thickness = new ClampedFloatParameter(0.08f, 0.005f, 1.0f);
        public ClampedFloatParameter missFade = new ClampedFloatParameter(0.75f, 0.0f, 6.0f);
        public ClampedFloatParameter fadeDistance = new ClampedFloatParameter(50.0f, 0.0f, 400.0f);
        public ClampedFloatParameter fresnelFade = new ClampedFloatParameter(0.8f, 0.0f, 1.0f);
        public ClampedIntParameter refinementSamples = new ClampedIntParameter(5, 0, 12);
        public BoolParameter debugReflectionOnly = new BoolParameter(false);
        public ClampedIntParameter debugMode = new ClampedIntParameter(0, 0, 6);

        /// <inheritdoc/>
        public bool IsActive() => enabled.value;

        /// <inheritdoc/>
        public bool IsTileCompatible() => false;
    }
}
