using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class ArcadePrototypeControllerRuntimeTuning
{
    [Header("Controller")]
    public LayerMask groundMask = ~0;
    public LayerMask bodyCollisionMask = ~0;
    [Min(0.01f)] public float suspensionRayExtraDistance = 0.2f;
    [Min(0.1f)] public float driveForceScale = 1.0f;
    [Min(0.1f)] public float lateralForceScale = 2.4f;
    [Min(0.1f)] public float longitudinalGripScale = 1.6f;
    [Min(0.1f)] public float brakeForceScale = 1.0f;
    [Min(0.1f)] public float stabilizerForceScale = 1.0f;
    [Min(0.1f)] public float compressionDampingScale = 1.0f;
    [Min(0.1f)] public float reboundDampingScale = 1.85f;
    [Range(0.0f, 1.0f)] public float maxReboundForceRatio = 0.45f;
    public float centerOfMassOffsetY = -0.45f;
    [Min(1.0f)] public float maxAngularVelocity = 10.0f;
    [Range(0.2f, 1.0f)] public float wheelProbeRadiusScale = 0.86f;
    [Min(0.0f)] public float uprightAssist = 4.0f;
    [Min(0.0f)] public float uprightAssistInAir = 1.5f;
    [Min(0.0f)] public float yawAssist = 1.5f;
    [Min(0.0f)] public float extraGravityInAir = 20.0f;
    [Min(0.0f)] public float coyoteTime = 0.08f;
    [Min(0.01f)] public float landingGripBlendTime = 0.18f;
    [Range(0.05f, 1.0f)] public float landingGripStart = 0.25f;
    [Min(0.0f)] public float airPitchTorque = 1200.0f;
    [Min(0.0f)] public float airYawTorque = 500.0f;
    [Min(0.0f)] public float airRollTorque = 800.0f;
    [Min(0.001f)] public float collisionSkin = 0.02f;
    [Range(1, 6)] public int maxSweepIterations = 3;
    [Range(1, 8)] public int maxDepenetrationIterations = 4;
    public bool disableLegacyCollisionShell = true;
    public bool useLocalInput = true;

    public void Validate()
    {
        if (groundMask.value == 0)
            groundMask = ~0;
        if (bodyCollisionMask.value == 0)
            bodyCollisionMask = ~0;

        suspensionRayExtraDistance = Mathf.Max(0.01f, suspensionRayExtraDistance);
        driveForceScale = Mathf.Max(0.1f, driveForceScale);
        lateralForceScale = Mathf.Max(0.1f, lateralForceScale);
        longitudinalGripScale = Mathf.Max(0.1f, longitudinalGripScale);
        brakeForceScale = Mathf.Max(0.1f, brakeForceScale);
        stabilizerForceScale = Mathf.Max(0.1f, stabilizerForceScale);
        compressionDampingScale = Mathf.Max(0.1f, compressionDampingScale);
        reboundDampingScale = Mathf.Max(0.1f, reboundDampingScale);
        maxReboundForceRatio = Mathf.Clamp01(maxReboundForceRatio);
        maxAngularVelocity = Mathf.Max(1.0f, maxAngularVelocity);
        wheelProbeRadiusScale = Mathf.Clamp(wheelProbeRadiusScale, 0.2f, 1.0f);
        uprightAssist = Mathf.Max(0.0f, uprightAssist);
        uprightAssistInAir = Mathf.Max(0.0f, uprightAssistInAir);
        yawAssist = Mathf.Max(0.0f, yawAssist);
        extraGravityInAir = Mathf.Max(0.0f, extraGravityInAir);
        coyoteTime = Mathf.Max(0.0f, coyoteTime);
        landingGripBlendTime = Mathf.Max(0.01f, landingGripBlendTime);
        landingGripStart = Mathf.Clamp(landingGripStart, 0.05f, 1.0f);
        airPitchTorque = Mathf.Max(0.0f, airPitchTorque);
        airYawTorque = Mathf.Max(0.0f, airYawTorque);
        airRollTorque = Mathf.Max(0.0f, airRollTorque);
        collisionSkin = Mathf.Max(0.001f, collisionSkin);
        maxSweepIterations = Mathf.Clamp(maxSweepIterations, 1, 6);
        maxDepenetrationIterations = Mathf.Clamp(maxDepenetrationIterations, 1, 8);
    }
}

[DisallowMultipleComponent]
public sealed class ArcadePrototypeSceneTuning : MonoBehaviour
{
    [System.Serializable]
    public sealed class ResolvedSetup
    {
        public PlayerCarConfig carConfig;
        public VehicleSettings handling;
        public BodySetConfig bodySet;
        public EngineGearboxConfig engine;
        public SuspensionConfig suspension;
        public List<CarCustomizationSelection> customizations = new List<CarCustomizationSelection>();
        public Color paint = Color.white;
        public bool hasPaint;
        public ArcadePrototypeControllerRuntimeTuning controllerTuning;
    }

    [System.Serializable]
    private sealed class LayoutTuning
    {
        public bool overrideBaseLayout;
        [Min(0.2f)] public float wheelBase = 2.4f;
        [Min(0.2f)] public float axleWidth = 1.5f;
        public float zOffset;
        public float wheelHeight = 0.35f;
        [Range(0.0f, 1.0f)] public float bodyRootHeightFactor = 0.3f;
        public bool liveWheelPositions = true;
        public bool useDefaultPaint = true;
        public Color defaultPaint = Color.white;
        public string paintProperty = "_MainColor";

        public void Validate()
        {
            wheelBase = Mathf.Max(0.2f, wheelBase);
            axleWidth = Mathf.Max(0.2f, axleWidth);
            bodyRootHeightFactor = Mathf.Clamp01(bodyRootHeightFactor);
            if (string.IsNullOrWhiteSpace(paintProperty))
                paintProperty = "_MainColor";
        }

        public void ApplyTo(PlayerCarConfig target, bool forceApply)
        {
            if (target == null)
                return;

            Validate();
            PlayerCarVisualSettings visual = target.Visual;
            if (visual == null)
                return;

            if (!overrideBaseLayout && !forceApply)
            {
                visual.Validate();
                return;
            }

            visual.wheelBase = wheelBase;
            visual.axleWidth = axleWidth;
            visual.zOffset = zOffset;
            visual.wheelHeight = wheelHeight;
            visual.bodyRootHeightFactor = bodyRootHeightFactor;
            visual.liveWheelPositions = liveWheelPositions;
            visual.useDefaultPaint = useDefaultPaint;
            visual.defaultPaint = defaultPaint;
            visual.paintProperty = paintProperty;
            visual.Validate();
        }
    }

    [System.Serializable]
    private sealed class HandlingTuning
    {
        public bool overrideBaseConfig;
        public float maxSteerAngle = 30.0f;
        public float steerResponse = 4.0f;
        public AnimationCurve steerBySpeed = new AnimationCurve(
            new Keyframe(0.0f, 1.0f),
            new Keyframe(50.0f, 0.7f),
            new Keyframe(120.0f, 0.4f));
        public float brakePower = 2600.0f;
        public float handbrakePower = 4200.0f;
        public float forwardFriction = 1.8f;
        public float sidewaysFriction = 2.2f;
        public float handbrakeFrictionMultiplier = 0.35f;
        public AnimationCurve handbrakeSidewaysBySpeed = new AnimationCurve(
            new Keyframe(0.0f, 1.0f),
            new Keyframe(40.0f, 0.7f),
            new Keyframe(90.0f, 0.4f));
        public float driftKickSteerThreshold = 0.2f;
        public float driftKickMaxForce = 8500.0f;
        public float driftKickRearOffset = 1.2f;
        public float driftKickResponse = 7.0f;
        public AnimationCurve driftKickBySpeed = new AnimationCurve(
            new Keyframe(0.0f, 0.0f),
            new Keyframe(30.0f, 0.6f),
            new Keyframe(80.0f, 1.0f));
        public float lateralStability = 4.5f;
        public float yawStability = 2.8f;
        public float wheelRadius = 0.35f;
        public float wheelWidth = 0.2f;
        public float mass = 1250.0f;
        public float downforce = 0.18f;
        public float rollingResistance = 18.0f;
        public float aerodynamicDrag = 0.32f;
        public bool nitroEnabled = true;
        public float nitroStart = 1.0f;
        public float nitroRegenPerSecond = 0.25f;
        public float nitroDrainPerSecond = 0.5f;
        public float nitroPowerMultiplier = 1.25f;
        public float nitroRpmResponseMultiplier = 1.5f;

        public void EnsureDefaults()
        {
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
        }

        public void ApplyTo(VehicleSettings target, bool forceApply)
        {
            if (target == null)
                return;

            EnsureDefaults();
            if (!overrideBaseConfig && !forceApply)
            {
                target.Validate();
                return;
            }

            target.maxSteerAngle = maxSteerAngle;
            target.steerResponse = steerResponse;
            target.steerBySpeed = CloneCurve(steerBySpeed);
            target.brakePower = brakePower;
            target.handbrakePower = handbrakePower;
            target.forwardFriction = forwardFriction;
            target.sidewaysFriction = sidewaysFriction;
            target.handbrakeFrictionMultiplier = handbrakeFrictionMultiplier;
            target.handbrakeSidewaysBySpeed = CloneCurve(handbrakeSidewaysBySpeed);
            target.driftKickSteerThreshold = driftKickSteerThreshold;
            target.driftKickMaxForce = driftKickMaxForce;
            target.driftKickRearOffset = driftKickRearOffset;
            target.driftKickResponse = driftKickResponse;
            target.driftKickBySpeed = CloneCurve(driftKickBySpeed);
            target.lateralStability = lateralStability;
            target.yawStability = yawStability;
            target.wheelRadius = wheelRadius;
            target.wheelWidth = wheelWidth;
            target.mass = mass;
            target.downforce = downforce;
            target.rollingResistance = rollingResistance;
            target.aerodynamicDrag = aerodynamicDrag;
            target.nitroEnabled = nitroEnabled;
            target.nitroStart = nitroStart;
            target.nitroRegenPerSecond = nitroRegenPerSecond;
            target.nitroDrainPerSecond = nitroDrainPerSecond;
            target.nitroPowerMultiplier = nitroPowerMultiplier;
            target.nitroRpmResponseMultiplier = nitroRpmResponseMultiplier;
            target.Validate();
        }
    }

    [System.Serializable]
    private sealed class EngineTuning
    {
        public bool overrideBaseConfig;
        public CarControllerBase.DriveType driveType = CarControllerBase.DriveType.Rwd;
        public float horsepower = 320.0f;
        public List<float> forwardGears = new List<float> { 3.1f, 2.2f, 1.6f, 1.2f, 1.0f };
        public float finalDrive = 3.42f;
        public float reverseRatio = 3.1f;
        public float shiftDuration = 0.2f;
        public bool automatic = true;
        public float upshiftRpm = 6000.0f;
        public float downshiftRpm = 2500.0f;
        public bool allowAutoReverse = true;
        public float idleRpm = 900.0f;
        public float maxRpm = 7000.0f;
        public float rpmResponse = 8.0f;
        public AnimationCurve powerCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.7f),
            new Keyframe(0.45f, 1.0f),
            new Keyframe(0.75f, 0.9f),
            new Keyframe(1.0f, 0.75f));

        public void EnsureDefaults()
        {
            if (forwardGears == null || forwardGears.Count == 0)
                forwardGears = new List<float> { 3.1f, 2.2f, 1.6f, 1.2f, 1.0f };

            if (powerCurve == null || powerCurve.length == 0)
            {
                powerCurve = new AnimationCurve(
                    new Keyframe(0.0f, 0.7f),
                    new Keyframe(0.45f, 1.0f),
                    new Keyframe(0.75f, 0.9f),
                    new Keyframe(1.0f, 0.75f));
            }
        }

        public void ApplyTo(EngineGearboxConfig target, bool forceApply)
        {
            if (target == null)
                return;

            EnsureDefaults();
            if (!overrideBaseConfig && !forceApply)
            {
                target.Validate();
                return;
            }

            target.driveType = driveType;
            target.horsepower = horsepower;
            target.powerCurve = CloneCurve(powerCurve);
            target.gearbox.finalDrive = finalDrive;
            target.gearbox.reverseRatio = reverseRatio;
            target.gearbox.shiftDuration = shiftDuration;
            target.gearbox.automatic = automatic;
            target.gearbox.upshiftRpm = upshiftRpm;
            target.gearbox.downshiftRpm = downshiftRpm;
            target.gearbox.allowAutoReverse = allowAutoReverse;
            target.gearbox.forwardGears = new List<float>(forwardGears);
            target.engine.idleRpm = idleRpm;
            target.engine.maxRpm = maxRpm;
            target.engine.rpmResponse = rpmResponse;
            target.Validate();
        }
    }

    [System.Serializable]
    private sealed class SuspensionTuning
    {
        public bool overrideBaseConfig;
        public bool applyVisualRideHeight = true;
        public float visualWheelHeight = 0.35f;
        public float suspensionDistance = 0.28f;
        public float suspensionFrequency = 2.2f;
        public float suspensionDamping = 0.65f;
        public float suspensionTargetPosition = 0.5f;
        public float frontWeightBias = 0.55f;
        public float antiRollFront = 5000.0f;
        public float antiRollRear = 4500.0f;
        public float centerOfMassHeight = 0.35f;

        public void ApplyTo(SuspensionConfig target, bool forceApply)
        {
            if (target == null)
                return;

            if (!overrideBaseConfig && !forceApply)
            {
                target.Validate();
                return;
            }

            target.applyVisualRideHeight = applyVisualRideHeight;
            target.visualWheelHeight = visualWheelHeight;
            target.suspensionDistance = suspensionDistance;
            target.suspensionFrequency = suspensionFrequency;
            target.suspensionDamping = suspensionDamping;
            target.suspensionTargetPosition = suspensionTargetPosition;
            target.frontWeightBias = frontWeightBias;
            target.antiRollFront = antiRollFront;
            target.antiRollRear = antiRollRear;
            target.centerOfMassHeight = centerOfMassHeight;
            target.Validate();
        }
    }

    [Header("Source")]
    [SerializeField] private bool useSelectedLoadoutAsBase = true;
    [SerializeField] private bool useSelectedPaint = true;

    [Header("Layout")]
    [SerializeField] private LayoutTuning layout = new LayoutTuning();

    [Header("Handling")]
    [SerializeField] private HandlingTuning handling = new HandlingTuning();

    [Header("Engine")]
    [SerializeField] private EngineTuning engine = new EngineTuning();

    [Header("Suspension")]
    [SerializeField] private SuspensionTuning suspension = new SuspensionTuning();

    [Header("Controller")]
    [SerializeField] private ArcadePrototypeControllerRuntimeTuning controller = new ArcadePrototypeControllerRuntimeTuning();

    public ResolvedSetup Resolve()
    {
        EnsureDefaults();

        PlayerCarSelection.TryGetPayload(out PlayerCarSelectionPayload payload);
        CarLoadoutConfig loadout = useSelectedLoadoutAsBase
            ? PlayerCarLoadoutUtility.ResolveLoadout(payload)
            : null;

        PlayerCarConfig baseCarConfig = useSelectedLoadoutAsBase
            ? (loadout != null && loadout.PlayerCarConfig != null
                ? loadout.PlayerCarConfig
                : PlayerCarSelection.SelectedCarConfig)
            : null;
        VehicleSettings baseHandling = useSelectedLoadoutAsBase
            ? PlayerCarLoadoutUtility.ResolveHandling(loadout, payload)
            : null;
        EngineGearboxConfig baseEngine = useSelectedLoadoutAsBase
            ? PlayerCarLoadoutUtility.ResolveEngine(loadout, payload)
            : null;
        SuspensionConfig baseSuspension = useSelectedLoadoutAsBase
            ? PlayerCarLoadoutUtility.ResolveSuspension(loadout, payload)
            : null;

        ResolvedSetup resolved = new ResolvedSetup
        {
            carConfig = CloneOrCreateCarConfig(baseCarConfig),
            handling = CloneOrCreate(baseHandling),
            engine = CloneOrCreate(baseEngine),
            suspension = CloneOrCreate(baseSuspension),
            bodySet = useSelectedLoadoutAsBase ? PlayerCarLoadoutUtility.ResolveBodySet(loadout, payload) : null,
            customizations = useSelectedLoadoutAsBase
                ? (PlayerCarLoadoutUtility.ResolveCustomizations(payload) ?? new List<CarCustomizationSelection>())
                : new List<CarCustomizationSelection>(),
            controllerTuning = CloneControllerTuning()
        };

        layout.ApplyTo(resolved.carConfig, baseCarConfig == null);
        handling.ApplyTo(resolved.handling, baseHandling == null);
        engine.ApplyTo(resolved.engine, baseEngine == null);
        suspension.ApplyTo(resolved.suspension, baseSuspension == null);

        if (useSelectedPaint && useSelectedLoadoutAsBase && PlayerCarLoadoutUtility.TryResolvePaint(loadout, payload, out Color paint))
        {
            resolved.paint = paint;
            resolved.hasPaint = true;
        }
        else
        {
            PlayerCarVisualSettings visual = resolved.carConfig != null ? resolved.carConfig.Visual : null;
            if (visual != null && visual.useDefaultPaint)
            {
                resolved.paint = visual.defaultPaint;
                resolved.hasPaint = true;
            }
        }

        return resolved;
    }

    private void OnValidate()
    {
        EnsureDefaults();
    }

    private void EnsureDefaults()
    {
        layout ??= new LayoutTuning();
        handling ??= new HandlingTuning();
        engine ??= new EngineTuning();
        suspension ??= new SuspensionTuning();
        controller ??= new ArcadePrototypeControllerRuntimeTuning();

        layout.Validate();
        handling.EnsureDefaults();
        engine.EnsureDefaults();
        controller.Validate();
    }

    private ArcadePrototypeControllerRuntimeTuning CloneControllerTuning()
    {
        ArcadePrototypeControllerRuntimeTuning clone = new ArcadePrototypeControllerRuntimeTuning
        {
            groundMask = controller.groundMask,
            bodyCollisionMask = controller.bodyCollisionMask,
            suspensionRayExtraDistance = controller.suspensionRayExtraDistance,
            driveForceScale = controller.driveForceScale,
            lateralForceScale = controller.lateralForceScale,
            longitudinalGripScale = controller.longitudinalGripScale,
            brakeForceScale = controller.brakeForceScale,
            stabilizerForceScale = controller.stabilizerForceScale,
            compressionDampingScale = controller.compressionDampingScale,
            reboundDampingScale = controller.reboundDampingScale,
            maxReboundForceRatio = controller.maxReboundForceRatio,
            centerOfMassOffsetY = controller.centerOfMassOffsetY,
            maxAngularVelocity = controller.maxAngularVelocity,
            wheelProbeRadiusScale = controller.wheelProbeRadiusScale,
            uprightAssist = controller.uprightAssist,
            uprightAssistInAir = controller.uprightAssistInAir,
            yawAssist = controller.yawAssist,
            extraGravityInAir = controller.extraGravityInAir,
            coyoteTime = controller.coyoteTime,
            landingGripBlendTime = controller.landingGripBlendTime,
            landingGripStart = controller.landingGripStart,
            airPitchTorque = controller.airPitchTorque,
            airYawTorque = controller.airYawTorque,
            airRollTorque = controller.airRollTorque,
            collisionSkin = controller.collisionSkin,
            maxSweepIterations = controller.maxSweepIterations,
            maxDepenetrationIterations = controller.maxDepenetrationIterations,
            disableLegacyCollisionShell = controller.disableLegacyCollisionShell,
            useLocalInput = controller.useLocalInput
        };
        clone.Validate();
        return clone;
    }

    private static PlayerCarConfig CloneOrCreateCarConfig(PlayerCarConfig source)
    {
        if (source != null)
        {
            PlayerCarConfig clone = Instantiate(source);
            clone.hideFlags = HideFlags.DontSave;
            return clone;
        }

        PlayerCarConfig created = ScriptableObject.CreateInstance<PlayerCarConfig>();
        created.hideFlags = HideFlags.DontSave;
        return created;
    }

    private static T CloneOrCreate<T>(T source) where T : ScriptableObject
    {
        T instance = source != null ? Instantiate(source) : ScriptableObject.CreateInstance<T>();
        instance.hideFlags = HideFlags.DontSave;
        return instance;
    }

    private static AnimationCurve CloneCurve(AnimationCurve source)
    {
        return source == null ? new AnimationCurve() : new AnimationCurve(source.keys);
    }
}
