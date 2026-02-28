using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class MaterialMigratorWindow : EditorWindow
{
    [Serializable]
    private sealed class Mapping
    {
        public string sourceName;
        public Material target;
    }

    [SerializeField] private GameObject root;
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool useSelection = true;
    [SerializeField] private List<Mapping> mappings = new List<Mapping>();

    private string lastReport = string.Empty;

    [MenuItem("Tools/Material Migrator")]
    public static void Open()
    {
        GetWindow<MaterialMigratorWindow>("Material Migrator");
    }

    private void OnEnable()
    {
        if (mappings == null)
            mappings = new List<Mapping>();
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
        EditorGUILayout.LabelField("Mappings (exact name)", EditorStyles.boldLabel);
        int removeIndex = -1;
        for (int i = 0; i < mappings.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(mappings[i].sourceName, GUILayout.Width(260));
                mappings[i].target = (Material)EditorGUILayout.ObjectField(mappings[i].target, typeof(Material), false);
                if (GUILayout.Button("X", GUILayout.Width(22)))
                    removeIndex = i;
            }
        }
        if (removeIndex >= 0)
            mappings.RemoveAt(removeIndex);

        if (GUILayout.Button("Collect Materials From Target"))
            CollectMaterials();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!CanRun()))
        {
            if (GUILayout.Button("Migrate Materials"))
                Migrate();
        }

        if (!string.IsNullOrEmpty(lastReport))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(lastReport, MessageType.Info);
        }
    }

    private bool CanRun()
    {
        return (useSelection ? Selection.activeGameObject != null : root != null) && mappings.Count > 0;
    }

    private void Migrate()
    {
        GameObject targetRoot = useSelection ? Selection.activeGameObject : root;
        if (targetRoot == null)
        {
            lastReport = "No target selected.";
            return;
        }

        int replaced = 0;
        Renderer[] renderers = targetRoot.GetComponentsInChildren<Renderer>(includeInactive);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int m = 0; m < materials.Length; m++)
            {
                Material source = materials[m];
                if (source == null)
                    continue;

                string sourceName = source.name;
                if (sourceName.EndsWith(" (Instance)", StringComparison.OrdinalIgnoreCase))
                    sourceName = sourceName.Substring(0, sourceName.Length - 11);

                Material replacement = FindReplacement(sourceName);
                if (replacement != null && replacement != source)
                {
                    materials[m] = replacement;
                    replaced++;
                    changed = true;
                }
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }

        lastReport = $"Done. Replaced materials: {replaced}";
    }

    private void CollectMaterials()
    {
        GameObject targetRoot = useSelection ? Selection.activeGameObject : root;
        if (targetRoot == null)
        {
            lastReport = "No target selected.";
            return;
        }

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<Mapping> updated = new List<Mapping>();
        Renderer[] renderers = targetRoot.GetComponentsInChildren<Renderer>(includeInactive);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material mat = materials[m];
                if (mat == null)
                    continue;

                string name = NormalizeMaterialName(mat.name);
                if (!seen.Add(name))
                    continue;

                Mapping existing = mappings.Find(r => string.Equals(r.sourceName, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    updated.Add(existing);
                else
                    updated.Add(new Mapping { sourceName = name });
            }
        }

        mappings = updated;
        lastReport = $"Collected materials: {mappings.Count}";
    }

    private Material FindReplacement(string sourceName)
    {
        for (int i = 0; i < mappings.Count; i++)
        {
            Mapping mapping = mappings[i];
            if (mapping == null || mapping.target == null || string.IsNullOrWhiteSpace(mapping.sourceName))
                continue;

            if (string.Equals(sourceName, mapping.sourceName, StringComparison.OrdinalIgnoreCase))
                return mapping.target;
        }
        return null;
    }

    private static string NormalizeMaterialName(string name)
    {
        if (name.EndsWith(" (Instance)", StringComparison.OrdinalIgnoreCase))
            return name.Substring(0, name.Length - 11);
        return name;
    }
}
