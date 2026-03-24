using System;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CarDamageController
{
    private void ApplyComputeDeformation()
    {
        if (!deformMeshWithCompute || damageDeformCompute == null || deformTargets == null || !hasBounds || runtimeTexture == null)
            return;

        int kernel;
        try
        {
            kernel = damageDeformCompute.FindKernel("Deform");
        }
        catch (Exception ex)
        {
            deformMeshWithCompute = false;
            damageDeformCompute = null;
            Debug.LogWarning($"CarDamageController: compute deformation was disabled because the 'Deform' kernel is unavailable. {ex.Message}", this);
            return;
        }

        damageDeformCompute.SetTexture(kernel, "_DamageTex", runtimeTexture);
        damageDeformCompute.SetVector("_VehicleSize", computedVehicleSize);
        damageDeformCompute.SetVector("_TexResolution", new Vector4(textureWidth, textureHeight, 0.0f, 0.0f));
        damageDeformCompute.SetVector("_BoundsMin", computedBoundsMin);
        damageDeformCompute.SetVector("_BoundsSize", computedVehicleSize);
        damageDeformCompute.SetFloat("_DamageAmplitude", computeDeformAmplitude);
        damageDeformCompute.SetFloat("_DamageDirection", computeDeformDirection);
        damageDeformCompute.SetFloat("_DamageSinFrequency", computeDeformSinFrequency);
        damageDeformCompute.SetFloat("_DamageSinStrength", computeDeformSinStrength);
        damageDeformCompute.SetFloat("_YieldThreshold", computeYieldThreshold);
        damageDeformCompute.SetFloat("_Hardening", computeHardening);
        damageDeformCompute.SetFloat("_MaxDeform", computeMaxDeform);
        damageDeformCompute.SetInt("_UseTwoLevelDamage", computeTwoLevelDamage ? 1 : 0);
        damageDeformCompute.SetInt("_CoarseRadius", Mathf.Max(0, computeCoarseRadius));
        damageDeformCompute.SetFloat("_CoarseWeight", Mathf.Clamp01(computeCoarseWeight));
        damageDeformCompute.SetFloat("_CoarseBoost", Mathf.Max(1.0f, computeCoarseBoost));
        damageDeformCompute.SetFloat("_CoarseDeformMeters", Mathf.Max(0.0f, computeCoarseDeformMeters));

        Matrix4x4 worldToCar = transform.worldToLocalMatrix;
        Matrix4x4 carToWorld = transform.localToWorldMatrix;
        bool hadPendingTargets = false;
        bool dispatchedAny = false;

        for (int i = 0; i < deformTargets.Length; i++)
        {
            MeshDeformTarget target = deformTargets[i];
            if ((target.Filter == null && target.Skinned == null) || target.Mesh == null || target.OriginalVertices == null)
                continue;
            if (target.ReadbackPending)
            {
                hadPendingTargets = true;
                continue;
            }

            EnsureComputeBuffers(ref target);

            Transform meshTransform = target.Filter != null ? target.Filter.transform : target.Skinned.transform;
            Matrix4x4 meshToWorld = meshTransform.localToWorldMatrix;
            Matrix4x4 worldToMesh = meshTransform.worldToLocalMatrix;
            Matrix4x4 carToMesh = worldToMesh * carToWorld;

            damageDeformCompute.SetInt("_VertexCount", target.OriginalVertices.Length);
            damageDeformCompute.SetMatrix("_MeshToWorld", meshToWorld);
            damageDeformCompute.SetMatrix("_WorldToCar", worldToCar);
            damageDeformCompute.SetMatrix("_CarToMesh", carToMesh);
            damageDeformCompute.SetInt("_UseNormals", (computeUseNormals && target.HasNormals) ? 1 : 0);
            damageDeformCompute.SetBuffer(kernel, "_Vertices", target.VertexBuffer);
            damageDeformCompute.SetBuffer(kernel, "_Normals", target.NormalBuffer);
            damageDeformCompute.SetBuffer(kernel, "_Output", target.OutputBuffer);

            int groups = Mathf.CeilToInt(target.OriginalVertices.Length / 64.0f);
            damageDeformCompute.Dispatch(kernel, groups, 1, 1);
            dispatchedAny = true;

            int index = i;
            target.ReadbackPending = true;
            deformTargets[index] = target;
            AsyncGPUReadback.Request(target.OutputBuffer, request => OnDeformReadback(request, index));
        }

        if (hadPendingTargets)
            computeRefreshQueued = true;
        else if (dispatchedAny)
            computeRefreshQueued = false;
    }

    private void ResetVertexColorsAlphaOne()
    {
        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;

            Mesh mesh = filter.sharedMesh;
            int count = mesh.vertexCount;
            if (count == 0)
                continue;

            Color[] colors = mesh.colors;
            if (colors == null || colors.Length != count)
            {
                colors = new Color[count];
                for (int c = 0; c < colors.Length; c++)
                    colors[c] = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            }
            else
            {
                for (int c = 0; c < colors.Length; c++)
                    colors[c].a = 1.0f;
            }

            mesh.colors = colors;
        }

        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer renderer = skinned[i];
            if (renderer == null || renderer.sharedMesh == null)
                continue;

            Mesh mesh = renderer.sharedMesh;
            int count = mesh.vertexCount;
            if (count == 0)
                continue;

            Color[] colors = mesh.colors;
            if (colors == null || colors.Length != count)
            {
                colors = new Color[count];
                for (int c = 0; c < colors.Length; c++)
                    colors[c] = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            }
            else
            {
                for (int c = 0; c < colors.Length; c++)
                    colors[c].a = 1.0f;
            }

            mesh.colors = colors;
        }
    }

    private void OnDeformReadback(AsyncGPUReadbackRequest request, int index)
    {
        if (deformTargets == null || index < 0 || index >= deformTargets.Length)
            return;

        MeshDeformTarget target = deformTargets[index];
        target.ReadbackPending = false;
        deformTargets[index] = target;

        if (request.hasError || target.Mesh == null)
            return;

        var data = request.GetData<Vector4>();
        Vector3[] vertices = new Vector3[data.Length];
        Color[] colors = GetOrCreateWorkingColors(ref target, data.Length);
        Color[] originals = GetOrCreateOriginalColors(ref target, data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            Vector4 packed = data[i];
            vertices[i] = new Vector3(packed.x, packed.y, packed.z);
            Color baseColor = originals[i];
            baseColor.a = Mathf.Clamp01(packed.w);
            colors[i] = baseColor;
        }

        target.Mesh.vertices = vertices;
        target.Mesh.colors = colors;
        if (computeRecalculateNormals)
            target.Mesh.RecalculateNormals();
        target.Mesh.RecalculateBounds();
        if (target.Skinned != null && target.Skinned.sharedMesh != target.Mesh)
            target.Skinned.sharedMesh = target.Mesh;

        if (computeRefreshQueued && !HasPendingReadbacks())
        {
            // Catch up deformation that arrived while previous GPU readbacks were in flight.
            computeRefreshQueued = false;
            ApplyComputeDeformation();
        }
    }

    private bool HasPendingReadbacks()
    {
        if (deformTargets == null)
            return false;

        for (int i = 0; i < deformTargets.Length; i++)
        {
            if (deformTargets[i].ReadbackPending)
                return true;
        }

        return false;
    }

    private void EnsureComputeBuffers(ref MeshDeformTarget target)
    {
        int count = target.OriginalVertices.Length;
        if (target.VertexBuffer == null || target.VertexBuffer.count != count)
        {
            ReleaseBuffers(ref target);
            target.VertexBuffer = new ComputeBuffer(count, sizeof(float) * 3);
            target.VertexBuffer.SetData(target.OriginalVertices);
            target.NormalBuffer = new ComputeBuffer(count, sizeof(float) * 3);
            if (target.OriginalNormals != null && target.OriginalNormals.Length == count)
                target.NormalBuffer.SetData(target.OriginalNormals);
            else
                target.NormalBuffer.SetData(new Vector3[count]);
            target.OutputBuffer = new ComputeBuffer(count, sizeof(float) * 4);
        }
    }

    private void ReleaseDeformTargets()
    {
        if (deformTargets == null)
            return;

        for (int i = 0; i < deformTargets.Length; i++)
        {
            MeshDeformTarget target = deformTargets[i];
            ReleaseBuffers(ref target);
        }

        deformTargets = null;
        computeRefreshQueued = false;
    }

    private static void ReleaseBuffers(ref MeshDeformTarget target)
    {
        if (target.VertexBuffer != null)
            target.VertexBuffer.Release();
        if (target.NormalBuffer != null)
            target.NormalBuffer.Release();
        if (target.OutputBuffer != null)
            target.OutputBuffer.Release();

        target.VertexBuffer = null;
        target.NormalBuffer = null;
        target.OutputBuffer = null;
        target.ReadbackPending = false;
    }

    private static Color[] GetOrCreateOriginalColors(ref MeshDeformTarget target, int count)
    {
        if (target.OriginalColors != null && target.OriginalColors.Length == count)
            return target.OriginalColors;

        Color[] colors = new Color[count];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.white;
        target.OriginalColors = colors;
        return colors;
    }

    private static Color[] GetOrCreateWorkingColors(ref MeshDeformTarget target, int count)
    {
        if (target.WorkingColors != null && target.WorkingColors.Length == count)
            return target.WorkingColors;

        target.WorkingColors = new Color[count];
        return target.WorkingColors;
    }

    private bool ShouldIgnoreDeform(string meshName)
    {
        if (string.IsNullOrEmpty(meshName) || deformIgnoreNameFragments == null)
            return false;

        for (int i = 0; i < deformIgnoreNameFragments.Length; i++)
        {
            string fragment = deformIgnoreNameFragments[i];
            if (string.IsNullOrEmpty(fragment))
                continue;
            if (meshName.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

}
