using UnityEngine;

public partial class CarDamageController
{
    private void ApplyCollisionDamage(Collision collision)
    {
        if (runtimeTexture == null || cpuTexture == null)
            return;

        ContactPoint[] contacts = collision.contacts;
        Vector3 worldPoint = contacts != null && contacts.Length > 0 ? contacts[0].point : transform.position;
        Vector3 worldNormal = contacts != null && contacts.Length > 0 ? contacts[0].normal : Vector3.up;
        ApplyDamageFromImpact(
            worldPoint,
            worldNormal,
            collision.relativeVelocity,
            collision.impulse.magnitude,
            collision.collider != null ? collision.collider.name : "collision",
            true,
            collision);
    }

    public bool ApplySyntheticCollisionDamage(
        Vector3 worldPoint,
        Vector3 worldNormal,
        Vector3 relativeVelocity,
        float impulseMagnitude,
        string debugLabel,
        bool notifyNetwork = true)
    {
        if (runtimeTexture == null || cpuTexture == null)
            return false;

        return ApplyDamageFromImpact(worldPoint, worldNormal, relativeVelocity, impulseMagnitude, debugLabel, notifyNetwork, null);
    }

    private bool ApplyDamageFromImpact(
        Vector3 worldPoint,
        Vector3 worldNormal,
        Vector3 relativeVelocity,
        float impulseMagnitude,
        string debugLabel,
        bool notifyNetwork,
        Collision sourceCollision)
    {
        float impactSpeedMps = relativeVelocity.magnitude;
        if (impactSpeedMps <= 0.001f)
            impactSpeedMps = GetAverageSpeed();
        float impactSpeedKmh = impactSpeedMps * 3.6f;
        float speed01 = maxSpeedForDamageKmh > minSpeedForDamageKmh + 0.001f
            ? Mathf.InverseLerp(minSpeedForDamageKmh, maxSpeedForDamageKmh, impactSpeedKmh)
            : 1.0f;
        float curveScale = speedDamageCurve != null ? Mathf.Clamp01(speedDamageCurve.Evaluate(speed01)) : speed01;
        float speedScale = Mathf.Lerp(minDamageScale, 1.0f, curveScale);
        float normalImpact = GetImpactNormalFactor(relativeVelocity, worldNormal);
        float alignment = Mathf.Pow(normalImpact, Mathf.Max(0.1f, impactAlignmentPower));
        float glancingScale = Mathf.Lerp(Mathf.Clamp01(glancingDamageScale), 1.0f, alignment);
        float effectiveImpulse = impulseMagnitude;
        if (effectiveImpulse <= 0.001f)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            float bodyMass = rb != null ? rb.mass : 1.0f;
            effectiveImpulse = impactSpeedMps * bodyMass * impulseFromSpeedFactor;
        }

        float baseAmount = Mathf.Clamp(effectiveImpulse * impulseToColor, 0.0f, maxColorStep);
        float amount = baseAmount * speedScale * glancingScale;
        if (amount <= 0.0001f)
            return false;

        if (damageManager != null)
        {
            if (sourceCollision != null)
                damageManager.SpawnCollisionEffect(sourceCollision);
            else
                damageManager.SpawnCollisionEffect(worldPoint, worldNormal);
        }
        followCarCamera?.PlayCollisionShake(effectiveImpulse, impactSpeedKmh);

        Debug.Log($"CarDamageController hit {debugLabel} impulse={impulseMagnitude:0.000} effectiveImpulse={effectiveImpulse:0.000} impactSpeed={impactSpeedKmh:0.0}km/h normalImpact={normalImpact:0.00} amount={amount:0.000}", this);

        int baseRadiusCells = Mathf.Clamp(Mathf.CeilToInt(effectiveImpulse * impulseToRadius), 0, maxRadiusCells);
        int radiusCells = Mathf.Clamp(
            Mathf.RoundToInt(baseRadiusCells * (1.0f + curveScale * speedRadiusBoost) * glancingScale),
            0,
            maxRadiusCells);

        ApplyDamageAtPoint(worldPoint, amount, radiusCells);
        cpuTexture.Apply();
        Graphics.Blit(cpuTexture, runtimeTexture);
        ApplyComputeDeformation();

        if (notifyNetwork)
            NotifyDamageMapChanged(worldPoint, worldNormal);

        return true;
    }

    private float GetImpactNormalFactor(Collision collision)
    {
        ContactPoint[] contacts = collision.contacts;
        Vector3 normal = contacts != null && contacts.Length > 0 ? contacts[0].normal : Vector3.up;
        return GetImpactNormalFactor(collision.relativeVelocity, normal);
    }

    private float GetImpactNormalFactor(Vector3 relativeVelocity, Vector3 worldNormal)
    {
        float speed = relativeVelocity.magnitude;
        if (speed <= 0.0001f)
            return 1.0f;

        Vector3 velocityDir = relativeVelocity / speed;
        return Mathf.Clamp01(Mathf.Abs(Vector3.Dot(velocityDir, worldNormal.normalized)));
    }

    private void ApplyDamageAtPoint(Vector3 worldPoint, float amount, int radiusCells)
    {
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);

        int x = GetCellIndex(localPoint.x, damageBounds.min.x, damageBounds.max.x, textureWidth);
        int y = GetCellIndex(localPoint.z, damageBounds.min.z, damageBounds.max.z, textureHeight);

        float height01 = Mathf.InverseLerp(damageBounds.min.y, damageBounds.max.y, localPoint.y);
        int hitLayer = Mathf.Clamp(Mathf.FloorToInt(height01 * 3.0f), 0, 2);

        int radius = Mathf.Max(0, radiusCells);
        int minX = Mathf.Max(0, x - radius);
        int maxX = Mathf.Min(textureWidth - 1, x + radius);
        int minY = Mathf.Max(0, y - radius);
        int maxY = Mathf.Min(textureHeight - 1, y + radius);

        float denom = Mathf.Max(1.0f, radius + 0.0001f);
        for (int ix = minX; ix <= maxX; ix++)
        {
            for (int iy = minY; iy <= maxY; iy++)
            {
                float dx = ix - x;
                float dy = iy - y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > radius + 0.001f)
                    continue;

                float normalized = Mathf.Clamp01(dist / denom);
                float radialWeight = damageFalloff != null ? Mathf.Clamp01(damageFalloff.Evaluate(normalized)) : 1.0f - normalized;
                if (radialWeight <= 0.0001f)
                    continue;

                Color current = cpuTexture.GetPixel(ix, iy);
                for (int layer = 0; layer < 3; layer++)
                {
                    float vNorm = Mathf.Abs(layer - hitLayer) / 2.0f;
                    float verticalWeight = verticalDamageFalloff != null
                        ? Mathf.Clamp01(verticalDamageFalloff.Evaluate(vNorm))
                        : (1.0f - vNorm);
                    float step = Mathf.Clamp01(amount * radialWeight * verticalWeight);
                    if (step <= 0.0001f)
                        continue;

                    if (layer == 2)
                        current.r = Mathf.Clamp01(current.r + step);
                    else if (layer == 1)
                        current.g = Mathf.Clamp01(current.g + step);
                    else
                        current.b = Mathf.Clamp01(current.b + step);
                }

                current.a = 1.0f;
                cpuTexture.SetPixel(ix, iy, current);
            }
        }
    }

    private static Color GetChannelColor(float heightNormalized)
    {
        if (heightNormalized >= 2.0f / 3.0f)
            return Color.red;
        if (heightNormalized >= 1.0f / 3.0f)
            return Color.green;
        return Color.blue;
    }

    private void PopulateHeightMap(Collider[] colliders)
    {
        if (colliders == null)
            return;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null)
                continue;
            if (!includeTriggers && col.isTrigger)
                continue;
            if (!includeWheelColliders && col is WheelCollider)
                continue;

            if (!TryGetLocalBounds(col, out Bounds localBounds))
                continue;

            int minX = GetCellIndexFloor(localBounds.min.x, damageBounds.min.x, damageBounds.max.x, textureWidth);
            int maxX = GetCellIndexCeil(localBounds.max.x, damageBounds.min.x, damageBounds.max.x, textureWidth);
            int minY = GetCellIndexFloor(localBounds.min.z, damageBounds.min.z, damageBounds.max.z, textureHeight);
            int maxY = GetCellIndexCeil(localBounds.max.z, damageBounds.min.z, damageBounds.max.z, textureHeight);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                    heightMap[x, y] = Mathf.Max(heightMap[x, y], localBounds.max.y);
            }
        }
    }

    private void PopulateHeightMap(Renderer[] renderers)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!TryGetLocalBounds(renderer, out Bounds localBounds))
                continue;

            int minX = GetCellIndexFloor(localBounds.min.x, damageBounds.min.x, damageBounds.max.x, textureWidth);
            int maxX = GetCellIndexCeil(localBounds.max.x, damageBounds.min.x, damageBounds.max.x, textureWidth);
            int minY = GetCellIndexFloor(localBounds.min.z, damageBounds.min.z, damageBounds.max.z, textureHeight);
            int maxY = GetCellIndexCeil(localBounds.max.z, damageBounds.min.z, damageBounds.max.z, textureHeight);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                    heightMap[x, y] = Mathf.Max(heightMap[x, y], localBounds.max.y);
            }
        }
    }

    private bool TryBuildBounds(Collider[] colliders, out Bounds bounds)
    {
        if (colliders == null)
        {
            bounds = default;
            return false;
        }

        bool initialized = false;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null)
                continue;
            if (!includeTriggers && col.isTrigger)
                continue;
            if (!includeWheelColliders && col is WheelCollider)
                continue;

            if (!TryGetLocalBounds(col, out Bounds localBounds))
                continue;

            if (!initialized)
            {
                min = localBounds.min;
                max = localBounds.max;
                initialized = true;
            }
            else
            {
                min = Vector3.Min(min, localBounds.min);
                max = Vector3.Max(max, localBounds.max);
            }
        }

        if (!initialized)
        {
            bounds = default;
            return false;
        }

        bounds = new Bounds((min + max) * 0.5f, max - min);
        return true;
    }

    private bool TryBuildBounds(Renderer[] renderers, out Bounds bounds)
    {
        if (renderers == null)
        {
            bounds = default;
            return false;
        }

        bool initialized = false;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!TryGetLocalBounds(renderer, out Bounds localBounds))
                continue;

            if (!initialized)
            {
                min = localBounds.min;
                max = localBounds.max;
                initialized = true;
            }
            else
            {
                min = Vector3.Min(min, localBounds.min);
                max = Vector3.Max(max, localBounds.max);
            }
        }

        if (!initialized)
        {
            bounds = default;
            return false;
        }

        bounds = new Bounds((min + max) * 0.5f, max - min);
        return true;
    }

    private bool TryGetLocalBounds(Collider col, out Bounds localBounds)
    {
        Bounds world = col.bounds;
        Vector3 center = world.center;
        Vector3 extents = world.extents;

        Vector3[] corners =
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y, extents.z),
            center + new Vector3(-extents.x, extents.y, -extents.z),
            center + new Vector3(-extents.x, extents.y, extents.z),
            center + new Vector3(extents.x, -extents.y, -extents.z),
            center + new Vector3(extents.x, -extents.y, extents.z),
            center + new Vector3(extents.x, extents.y, -extents.z),
            center + new Vector3(extents.x, extents.y, extents.z)
        };

        Vector3 localMin = transform.InverseTransformPoint(corners[0]);
        Vector3 localMax = localMin;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 local = transform.InverseTransformPoint(corners[i]);
            localMin = Vector3.Min(localMin, local);
            localMax = Vector3.Max(localMax, local);
        }

        localBounds = new Bounds((localMin + localMax) * 0.5f, localMax - localMin);
        return true;
    }

    private bool TryGetLocalBounds(Renderer renderer, out Bounds localBounds)
    {
        Bounds world = renderer.bounds;
        Vector3 center = world.center;
        Vector3 extents = world.extents;

        Vector3[] corners =
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y, extents.z),
            center + new Vector3(-extents.x, extents.y, -extents.z),
            center + new Vector3(-extents.x, extents.y, extents.z),
            center + new Vector3(extents.x, -extents.y, -extents.z),
            center + new Vector3(extents.x, -extents.y, extents.z),
            center + new Vector3(extents.x, extents.y, -extents.z),
            center + new Vector3(extents.x, extents.y, extents.z)
        };

        Vector3 localMin = transform.InverseTransformPoint(corners[0]);
        Vector3 localMax = localMin;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 local = transform.InverseTransformPoint(corners[i]);
            localMin = Vector3.Min(localMin, local);
            localMax = Vector3.Max(localMax, local);
        }

        localBounds = new Bounds((localMin + localMax) * 0.5f, localMax - localMin);
        return true;
    }

    private static int GetCellIndex(float value, float min, float max, int size)
    {
        float t = Mathf.InverseLerp(min, max, value);
        return Mathf.Clamp(Mathf.RoundToInt(t * (size - 1)), 0, size - 1);
    }

    private static int GetCellIndexFloor(float value, float min, float max, int size)
    {
        float t = Mathf.InverseLerp(min, max, value);
        return Mathf.Clamp(Mathf.FloorToInt(t * size), 0, size - 1);
    }

    private static int GetCellIndexCeil(float value, float min, float max, int size)
    {
        float t = Mathf.InverseLerp(min, max, value);
        return Mathf.Clamp(Mathf.CeilToInt(t * size) - 1, 0, size - 1);
    }

    private float GetAverageSpeed()
    {
        if (velocitySampleFilled == 0)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            return rb != null ? rb.linearVelocity.magnitude : 0.0f;
        }

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < velocitySampleFilled; i++)
            sum += velocitySamples[i];
        return (sum / velocitySampleFilled).magnitude;
    }

    private static void EnsureVoxelResources()
    {
        if (voxelMesh == null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            voxelMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);
        }

        if (voxelMaterial != null)
            return;

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            return;

        voxelMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        voxelMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        voxelMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        voxelMaterial.SetInt("_ZWrite", 0);
        voxelMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        voxelMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
    }

    private void OnDisable()
    {
        ClearRecentCollisionHistory();
        ReleaseDeformTargets();
    }

    private void UpdateComputedSizes()
    {
        float vehicleHeight = damageBounds.size.y;
        computedVehicleSize = new Vector3(damageBounds.size.x, vehicleHeight, damageBounds.size.z);
        computedVoxelSize = new Vector3(
            damageBounds.size.x / Mathf.Max(1, textureWidth),
            vehicleHeight / 3.0f,
            damageBounds.size.z / Mathf.Max(1, textureHeight));
        computedBoundsMin = damageBounds.min;
        computedBoundsMax = damageBounds.max;
        ApplyVehicleSizeToMaterials();
    }

    private void ApplyVehicleSizeToMaterials()
    {
        Material[] materials = targetMaterials;
        if (materials == null || materials.Length == 0)
        {
            if (targetRenderer != null)
                materials = targetRenderer.sharedMaterials;
        }

        if (materials == null || materials.Length == 0)
            return;

        Vector4 texResolution = new Vector4(textureWidth, textureHeight, 0.0f, 0.0f);
        Vector4 boundsMin = computedBoundsMin;
        Vector4 boundsSize = computedVehicleSize;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            if (material.HasProperty("_VehicleSize"))
                material.SetVector("_VehicleSize", computedVehicleSize);
            if (material.HasProperty("_BoundsMin"))
                material.SetVector("_BoundsMin", boundsMin);
            if (material.HasProperty("_BoundsSize"))
                material.SetVector("_BoundsSize", boundsSize);
            if (material.HasProperty("_TexResolution"))
                material.SetVector("_TexResolution", texResolution);
        }
    }
}
