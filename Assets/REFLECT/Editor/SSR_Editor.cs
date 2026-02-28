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
        SerializedDataParameter missFade;
        SerializedDataParameter fadeDistance;
        SerializedDataParameter fresnelFade;
        SerializedDataParameter refinementSamples;
        SerializedDataParameter debugReflectionOnly;
        SerializedDataParameter debugMode;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceReflections>(serializedObject);

            enabled = Unpack(o.Find(x => x.enabled));
            maxDistance = Unpack(o.Find(x => x.maxDistance));
            traceQuality = Unpack(o.Find(x => x.traceQuality));
            thickness = Unpack(o.Find(x => x.thickness));
            missFade = Unpack(o.Find(x => x.missFade));
            fadeDistance = Unpack(o.Find(x => x.fadeDistance));
            fresnelFade = Unpack(o.Find(x => x.fresnelFade));
            refinementSamples = Unpack(o.Find(x => x.refinementSamples));
            debugReflectionOnly = Unpack(o.Find(x => x.debugReflectionOnly));
            debugMode = Unpack(o.Find(x => x.debugMode));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(enabled, new GUIContent("Enabled / Toggle"));
            PropertyField(maxDistance, new GUIContent("Max Distance"));
            PropertyField(traceQuality, new GUIContent("Resolution / Trace Quality"));
            PropertyField(thickness, new GUIContent("Thickness / Depth Buffer"));
            PropertyField(missFade, new GUIContent("Miss Fade Softness"));
            PropertyField(fadeDistance, new GUIContent("Fade Distance"));
            PropertyField(fresnelFade, new GUIContent("Fresnel Fade"));
            PropertyField(refinementSamples, new GUIContent("Refinement / Samples"));
            PropertyField(debugReflectionOnly);
            PropertyField(debugMode);
        }
    }
}
