using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class CarBodyAssemblerWindow : EditorWindow
{
    [Serializable]
    private sealed class MaterialRule
    {
        public string contains;
        public Material target;
    }

    [SerializeField] private DefaultAsset sourceFolder;
    [SerializeField] private DefaultAsset outputFolder;
    [SerializeField] private bool includeSubfolders = true;

    [Header("Body Selection")]
    [SerializeField] private string baseBodyName = "Car_Buick_GrandNational_1987";
    [SerializeField] private bool autoBaseBodyName = true;
    [SerializeField] private bool autoSearchSubfolders = true;
    [SerializeField] private bool includeBaseBody = true;
    [SerializeField] private bool includeNoSetParts = true;
    [SerializeField] private string setSuffixes = "_SetA;_Set_A";
    [SerializeField] private string excludeNameContains = "wheel";

    [Header("Scale")]
    [SerializeField] private bool applyReferenceScale = true;
    [SerializeField] private bool autoFindScaleFromPrefabName = true;
    [SerializeField] private GameObject scaleReferencePrefab;
    [SerializeField] private Vector3 fallbackScale = Vector3.one;

    [Header("Materials")]
    [SerializeField] private bool replaceMaterials = true;
    [SerializeField] private List<MaterialRule> materialRules = new List<MaterialRule>();

    private GameObject previewRoot;
    private string lastReport = string.Empty;

    [MenuItem("Tools/Car Body Assembler")]
    public static void Open()
    {
        GetWindow<CarBodyAssemblerWindow>("Car Body Assembler");
    }

    private void OnEnable()
    {
        if (materialRules == null || materialRules.Count == 0)
            materialRules = CreateDefaultRules();
    }

    private void OnDisable()
    {
        DestroyPreview();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Models Folder", sourceFolder, typeof(DefaultAsset), false);
        includeSubfolders = EditorGUILayout.Toggle("Include Subfolders", includeSubfolders);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Body Selection", EditorStyles.boldLabel);
        autoBaseBodyName = EditorGUILayout.Toggle("Auto Base Body Name", autoBaseBodyName);
        if (autoBaseBodyName)
            autoSearchSubfolders = EditorGUILayout.Toggle("Auto Search Subfolders", autoSearchSubfolders);
        using (new EditorGUI.DisabledScope(autoBaseBodyName))
        {
            baseBodyName = EditorGUILayout.TextField("Base Body Name", baseBodyName);
        }
        includeBaseBody = EditorGUILayout.Toggle("Include Base Body", includeBaseBody);
        includeNoSetParts = EditorGUILayout.Toggle("Include Parts Without _Set", includeNoSetParts);
        setSuffixes = EditorGUILayout.TextField("Set Suffixes", setSuffixes);
        excludeNameContains = EditorGUILayout.TextField("Exclude If Name Contains", excludeNameContains);
        if (autoBaseBodyName && sourceFolder != null)
        {
            string sourcePath = AssetDatabase.GetAssetPath(sourceFolder);
            if (AssetDatabase.IsValidFolder(sourcePath))
            {
                string resolved = ResolveBaseBodyName(sourcePath, autoSearchSubfolders);
                EditorGUILayout.LabelField("Auto Base Body", resolved);
                baseBodyName = resolved;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scale", EditorStyles.boldLabel);
        applyReferenceScale = EditorGUILayout.Toggle("Apply Reference Scale", applyReferenceScale);
        if (applyReferenceScale)
        {
            autoFindScaleFromPrefabName = EditorGUILayout.Toggle("Auto Find Scale By Name", autoFindScaleFromPrefabName);
            scaleReferencePrefab = (GameObject)EditorGUILayout.ObjectField("Scale Reference Prefab", scaleReferencePrefab, typeof(GameObject), false);
        }
        fallbackScale = EditorGUILayout.Vector3Field("Fallback Scale", fallbackScale);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Materials", EditorStyles.boldLabel);
        replaceMaterials = EditorGUILayout.Toggle("Replace Materials", replaceMaterials);
        if (replaceMaterials)
        {
            int removeIndex = -1;
            for (int i = 0; i < materialRules.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    materialRules[i].contains = EditorGUILayout.TextField(materialRules[i].contains, GUILayout.Width(220));
                    materialRules[i].target = (Material)EditorGUILayout.ObjectField(materialRules[i].target, typeof(Material), false);
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                        removeIndex = i;
                }
            }

            if (removeIndex >= 0)
                materialRules.RemoveAt(removeIndex);

            if (GUILayout.Button("Add Rule"))
                materialRules.Add(new MaterialRule());
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!CanBuild()))
            {
                if (GUILayout.Button("Create Preview"))
                    CreatePreview();
            }

            if (GUILayout.Button("Clear Preview"))
                DestroyPreview();
        }

        using (new EditorGUI.DisabledScope(!CanBuild()))
        {
            if (GUILayout.Button("Build Body Prefab"))
                BuildPrefab();
        }

        if (!string.IsNullOrEmpty(lastReport))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(lastReport, MessageType.Info);
        }
    }

    private bool CanBuild()
    {
        return sourceFolder != null && outputFolder != null;
    }

    private void CreatePreview()
    {
        DestroyPreview();

        if (!TryBuildInstance(out GameObject instance, out string error))
        {
            lastReport = error;
            return;
        }

        previewRoot = instance;
        previewRoot.hideFlags = HideFlags.DontSave;
        Selection.activeGameObject = previewRoot;
        SceneView.lastActiveSceneView?.FrameSelected();
        lastReport = "Preview created in scene.";
    }

    private void DestroyPreview()
    {
        if (previewRoot != null)
        {
            DestroyImmediate(previewRoot);
            previewRoot = null;
        }
    }

    private void BuildPrefab()
    {
        if (!TryBuildInstance(out GameObject instance, out string error))
        {
            lastReport = error;
            return;
        }

        string outputPath = AssetDatabase.GetAssetPath(outputFolder);
        string resolvedBaseName = ResolveBaseBodyName(AssetDatabase.GetAssetPath(sourceFolder), autoSearchSubfolders);
        string prefabName = resolvedBaseName + "_Body.prefab";
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{outputPath}/{prefabName}");
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        lastReport = $"Prefab saved: {prefabPath}";
    }

    private bool TryBuildInstance(out GameObject instance, out string error)
    {
        instance = null;
        error = string.Empty;

        string sourcePath = AssetDatabase.GetAssetPath(sourceFolder);
        if (!AssetDatabase.IsValidFolder(sourcePath))
        {
            error = "Source folder is invalid.";
            return false;
        }

        string resolvedBaseName = ResolveBaseBodyName(sourcePath, autoSearchSubfolders);
        GameObject baseBodyAsset = null;
        if (includeBaseBody)
        {
            baseBodyAsset = FindAssetByName(sourcePath, resolvedBaseName, autoSearchSubfolders);
            if (baseBodyAsset == null)
            {
                error = $"Base body not found: {resolvedBaseName}";
                return false;
            }
        }

        instance = new GameObject(resolvedBaseName + "_BodyRoot");
        instance.transform.localScale = ResolveRootScale(sourcePath, resolvedBaseName);

        if (baseBodyAsset != null)
        {
            GameObject body = InstantiateAsset(baseBodyAsset, instance.transform);
            body.name = baseBodyAsset.name;
        }

        List<string> suffixList = ParseSuffixes(setSuffixes);
        List<GameObject> setParts = FindAssetsBySuffixes(sourcePath, suffixList, includeNoSetParts);
        foreach (GameObject partAsset in setParts)
        {
            if (partAsset == null)
                continue;

            string partName = partAsset.name;
            if (baseBodyAsset != null && string.Equals(partName, baseBodyAsset.name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(excludeNameContains) &&
                partName.IndexOf(excludeNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            GameObject partInstance = InstantiateAsset(partAsset, instance.transform);
            partInstance.name = partName;
        }

        if (replaceMaterials)
            ApplyMaterialRules(instance);

        return true;
    }

    private List<string> ParseSuffixes(string raw)
    {
        List<string> suffixes = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return suffixes;

        string[] parts = raw.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string trimmed = parts[i].Trim();
            if (!string.IsNullOrEmpty(trimmed))
                suffixes.Add(trimmed);
        }

        return suffixes;
    }

    private GameObject FindAssetByName(string sourcePath, string assetName, bool searchSubfolders)
    {
        string filter = $"{assetName} t:Prefab";
        string[] guids = AssetDatabase.FindAssets(filter, new[] { sourcePath });
        if (guids.Length == 0)
        {
            filter = $"{assetName} t:Model";
            guids = AssetDatabase.FindAssets(filter, new[] { sourcePath });
        }

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!PathMatches(path, sourcePath))
                continue;
            if (!searchSubfolders && PathIsInSubfolder(sourcePath, path))
                continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(fileName, assetName, StringComparison.OrdinalIgnoreCase))
                continue;

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        return null;
    }

    private List<GameObject> FindAssetsBySuffixes(string sourcePath, List<string> suffixes)
    {
        return FindAssetsBySuffixes(sourcePath, suffixes, includeNoSetParts);
    }

    private List<GameObject> FindAssetsBySuffixes(string sourcePath, List<string> suffixes, bool includeNoSet)
    {
        List<GameObject> assets = new List<GameObject>();
        if (suffixes.Count == 0 && !includeNoSet)
            return assets;

        CollectAssetsBySuffixes(sourcePath, suffixes, includeNoSet, "t:Prefab", assets);
        CollectAssetsBySuffixes(sourcePath, suffixes, includeNoSet, "t:Model", assets);
        return assets;
    }

    private void CollectAssetsBySuffixes(string sourcePath, List<string> suffixes, bool includeNoSet, string filter, List<GameObject> assets)
    {
        string[] guids = AssetDatabase.FindAssets(filter, new[] { sourcePath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!PathMatches(path, sourcePath))
                continue;
            if (!includeSubfolders && PathIsInSubfolder(sourcePath, path))
                continue;

            string name = Path.GetFileNameWithoutExtension(path);
            if (!MatchesAnySuffix(name, suffixes) && !(includeNoSet && !ContainsSetToken(name)))
                continue;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null)
                assets.Add(asset);
        }
    }

    private static bool MatchesAnySuffix(string name, List<string> suffixes)
    {
        for (int i = 0; i < suffixes.Count; i++)
        {
            if (name.IndexOf(suffixes[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool ContainsSetToken(string name)
    {
        return name.IndexOf("_Set", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool PathMatches(string path, string root)
    {
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!path.Replace('\\', '/').Contains("/"))
            return false;

        return true;
    }

    private static bool PathIsInSubfolder(string root, string path)
    {
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;

        string relative = path.Substring(root.Length).TrimStart('/', '\\');
        return relative.Contains("/") || relative.Contains("\\");
    }

    private GameObject InstantiateAsset(GameObject asset, Transform parent)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        if (instance == null)
            instance = Instantiate(asset);

        instance.transform.SetParent(parent, false);
        return instance;
    }

    private Vector3 ResolveRootScale(string sourcePath, string resolvedBaseName)
    {
        if (!applyReferenceScale)
            return fallbackScale;

        GameObject reference = scaleReferencePrefab;
        if (reference == null && autoFindScaleFromPrefabName)
            reference = FindPrefabByNameAnywhere(resolvedBaseName, sourcePath);

        if (reference == null)
            return fallbackScale;

        string path = AssetDatabase.GetAssetPath(reference);
        if (string.IsNullOrEmpty(path))
            return fallbackScale;

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        Vector3 scale = contents != null ? contents.transform.localScale : fallbackScale;
        if (contents != null)
            PrefabUtility.UnloadPrefabContents(contents);

        return scale;
    }

    private string ResolveBaseBodyName(string sourcePath, bool searchSubfolders)
    {
        if (!autoBaseBodyName || !AssetDatabase.IsValidFolder(sourcePath))
            return baseBodyName;

        List<string> allNames = CollectAllAssetNames(sourcePath, searchSubfolders);
        List<string> candidates = CollectCandidateBaseNames(allNames);
        List<string> underscoreMatches = FilterByUnderscoreCount(candidates, 3);

        List<string> pickFrom = underscoreMatches.Count > 0 ? underscoreMatches : candidates;
        if (pickFrom.Count == 0)
            return baseBodyName;

        string best = pickFrom[0];
        int bestScore = ScoreCandidate(best, allNames);

        for (int i = 1; i < pickFrom.Count; i++)
        {
            string candidate = pickFrom[i];
            int score = ScoreCandidate(candidate, allNames);
            if (score > bestScore || (score == bestScore && candidate.Length > best.Length))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private List<string> CollectAllAssetNames(string sourcePath, bool searchSubfolders)
    {
        List<string> names = new List<string>();
        CollectCandidates(sourcePath, "t:Prefab", names, searchSubfolders);
        CollectCandidates(sourcePath, "t:Model", names, searchSubfolders);
        return names;
    }

    private List<string> CollectCandidateBaseNames(List<string> allNames)
    {
        List<string> candidates = new List<string>();
        for (int i = 0; i < allNames.Count; i++)
        {
            string name = allNames[i];
            if (name.IndexOf("_Set", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (!string.IsNullOrEmpty(excludeNameContains) &&
                name.IndexOf(excludeNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            if (!ContainsIgnoreCase(candidates, name))
                candidates.Add(name);
        }

        return candidates;
    }

    private void CollectCandidates(string sourcePath, string filter, List<string> names, bool searchSubfolders)
    {
        string[] guids = AssetDatabase.FindAssets(filter, new[] { sourcePath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!PathMatches(path, sourcePath))
                continue;
            if (!searchSubfolders && PathIsInSubfolder(sourcePath, path))
                continue;

            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name))
                continue;

            if (!ContainsIgnoreCase(names, name))
                names.Add(name);
        }
    }

    private int ScoreCandidate(string candidate, List<string> allNames)
    {
        int score = 0;
        for (int i = 0; i < allNames.Count; i++)
        {
            string other = allNames[i];
            if (other.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                score++;
        }
        return score;
    }

    private static List<string> FilterByUnderscoreCount(List<string> names, int count)
    {
        List<string> result = new List<string>();
        for (int i = 0; i < names.Count; i++)
        {
            if (CountUnderscores(names[i]) == count)
                result.Add(names[i]);
        }
        return result;
    }

    private static int CountUnderscores(string value)
    {
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '_')
                count++;
        }
        return count;
    }

    private static bool ContainsIgnoreCase(List<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private GameObject FindPrefabByNameAnywhere(string assetName, string excludeRoot)
    {
        string[] guids = AssetDatabase.FindAssets($"{assetName} t:Prefab", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                continue;
            if (path.StartsWith(excludeRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(fileName, assetName, StringComparison.OrdinalIgnoreCase))
                continue;

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        return null;
    }

    private void ApplyMaterialRules(GameObject root)
    {
        if (materialRules == null || materialRules.Count == 0)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
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
                    changed = true;
                }
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private Material FindReplacement(string sourceName)
    {
        string lowered = sourceName.ToLowerInvariant();
        for (int i = 0; i < materialRules.Count; i++)
        {
            MaterialRule rule = materialRules[i];
            if (rule == null || rule.target == null || string.IsNullOrWhiteSpace(rule.contains))
                continue;

            if (lowered.Contains(rule.contains.ToLowerInvariant()))
                return rule.target;
        }

        return null;
    }

    private static List<MaterialRule> CreateDefaultRules()
    {
        List<MaterialRule> rules = new List<MaterialRule>();
        rules.Add(new MaterialRule { contains = "carpaint", target = FindMaterialByName("CarPaint") });
        rules.Add(new MaterialRule { contains = "metallicrough", target = FindMaterialByName("MetallicRough") });
        rules.Add(new MaterialRule { contains = "metallic rough", target = FindMaterialByName("MetallicRough") });
        rules.Add(new MaterialRule { contains = "metallicsmooth", target = FindMaterialByName("MetallicSmooth") });
        rules.Add(new MaterialRule { contains = "metallic smooth", target = FindMaterialByName("MetallicSmooth") });
        rules.Add(new MaterialRule { contains = "plasticrough", target = FindMaterialByName("PlasticRough") });
        rules.Add(new MaterialRule { contains = "plastic rough", target = FindMaterialByName("PlasticRough") });
        rules.Add(new MaterialRule { contains = "plasticsmooth", target = FindMaterialByName("PlasticSmooth") });
        rules.Add(new MaterialRule { contains = "plastic smooth", target = FindMaterialByName("PlasticSmooth") });
        rules.Add(new MaterialRule { contains = "glassred", target = FindMaterialByName("GlassRed") });
        rules.Add(new MaterialRule { contains = "glass red", target = FindMaterialByName("GlassRed") });
        rules.Add(new MaterialRule { contains = "glass", target = FindMaterialByName("Glass") });
        return rules;
    }

    private static Material FindMaterialByName(string materialName)
    {
        string[] guids = AssetDatabase.FindAssets($"{materialName} t:Material", new[] { "Assets" });
        if (guids.Length == 0)
            return null;

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }
}
