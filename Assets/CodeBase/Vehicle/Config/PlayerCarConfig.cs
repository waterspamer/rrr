using UnityEngine;

[CreateAssetMenu(menuName = "Vehicles/Player Car Config", fileName = "PlayerCarConfig")]
public class PlayerCarConfig : ScriptableObject
{
    [Header("Visual")]
    [SerializeField] private PlayerCarVisualSettings visual = new PlayerCarVisualSettings();

    [Header("Damage")]
    [SerializeField] private PlayerCarDamageSettings damage = new PlayerCarDamageSettings();

    public PlayerCarVisualSettings Visual => visual;
    public PlayerCarDamageSettings Damage => damage;

    private void OnValidate()
    {
        visual?.Validate();
        damage?.Validate();
    }
}

[System.Serializable]
public class PlayerCarVisualSettings
{
    [Header("Prefabs")]
    public GameObject bodyPrefab;
    public GameObject wheelPrefab;
    public bool addBodyCollider = false;
    public bool generateConvexBodyColliders = false;

    [Header("Wheel Layout (Local)")]
    [Min(0.2f)] public float wheelBase = 2.4f;
    [Min(0.2f)] public float axleWidth = 1.5f;
    public float zOffset = 0.0f;
    public float wheelHeight = 0.35f;

    [Header("Rig Options")]
    [Range(0.0f, 1.0f)] public float bodyRootHeightFactor = 0.3f;
    public bool liveWheelPositions = true;

    [Header("Paint")]
    public bool useDefaultPaint = true;
    public Color defaultPaint = Color.white;
    public string paintProperty = "_MainColor";
    public bool paintAllChildRenderers = true;
    public Renderer[] paintRenderers;

    public void Validate()
    {
        wheelBase = Mathf.Max(0.2f, wheelBase);
        axleWidth = Mathf.Max(0.2f, axleWidth);
        bodyRootHeightFactor = Mathf.Clamp01(bodyRootHeightFactor);
        if (string.IsNullOrWhiteSpace(paintProperty))
            paintProperty = "_MainColor";
    }
}

[System.Serializable]
public class PlayerCarDamageSettings
{
    [Header("Texture")]
    public RenderTexture damageTexture;
    public Renderer targetRenderer;
    public Material[] targetMaterials;
    public string textureProperty = "_MainTex";
    [Min(1)] public int textureWidth = 16;
    [Min(1)] public int textureHeight = 8;

    [Header("Collision")]
    public string obstacleTag = "Obstacle";
    [Min(0.0001f)] public float impulseToColor = 0.0025f;
    [Range(0.0f, 1.0f)] public float maxColorStep = 0.35f;
    [Min(0.0f)] public float impulseToRadius = 0.02f;
    [Min(0)] public int maxRadiusCells = 3;
    [Min(0.1f)] public float minSpeedForDamageKmh = 5.0f;
    [Min(1.0f)] public float maxSpeedForDamageKmh = 80.0f;
    [Range(0.0f, 1.0f)] public float minDamageScale = 0.01f;
    [Range(0.0f, 1.0f)] public float glancingDamageScale = 0.2f;
    [Min(0.1f)] public float impactAlignmentPower = 1.5f;
    public AnimationCurve speedDamageCurve = new AnimationCurve(
        new Keyframe(0.0f, 0.0f),
        new Keyframe(1.0f, 1.0f));
    [Range(0.0f, 2.0f)] public float speedRadiusBoost = 0.4f;
    public AnimationCurve damageFalloff = new AnimationCurve(
        new Keyframe(0.0f, 1.0f),
        new Keyframe(1.0f, 0.0f));
    public AnimationCurve verticalDamageFalloff = new AnimationCurve(
        new Keyframe(0.0f, 1.0f),
        new Keyframe(0.5f, 0.5f),
        new Keyframe(1.0f, 0.0f));

    [Header("Compute Deformation")]
    public bool deformMeshWithCompute = false;
    public ComputeShader damageDeformCompute;
    public string[] deformIgnoreNameFragments;
    public bool computeUseNormals = true;
    public bool computeRecalculateNormals = true;
    [Range(0.0f, 0.5f)] public float computeDeformAmplitude = 0.08f;
    [Range(-1.0f, 1.0f)] public float computeDeformDirection = -1.0f;
    [Range(0.0f, 40.0f)] public float computeDeformSinFrequency = 10.0f;
    [Range(0.0f, 1.0f)] public float computeDeformSinStrength = 0.25f;
    [Range(0.0f, 1.0f)] public float computeYieldThreshold = 0.15f;
    [Range(0.1f, 8.0f)] public float computeHardening = 2.0f;
    [Range(0.0f, 0.5f)] public float computeMaxDeform = 0.25f;
    public bool computeTwoLevelDamage = false;
    [Range(1, 6)] public int computeCoarseRadius = 2;
    [Range(0.0f, 1.0f)] public float computeCoarseWeight = 0.5f;
    [Range(1.0f, 4.0f)] public float computeCoarseBoost = 1.5f;
    [Range(0.0f, 0.6f)] public float computeCoarseDeformMeters = 0.3f;

    [Header("Map")]
    public bool includeTriggers = false;
    public bool includeWheelColliders = false;

    [Header("Debug")]
    public bool showDebugVoxels = false;
    [Min(0.1f)] public float debugVoxelHeightScale = 1.0f;
    [Range(0.05f, 1.0f)] public float debugVoxelOpacity = 0.5f;
    public float debugVoxelYOffset = 0.02f;
    [Range(0.1f, 2.0f)] public float debugVoxelScale = 0.98f;

    public void Validate()
    {
        textureWidth = Mathf.Max(1, textureWidth);
        textureHeight = Mathf.Max(1, textureHeight);
        impulseToColor = Mathf.Max(0.0001f, impulseToColor);
        maxColorStep = Mathf.Clamp01(maxColorStep);
        impulseToRadius = Mathf.Max(0.0f, impulseToRadius);
        maxRadiusCells = Mathf.Max(0, maxRadiusCells);
        minSpeedForDamageKmh = Mathf.Max(0.1f, minSpeedForDamageKmh);
        maxSpeedForDamageKmh = Mathf.Max(1.0f, maxSpeedForDamageKmh);
        minDamageScale = Mathf.Clamp01(minDamageScale);
        glancingDamageScale = Mathf.Clamp01(glancingDamageScale);
        impactAlignmentPower = Mathf.Max(0.1f, impactAlignmentPower);
        speedRadiusBoost = Mathf.Clamp(speedRadiusBoost, 0.0f, 2.0f);
        computeDeformAmplitude = Mathf.Clamp(computeDeformAmplitude, 0.0f, 0.5f);
        computeDeformDirection = Mathf.Clamp(computeDeformDirection, -1.0f, 1.0f);
        computeDeformSinFrequency = Mathf.Clamp(computeDeformSinFrequency, 0.0f, 40.0f);
        computeDeformSinStrength = Mathf.Clamp01(computeDeformSinStrength);
        computeYieldThreshold = Mathf.Clamp01(computeYieldThreshold);
        computeHardening = Mathf.Clamp(computeHardening, 0.1f, 8.0f);
        computeMaxDeform = Mathf.Clamp(computeMaxDeform, 0.0f, 0.5f);
        computeCoarseRadius = Mathf.Clamp(computeCoarseRadius, 1, 6);
        computeCoarseWeight = Mathf.Clamp01(computeCoarseWeight);
        computeCoarseBoost = Mathf.Clamp(computeCoarseBoost, 1.0f, 4.0f);
        computeCoarseDeformMeters = Mathf.Clamp(computeCoarseDeformMeters, 0.0f, 0.6f);
        debugVoxelHeightScale = Mathf.Max(0.1f, debugVoxelHeightScale);
        debugVoxelOpacity = Mathf.Clamp(debugVoxelOpacity, 0.05f, 1.0f);
        debugVoxelScale = Mathf.Clamp(debugVoxelScale, 0.1f, 2.0f);
    }
}
