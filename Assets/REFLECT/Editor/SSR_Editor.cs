using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(ScreenSpaceReflections))]
    sealed class SSR_Editor : VolumeComponentEditor
    {
        SerializedDataParameter enabled;
        SerializedDataParameter maxDistance;
        SerializedDataParameter traceQuality;
        SerializedDataParameter thickness;
        SerializedDataParameter depthCrossTolerance;
        SerializedDataParameter minHitDistance;
        SerializedDataParameter hierarchicalTraversal;
        SerializedDataParameter dualLayerThickness;
        SerializedDataParameter dualLayerRadius;
        SerializedDataParameter missFade;
        SerializedDataParameter fadeDistance;
        SerializedDataParameter fresnelFade;
        SerializedDataParameter refinementSamples;
        SerializedDataParameter debugReflectionOnly;
        SerializedDataParameter debugMode;
        SerializedDataParameter debugLogResources;
        SerializedDataParameter debugLogInterval;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceReflections>(serializedObject);

            enabled = Unpack(o.Find(x => x.enabled));
            maxDistance = Unpack(o.Find(x => x.maxDistance));
            traceQuality = Unpack(o.Find(x => x.traceQuality));
            thickness = Unpack(o.Find(x => x.thickness));
            depthCrossTolerance = Unpack(o.Find(x => x.depthCrossTolerance));
            minHitDistance = Unpack(o.Find(x => x.minHitDistance));
            hierarchicalTraversal = Unpack(o.Find(x => x.hierarchicalTraversal));
            dualLayerThickness = Unpack(o.Find(x => x.dualLayerThickness));
            dualLayerRadius = Unpack(o.Find(x => x.dualLayerRadius));
            missFade = Unpack(o.Find(x => x.missFade));
            fadeDistance = Unpack(o.Find(x => x.fadeDistance));
            fresnelFade = Unpack(o.Find(x => x.fresnelFade));
            refinementSamples = Unpack(o.Find(x => x.refinementSamples));
            debugReflectionOnly = Unpack(o.Find(x => x.debugReflectionOnly));
            debugMode = Unpack(o.Find(x => x.debugMode));
            debugLogResources = Unpack(o.Find(x => x.debugLogResources));
            debugLogInterval = Unpack(o.Find(x => x.debugLogInterval));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(enabled, new GUIContent("Enabled / Toggle"));
            PropertyField(maxDistance, new GUIContent("Max Distance"));
            PropertyField(traceQuality, new GUIContent("Resolution / Trace Quality"));
            PropertyField(thickness, new GUIContent("Thickness / Depth Buffer"));
            PropertyField(depthCrossTolerance, new GUIContent("Depth Cross Tolerance"));
            PropertyField(minHitDistance, new GUIContent("Min Hit Distance"));
            PropertyField(hierarchicalTraversal, new GUIContent("Hierarchical Traversal"));
            PropertyField(dualLayerThickness, new GUIContent("Dual Layer Thickness"));
            PropertyField(dualLayerRadius, new GUIContent("Dual Layer Radius"));
            PropertyField(missFade, new GUIContent("Miss Fade Softness"));
            PropertyField(fadeDistance, new GUIContent("Fade Distance"));
            PropertyField(fresnelFade, new GUIContent("Fresnel Fade"));
            PropertyField(refinementSamples, new GUIContent("Refinement / Samples"));
            PropertyField(debugReflectionOnly);
            PropertyField(debugMode);
            PropertyField(debugLogResources);
            PropertyField(debugLogInterval);
        }
    }
}
