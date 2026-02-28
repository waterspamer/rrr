using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ReflectRenderPass))]
public sealed class ReflectRenderPassEditor : Editor
{
    private SerializedProperty renderPassProp;

    private void OnEnable()
    {
        renderPassProp = serializedObject.FindProperty("renderPass");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(renderPassProp);
        EditorGUILayout.HelpBox(
            "SSR quality and debug settings are configured in Volume component 'Screen Space Reflections'.",
            MessageType.Info);
        serializedObject.ApplyModifiedProperties();
    }
}
