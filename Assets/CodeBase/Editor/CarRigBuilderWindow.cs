using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class CarRigBuilderWindow : EditorWindow
{
    [SerializeField] private DefaultAsset sourceFolder;
    [SerializeField] private DefaultAsset outputFolder;
    [SerializeField] private bool includeSubfolders = true;

    [SerializeField] private string baseBodyName = "Car_Buick_GrandNational_1987";
    [SerializeField] private string setSuffix = "_SetA";
    [SerializeField] private string wheelSourceName = "WheelFL_SetA";
    [SerializeField] private bool autoFindWheelSource = true;
    [SerializeField] private Mesh wheelMesh;
    [SerializeField] private Material wheelMaterial;

    [SerializeField] private Vector3 frontLeftPos = new Vector3(-0.75f, 0.35f, 1.2f);
    [SerializeField] private Vector3 frontRightPos = new Vector3(0.75f, 0.35f, 1.2f);
    [SerializeField] private Vector3 rearLeftPos = new Vector3(-0.75f, 0.35f, -1.2f);
    [SerializeField] private Vector3 rearRightPos = new Vector3(0.75f, 0.35f, -1.2f);

    [SerializeField] private bool reparentWheelVisuals;
    [SerializeField] private Vector3 wheelColliderOffset = Vector3.zero;

    private GameObject previewRoot;
    private CarRig previewRig;
    private string lastReport = string.Empty;

    [MenuItem("Tools/Car Rig Builder")]
    public static void Open()
    {
        GetWindow<CarRigBuilderWindow>("Car Rig Builder");
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
        EditorGUILayout.LabelField("Names", EditorStyles.boldLabel);
        baseBodyName = EditorGUILayout.TextField("Base Body Name", baseBodyName);
        setSuffix = EditorGUILayout.TextField("Set Suffix", setSuffix);
        wheelSourceName = EditorGUILayout.TextField("Wheel Source", wheelSourceName);
        autoFindWheelSource = EditorGUILayout.Toggle("Auto Find Wheel Source", autoFindWheelSource);
        wheelMesh = (Mesh)EditorGUILayout.ObjectField("Wheel Mesh", wheelMesh, typeof(Mesh), false);
        wheelMaterial = (Material)EditorGUILayout.ObjectField("Wheel Material", wheelMaterial, typeof(Material), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Wheel Positions (Local)", EditorStyles.boldLabel);
        frontLeftPos = EditorGUILayout.Vector3Field("Front Left", frontLeftPos);
        frontRightPos = EditorGUILayout.Vector3Field("Front Right", frontRightPos);
        rearLeftPos = EditorGUILayout.Vector3Field("Rear Left", rearLeftPos);
        rearRightPos = EditorGUILayout.Vector3Field("Rear Right", rearRightPos);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rig Options", EditorStyles.boldLabel);
        reparentWheelVisuals = EditorGUILayout.Toggle("Reparent Wheel Visuals", reparentWheelVisuals);
        wheelColliderOffset = EditorGUILayout.Vector3Field("Wheel Collider Offset", wheelColliderOffset);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!CanBuild()))
            {
                if (GUILayout.Button("Create Preview"))
                    CreatePreview();
            }

            if (GUILayout.Button("Pull Wheel Positions"))
                PullWheelPositionsFromPreview();

            if (GUILayout.Button("Clear Preview"))
                DestroyPreview();
        }

        using (new EditorGUI.DisabledScope(!CanBuild()))
        {
            if (GUILayout.Button("Build CarRig Prefab"))
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
        previewRig = previewRoot.GetComponent<CarRig>();
        Selection.activeGameObject = previewRoot;
        SceneView.lastActiveSceneView?.FrameSelected();
        lastReport = "Preview created in scene. Move wheels, then click Pull Wheel Positions.";
    }

    private void DestroyPreview()
    {
        if (previewRoot != null)
        {
            DestroyImmediate(previewRoot);
            previewRoot = null;
            previewRig = null;
        }
    }

    private void PullWheelPositionsFromPreview()
    {
        if (previewRig == null)
            return;

        frontLeftPos = previewRig.FrontLeft != null ? previewRig.FrontLeft.localPosition : frontLeftPos;
        frontRightPos = previewRig.FrontRight != null ? previewRig.FrontRight.localPosition : frontRightPos;
        rearLeftPos = previewRig.RearLeft != null ? previewRig.RearLeft.localPosition : rearLeftPos;
        rearRightPos = previewRig.RearRight != null ? previewRig.RearRight.localPosition : rearRightPos;
        Repaint();
    }

    private void BuildPrefab()
    {
        if (!TryBuildInstance(out GameObject instance, out string error))
        {
            lastReport = error;
            return;
        }

        string outputPath = AssetDatabase.GetAssetPath(outputFolder);
        string prefabName = baseBodyName + "_CarRig.prefab";
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

        GameObject baseBodyAsset = FindAssetByName(sourcePath, baseBodyName);
        if (baseBodyAsset == null)
        {
            error = $"Base body not found: {baseBodyName}";
            return false;
        }

        GameObject wheelSourceAsset = null;
        if (wheelMesh == null)
        {
            wheelSourceAsset = FindAssetByName(sourcePath, wheelSourceName);
            if (wheelSourceAsset == null && autoFindWheelSource)
                wheelSourceAsset = FindWheelAssetByHint(sourcePath);
            if (wheelSourceAsset == null)
            {
                error = $"Wheel source not found: {wheelSourceName}";
                return false;
            }
        }
        else if (wheelMaterial == null)
        {
            wheelMaterial = FindMaterialForMesh(wheelMesh);
        }

        instance = new GameObject(baseBodyName + "_Rig");
        GameObject body = InstantiateAsset(baseBodyAsset, instance.transform);
        body.name = baseBodyAsset.name;

        List<GameObject> setParts = FindAssetsBySuffix(sourcePath, setSuffix);
        foreach (GameObject partAsset in setParts)
        {
            if (partAsset == null)
                continue;

            string partName = partAsset.name;
            if (string.Equals(partName, baseBodyAsset.name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(partName, wheelSourceAsset.name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (partName.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            GameObject partInstance = InstantiateAsset(partAsset, instance.transform);
            partInstance.name = partName;
        }

        GameObject wheelsRoot = new GameObject("Wheels");
        wheelsRoot.transform.SetParent(instance.transform, false);

        Transform fl = CreateWheelInstance(wheelSourceAsset, wheelsRoot.transform, "WheelFL", frontLeftPos);
        Transform fr = CreateWheelInstance(wheelSourceAsset, wheelsRoot.transform, "WheelFR", frontRightPos);
        Transform rl = CreateWheelInstance(wheelSourceAsset, wheelsRoot.transform, "WheelRL", rearLeftPos);
        Transform rr = CreateWheelInstance(wheelSourceAsset, wheelsRoot.transform, "WheelRR", rearRightPos);

        CarRig rig = instance.AddComponent<CarRig>();
        AssignRig(rig, body.transform, fl, fr, rl, rr);

        return true;
    }

    private GameObject FindAssetByName(string sourcePath, string assetName)
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
            if (!includeSubfolders && PathIsInSubfolder(sourcePath, path))
                continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(fileName, assetName, StringComparison.OrdinalIgnoreCase))
                continue;

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        return null;
    }

    private List<GameObject> FindAssetsBySuffix(string sourcePath, string suffix)
    {
        List<GameObject> assets = new List<GameObject>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { sourcePath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!PathMatches(path, sourcePath))
                continue;
            if (!includeSubfolders && PathIsInSubfolder(sourcePath, path))
                continue;

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null)
                assets.Add(asset);
        }

        guids = AssetDatabase.FindAssets("t:Model", new[] { sourcePath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!PathMatches(path, sourcePath))
                continue;
            if (!includeSubfolders && PathIsInSubfolder(sourcePath, path))
                continue;

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null)
                assets.Add(asset);
        }

        return assets;
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

    private Transform CreateWheelInstance(GameObject wheelAsset, Transform parent, string name, Vector3 localPos)
    {
        GameObject instance = wheelMesh != null
            ? CreateWheelFromMesh(parent)
            : InstantiateAsset(wheelAsset, parent);

        instance.name = name;
        instance.transform.localPosition = localPos;
        return instance.transform;
    }

    private void AssignRig(CarRig rig, Transform body, Transform fl, Transform fr, Transform rl, Transform rr)
    {
        SerializedObject so = new SerializedObject(rig);
        so.FindProperty("bodyRoot").objectReferenceValue = body;
        so.FindProperty("frontLeft").objectReferenceValue = fl;
        so.FindProperty("frontRight").objectReferenceValue = fr;
        so.FindProperty("rearLeft").objectReferenceValue = rl;
        so.FindProperty("rearRight").objectReferenceValue = rr;
        so.FindProperty("reparentWheelVisuals").boolValue = reparentWheelVisuals;
        so.FindProperty("wheelColliderOffset").vector3Value = wheelColliderOffset;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private GameObject CreateWheelFromMesh(Transform parent)
    {
        GameObject wheel = new GameObject("WheelMesh");
        wheel.transform.SetParent(parent, false);

        MeshFilter filter = wheel.AddComponent<MeshFilter>();
        filter.sharedMesh = wheelMesh;

        MeshRenderer renderer = wheel.AddComponent<MeshRenderer>();
        if (wheelMaterial != null)
            renderer.sharedMaterial = wheelMaterial;

        return wheel;
    }

    private GameObject FindWheelAssetByHint(string sourcePath)
    {
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { sourcePath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!PathMatches(path, sourcePath))
                continue;
            if (!includeSubfolders && PathIsInSubfolder(sourcePath, path))
                continue;

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null)
                return asset;
        }

        return null;
    }

    private Material FindMaterialForMesh(Mesh mesh)
    {
        string path = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(path))
            return null;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Material material)
                return material;
        }

        return null;
    }
}
