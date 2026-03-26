using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Vehicles/Player Car Config", fileName = "PlayerCarConfig")]
public class PlayerCarConfig : ScriptableObject
{
    [Header("Visual")]
    [SerializeField] private PlayerCarVisualSettings visual = new PlayerCarVisualSettings();

    [Header("Arcade Prototype")]
    [SerializeField] private bool useArcadePrototypeControllerTuning;
    [SerializeField] private ArcadePrototypeControllerRuntimeTuning arcadePrototypeController = new ArcadePrototypeControllerRuntimeTuning();

    [Header("Direct Multiplayer")]
    [SerializeField] private DirectMultiplayerSimulationBackend directMultiplayerSimulationBackend =
        DirectMultiplayerSimulationBackend.LegacyController;

    [Header("Damage")]
    [SerializeField] private PlayerCarDamageSettings damage = new PlayerCarDamageSettings();

    public PlayerCarVisualSettings Visual => visual;
    public bool UseArcadePrototypeControllerTuning => useArcadePrototypeControllerTuning;
    public ArcadePrototypeControllerRuntimeTuning ArcadePrototypeController => arcadePrototypeController;
    public DirectMultiplayerSimulationBackend DirectMultiplayerSimulationBackend => directMultiplayerSimulationBackend;
    public PlayerCarDamageSettings Damage => damage;

    private void OnValidate()
    {
        visual?.Validate();
        arcadePrototypeController?.Validate();
        damage?.Validate();
    }
}

[Serializable]
public enum DirectMultiplayerSimulationBackend : byte
{
    LegacyController = 0,
    ArcadePrototype = 1
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
    [Min(0.0f)] public float impulseFromSpeedFactor = 0.25f;
    [Min(0.0f)] public float collisionRepeatCooldownSeconds = 0.18f;
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
        impulseFromSpeedFactor = Mathf.Max(0.0f, impulseFromSpeedFactor);
        collisionRepeatCooldownSeconds = Mathf.Clamp(collisionRepeatCooldownSeconds, 0.0f, 2.0f);
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

[System.Serializable]
public sealed class CarDamageRuntimeTuning
{
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public int revision;
    public long updatedAtUnixMs;
    public string source = string.Empty;
    public string obstacleTag = "Obstacle";
    public float impulseToColor = 0.0025f;
    public float maxColorStep = 0.35f;
    public float impulseToRadius = 0.02f;
    public float impulseFromSpeedFactor = 0.25f;
    public float collisionRepeatCooldownSeconds = 0.18f;
    public int maxRadiusCells = 3;
    public float minSpeedForDamageKmh = 5.0f;
    public float maxSpeedForDamageKmh = 80.0f;
    public float minDamageScale = 0.01f;
    public float glancingDamageScale = 0.2f;
    public float impactAlignmentPower = 1.5f;
    public float speedRadiusBoost = 0.4f;
    public float computeDeformAmplitude = 0.08f;
    public float computeDeformDirection = -1.0f;
    public float computeDeformSinFrequency = 10.0f;
    public float computeDeformSinStrength = 0.25f;
    public float computeYieldThreshold = 0.15f;
    public float computeHardening = 2.0f;
    public float computeMaxDeform = 0.25f;
    public bool computeTwoLevelDamage;
    public int computeCoarseRadius = 2;
    public float computeCoarseWeight = 0.5f;
    public float computeCoarseBoost = 1.5f;
    public float computeCoarseDeformMeters = 0.3f;

    public void Validate()
    {
        version = Mathf.Max(1, version);
        obstacleTag = string.IsNullOrWhiteSpace(obstacleTag) ? "Obstacle" : obstacleTag.Trim();
        impulseToColor = Mathf.Max(0.0001f, impulseToColor);
        maxColorStep = Mathf.Clamp01(maxColorStep);
        impulseToRadius = Mathf.Max(0.0f, impulseToRadius);
        impulseFromSpeedFactor = Mathf.Max(0.0f, impulseFromSpeedFactor);
        collisionRepeatCooldownSeconds = Mathf.Clamp(collisionRepeatCooldownSeconds, 0.0f, 2.0f);
        maxRadiusCells = Mathf.Max(0, maxRadiusCells);
        minSpeedForDamageKmh = Mathf.Max(0.1f, minSpeedForDamageKmh);
        maxSpeedForDamageKmh = Mathf.Max(minSpeedForDamageKmh + 0.1f, maxSpeedForDamageKmh);
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
        if (string.IsNullOrWhiteSpace(source))
            source = string.Empty;
    }

    public CarDamageRuntimeTuning Clone()
    {
        string json = JsonUtility.ToJson(this);
        CarDamageRuntimeTuning clone = string.IsNullOrWhiteSpace(json)
            ? new CarDamageRuntimeTuning()
            : JsonUtility.FromJson<CarDamageRuntimeTuning>(json);
        clone?.Validate();
        return clone;
    }

    public bool IsEquivalentTo(CarDamageRuntimeTuning other)
    {
        return other != null &&
               string.Equals(obstacleTag ?? string.Empty, other.obstacleTag ?? string.Empty, StringComparison.Ordinal) &&
               Approximately(impulseToColor, other.impulseToColor) &&
               Approximately(maxColorStep, other.maxColorStep) &&
               Approximately(impulseToRadius, other.impulseToRadius) &&
               Approximately(impulseFromSpeedFactor, other.impulseFromSpeedFactor) &&
               Approximately(collisionRepeatCooldownSeconds, other.collisionRepeatCooldownSeconds) &&
               maxRadiusCells == other.maxRadiusCells &&
               Approximately(minSpeedForDamageKmh, other.minSpeedForDamageKmh) &&
               Approximately(maxSpeedForDamageKmh, other.maxSpeedForDamageKmh) &&
               Approximately(minDamageScale, other.minDamageScale) &&
               Approximately(glancingDamageScale, other.glancingDamageScale) &&
               Approximately(impactAlignmentPower, other.impactAlignmentPower) &&
               Approximately(speedRadiusBoost, other.speedRadiusBoost) &&
               Approximately(computeDeformAmplitude, other.computeDeformAmplitude) &&
               Approximately(computeDeformDirection, other.computeDeformDirection) &&
               Approximately(computeDeformSinFrequency, other.computeDeformSinFrequency) &&
               Approximately(computeDeformSinStrength, other.computeDeformSinStrength) &&
               Approximately(computeYieldThreshold, other.computeYieldThreshold) &&
               Approximately(computeHardening, other.computeHardening) &&
               Approximately(computeMaxDeform, other.computeMaxDeform) &&
               computeTwoLevelDamage == other.computeTwoLevelDamage &&
               computeCoarseRadius == other.computeCoarseRadius &&
               Approximately(computeCoarseWeight, other.computeCoarseWeight) &&
               Approximately(computeCoarseBoost, other.computeCoarseBoost) &&
               Approximately(computeCoarseDeformMeters, other.computeCoarseDeformMeters);
    }

    private static bool Approximately(float a, float b)
    {
        return Mathf.Abs(a - b) <= 0.0001f;
    }

    public static CarDamageRuntimeTuning FromSettings(PlayerCarDamageSettings settings)
    {
        if (settings == null)
            return null;

        settings.Validate();
        return new CarDamageRuntimeTuning
        {
            version = CurrentVersion,
            obstacleTag = settings.obstacleTag,
            impulseToColor = settings.impulseToColor,
            maxColorStep = settings.maxColorStep,
            impulseToRadius = settings.impulseToRadius,
            impulseFromSpeedFactor = settings.impulseFromSpeedFactor,
            collisionRepeatCooldownSeconds = settings.collisionRepeatCooldownSeconds,
            maxRadiusCells = settings.maxRadiusCells,
            minSpeedForDamageKmh = settings.minSpeedForDamageKmh,
            maxSpeedForDamageKmh = settings.maxSpeedForDamageKmh,
            minDamageScale = settings.minDamageScale,
            glancingDamageScale = settings.glancingDamageScale,
            impactAlignmentPower = settings.impactAlignmentPower,
            speedRadiusBoost = settings.speedRadiusBoost,
            computeDeformAmplitude = settings.computeDeformAmplitude,
            computeDeformDirection = settings.computeDeformDirection,
            computeDeformSinFrequency = settings.computeDeformSinFrequency,
            computeDeformSinStrength = settings.computeDeformSinStrength,
            computeYieldThreshold = settings.computeYieldThreshold,
            computeHardening = settings.computeHardening,
            computeMaxDeform = settings.computeMaxDeform,
            computeTwoLevelDamage = settings.computeTwoLevelDamage,
            computeCoarseRadius = settings.computeCoarseRadius,
            computeCoarseWeight = settings.computeCoarseWeight,
            computeCoarseBoost = settings.computeCoarseBoost,
            computeCoarseDeformMeters = settings.computeCoarseDeformMeters
        };
    }
}
