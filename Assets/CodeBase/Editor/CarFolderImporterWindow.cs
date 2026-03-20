using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class CarFolderImporterWindow : EditorWindow
{
    private const string DefaultDamageComputePath = "Assets/Shaders/VehicleDamageDeform.compute";

    private sealed class PartImportInfo
    {
        public string CategoryName;
        public string SelectorGroupName;
        public string VariantName;
    }

    private readonly struct PaintDefinition
    {
        public readonly string DisplayName;
        public readonly string AssetSuffix;
        public readonly Color Color;

        public PaintDefinition(string displayName, Color color)
        {
            DisplayName = displayName;
            AssetSuffix = displayName.Replace(" ", string.Empty);
            Color = color;
        }
    }

    [SerializeField] private DefaultAsset sourceFolder;
    [SerializeField] private DefaultAsset outputFolder;
    [SerializeField] private bool includeSubfolders = true;

    [Header("Naming")]
    [SerializeField] private string vehicleId = "NewCar";
    [SerializeField] private string displayName = "New Car";
    [SerializeField] private bool autoDetectBaseBody = true;
    [SerializeField] private string baseBodyName = string.Empty;
    [SerializeField] private string wheelNameHint = "WheelFL";
    [SerializeField] private string setSuffixes = "_SetA;_SetGTR;_SetR;_Set";
    [SerializeField] private bool includePartsWithoutSet = true;
    [SerializeField] private string excludeNameContains = "wheel";

    [Header("Prefab Settings")]
    [SerializeField] private Vector3 rootScale = Vector3.one;
    [SerializeField] private bool overwriteExistingAssets = false;
    [SerializeField] private CarMaterialRemapProfile materialRemapProfile;

    [Header("PlayerCar Defaults")]
    [SerializeField] private bool createPlayerCarConfig = true;
    [SerializeField] private bool createGameplayConfigs = true;
    [SerializeField] private float wheelBase = 2.75f;
    [SerializeField] private float axleWidth = 1.5f;
    [SerializeField] private float wheelHeight = 0.35f;
    [SerializeField] private bool generateConvexBodyColliders = true;

    private string lastReport = string.Empty;

    [MenuItem("Tools/Vehicle Importer")]
    public static void Open()
    {
        GetWindow<CarFolderImporterWindow>("Vehicle Importer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Car Art Folder", sourceFolder, typeof(DefaultAsset), false);
        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
        includeSubfolders = EditorGUILayout.Toggle("Include Subfolders", includeSubfolders);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Naming", EditorStyles.boldLabel);
        vehicleId = EditorGUILayout.TextField("Vehicle Id", vehicleId);
        displayName = EditorGUILayout.TextField("Display Name", displayName);
        autoDetectBaseBody = EditorGUILayout.Toggle("Auto Detect Base Body", autoDetectBaseBody);
        using (new EditorGUI.DisabledScope(autoDetectBaseBody))
        {
            baseBodyName = EditorGUILayout.TextField("Base Body Name", baseBodyName);
        }

        wheelNameHint = EditorGUILayout.TextField("Wheel Hint", wheelNameHint);
        setSuffixes = EditorGUILayout.TextField("Set Suffixes", setSuffixes);
        includePartsWithoutSet = EditorGUILayout.Toggle("Include Parts Without Set", includePartsWithoutSet);
        excludeNameContains = EditorGUILayout.TextField("Exclude If Name Contains", excludeNameContains);

        if (autoDetectBaseBody && sourceFolder != null)
        {
            string sourcePath = AssetDatabase.GetAssetPath(sourceFolder);
            if (AssetDatabase.IsValidFolder(sourcePath))
                EditorGUILayout.LabelField("Detected Base", DetectBaseBodyName(sourcePath));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prefab Settings", EditorStyles.boldLabel);
        rootScale = EditorGUILayout.Vector3Field("Root Scale", rootScale);
        overwriteExistingAssets = EditorGUILayout.Toggle("Overwrite Existing", overwriteExistingAssets);
        materialRemapProfile = (CarMaterialRemapProfile)EditorGUILayout.ObjectField("Material Remap Profile", materialRemapProfile, typeof(CarMaterialRemapProfile), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("PlayerCar Defaults", EditorStyles.boldLabel);
        createPlayerCarConfig = EditorGUILayout.Toggle("Create PlayerCar Config", createPlayerCarConfig);
        createGameplayConfigs = EditorGUILayout.Toggle("Create Gameplay Configs", createGameplayConfigs);
        wheelBase = EditorGUILayout.FloatField("Wheel Base", wheelBase);
        axleWidth = EditorGUILayout.FloatField("Axle Width", axleWidth);
        wheelHeight = EditorGUILayout.FloatField("Wheel Height", wheelHeight);
        generateConvexBodyColliders = EditorGUILayout.Toggle("Generate Convex Body Colliders", generateConvexBodyColliders);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!CanBuild()))
        {
            if (GUILayout.Button("Build Vehicle Assets"))
                BuildVehicleAssets();
        }

        if (!string.IsNullOrWhiteSpace(lastReport))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(lastReport, MessageType.Info);
        }
    }

    private bool CanBuild()
    {
        return sourceFolder != null && outputFolder != null && !string.IsNullOrWhiteSpace(vehicleId);
    }

    private void BuildVehicleAssets()
    {
        string sourcePath = AssetDatabase.GetAssetPath(sourceFolder);
        string outputPath = AssetDatabase.GetAssetPath(outputFolder);
        if (!AssetDatabase.IsValidFolder(sourcePath) || !AssetDatabase.IsValidFolder(outputPath))
        {
            lastReport = "Source or output folder is invalid.";
            return;
        }

        string resolvedBaseBodyName = autoDetectBaseBody ? DetectBaseBodyName(sourcePath) : baseBodyName;
        if (string.IsNullOrWhiteSpace(resolvedBaseBodyName))
        {
            lastReport = "Failed to detect base body asset.";
            return;
        }

        GameObject baseBodyAsset = FindAssetByExactName(sourcePath, resolvedBaseBodyName);
        if (baseBodyAsset == null)
        {
            lastReport = $"Base body asset not found: {resolvedBaseBodyName}";
            return;
        }

        GameObject wheelAsset = FindWheelAsset(sourcePath);
        if (wheelAsset == null)
        {
            lastReport = $"Wheel asset not found. Hint: {wheelNameHint}";
            return;
        }

        EnsureReadableModel(baseBodyAsset);
        EnsureReadableModel(wheelAsset);

        string bodyPrefabPath = ResolveGeneratedAssetPath(outputPath, $"{vehicleId}_Body.prefab");
        string wheelPrefabPath = ResolveGeneratedAssetPath(outputPath, $"{vehicleId}_Wheel.prefab");
        string playerCarConfigPath = ResolveGeneratedAssetPath(outputPath, $"{vehicleId}_PlayerCar.asset");
        string handlingConfigPath = ResolveGeneratedAssetPath(outputPath, $"{vehicleId}_VehicleHandling.asset");
        string engineConfigPath = ResolveGeneratedAssetPath(outputPath, $"{vehicleId}_Engine.asset");
        string suspensionConfigPath = ResolveGeneratedAssetPath(outputPath, $"{vehicleId}_Suspension.asset");
        string loadoutConfigPath = ResolveGeneratedAssetPath(outputPath, $"{vehicleId}_Loadout.asset");
        string materialsFolderPath = EnsureChildFolder(outputPath, "Materials");
        string paintsFolderPath = EnsureChildFolder(outputPath, "Paints");

        GameObject bodyInstance = BuildBodyInstance(sourcePath, baseBodyAsset);
        GameObject wheelInstance = BuildWheelInstance(wheelAsset);

        try
        {
            CarMaterialRemapProfile profile = ResolveMaterialRemapProfile();
            Dictionary<string, Texture2D> textures = CollectTextureAssets(sourcePath);
            ApplyMaterialRemap(bodyInstance, materialsFolderPath, profile, textures);
            ApplyMaterialRemap(wheelInstance, materialsFolderPath, profile, textures);

            GameObject bodyPrefab = PrefabUtility.SaveAsPrefabAsset(bodyInstance, bodyPrefabPath);
            GameObject wheelPrefab = PrefabUtility.SaveAsPrefabAsset(wheelInstance, wheelPrefabPath);

            string report = $"Body: {bodyPrefabPath}\nWheel: {wheelPrefabPath}";
            PlayerCarConfig playerCarConfig = null;
            if (createPlayerCarConfig)
            {
                CreateOrUpdatePlayerCarConfig(playerCarConfigPath, bodyPrefab, wheelPrefab);
                playerCarConfig = AssetDatabase.LoadAssetAtPath<PlayerCarConfig>(playerCarConfigPath);
                report += $"\nPlayerCarConfig: {playerCarConfigPath}";
            }

            if (createGameplayConfigs)
            {
                VehicleSettings handling = CreateOrUpdateVehicleSettings(handlingConfigPath);
                EngineGearboxConfig engine = CreateOrUpdateEngineConfig(engineConfigPath);
                SuspensionConfig suspension = CreateOrUpdateSuspensionConfig(suspensionConfigPath);
                List<PaintConfig> paints = CreateOrUpdatePaintConfigs(paintsFolderPath);
                CreateOrUpdateLoadout(loadoutConfigPath, playerCarConfig, handling, engine, suspension, paints);
                ValidateGeneratedSetup(playerCarConfigPath, loadoutConfigPath, bodyPrefabPath, wheelPrefabPath);

                report += $"\nHandling: {handlingConfigPath}";
                report += $"\nEngine: {engineConfigPath}";
                report += $"\nSuspension: {suspensionConfigPath}";
                report += $"\nLoadout: {loadoutConfigPath}";
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            lastReport = report;
            EditorGUIUtility.PingObject(bodyPrefab);
        }
        finally
        {
            if (bodyInstance != null)
                DestroyImmediate(bodyInstance);
            if (wheelInstance != null)
                DestroyImmediate(wheelInstance);
        }
    }

    private GameObject BuildBodyInstance(string sourcePath, GameObject baseBodyAsset)
    {
        GameObject root = new GameObject($"{vehicleId}_Body");
        root.transform.localScale = rootScale;
        Transform bodyRoot = GetOrCreateChild(root.transform, "Body");
        Transform customsRoot = GetOrCreateChild(root.transform, "Customs");

        ExtractRenderableChildren(baseBodyAsset, GetOrCreateChild(bodyRoot, "Default"), baseBodyAsset.name);

        List<GameObject> assets = new List<GameObject>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GameObject asset in FindBodyPartAssets(sourcePath))
        {
            if (asset == null || string.Equals(asset.name, baseBodyAsset.name, StringComparison.OrdinalIgnoreCase) || !seen.Add(asset.name))
                continue;
            assets.Add(asset);
        }

        assets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

        Dictionary<string, Transform> variantRoots = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        HashSet<Transform> variantContainers = new HashSet<Transform>();
        for (int i = 0; i < assets.Count; i++)
        {
            GameObject asset = assets[i];
            EnsureReadableModel(asset);
            PartImportInfo info = BuildPartImportInfo(asset.name, false);
            Transform variantRoot = GetOrCreateVariantRoot(customsRoot, info, variantRoots, variantContainers);
            ExtractRenderableChildren(asset, variantRoot, asset.name);
        }

        ApplyVariantActivation(variantContainers);
        return root;
    }

    private GameObject BuildWheelInstance(GameObject wheelAsset)
    {
        GameObject root = new GameObject($"{vehicleId}_Wheel");
        root.transform.localScale = Vector3.one;

        ExtractRenderableChildren(wheelAsset, root.transform, wheelAsset.name);
        return root;
    }

    private CarMaterialRemapProfile ResolveMaterialRemapProfile()
    {
        if (materialRemapProfile != null)
            return materialRemapProfile;

        materialRemapProfile = AssetDatabase.LoadAssetAtPath<CarMaterialRemapProfile>(
            "Assets/Art/CarMaterials/DefaultCarMaterialRemapProfile.asset");
        return materialRemapProfile;
    }

    private void ApplyMaterialRemap(
        GameObject root,
        string materialsFolderPath,
        CarMaterialRemapProfile profile,
        Dictionary<string, Texture2D> textures)
    {
        if (root == null || profile == null)
            return;

        Dictionary<string, Material> generatedMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;

            Material[] sharedMaterials = renderer.sharedMaterials;
            bool changed = false;
            for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
            {
                Material sourceMaterial = sharedMaterials[materialIndex];
                string sourceMaterialName = GetSourceMaterialName(sourceMaterial);
                if (string.IsNullOrWhiteSpace(sourceMaterialName))
                    continue;

                if (!generatedMaterials.TryGetValue(sourceMaterialName, out Material generatedMaterial))
                {
                    generatedMaterial = CreateGeneratedMaterial(sourceMaterialName, materialsFolderPath, profile, textures);
                    if (generatedMaterial != null)
                        generatedMaterials[sourceMaterialName] = generatedMaterial;
                }

                if (generatedMaterial == null)
                    continue;

                sharedMaterials[materialIndex] = generatedMaterial;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = sharedMaterials;
        }
    }

    private Material CreateGeneratedMaterial(
        string sourceMaterialName,
        string materialsFolderPath,
        CarMaterialRemapProfile profile,
        Dictionary<string, Texture2D> textures)
    {
        Material template = profile.ResolveTemplate(sourceMaterialName);
        if (template == null)
            return null;

        string materialPath = ResolveGeneratedAssetPath(materialsFolderPath, $"{SanitizeFileName(sourceMaterialName)}.mat");
        Material generated = new Material(template)
        {
            name = sourceMaterialName
        };

        ApplySpecialTextureMappings(generated, sourceMaterialName, textures);
        AssetDatabase.DeleteAsset(materialPath);
        AssetDatabase.CreateAsset(generated, materialPath);
        return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
    }

    private static Dictionary<string, Texture2D> CollectTextureAssets(string sourcePath)
    {
        Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        string textureFolderPath = $"{sourcePath}/Texture2D";
        if (!AssetDatabase.IsValidFolder(textureFolderPath))
            return textures;

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolderPath });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                continue;

            textures[Path.GetFileNameWithoutExtension(path)] = texture;
        }

        return textures;
    }

    private static void ApplySpecialTextureMappings(Material material, string sourceMaterialName, Dictionary<string, Texture2D> textures)
    {
        if (material == null || textures == null)
            return;

        if (string.Equals(sourceMaterialName, "M_Engine_Max", StringComparison.OrdinalIgnoreCase))
        {
            ApplyTextureSet(material, textures, "Engine");
            return;
        }

        if (string.Equals(sourceMaterialName, "M_Interior_Max", StringComparison.OrdinalIgnoreCase))
            ApplyTextureSet(material, textures, "Interior");
    }

    private static void ApplyTextureSet(Material material, Dictionary<string, Texture2D> textures, string slotName)
    {
        Texture2D albedo = FindTextureBySuffix(textures, $"_{slotName}_D");
        Texture2D emission = FindTextureBySuffix(textures, $"_{slotName}_E");
        Texture2D metallic = FindTextureBySuffix(textures, $"_{slotName}_M");
        Texture2D normalOcclusion = FindTextureBySuffix(textures, $"_{slotName}_NO");

        FixNormalMapImport(normalOcclusion);

        SetTextureIfPresent(material, "_BaseMap", albedo);
        SetTextureIfPresent(material, "_MainTex", albedo);
        SetTextureIfPresent(material, "_EmissionMap", emission);
        SetTextureIfPresent(material, "_MetallicGlossMap", metallic);
        SetTextureIfPresent(material, "_BumpMap", normalOcclusion);
        SetTextureIfPresent(material, "_OcclusionMap", normalOcclusion);

        if (normalOcclusion != null)
            material.EnableKeyword("_NORMALMAP");

        if (emission != null)
        {
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.white);
        }
    }

    private static Texture2D FindTextureBySuffix(Dictionary<string, Texture2D> textures, string suffix)
    {
        foreach (KeyValuePair<string, Texture2D> pair in textures)
        {
            if (pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    private static void SetTextureIfPresent(Material material, string propertyName, Texture texture)
    {
        if (texture == null || material == null || !material.HasProperty(propertyName))
            return;

        material.SetTexture(propertyName, texture);
    }

    private static void FixNormalMapImport(Texture2D texture)
    {
        if (texture == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.NormalMap)
            return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.sRGBTexture = false;
        importer.SaveAndReimport();
    }

    private static string GetSourceMaterialName(Material sourceMaterial)
    {
        if (sourceMaterial == null)
            return string.Empty;

        string name = sourceMaterial.name ?? string.Empty;
        int instanceIndex = name.IndexOf(" (Instance)", StringComparison.Ordinal);
        if (instanceIndex >= 0)
            name = name.Substring(0, instanceIndex);
        return name.Trim();
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Material";

        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] buffer = value.ToCharArray();
        for (int i = 0; i < buffer.Length; i++)
        {
            if (invalidChars.Contains(buffer[i]))
                buffer[i] = '_';
        }

        return new string(buffer);
    }

    private void CreateOrUpdatePlayerCarConfig(string assetPath, GameObject bodyPrefab, GameObject wheelPrefab)
    {
        PlayerCarConfig config = AssetDatabase.LoadAssetAtPath<PlayerCarConfig>(assetPath);
        if (config == null)
        {
            config = CreateInstance<PlayerCarConfig>();
            AssetDatabase.CreateAsset(config, assetPath);
        }

        SerializedObject serializedObject = new SerializedObject(config);
        serializedObject.FindProperty("visual.bodyPrefab").objectReferenceValue = bodyPrefab;
        serializedObject.FindProperty("visual.wheelPrefab").objectReferenceValue = wheelPrefab;
        serializedObject.FindProperty("visual.addBodyCollider").boolValue = false;
        serializedObject.FindProperty("visual.generateConvexBodyColliders").boolValue = generateConvexBodyColliders;
        serializedObject.FindProperty("visual.wheelBase").floatValue = Mathf.Max(0.2f, wheelBase);
        serializedObject.FindProperty("visual.axleWidth").floatValue = Mathf.Max(0.2f, axleWidth);
        serializedObject.FindProperty("visual.wheelHeight").floatValue = wheelHeight;
        serializedObject.FindProperty("visual.useDefaultPaint").boolValue = true;
        serializedObject.FindProperty("visual.defaultPaint").colorValue = Color.white;
        serializedObject.FindProperty("visual.paintProperty").stringValue = "_MainColor";
        serializedObject.FindProperty("visual.paintAllChildRenderers").boolValue = true;
        serializedObject.FindProperty("damage.textureWidth").intValue = 8;
        serializedObject.FindProperty("damage.textureHeight").intValue = 16;
        serializedObject.FindProperty("damage.deformMeshWithCompute").boolValue = true;
        serializedObject.FindProperty("damage.damageDeformCompute").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>(DefaultDamageComputePath);
        serializedObject.FindProperty("damage.computeUseNormals").boolValue = true;
        serializedObject.FindProperty("damage.computeRecalculateNormals").boolValue = true;
        serializedObject.FindProperty("damage.computeTwoLevelDamage").boolValue = true;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(config);
    }

    private VehicleSettings CreateOrUpdateVehicleSettings(string assetPath)
    {
        VehicleSettings asset = AssetDatabase.LoadAssetAtPath<VehicleSettings>(assetPath);
        if (asset == null)
        {
            asset = CreateInstance<VehicleSettings>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        asset.Validate();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private EngineGearboxConfig CreateOrUpdateEngineConfig(string assetPath)
    {
        EngineGearboxConfig asset = AssetDatabase.LoadAssetAtPath<EngineGearboxConfig>(assetPath);
        if (asset == null)
        {
            asset = CreateInstance<EngineGearboxConfig>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        asset.Validate();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private SuspensionConfig CreateOrUpdateSuspensionConfig(string assetPath)
    {
        SuspensionConfig asset = AssetDatabase.LoadAssetAtPath<SuspensionConfig>(assetPath);
        if (asset == null)
        {
            asset = CreateInstance<SuspensionConfig>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        asset.visualWheelHeight = wheelHeight;
        asset.Validate();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private List<PaintConfig> CreateOrUpdatePaintConfigs(string paintsFolderPath)
    {
        PaintDefinition[] defaults =
        {
            new PaintDefinition("White", Color.white),
            new PaintDefinition("Red", new Color(0.73f, 0.08f, 0.08f)),
            new PaintDefinition("Green", new Color(0.10f, 0.25f, 0.14f)),
            new PaintDefinition("Blue", new Color(0.09f, 0.17f, 0.36f)),
            new PaintDefinition("Black", new Color(0.05f, 0.05f, 0.06f))
        };

        List<PaintConfig> paints = new List<PaintConfig>(defaults.Length);
        for (int i = 0; i < defaults.Length; i++)
        {
            string path = $"{paintsFolderPath}/{vehicleId}_Paint_{defaults[i].AssetSuffix}.asset";
            PaintConfig asset = AssetDatabase.LoadAssetAtPath<PaintConfig>(path);
            if (asset == null)
            {
                asset = CreateInstance<PaintConfig>();
                AssetDatabase.CreateAsset(asset, path);
            }

            SerializedObject serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("displayName").stringValue = defaults[i].DisplayName;
            serializedObject.FindProperty("color").colorValue = defaults[i].Color;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            paints.Add(asset);
        }

        return paints;
    }

    private void CreateOrUpdateLoadout(
        string assetPath,
        PlayerCarConfig playerCarConfig,
        VehicleSettings handling,
        EngineGearboxConfig engine,
        SuspensionConfig suspension,
        IReadOnlyList<PaintConfig> paints)
    {
        CarLoadoutConfig asset = AssetDatabase.LoadAssetAtPath<CarLoadoutConfig>(assetPath);
        if (asset == null)
        {
            asset = CreateInstance<CarLoadoutConfig>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        SerializedObject serializedObject = new SerializedObject(asset);
        serializedObject.FindProperty("displayName").stringValue = displayName;
        serializedObject.FindProperty("playerCarConfig").objectReferenceValue = playerCarConfig;
        serializedObject.FindProperty("handlingConfig").objectReferenceValue = handling;
        serializedObject.FindProperty("includeStockBodyOption").boolValue = true;
        serializedObject.FindProperty("defaultBodySetIndex").intValue = -1;
        serializedObject.FindProperty("defaultEngineIndex").intValue = 0;
        serializedObject.FindProperty("defaultSuspensionIndex").intValue = 0;
        serializedObject.FindProperty("defaultPaintIndex").intValue = 0;

        SerializedProperty bodySets = serializedObject.FindProperty("bodySets");
        bodySets.arraySize = 0;

        SerializedProperty engines = serializedObject.FindProperty("engineConfigs");
        engines.arraySize = 1;
        engines.GetArrayElementAtIndex(0).objectReferenceValue = engine;

        SerializedProperty suspensions = serializedObject.FindProperty("suspensionConfigs");
        suspensions.arraySize = 1;
        suspensions.GetArrayElementAtIndex(0).objectReferenceValue = suspension;

        SerializedProperty paintOptions = serializedObject.FindProperty("paintOptions");
        paintOptions.arraySize = paints != null ? paints.Count : 0;
        for (int i = 0; i < paintOptions.arraySize; i++)
            paintOptions.GetArrayElementAtIndex(i).objectReferenceValue = paints[i];

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    private IEnumerable<GameObject> FindBodyPartAssets(string sourcePath)
    {
        List<string> suffixes = ParseSuffixes(setSuffixes);
        List<GameObject> results = new List<GameObject>();
        CollectAssets(sourcePath, "t:Prefab", suffixes, results);
        CollectAssets(sourcePath, "t:Model", suffixes, results);
        return results;
    }

    private void CollectAssets(string sourcePath, string filter, List<string> suffixes, List<GameObject> results)
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
            if (ShouldSkipAssetName(name))
                continue;
            if (!MatchesAnySuffix(name, suffixes) && !(includePartsWithoutSet && !ContainsSetToken(name)))
                continue;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null)
                results.Add(asset);
        }
    }

    private GameObject FindWheelAsset(string sourcePath)
    {
        GameObject byHint = FindFirstAssetContaining(sourcePath, wheelNameHint);
        if (byHint != null)
            return byHint;

        return FindFirstAssetContaining(sourcePath, "wheel");
    }

    private GameObject FindFirstAssetContaining(string sourcePath, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        foreach (string filter in new[] { "t:Prefab", "t:Model" })
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
                if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null)
                    return asset;
            }
        }

        return null;
    }

    private GameObject FindAssetByExactName(string sourcePath, string assetName)
    {
        foreach (string filter in new[] { "t:Prefab", "t:Model" })
        {
            string[] guids = AssetDatabase.FindAssets($"{assetName} {filter}", new[] { sourcePath });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!PathMatches(path, sourcePath))
                    continue;
                if (!includeSubfolders && PathIsInSubfolder(sourcePath, path))
                    continue;

                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!string.Equals(fileName, assetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null)
                    return asset;
            }
        }

        return null;
    }

    private string DetectBaseBodyName(string sourcePath)
    {
        List<string> candidates = new List<string>();
        foreach (string filter in new[] { "t:Prefab", "t:Model" })
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
                if (ShouldSkipAssetName(name))
                    continue;
                if (ContainsSetToken(name))
                    continue;
                if (!candidates.Contains(name))
                    candidates.Add(name);
            }
        }

        string best = string.Empty;
        int bestScore = int.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            string candidate = candidates[i];
            int score = ScoreBaseBodyCandidate(candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private int ScoreBaseBodyCandidate(string name)
    {
        int score = 0;
        if (name.IndexOf("car_", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 10;
        if (name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 4;
        score += CountUnderscores(name);
        score += name.Length / 8;
        return score;
    }

    private bool ShouldSkipAssetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        string[] tokens = name.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].StartsWith("Alt", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (name.IndexOf("_Alt", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (!string.IsNullOrWhiteSpace(excludeNameContains) &&
            name.IndexOf(excludeNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private static bool ContainsSetToken(string name)
    {
        return name.IndexOf("_Set", StringComparison.OrdinalIgnoreCase) >= 0;
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

    private static List<string> ParseSuffixes(string raw)
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

    private static bool PathMatches(string path, string root)
    {
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool PathIsInSubfolder(string root, string path)
    {
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;

        string relative = path.Substring(root.Length).TrimStart('/', '\\');
        return relative.Contains("/") || relative.Contains("\\");
    }

    private static GameObject InstantiateAsset(GameObject asset, Transform parent)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        if (instance == null)
            instance = Instantiate(asset);

        instance.transform.SetParent(parent, false);
        return instance;
    }

    private static void ExtractRenderableChildren(GameObject asset, Transform targetRoot, string sourceAssetName)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        if (instance == null)
            instance = Instantiate(asset);

        try
        {
            Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
            int extractedCount = 0;

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform source = transforms[i];
                if (source == null || source == instance.transform)
                    continue;
                if (!HasRenderable(source.gameObject))
                    continue;

                string extractedName = extractedCount == 0
                    ? sourceAssetName
                    : $"{sourceAssetName}_{source.name}";
                GameObject extracted = new GameObject(extractedName);
                extracted.transform.SetParent(targetRoot, false);
                extracted.transform.localPosition = instance.transform.InverseTransformPoint(source.position);
                extracted.transform.localRotation = Quaternion.Inverse(instance.transform.rotation) * source.rotation;
                extracted.transform.localScale = Vector3.one;

                CopyRenderableComponents(source.gameObject, extracted);
                extractedCount++;
            }

            if (extractedCount == 0)
            {
                GameObject fallback = new GameObject(sourceAssetName);
                fallback.transform.SetParent(targetRoot, false);
                fallback.transform.localScale = Vector3.one;
                CopyRenderableComponents(instance, fallback);
            }
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    private static Transform GetOrCreateVariantRoot(
        Transform root,
        PartImportInfo info,
        Dictionary<string, Transform> variantRoots,
        HashSet<Transform> variantContainers)
    {
        string categoryPath = info.CategoryName;
        Transform categoryRoot = GetOrCreateChild(root, categoryPath);
        Transform variantContainer = categoryRoot;
        if (!string.IsNullOrEmpty(info.SelectorGroupName))
            variantContainer = GetOrCreateChild(categoryRoot, info.SelectorGroupName);

        variantContainers.Add(variantContainer);

        string variantKey = $"{info.CategoryName}/{info.SelectorGroupName}/{info.VariantName}";
        if (variantRoots.TryGetValue(variantKey, out Transform variantRoot))
            return variantRoot;

        variantRoot = GetOrCreateChild(variantContainer, info.VariantName);
        variantRoots[variantKey] = variantRoot;
        return variantRoot;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
            return existing;

        GameObject child = new GameObject(childName);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static void ApplyVariantActivation(HashSet<Transform> variantContainers)
    {
        foreach (Transform container in variantContainers)
        {
            if (container == null)
                continue;

            List<Transform> variants = new List<Transform>();
            for (int i = 0; i < container.childCount; i++)
                variants.Add(container.GetChild(i));

            variants.Sort((a, b) => CompareVariantNames(a.name, b.name));
            for (int i = 0; i < variants.Count; i++)
                variants[i].gameObject.SetActive(i == 0);
        }
    }

    private static int CompareVariantNames(string left, string right)
    {
        bool leftDefault = string.Equals(left, "Default", StringComparison.OrdinalIgnoreCase);
        bool rightDefault = string.Equals(right, "Default", StringComparison.OrdinalIgnoreCase);
        if (leftDefault && !rightDefault)
            return -1;
        if (!leftDefault && rightDefault)
            return 1;
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static PartImportInfo BuildPartImportInfo(string assetName, bool isBaseBody)
    {
        if (isBaseBody)
        {
            return new PartImportInfo
            {
                CategoryName = "Body",
                VariantName = "Default"
            };
        }

        ParseAssetTokens(assetName, out string detailName, out string setName);

        string categoryName = NormalizeCategoryName(detailName);
        string selectorGroup = ResolveSelectorGroup(detailName);
        return new PartImportInfo
        {
            CategoryName = string.IsNullOrEmpty(categoryName) ? "Misc" : categoryName,
            SelectorGroupName = selectorGroup,
            VariantName = string.IsNullOrEmpty(setName) ? "Default" : setName
        };
    }

    private static void ParseAssetTokens(string assetName, out string detailName, out string setName)
    {
        detailName = assetName;
        setName = string.Empty;

        string[] tokens = assetName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        int yearIndex = -1;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Length == 4 && int.TryParse(tokens[i], out _))
            {
                yearIndex = i;
                break;
            }
        }

        if (yearIndex < 0 || yearIndex >= tokens.Length - 1)
            return;

        List<string> detailTokens = new List<string>();
        for (int i = yearIndex + 1; i < tokens.Length; i++)
        {
            if (tokens[i].StartsWith("Set", StringComparison.OrdinalIgnoreCase))
            {
                setName = tokens[i];
                break;
            }

            detailTokens.Add(tokens[i]);
        }

        if (detailTokens.Count > 0)
            detailName = string.Join("_", detailTokens);
    }

    private static string NormalizeCategoryName(string detailName)
    {
        if (string.IsNullOrEmpty(detailName))
            return "Misc";

        if (detailName.StartsWith("Bumper", StringComparison.OrdinalIgnoreCase))
            return "Bumper";
        if (detailName.StartsWith("Mirror", StringComparison.OrdinalIgnoreCase))
            return "Mirror";
        if (detailName.StartsWith("Fender", StringComparison.OrdinalIgnoreCase) ||
            detailName.StartsWith("Fenders", StringComparison.OrdinalIgnoreCase))
            return "Fender";
        if (detailName.StartsWith("Door", StringComparison.OrdinalIgnoreCase))
            return "Door";
        if (detailName.StartsWith("HeadLight", StringComparison.OrdinalIgnoreCase) ||
            detailName.StartsWith("HeadLights", StringComparison.OrdinalIgnoreCase))
            return "HeadLights";
        if (detailName.StartsWith("TailLight", StringComparison.OrdinalIgnoreCase) ||
            detailName.StartsWith("TailLights", StringComparison.OrdinalIgnoreCase))
            return "TailLights";

        return StripDirectionalSuffix(detailName);
    }

    private static string ResolveSelectorGroup(string detailName)
    {
        if (string.IsNullOrEmpty(detailName))
            return null;

        if (MatchesAny(detailName, "BumperF", "BumperChassisF", "FenderFL", "FenderFR", "FendersChassisF"))
            return "Front";
        if (MatchesAny(detailName, "BumperR", "BumperChassisR", "FendersR", "FendersChassisR"))
            return "Rear";
        if (MatchesAny(detailName, "MirrorBaseL", "MirrorL"))
            return "Left";
        if (MatchesAny(detailName, "MirrorBaseR", "MirrorR"))
            return "Right";

        return null;
    }

    private static bool MatchesAny(string value, params string[] options)
    {
        for (int i = 0; i < options.Length; i++)
        {
            if (string.Equals(value, options[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string StripDirectionalSuffix(string detailName)
    {
        string[] suffixes =
        {
            "ChassisF",
            "ChassisR",
            "BaseL",
            "BaseR",
            "FL",
            "FR",
            "RL",
            "RR",
            "F",
            "R",
            "L"
        };

        for (int i = 0; i < suffixes.Length; i++)
        {
            string suffix = suffixes[i];
            if (detailName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && detailName.Length > suffix.Length)
                return detailName.Substring(0, detailName.Length - suffix.Length);
        }

        return detailName;
    }

    private static bool HasRenderable(GameObject gameObject)
    {
        return gameObject.GetComponent<MeshRenderer>() != null ||
               gameObject.GetComponent<SkinnedMeshRenderer>() != null;
    }

    private static void CopyRenderableComponents(GameObject source, GameObject target)
    {
        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter != null)
        {
            MeshFilter targetFilter = target.AddComponent<MeshFilter>();
            targetFilter.sharedMesh = sourceFilter.sharedMesh;
        }

        MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceRenderer != null)
        {
            MeshRenderer targetRenderer = target.AddComponent<MeshRenderer>();
            targetRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            targetRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            targetRenderer.receiveShadows = sourceRenderer.receiveShadows;
            targetRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            targetRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        }

        SkinnedMeshRenderer sourceSkinned = source.GetComponent<SkinnedMeshRenderer>();
        if (sourceSkinned != null)
        {
            SkinnedMeshRenderer targetSkinned = target.AddComponent<SkinnedMeshRenderer>();
            targetSkinned.sharedMesh = sourceSkinned.sharedMesh;
            targetSkinned.sharedMaterials = sourceSkinned.sharedMaterials;
            targetSkinned.shadowCastingMode = sourceSkinned.shadowCastingMode;
            targetSkinned.receiveShadows = sourceSkinned.receiveShadows;
            targetSkinned.lightProbeUsage = sourceSkinned.lightProbeUsage;
            targetSkinned.reflectionProbeUsage = sourceSkinned.reflectionProbeUsage;
            targetSkinned.localBounds = sourceSkinned.localBounds;
        }
    }

    private string ResolveGeneratedAssetPath(string outputPath, string fileName)
    {
        return $"{outputPath}/{fileName}";
    }

    private static string EnsureChildFolder(string parentPath, string childName)
    {
        string folderPath = $"{parentPath}/{childName}";
        if (AssetDatabase.IsValidFolder(folderPath))
            return folderPath;

        AssetDatabase.CreateFolder(parentPath, childName);
        return folderPath;
    }

    private static void EnsureReadableModel(GameObject asset)
    {
        if (asset == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null || importer.isReadable)
            return;

        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    private void ValidateGeneratedSetup(
        string playerCarConfigPath,
        string loadoutConfigPath,
        string bodyPrefabPath,
        string wheelPrefabPath)
    {
        PlayerCarConfig playerCar = AssetDatabase.LoadAssetAtPath<PlayerCarConfig>(playerCarConfigPath);
        CarLoadoutConfig loadout = AssetDatabase.LoadAssetAtPath<CarLoadoutConfig>(loadoutConfigPath);
        GameObject bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPrefabPath);
        GameObject wheelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(wheelPrefabPath);

        if (playerCar == null)
            throw new InvalidOperationException($"Failed to load generated PlayerCarConfig at {playerCarConfigPath}");
        if (bodyPrefab == null || wheelPrefab == null)
            throw new InvalidOperationException("Generated body or wheel prefab could not be loaded back from AssetDatabase.");
        if (playerCar.Visual == null || playerCar.Visual.bodyPrefab == null || playerCar.Visual.wheelPrefab == null)
            throw new InvalidOperationException($"Generated PlayerCarConfig '{playerCarConfigPath}' has missing prefab references.");
        if (loadout == null || loadout.PlayerCarConfig == null)
            throw new InvalidOperationException($"Generated Loadout '{loadoutConfigPath}' has missing PlayerCarConfig reference.");
    }
}
