using System.Collections.Generic;
using UnityEngine;

public abstract partial class CarControllerBase
{
    public void SetVehicleSettings(VehicleSettings vehicleSettings)
    {
        settings = vehicleSettings;
        ApplySettings();
    }

    public void SetEngineGearboxSettings(EngineGearboxConfig config)
    {
        if (config == null)
            return;

        config.Validate();
        driveType = config.driveType;
        horsepower = config.horsepower;
        powerCurve = config.powerCurve;
        CopyGearboxSettings(config.gearbox);
        CopyEngineSettings(config.engine);
    }

    public void SetSuspensionSettings(SuspensionConfig config)
    {
        if (config == null)
            return;

        config.Validate();
        suspensionDistance = config.suspensionDistance;
        suspensionFrequency = config.suspensionFrequency;
        suspensionDamping = config.suspensionDamping;
        suspensionTargetPosition = config.suspensionTargetPosition;
        frontWeightBias = config.frontWeightBias;
        antiRollFront = config.antiRollFront;
        antiRollRear = config.antiRollRear;
        centerOfMassHeight = config.centerOfMassHeight;

        if (rb != null)
        {
            UpdateCenterOfMass();
            ApplySuspensionSettings();
        }
    }

    private void ApplySettings()
    {
        if (settings == null)
            return;

        settings.Validate();
        autoCreateHud = settings.autoCreateHud;
        maxSteerAngle = settings.maxSteerAngle;
        steerResponse = settings.steerResponse;
        steerBySpeed = settings.steerBySpeed;
        brakePower = settings.brakePower;
        handbrakePower = settings.handbrakePower;
        forwardFriction = settings.forwardFriction;
        sidewaysFriction = settings.sidewaysFriction;
        handbrakeFrictionMultiplier = settings.handbrakeFrictionMultiplier;
        handbrakeSidewaysBySpeed = settings.handbrakeSidewaysBySpeed;
        driftKickSteerThreshold = settings.driftKickSteerThreshold;
        driftKickMaxForce = settings.driftKickMaxForce;
        driftKickRearOffset = settings.driftKickRearOffset;
        driftKickResponse = settings.driftKickResponse;
        driftKickBySpeed = settings.driftKickBySpeed;
        lateralStability = settings.lateralStability;
        yawStability = settings.yawStability;
        mass = settings.mass;
        downforce = settings.downforce;
        rollingResistance = settings.rollingResistance;
        aerodynamicDrag = settings.aerodynamicDrag;
        forwardExtremumSlip = settings.forwardExtremumSlip;
        forwardExtremumValue = settings.forwardExtremumValue;
        forwardAsymptoteSlip = settings.forwardAsymptoteSlip;
        forwardAsymptoteValue = settings.forwardAsymptoteValue;
        sidewaysExtremumSlip = settings.sidewaysExtremumSlip;
        sidewaysExtremumValue = settings.sidewaysExtremumValue;
        sidewaysAsymptoteSlip = settings.sidewaysAsymptoteSlip;
        sidewaysAsymptoteValue = settings.sidewaysAsymptoteValue;
        wheelRadius = settings.wheelRadius;
        wheelWidth = settings.wheelWidth;
        nitroEnabled = settings.nitroEnabled;
        nitroStart = settings.nitroStart;
        nitroRegenPerSecond = settings.nitroRegenPerSecond;
        nitroDrainPerSecond = settings.nitroDrainPerSecond;
        nitroPowerMultiplier = settings.nitroPowerMultiplier;
        nitroRpmResponseMultiplier = settings.nitroRpmResponseMultiplier;
    }

    private void CopyGearboxSettings(GearboxSettings source)
    {
        if (source == null)
            return;

        gearbox = new GearboxSettings
        {
            finalDrive = source.finalDrive,
            reverseRatio = source.reverseRatio,
            shiftDuration = source.shiftDuration,
            automatic = source.automatic,
            upshiftRpm = source.upshiftRpm,
            downshiftRpm = source.downshiftRpm,
            allowAutoReverse = source.allowAutoReverse,
            forwardGears = source.forwardGears != null ? new List<float>(source.forwardGears) : new List<float>()
        };
    }

    private void CopyEngineSettings(EngineSettings source)
    {
        if (source == null)
            return;

        engine = new EngineSettings
        {
            idleRpm = source.idleRpm,
            maxRpm = source.maxRpm,
            rpmResponse = source.rpmResponse
        };
    }

    protected virtual void OnValidate()
    {
        ApplySettings();
        wheelRadius = Mathf.Clamp(wheelRadius, 0.05f, 2.0f);
        wheelWidth = Mathf.Clamp(wheelWidth, 0.05f, 1.0f);
        mass = Mathf.Max(50.0f, mass);
        horsepower = Mathf.Max(10.0f, horsepower);
        maxSteerAngle = Mathf.Clamp(maxSteerAngle, 1.0f, 60.0f);
        steerResponse = Mathf.Clamp(steerResponse, 1.0f, 20.0f);
        brakePower = Mathf.Max(0.0f, brakePower);
        handbrakePower = Mathf.Max(0.0f, handbrakePower);
        suspensionDistance = Mathf.Clamp(suspensionDistance, 0.05f, 0.5f);
        suspensionFrequency = Mathf.Clamp(suspensionFrequency, 1.0f, 6.0f);
        suspensionDamping = Mathf.Clamp(suspensionDamping, 0.1f, 1.0f);
        suspensionTargetPosition = Mathf.Clamp01(suspensionTargetPosition);
        frontWeightBias = Mathf.Clamp(frontWeightBias, 0.3f, 0.7f);
        antiRollFront = Mathf.Max(0.0f, antiRollFront);
        antiRollRear = Mathf.Max(0.0f, antiRollRear);
        downforce = Mathf.Max(0.0f, downforce);
        rollingResistance = Mathf.Max(0.0f, rollingResistance);
        aerodynamicDrag = Mathf.Max(0.0f, aerodynamicDrag);
        forwardFriction = Mathf.Clamp(forwardFriction, 0.1f, 5.0f);
        sidewaysFriction = Mathf.Clamp(sidewaysFriction, 0.1f, 5.0f);
        handbrakeFrictionMultiplier = Mathf.Clamp(handbrakeFrictionMultiplier, 0.05f, 1.0f);
        lateralStability = Mathf.Max(0.0f, lateralStability);
        yawStability = Mathf.Max(0.0f, yawStability);
        driftKickSteerThreshold = Mathf.Clamp01(driftKickSteerThreshold);
        driftKickMaxForce = Mathf.Max(0.0f, driftKickMaxForce);
        driftKickRearOffset = Mathf.Max(0.0f, driftKickRearOffset);
        driftKickResponse = Mathf.Clamp(driftKickResponse, 1.0f, 20.0f);
        forwardExtremumSlip = Mathf.Clamp(forwardExtremumSlip, 0.1f, 5.0f);
        forwardExtremumValue = Mathf.Clamp(forwardExtremumValue, 0.1f, 5.0f);
        forwardAsymptoteSlip = Mathf.Clamp(forwardAsymptoteSlip, 0.1f, 5.0f);
        forwardAsymptoteValue = Mathf.Clamp(forwardAsymptoteValue, 0.1f, 5.0f);
        sidewaysExtremumSlip = Mathf.Clamp(sidewaysExtremumSlip, 0.1f, 5.0f);
        sidewaysExtremumValue = Mathf.Clamp(sidewaysExtremumValue, 0.1f, 5.0f);
        sidewaysAsymptoteSlip = Mathf.Clamp(sidewaysAsymptoteSlip, 0.1f, 5.0f);
        sidewaysAsymptoteValue = Mathf.Clamp(sidewaysAsymptoteValue, 0.1f, 5.0f);
        centerOfMassHeight = Mathf.Clamp01(centerOfMassHeight);
        nitroStart = Mathf.Clamp01(nitroStart);
        nitroRegenPerSecond = Mathf.Max(0.0f, nitroRegenPerSecond);
        nitroDrainPerSecond = Mathf.Max(0.0f, nitroDrainPerSecond);
        nitroPowerMultiplier = Mathf.Clamp(nitroPowerMultiplier, 1.0f, 3.0f);
        nitroRpmResponseMultiplier = Mathf.Clamp(nitroRpmResponseMultiplier, 1.0f, 3.0f);

        if (steerBySpeed == null || steerBySpeed.length == 0)
        {
            steerBySpeed = new AnimationCurve(
                new Keyframe(0.0f, 1.0f),
                new Keyframe(50.0f, 0.7f),
                new Keyframe(120.0f, 0.4f));
        }

        if (handbrakeSidewaysBySpeed == null || handbrakeSidewaysBySpeed.length == 0)
        {
            handbrakeSidewaysBySpeed = new AnimationCurve(
                new Keyframe(0.0f, 1.0f),
                new Keyframe(40.0f, 0.7f),
                new Keyframe(90.0f, 0.4f));
        }

        if (driftKickBySpeed == null || driftKickBySpeed.length == 0)
        {
            driftKickBySpeed = new AnimationCurve(
                new Keyframe(0.0f, 0.0f),
                new Keyframe(30.0f, 0.6f),
                new Keyframe(80.0f, 1.0f));
        }

        if (powerCurve == null || powerCurve.length == 0)
        {
            powerCurve = new AnimationCurve(
                new Keyframe(0.0f, 0.7f),
                new Keyframe(0.45f, 1.0f),
                new Keyframe(0.75f, 0.9f),
                new Keyframe(1.0f, 0.75f));
        }

        if (gearbox != null)
        {
            gearbox.finalDrive = Mathf.Max(0.1f, gearbox.finalDrive);
            gearbox.reverseRatio = Mathf.Max(0.1f, gearbox.reverseRatio);
            gearbox.shiftDuration = Mathf.Max(0.01f, gearbox.shiftDuration);
            gearbox.upshiftRpm = Mathf.Max(500.0f, gearbox.upshiftRpm);
            gearbox.downshiftRpm = Mathf.Max(500.0f, gearbox.downshiftRpm);
            if (gearbox.forwardGears == null)
                gearbox.forwardGears = new List<float>();

            for (int i = 0; i < gearbox.forwardGears.Count; i++)
                gearbox.forwardGears[i] = Mathf.Max(0.1f, gearbox.forwardGears[i]);
        }

        if (engine != null)
        {
            engine.idleRpm = Mathf.Max(400.0f, engine.idleRpm);
            engine.maxRpm = Mathf.Max(engine.idleRpm + 500.0f, engine.maxRpm);
            engine.rpmResponse = Mathf.Clamp(engine.rpmResponse, 1.0f, 25.0f);
        }

        if (rb != null)
        {
            rb.mass = mass;
            UpdateCenterOfMass();
        }

        ApplySuspensionSettings();
    }

}
