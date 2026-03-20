using Barmetler.RoadSystem;
using Barmetler.RoadSystem.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BRSRoad = Barmetler.RoadSystem.Road;
using BRSRoadMeshGenerator = Barmetler.RoadSystem.RoadMeshGenerator;
using BRSRoadSystem = Barmetler.RoadSystem.RoadSystem;
using BRSRoadDirection = Barmetler.RoadSystem.RoadDirection;

public static class RoadSystemSceneBootstrap
{
    private const string TargetScenePath = "Assets/Scenes/Game.unity";
    private const string RootName = "RoadSystem_AutoCreated";
    private const string RoadName = "Road_AutoCreated";

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.update -= TryCreateRoadInActiveScene;
        EditorApplication.update += TryCreateRoadInActiveScene;
    }

    [MenuItem("Tools/RoadSystem/Create Demo Road In Active Scene", priority = 50)]
    private static void CreateDemoRoadMenu()
    {
        CreateRoadInActiveScene(force: true);
    }

    private static void TryCreateRoadInActiveScene()
    {
        if (CreateRoadInActiveScene(force: false))
            EditorApplication.update -= TryCreateRoadInActiveScene;
    }

    private static bool CreateRoadInActiveScene(bool force)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
            return false;

        if (!force && activeScene.path != TargetScenePath)
            return false;

        if (!force && GameObject.Find(RootName) != null)
            return true;

        GameObject sourceMeshAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/RoadSystemSamples/Models/Straight.fbx");
        Material roadMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/RoadSystemSamples/Materials/RS_Road_mat.mat");
        Material sidewalkMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/RoadSystemSamples/Materials/RS_Sidewalk_mat.mat");

        if (sourceMeshAsset == null || roadMaterial == null || sidewalkMaterial == null)
        {
            Debug.LogError("RoadSystem bootstrap: package sample assets not found.");
            return true;
        }

        MeshFilter sourceMeshFilter = sourceMeshAsset.GetComponentInChildren<MeshFilter>();
        if (sourceMeshFilter == null)
        {
            Debug.LogError("RoadSystem bootstrap: source mesh filter not found in Straight.fbx.");
            return true;
        }

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Road System");
        SceneManager.MoveGameObjectToScene(root, activeScene);

        BRSRoadSystem roadSystem = root.AddComponent<BRSRoadSystem>();
        root.transform.position = Vector3.zero;

        GameObject roadObject = new GameObject(RoadName);
        Undo.RegisterCreatedObjectUndo(roadObject, "Create Road");
        roadObject.transform.SetParent(root.transform, false);

        MeshFilter meshFilter = roadObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = roadObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = roadObject.AddComponent<MeshCollider>();
        BRSRoad road = roadObject.AddComponent<BRSRoad>();
        BRSRoadMeshGenerator meshGenerator = roadObject.AddComponent<BRSRoadMeshGenerator>();

        meshRenderer.sharedMaterials = new[] { roadMaterial, sidewalkMaterial };
        meshCollider.convex = false;

        road.direction = BRSRoadDirection.Bidirectional;
        road.OnValidate();
        road.MovePoint(0, new Vector3(-20.0f, 0.0f, 0.0f));
        road.MovePoint(1, new Vector3(-8.0f, 0.0f, 0.0f));
        road.MovePoint(2, new Vector3(8.0f, 0.0f, 0.0f));
        road.MovePoint(3, new Vector3(20.0f, 0.0f, 0.0f));
        road.MoveNormal(0, Vector3.up);
        road.MoveNormal(1, Vector3.up);

        meshGenerator.settings = new BRSRoadMeshGenerator.RoadMeshSettings
        {
            SourceOrientation = MeshConversion.MeshOrientation.Presets["BLENDER"],
            uvOffset = Vector2.up
        };
        meshGenerator.SourceMesh = sourceMeshFilter;
        meshGenerator.AutoGenerate = true;

        roadSystem.RebuildAllRoads();

        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Selection.activeGameObject = roadObject;
        Debug.Log($"RoadSystem bootstrap: created {RoadName} in {activeScene.path}.");
        return true;
    }
}
