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
        public ClampedFloatParameter depthCrossTolerance = new ClampedFloatParameter(2.0f, 0.5f, 12.0f);
        public ClampedFloatParameter minHitDistance = new ClampedFloatParameter(0.02f, 0.0f, 1.0f);
        public BoolParameter hierarchicalTraversal = new BoolParameter(false);
        public BoolParameter dualLayerThickness = new BoolParameter(false);
        public ClampedFloatParameter dualLayerRadius = new ClampedFloatParameter(1.5f, 0.5f, 4.0f);
        public ClampedFloatParameter missFade = new ClampedFloatParameter(0.75f, 0.0f, 6.0f);
        public ClampedFloatParameter fadeDistance = new ClampedFloatParameter(50.0f, 0.0f, 400.0f);
        public ClampedFloatParameter fresnelFade = new ClampedFloatParameter(0.8f, 0.0f, 1.0f);
        public ClampedFloatParameter reflectionIntensity = new ClampedFloatParameter(1.0f, 0.0f, 4.0f);
        public ClampedIntParameter refinementSamples = new ClampedIntParameter(5, 0, 12);
        public BoolParameter debugReflectionOnly = new BoolParameter(false);
        public ClampedIntParameter debugMode = new ClampedIntParameter(0, 0, 40);
        public BoolParameter debugLogResources = new BoolParameter(false);
        public ClampedIntParameter debugLogInterval = new ClampedIntParameter(30, 1, 300);

        /// <inheritdoc/>
        public bool IsActive() => enabled.value;

        /// <inheritdoc/>
        public bool IsTileCompatible() => false;
    }
}
