using UnityEngine;

public partial class CarDamageController
{
    public void ApplyDamageSettings(PlayerCarDamageSettings settings)
    {
        if (settings == null)
            return;

        settings.Validate();

        damageTexture = settings.damageTexture;
        targetRenderer = settings.targetRenderer;
        targetMaterials = settings.targetMaterials;
        textureProperty = settings.textureProperty;
        textureWidth = settings.textureWidth;
        textureHeight = settings.textureHeight;

        obstacleTag = settings.obstacleTag;
        obstacleTagIsValid = string.IsNullOrWhiteSpace(obstacleTag) || IsValidTag(obstacleTag);
        obstacleTagWarningShown = false;
        impulseToColor = settings.impulseToColor;
        maxColorStep = settings.maxColorStep;
        impulseToRadius = settings.impulseToRadius;
        impulseFromSpeedFactor = settings.impulseFromSpeedFactor;
        collisionRepeatCooldownSeconds = settings.collisionRepeatCooldownSeconds;
        maxRadiusCells = settings.maxRadiusCells;
        minSpeedForDamageKmh = settings.minSpeedForDamageKmh;
        maxSpeedForDamageKmh = settings.maxSpeedForDamageKmh;
        minDamageScale = settings.minDamageScale;
        glancingDamageScale = settings.glancingDamageScale;
        impactAlignmentPower = settings.impactAlignmentPower;
        speedDamageCurve = settings.speedDamageCurve;
        speedRadiusBoost = settings.speedRadiusBoost;
        damageFalloff = settings.damageFalloff;
        verticalDamageFalloff = settings.verticalDamageFalloff;

        deformMeshWithCompute = settings.deformMeshWithCompute;
        damageDeformCompute = settings.damageDeformCompute;
        deformIgnoreNameFragments = settings.deformIgnoreNameFragments;
        computeUseNormals = settings.computeUseNormals;
        computeRecalculateNormals = settings.computeRecalculateNormals;
        computeDeformAmplitude = settings.computeDeformAmplitude;
        computeDeformDirection = settings.computeDeformDirection;
        computeDeformSinFrequency = settings.computeDeformSinFrequency;
        computeDeformSinStrength = settings.computeDeformSinStrength;
        computeYieldThreshold = settings.computeYieldThreshold;
        computeHardening = settings.computeHardening;
        computeMaxDeform = settings.computeMaxDeform;
        computeTwoLevelDamage = settings.computeTwoLevelDamage;
        computeCoarseRadius = settings.computeCoarseRadius;
        computeCoarseWeight = settings.computeCoarseWeight;
        computeCoarseBoost = settings.computeCoarseBoost;
        computeCoarseDeformMeters = settings.computeCoarseDeformMeters;

        includeTriggers = settings.includeTriggers;
        includeWheelColliders = settings.includeWheelColliders;

        showDebugVoxels = settings.showDebugVoxels;
        debugVoxelHeightScale = settings.debugVoxelHeightScale;
        debugVoxelOpacity = settings.debugVoxelOpacity;
        debugVoxelYOffset = settings.debugVoxelYOffset;
        debugVoxelScale = settings.debugVoxelScale;
    }

    public CarDamageRuntimeTuning CaptureRuntimeTuning()
    {
        CarDamageRuntimeTuning tuning = new CarDamageRuntimeTuning
        {
            obstacleTag = obstacleTag,
            impulseToColor = impulseToColor,
            maxColorStep = maxColorStep,
            impulseToRadius = impulseToRadius,
            impulseFromSpeedFactor = impulseFromSpeedFactor,
            collisionRepeatCooldownSeconds = collisionRepeatCooldownSeconds,
            maxRadiusCells = maxRadiusCells,
            minSpeedForDamageKmh = minSpeedForDamageKmh,
            maxSpeedForDamageKmh = maxSpeedForDamageKmh,
            minDamageScale = minDamageScale,
            glancingDamageScale = glancingDamageScale,
            impactAlignmentPower = impactAlignmentPower,
            speedRadiusBoost = speedRadiusBoost,
            computeDeformAmplitude = computeDeformAmplitude,
            computeDeformDirection = computeDeformDirection,
            computeDeformSinFrequency = computeDeformSinFrequency,
            computeDeformSinStrength = computeDeformSinStrength,
            computeYieldThreshold = computeYieldThreshold,
            computeHardening = computeHardening,
            computeMaxDeform = computeMaxDeform,
            computeTwoLevelDamage = computeTwoLevelDamage,
            computeCoarseRadius = computeCoarseRadius,
            computeCoarseWeight = computeCoarseWeight,
            computeCoarseBoost = computeCoarseBoost,
            computeCoarseDeformMeters = computeCoarseDeformMeters
        };
        tuning.Validate();
        return tuning;
    }

    public void ApplyRuntimeTuning(CarDamageRuntimeTuning tuning)
    {
        if (tuning == null)
            return;

        tuning.Validate();

        obstacleTag = tuning.obstacleTag;
        obstacleTagIsValid = string.IsNullOrWhiteSpace(obstacleTag) || IsValidTag(obstacleTag);
        obstacleTagWarningShown = false;
        impulseToColor = tuning.impulseToColor;
        maxColorStep = tuning.maxColorStep;
        impulseToRadius = tuning.impulseToRadius;
        impulseFromSpeedFactor = tuning.impulseFromSpeedFactor;
        collisionRepeatCooldownSeconds = tuning.collisionRepeatCooldownSeconds;
        maxRadiusCells = tuning.maxRadiusCells;
        minSpeedForDamageKmh = tuning.minSpeedForDamageKmh;
        maxSpeedForDamageKmh = tuning.maxSpeedForDamageKmh;
        minDamageScale = tuning.minDamageScale;
        glancingDamageScale = tuning.glancingDamageScale;
        impactAlignmentPower = tuning.impactAlignmentPower;
        speedRadiusBoost = tuning.speedRadiusBoost;
        computeDeformAmplitude = tuning.computeDeformAmplitude;
        computeDeformDirection = tuning.computeDeformDirection;
        computeDeformSinFrequency = tuning.computeDeformSinFrequency;
        computeDeformSinStrength = tuning.computeDeformSinStrength;
        computeYieldThreshold = tuning.computeYieldThreshold;
        computeHardening = tuning.computeHardening;
        computeMaxDeform = tuning.computeMaxDeform;
        computeTwoLevelDamage = tuning.computeTwoLevelDamage;
        computeCoarseRadius = tuning.computeCoarseRadius;
        computeCoarseWeight = tuning.computeCoarseWeight;
        computeCoarseBoost = tuning.computeCoarseBoost;
        computeCoarseDeformMeters = tuning.computeCoarseDeformMeters;

        if (deformMeshWithCompute && isInitialized)
            ApplyComputeDeformation();
    }

    public void OverrideRuntimeTargets(Renderer runtimeTargetRenderer, Renderer[] runtimeRenderers, Material[] runtimeTargetMaterials)
    {
        targetRenderer = runtimeTargetRenderer;
        runtimeTargetRenderers = runtimeRenderers;
        targetMaterials = runtimeTargetMaterials;
    }
}
