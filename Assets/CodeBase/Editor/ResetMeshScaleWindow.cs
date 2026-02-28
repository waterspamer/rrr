using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class ResetMeshScaleWindow : EditorWindow
{
    [SerializeField] private GameObject root;
    [SerializeField] private bool useSelection = true;
    [SerializeField] private bool includeInactive = true;
    private string lastReport = string.Empty;

    [MenuItem("Tools/Reset Mesh Scales")]
    public static void Open()
    {
        GetWindow<ResetMeshScaleWindow>("Reset Mesh Scales");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
        useSelection = EditorGUILayout.Toggle("Use Selection", useSelection);
        using (new EditorGUI.DisabledScope(useSelection))
        {
            root = (GameObject)EditorGUILayout.ObjectField("Root", root, typeof(GameObject), true);
        }
        includeInactive = EditorGUILayout.Toggle("Include Inactive", includeInactive);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!CanRun()))
        {
            if (GUILayout.Button("Reset Scales"))
                ResetScales();
        }

        if (!string.IsNullOrEmpty(lastReport))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(lastReport, MessageType.Info);
        }
    }

    private bool CanRun()
    {
        if (useSelection)
            return Selection.activeGameObject != null;

        return root != null;
    }

    private void ResetScales()
    {
        GameObject target = useSelection ? Selection.activeGameObject : root;
        if (target == null)
        {
            lastReport = "No target selected.";
            return;
        }

        Transform[] transforms = target.GetComponentsInChildren<Transform>(includeInactive);

        int changed = 0;
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null)
                continue;

            if (t.localScale == Vector3.one)
                continue;

            Undo.RecordObject(t, "Reset Mesh Scale");
            t.localScale = Vector3.one;
            EditorUtility.SetDirty(t);
            changed++;
        }

        lastReport = $"Reset scales on {changed} objects.";
    }
}
