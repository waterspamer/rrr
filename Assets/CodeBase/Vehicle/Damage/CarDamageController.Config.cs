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

    public void OverrideRuntimeTargets(Renderer runtimeTargetRenderer, Material[] runtimeTargetMaterials)
    {
        targetRenderer = runtimeTargetRenderer;
        targetMaterials = runtimeTargetMaterials;
    }
}
