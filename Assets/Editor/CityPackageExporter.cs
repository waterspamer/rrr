using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class CityPackageExporter
{
    private const string CityRootName = "City";
    private const string CityRootFolder = "Assets/City";
    private const string PrefabFolder = CityRootFolder + "/Prefabs";
    private const string ExportFolder = "Exports";
    private const string PrefabPath = PrefabFolder + "/City.prefab";

    [MenuItem("Tools/City/Build Prefab + Export UnityPackage")]
    public static void BuildAndExport()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[CityPackageExporter] Active scene is not loaded.");
            return;
        }

        GameObject cityRoot = FindCityRoot(scene);
        if (cityRoot == null)
        {
            Debug.LogError("[CityPackageExporter] Root object 'City' was not found in active scene.");
            return;
        }

        if (cityRoot.transform.childCount == 0)
        {
            Debug.LogError("[CityPackageExporter] 'City' has no children. Nothing to export.");
            return;
        }

        EnsureFolder("Assets", "City");
        EnsureFolder(CityRootFolder, "Prefabs");

        string warningReport = BuildCityContentWarnings(cityRoot);
        if (!string.IsNullOrEmpty(warningReport))
        {
            Debug.LogWarning(warningReport, cityRoot);
        }

        bool prefabOk;
        PrefabUtility.SaveAsPrefabAssetAndConnect(cityRoot, PrefabPath, InteractionMode.UserAction, out prefabOk);
        if (!prefabOk)
        {
            Debug.LogError("[CityPackageExporter] Failed to save prefab.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string[] deps = AssetDatabase.GetDependencies(PrefabPath, true)
            .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (deps.Length == 0)
        {
            Debug.LogError("[CityPackageExporter] No dependencies found for prefab export.");
            return;
        }

        string projectRoot = Directory.GetCurrentDirectory();
        string exportDirAbsolute = Path.Combine(projectRoot, ExportFolder);
        Directory.CreateDirectory(exportDirAbsolute);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string packageAbsolutePath = Path.Combine(exportDirAbsolute, $"City_{stamp}.unitypackage");

        AssetDatabase.ExportPackage(
            deps,
            packageAbsolutePath,
            ExportPackageOptions.Default);

        Debug.Log(
            "[CityPackageExporter] Export completed\n" +
            $"Scene: {scene.path}\n" +
            $"City root: {GetHierarchyPath(cityRoot)}\n" +
            $"Prefab: {PrefabPath}\n" +
            $"Dependencies: {deps.Length}\n" +
            $"Package: {packageAbsolutePath}");
    }

    [MenuItem("Tools/City/Validate City Root")]
    public static void ValidateCityRoot()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[CityPackageExporter] Active scene is not loaded.");
            return;
        }

        GameObject cityRoot = FindCityRoot(scene);
        if (cityRoot == null)
        {
            Debug.LogError("[CityPackageExporter] Root object 'City' was not found.");
            return;
        }

        string warnings = BuildCityContentWarnings(cityRoot);
        if (string.IsNullOrEmpty(warnings))
        {
            Debug.Log($"[CityPackageExporter] City root looks good: {GetHierarchyPath(cityRoot)}");
        }
        else
        {
            Debug.LogWarning(warnings, cityRoot);
        }
    }

    private static GameObject FindCityRoot(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (string.Equals(root.name, CityRootName, StringComparison.Ordinal))
            {
                return root;
            }
        }

        return null;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string combined = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(combined))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static string BuildCityContentWarnings(GameObject cityRoot)
    {
        int lights = 0;
        int cameras = 0;
        int volumes = 0;

        foreach (Transform t in cityRoot.GetComponentsInChildren<Transform>(true))
        {
            GameObject go = t.gameObject;
            if (go.GetComponent<Light>() != null)
            {
                lights++;
            }

            if (go.GetComponent<Camera>() != null)
            {
                cameras++;
            }

            if (go.GetComponent<Volume>() != null)
            {
                volumes++;
            }
        }

        if (lights == 0 && cameras == 0 && volumes == 0)
        {
            return string.Empty;
        }

        return "[CityPackageExporter] Validation warnings for City root\n" +
               $"Lights inside City: {lights}\n" +
               $"Cameras inside City: {cameras}\n" +
               $"Volumes inside City: {volumes}\n" +
               "If these are global scene systems, move them outside City before final export.";
    }

    private static string GetHierarchyPath(GameObject go)
    {
        List<string> parts = new List<string>();
        Transform current = go.transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }
}