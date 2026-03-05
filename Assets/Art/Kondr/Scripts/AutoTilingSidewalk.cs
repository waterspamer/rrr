using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class AutoTilingSidewalk : MonoBehaviour
{
    [Min(0.01f)] public float tilesPerUnit = 6f;

    Renderer rend;
    MaterialPropertyBlock block;

    Vector3 lastScale;

    void OnEnable()
    {
        rend = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        lastScale = Vector3.zero;
        Apply();
    }

    void Update()
    {
        // В Edit Mode и в Play: если scale поменялся, пересчитать
        if (transform.lossyScale != lastScale)
            Apply();
    }

    void OnValidate()
    {
        if (rend == null) rend = GetComponent<Renderer>();
        if (block == null) block = new MaterialPropertyBlock();
        Apply();
    }

    void Apply()
    {
        lastScale = transform.lossyScale;

        float x = lastScale.x * tilesPerUnit;
        float z = lastScale.z * tilesPerUnit;

        rend.GetPropertyBlock(block);

        // URP/Lit
        if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_BaseMap"))
            block.SetVector("_BaseMap_ST", new Vector4(x, z, 0, 0));
        // Built-in/Standard
        else
            block.SetVector("_MainTex_ST", new Vector4(x, z, 0, 0));

        rend.SetPropertyBlock(block);
    }
}