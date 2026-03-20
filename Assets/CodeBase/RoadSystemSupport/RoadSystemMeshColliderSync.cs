using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class RoadSystemMeshColliderSync : MonoBehaviour
{
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private Mesh lastMesh;

    private void OnEnable()
    {
        CacheComponents();
        Sync();
    }

    private void OnValidate()
    {
        CacheComponents();
        Sync();
    }

    private void LateUpdate()
    {
        if (!enabled)
            return;

        CacheComponents();
        if (meshFilter == null || meshCollider == null)
            return;

        if (meshFilter.sharedMesh != lastMesh)
            Sync();
    }

    public void Sync()
    {
        CacheComponents();
        if (meshFilter == null || meshCollider == null)
            return;

        lastMesh = meshFilter.sharedMesh;
        meshCollider.convex = false;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = lastMesh;
    }

    private void CacheComponents()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();
        if (meshCollider == null)
            meshCollider = GetComponent<MeshCollider>();
    }
}
