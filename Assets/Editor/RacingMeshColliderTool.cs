using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RacingMeshColliderTool
{
    private static readonly string[] TargetBaseNames =
    {
        "Road 2",
        "Fence",
        "Asphalt",
        "mini wall",
        "cityDepot",
        "bus_stop2",
        "towtruck",
        "barrier",
        "barrier_2",
        "rioad_bloc",
        "fire_truck",
        "taxi",
        "citybus"
    };

    private const int WarningTrianglesPerCollider = 20000;
    private const int WarningTotalTriangles = 500000;
    private static readonly string[] ColliderExcludeNameTokens = { "glass", "decal" };

    private static readonly Regex DuplicateSuffixRegex = new Regex(@"\s*\(\d+\)$", RegexOptions.Compiled);
    private static readonly Regex CloneSuffixRegex = new Regex(@"\s*\(Clone\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [MenuItem("Tools/Colliders/Racing MeshColliders/Apply To Active Scene")]
    public static void ApplyToActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[RacingMeshColliderTool] Active scene is not loaded.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply Racing MeshColliders");

        HashSet<string> targetKeys = BuildTargetKeySet();
        Dictionary<string, NameStats> perNameStats = CreateNameStatsMap();

        int added = 0;
        int replacedOtherCollider = 0;
        int removedExcludedMeshColliders = 0;
        int alreadyConfigured = 0;
        int skippedNoMeshRoots = 0;
        int errors = 0;
        int meshTargetsProcessed = 0;

        List<RiskItem> riskyItems = new List<RiskItem>();
        HashSet<int> processedMeshTargetIds = new HashSet<int>();
        HashSet<int> processedExcludeCleanupIds = new HashSet<int>();

        foreach (GameObject go in EnumerateSceneGameObjects(scene))
        {
            string baseName = ExtractBaseName(go.name);
            string key = Canonicalize(baseName);
            if (!targetKeys.Contains(key))
            {
                continue;
            }

            if (perNameStats.TryGetValue(key, out NameStats stats))
            {
                stats.Found++;
            }

            removedExcludedMeshColliders += RemoveExcludedMeshColliders(go, go.transform, targetKeys, processedExcludeCleanupIds);
            List<MeshTarget> meshTargets = GetMeshTargets(go, targetKeys);
            if (meshTargets.Count == 0)
            {
                skippedNoMeshRoots++;
                if (perNameStats.TryGetValue(key, out NameStats noMeshStats))
                {
                    noMeshStats.SkippedNoMesh++;
                }
                continue;
            }

            foreach (MeshTarget target in meshTargets)
            {
                if (!processedMeshTargetIds.Add(target.GameObject.GetInstanceID()))
                {
                    continue;
                }

                meshTargetsProcessed++;
                if (perNameStats.TryGetValue(key, out NameStats processedStats))
                {
                    processedStats.MeshTargets++;
                }

                try
                {
                    Collider[] allColliders = target.GameObject.GetComponents<Collider>();
                    MeshCollider meshCollider = target.GameObject.GetComponent<MeshCollider>();

                    foreach (Collider collider in allColliders)
                    {
                        if (collider is MeshCollider)
                        {
                            continue;
                        }

                        Undo.DestroyObjectImmediate(collider);
                        replacedOtherCollider++;
                        if (perNameStats.TryGetValue(key, out NameStats replacedStats))
                        {
                            replacedStats.ReplacedOtherCollider++;
                        }
                    }

                    bool created = false;
                    if (meshCollider == null)
                    {
                        meshCollider = Undo.AddComponent<MeshCollider>(target.GameObject);
                        created = true;
                        added++;
                        if (perNameStats.TryGetValue(key, out NameStats addStats))
                        {
                            addStats.Added++;
                        }
                    }

                    Undo.RecordObject(meshCollider, "Configure MeshCollider");
                    meshCollider.sharedMesh = target.Mesh;
                    meshCollider.convex = false;

                    if (!target.GameObject.isStatic)
                    {
                        Undo.RecordObject(target.GameObject, "Mark Static");
                        target.GameObject.isStatic = true;
                    }

                    if (!created)
                    {
                        alreadyConfigured++;
                        if (perNameStats.TryGetValue(key, out NameStats configuredStats))
                        {
                            configuredStats.AlreadyOrUpdated++;
                        }
                    }

                    int triangles = GetTriangleCount(target.Mesh);
                    if (triangles >= WarningTrianglesPerCollider)
                    {
                        riskyItems.Add(new RiskItem(target.GameObject, triangles));
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    if (perNameStats.TryGetValue(key, out NameStats errStats))
                    {
                        errStats.Errors++;
                    }

                    Debug.LogError($"[RacingMeshColliderTool] Failed on '{GetHierarchyPath(target.GameObject)}': {ex.Message}", target.GameObject);
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);

        StringBuilder summary = BuildSummary(
            scene,
            perNameStats,
            added,
            replacedOtherCollider,
            removedExcludedMeshColliders,
            alreadyConfigured,
            skippedNoMeshRoots,
            errors,
            meshTargetsProcessed,
            riskyItems);

        Debug.Log(summary.ToString());
    }

    [MenuItem("Tools/Colliders/Racing MeshColliders/Validate Active Scene")]
    public static void ValidateActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[RacingMeshColliderTool] Active scene is not loaded.");
            return;
        }

        HashSet<string> targetKeys = BuildTargetKeySet();
        Dictionary<string, NameStats> perNameStats = CreateNameStatsMap();

        int valid = 0;
        int missingCollider = 0;
        int missingMeshReference = 0;
        int meshTargetsExpected = 0;
        int skippedNoMeshRoots = 0;
        int totalTriangles = 0;

        List<RiskItem> riskyItems = new List<RiskItem>();
        HashSet<int> validatedMeshTargetIds = new HashSet<int>();

        foreach (GameObject go in EnumerateSceneGameObjects(scene))
        {
            string baseName = ExtractBaseName(go.name);
            string key = Canonicalize(baseName);
            if (!targetKeys.Contains(key))
            {
                continue;
            }

            if (perNameStats.TryGetValue(key, out NameStats stats))
            {
                stats.Found++;
            }

            List<MeshTarget> meshTargets = GetMeshTargets(go, targetKeys);
            if (meshTargets.Count == 0)
            {
                skippedNoMeshRoots++;
                continue;
            }

            foreach (MeshTarget target in meshTargets)
            {
                if (!validatedMeshTargetIds.Add(target.GameObject.GetInstanceID()))
                {
                    continue;
                }

                meshTargetsExpected++;
                if (perNameStats.TryGetValue(key, out NameStats expectedStats))
                {
                    expectedStats.MeshTargets++;
                }

                MeshCollider meshCollider = target.GameObject.GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    missingCollider++;
                    if (perNameStats.TryGetValue(key, out NameStats missColStats))
                    {
                        missColStats.MissingCollider++;
                    }
                    continue;
                }

                if (meshCollider.sharedMesh == null)
                {
                    missingMeshReference++;
                    if (perNameStats.TryGetValue(key, out NameStats missMeshStats))
                    {
                        missMeshStats.MissingMeshRef++;
                    }
                    continue;
                }

                valid++;
                if (perNameStats.TryGetValue(key, out NameStats validStats))
                {
                    validStats.Valid++;
                }

                int triangles = GetTriangleCount(meshCollider.sharedMesh);
                totalTriangles += triangles;
                if (triangles >= WarningTrianglesPerCollider)
                {
                    riskyItems.Add(new RiskItem(target.GameObject, triangles));
                }
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[RacingMeshColliderTool] Validation Report");
        sb.AppendLine($"Scene: {scene.path}");
        sb.AppendLine($"Found target objects: {perNameStats.Values.Sum(x => x.Found)}");
        sb.AppendLine($"Expected mesh targets: {meshTargetsExpected}");
        sb.AppendLine($"Valid MeshColliders: {valid}");
        sb.AppendLine($"Missing MeshCollider: {missingCollider}");
        sb.AppendLine($"MeshCollider without mesh: {missingMeshReference}");
        sb.AppendLine($"Skipped roots without mesh in subtree: {skippedNoMeshRoots}");
        sb.AppendLine($"Total collider triangles (approx): {totalTriangles}");
        sb.AppendLine();

        sb.AppendLine("Per name:");
        foreach (string baseName in TargetBaseNames)
        {
            string key = Canonicalize(baseName);
            NameStats stats = perNameStats[key];
            sb.AppendLine($"- {baseName}: found={stats.Found}, meshTargets={stats.MeshTargets}, valid={stats.Valid}, missingCollider={stats.MissingCollider}, missingMeshRef={stats.MissingMeshRef}");
        }

        if (totalTriangles >= WarningTotalTriangles)
        {
            sb.AppendLine();
            sb.AppendLine($"Performance warning: total triangle budget is high ({totalTriangles} >= {WarningTotalTriangles}).");
        }

        if (riskyItems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Heavy colliders (>= {WarningTrianglesPerCollider} triangles):");
            foreach (RiskItem item in riskyItems.OrderByDescending(x => x.Triangles).Take(20))
            {
                sb.AppendLine($"- {GetHierarchyPath(item.GameObject)}: {item.Triangles}");
            }
        }

        Debug.Log(sb.ToString());
    }

    private static StringBuilder BuildSummary(
        Scene scene,
        Dictionary<string, NameStats> perNameStats,
        int added,
        int replacedOtherCollider,
        int removedExcludedMeshColliders,
        int alreadyConfigured,
        int skippedNoMeshRoots,
        int errors,
        int meshTargetsProcessed,
        List<RiskItem> riskyItems)
    {
        int foundTotal = perNameStats.Values.Sum(x => x.Found);
        int totalApproxTriangles = riskyItems.Sum(x => x.Triangles);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[RacingMeshColliderTool] Apply Report");
        sb.AppendLine($"Scene: {scene.path}");
        sb.AppendLine($"Found target objects: {foundTotal}");
        sb.AppendLine($"Mesh targets processed: {meshTargetsProcessed}");
        sb.AppendLine($"MeshColliders added: {added}");
        sb.AppendLine($"Other colliders replaced: {replacedOtherCollider}");
        sb.AppendLine($"Excluded MeshColliders removed: {removedExcludedMeshColliders}");
        sb.AppendLine($"Existing MeshColliders updated: {alreadyConfigured}");
        sb.AppendLine($"Skipped roots (no mesh in subtree): {skippedNoMeshRoots}");
        sb.AppendLine($"Errors: {errors}");
        sb.AppendLine();

        sb.AppendLine("Per name:");
        foreach (string baseName in TargetBaseNames)
        {
            string key = Canonicalize(baseName);
            NameStats stats = perNameStats[key];
            sb.AppendLine($"- {baseName}: found={stats.Found}, meshTargets={stats.MeshTargets}, added={stats.Added}, updated={stats.AlreadyOrUpdated}, replacedOther={stats.ReplacedOtherCollider}, noMesh={stats.SkippedNoMesh}, errors={stats.Errors}");
        }

        if (riskyItems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Performance caution: {riskyItems.Count} colliders are heavy (>= {WarningTrianglesPerCollider} triangles). Top entries:");
            foreach (RiskItem item in riskyItems.OrderByDescending(x => x.Triangles).Take(20))
            {
                sb.AppendLine($"- {GetHierarchyPath(item.GameObject)}: {item.Triangles}");
            }
            sb.AppendLine($"Approx triangles in heavy subset only: {totalApproxTriangles}");
        }

        return sb;
    }

    private static Dictionary<string, NameStats> CreateNameStatsMap()
    {
        Dictionary<string, NameStats> map = new Dictionary<string, NameStats>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in TargetBaseNames)
        {
            map[Canonicalize(name)] = new NameStats();
        }

        return map;
    }

    private static HashSet<string> BuildTargetKeySet()
    {
        HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in TargetBaseNames)
        {
            set.Add(Canonicalize(name));
        }

        return set;
    }

    private static IEnumerable<GameObject> EnumerateSceneGameObjects(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                yield return t.gameObject;
            }
        }
    }

    private static string ExtractBaseName(string name)
    {
        string value = name.Trim();
        value = CloneSuffixRegex.Replace(value, string.Empty).Trim();
        value = DuplicateSuffixRegex.Replace(value, string.Empty).Trim();
        return value;
    }

    private static string Canonicalize(string name)
    {
        string value = ExtractBaseName(name).ToLowerInvariant();
        value = value.Replace(" ", string.Empty);
        value = value.Replace("_", string.Empty);
        value = value.Replace("-", string.Empty);
        return value;
    }

    private static List<MeshTarget> GetMeshTargets(GameObject root, HashSet<string> targetKeys)
    {
        List<MeshTarget> result = new List<MeshTarget>();
        CollectMeshTargetsRecursive(root.transform, root.transform, targetKeys, result);

        return result;
    }

    private static void CollectMeshTargetsRecursive(
        Transform current,
        Transform root,
        HashSet<string> targetKeys,
        List<MeshTarget> result)
    {
        GameObject go = current.gameObject;
        bool isRoot = current == root;
        if (!isRoot)
        {
            string key = Canonicalize(go.name);
            if (targetKeys.Contains(key))
            {
                return;
            }
        }

        MeshFilter meshFilter = go.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null && !ShouldExcludeMesh(go, meshFilter.sharedMesh))
        {
            result.Add(new MeshTarget(go, meshFilter.sharedMesh));
        }
        else
        {
            SkinnedMeshRenderer skinnedMesh = go.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMesh != null && skinnedMesh.sharedMesh != null && !ShouldExcludeMesh(go, skinnedMesh.sharedMesh))
            {
                result.Add(new MeshTarget(go, skinnedMesh.sharedMesh));
            }
        }

        foreach (Transform child in current)
        {
            CollectMeshTargetsRecursive(child, root, targetKeys, result);
        }
    }

    private static bool ShouldExcludeMesh(GameObject go, Mesh mesh)
    {
        string text = (go.name + " " + mesh.name).ToLowerInvariant();
        foreach (string token in ColliderExcludeNameTokens)
        {
            if (text.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private static int RemoveExcludedMeshColliders(
        GameObject rootObject,
        Transform current,
        HashSet<string> targetKeys,
        HashSet<int> processedIds)
    {
        bool isRoot = current == rootObject.transform;
        GameObject go = current.gameObject;
        if (!isRoot)
        {
            string key = Canonicalize(go.name);
            if (targetKeys.Contains(key))
            {
                return 0;
            }
        }

        int removed = 0;
        if (processedIds.Add(go.GetInstanceID()))
        {
            Mesh mesh = null;
            MeshFilter meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                mesh = meshFilter.sharedMesh;
            }
            else
            {
                SkinnedMeshRenderer skinned = go.GetComponent<SkinnedMeshRenderer>();
                if (skinned != null)
                {
                    mesh = skinned.sharedMesh;
                }
            }

            if (mesh != null && ShouldExcludeMesh(go, mesh))
            {
                MeshCollider collider = go.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    Undo.DestroyObjectImmediate(collider);
                    removed++;
                }
            }
        }

        foreach (Transform child in current)
        {
            removed += RemoveExcludedMeshColliders(rootObject, child, targetKeys, processedIds);
        }

        return removed;
    }

    private static int GetTriangleCount(Mesh mesh)
    {
        if (mesh == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            count += (int)mesh.GetIndexCount(i) / 3;
        }

        return count;
    }

    private static string GetHierarchyPath(GameObject go)
    {
        List<string> segments = new List<string>();
        Transform current = go.transform;
        while (current != null)
        {
            segments.Add(current.name);
            current = current.parent;
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    private sealed class NameStats
    {
        public int Found;
        public int MeshTargets;
        public int Added;
        public int AlreadyOrUpdated;
        public int ReplacedOtherCollider;
        public int SkippedNoMesh;
        public int Errors;
        public int Valid;
        public int MissingCollider;
        public int MissingMeshRef;
    }

    private readonly struct RiskItem
    {
        public RiskItem(GameObject gameObject, int triangles)
        {
            GameObject = gameObject;
            Triangles = triangles;
        }

        public GameObject GameObject { get; }
        public int Triangles { get; }
    }

    private readonly struct MeshTarget
    {
        public MeshTarget(GameObject gameObject, Mesh mesh)
        {
            GameObject = gameObject;
            Mesh = mesh;
        }

        public GameObject GameObject { get; }
        public Mesh Mesh { get; }
    }
}
