using Barmetler.RoadSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RoadSystemColliderBootstrap
{
    static RoadSystemColliderBootstrap()
    {
        EditorApplication.delayCall += EnsureInOpenScenes;
        EditorApplication.hierarchyChanged += EnsureInOpenScenes;
        EditorSceneManager.sceneOpened += (_, _) => EnsureInOpenScenes();
    }

    [MenuItem("Tools/RoadSystem/Ensure Mesh Colliders", priority = 51)]
    private static void EnsureMenu()
    {
        EnsureInOpenScenes();
    }

    private static void EnsureInOpenScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            bool changed = false;
            changed |= EnsureForRoots<Road>(scene);
            changed |= EnsureForRoots<Intersection>(scene);

            if (changed)
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    private static bool EnsureForRoots<T>(Scene scene) where T : Component
    {
        bool changed = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T[] objects = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < objects.Length; i++)
                changed |= EnsureForHierarchy(objects[i]);
        }

        return changed;
    }

    private static bool EnsureForHierarchy(Component root)
    {
        if (root == null)
            return false;

        bool changed = false;
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter == null)
                continue;

            GameObject target = meshFilter.gameObject;
            MeshCollider meshCollider = target.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = Undo.AddComponent<MeshCollider>(target);
                meshCollider.convex = false;
                changed = true;
            }

            RoadSystemMeshColliderSync sync = target.GetComponent<RoadSystemMeshColliderSync>();
            if (sync == null)
            {
                sync = Undo.AddComponent<RoadSystemMeshColliderSync>(target);
                changed = true;
            }

            sync.Sync();
        }

        return changed;
    }
}
