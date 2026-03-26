using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed partial class ArcadePrototypeCarController : MonoBehaviour
{
    private const int WheelCount = 4;

    [Serializable]
    public struct VehicleInput
    {
        public float steer;
        public float throttle;
        public float brake;
        public float handbrake;
    }

    [Serializable]
    public struct VehicleState
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public int groundedWheels;
        public float airTime;
        public float timeSinceGrounded;
        public float landingGripBlend;
        public bool wasGroundedLastFrame;
        public SimulationState simulation;
    }

    [Serializable]
    private struct VehicleInputs
    {
        public float Motor;
        public float Steer;
        public bool Brake;
        public bool Handbrake;
    }

    [Serializable]
    private struct WheelBinding
    {
        public string name;
        public Transform hardpoint;
        public Transform visualRoot;
        public bool drive;
        public bool steer;
        public bool handbrake;
        public Vector3 baseHardpointLocalPosition;
        public Quaternion baseHardpointLocalRotation;
        public Quaternion baseVisualRotation;
    }

    [Serializable]
    private struct WheelRuntimeState
    {
        public bool grounded;
        public float suspensionLength;
        public float compression;
        public float compression01;
        public float spinAngle;
        public float steerAngle;
        public float springForce;
        public Vector3 contactPoint;
        public Vector3 contactNormal;
    }

    [Header("Source")]
    [SerializeField] private PlayerCar sourceCar;
    [SerializeField] private Rigidbody body;
    [SerializeField] private VehicleSettings handlingConfig;
    [SerializeField] private EngineGearboxConfig engineConfig;
    [SerializeField] private SuspensionConfig suspensionConfig;

    [Header("Physics")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private LayerMask bodyCollisionMask = ~0;
    [SerializeField, Min(0.01f)] private float suspensionRayExtraDistance = 0.2f;
    [SerializeField, Min(0.1f)] private float driveForceScale = 1.0f;
    [SerializeField, Min(0.1f)] private float lateralForceScale = 2.4f;
    [SerializeField, Min(0.1f)] private float longitudinalGripScale = 1.6f;
    [SerializeField, Min(0.1f)] private float brakeForceScale = 1.0f;
    [SerializeField, Min(0.1f)] private float stabilizerForceScale = 1.0f;
    [SerializeField, Min(0.1f)] private float compressionDampingScale = 1.0f;
    [SerializeField, Min(0.1f)] private float reboundDampingScale = 1.85f;
    [SerializeField, Range(0.0f, 1.0f)] private float maxReboundForceRatio = 0.45f;
    [SerializeField] private float centerOfMassOffsetY = -0.45f;
    [SerializeField, Min(1.0f)] private float maxAngularVelocity = 10.0f;
    [SerializeField, Range(0.2f, 1.0f)] private float wheelProbeRadiusScale = 0.86f;
    [SerializeField, Min(0.0f)] private float uprightAssist = 4.0f;
    [SerializeField, Min(0.0f)] private float uprightAssistInAir = 1.5f;
    [SerializeField, Min(0.0f)] private float yawAssist = 1.5f;
    [SerializeField, Min(0.0f)] private float extraGravityInAir = 20.0f;
    [SerializeField, Min(0.0f)] private float coyoteTime = 0.08f;
    [SerializeField, Min(0.01f)] private float landingGripBlendTime = 0.18f;
    [SerializeField, Range(0.05f, 1.0f)] private float landingGripStart = 0.25f;
    [SerializeField, Min(0.0f)] private float airPitchTorque = 1200.0f;
    [SerializeField, Min(0.0f)] private float airYawTorque = 500.0f;
    [SerializeField, Min(0.0f)] private float airRollTorque = 800.0f;
    [SerializeField, Min(0.001f)] private float collisionSkin = 0.02f;
    [SerializeField, Range(1, 6)] private int maxSweepIterations = 3;
    [SerializeField, Range(1, 8)] private int maxDepenetrationIterations = 4;
    [SerializeField] private bool disableLegacyCollisionShell = true;
    [SerializeField] private bool useLocalInput = true;

    [Header("Visuals")]
    [SerializeField] private string steeringWheelName = "Steering";
    [SerializeField] private string cameraTargetName = "CameraTarget";
    [SerializeField, Min(1.0f)] private float steeringWheelMaxRotation = 540.0f;
    [SerializeField, Min(1.0f)] private float steeringWheelResponse = 14.0f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug;

    private readonly WheelBinding[] wheelBindings = new WheelBinding[WheelCount];
    private readonly WheelRuntimeState[] wheelStates = new WheelRuntimeState[WheelCount];
    private readonly RaycastHit[] raycastHits = new RaycastHit[12];
    private readonly RaycastHit[] bodySweepHits = new RaycastHit[12];
    private readonly Collider[] overlapHits = new Collider[16];

    private SimulationState simulationState;
    private Transform bodyVisualRoot;
    private Transform steeringWheel;
    private Transform cameraTargetAnchor;
    private Vector3 bodyVisualBaseLocalPosition;
    private Quaternion bodyVisualBaseLocalRotation = Quaternion.identity;
    private Quaternion steeringWheelBaseRotation = Quaternion.identity;
    private BoxCollider bodyCollider;
    private Vector3 bodyColliderCenterLocal;
    private Vector3 bodyColliderHalfExtents;
    private Vector3 customLinearVelocity;
    private Vector3 customAngularVelocity;
    private Vector3 accumulatedForce;
    private Vector3 accumulatedTorque;
    private VehicleInput currentInput;
    private bool currentNitroInput;
    private int groundedWheels;
    private float airTime;
    private float timeSinceGrounded;
    private float landingGripBlend = 1.0f;
    private bool wasGroundedLastFrame;
    private Vector3 previousSimulationPosition;
    private Quaternion previousSimulationRotation = Quaternion.identity;
    private Vector3 currentSimulationPosition;
    private Quaternion currentSimulationRotation = Quaternion.identity;
    private float lastSimulationTime;
    private float lastSimulationDeltaTime = 0.02f;
    private bool renderPoseInitialized;

    public float SpeedKph => customLinearVelocity.magnitude * 3.6f;
    public float SpeedForward => Vector3.Dot(customLinearVelocity, transform.forward);
    public float SpeedAbs => customLinearVelocity.magnitude;
    public float CurrentRpm => simulationState.currentRpm;
    public int CurrentGear => simulationState.currentGear;
    public float ShiftTimeRemaining => simulationState.shiftTimer;
    public float CurrentGearRatio => GetGearRatio(simulationState.currentGear);
    public float MaxRpm => engineConfig != null && engineConfig.engine != null ? engineConfig.engine.maxRpm : 0.0f;
    public float IdleRpm => engineConfig != null && engineConfig.engine != null ? engineConfig.engine.idleRpm : 0.0f;
    public float EngineHorsepower => engineConfig != null ? engineConfig.horsepower : 0.0f;
    public int GroundedWheels => groundedWheels;
    public bool IsGrounded => groundedWheels > 0 || timeSinceGrounded < coyoteTime;
    public VehicleState CurrentState => CaptureState();
    public Transform CameraTarget => cameraTargetAnchor != null ? cameraTargetAnchor : transform;

    public void ConfigureResolved(
        VehicleSettings handling,
        EngineGearboxConfig engine,
        SuspensionConfig suspension,
        PlayerCar playerCar = null)
    {
        sourceCar = playerCar;
        handlingConfig = handling;
        engineConfig = engine;
        suspensionConfig = suspension;

        ResolveReferences();
        PreparePrototypeHierarchy();
        ApplyBodySetup();
        ResetSimulationState();
        ResetPresentationBaseline();
    }

    public void ConfigureFromPlayerCar(PlayerCar playerCar)
    {
        sourceCar = playerCar;
        if (sourceCar != null)
        {
            handlingConfig = sourceCar.HandlingConfig;
            engineConfig = sourceCar.EngineConfig;
            suspensionConfig = sourceCar.SuspensionConfig;
        }

        ConfigureResolved(handlingConfig, engineConfig, suspensionConfig, sourceCar);
    }

    public void ApplyRuntimeTuning(ArcadePrototypeControllerRuntimeTuning tuning)
    {
        if (tuning == null)
            return;

        tuning.Validate();
        groundMask = tuning.groundMask;
        bodyCollisionMask = tuning.bodyCollisionMask;
        suspensionRayExtraDistance = tuning.suspensionRayExtraDistance;
        driveForceScale = tuning.driveForceScale;
        lateralForceScale = tuning.lateralForceScale;
        longitudinalGripScale = tuning.longitudinalGripScale;
        brakeForceScale = tuning.brakeForceScale;
        stabilizerForceScale = tuning.stabilizerForceScale;
        compressionDampingScale = tuning.compressionDampingScale;
        reboundDampingScale = tuning.reboundDampingScale;
        maxReboundForceRatio = tuning.maxReboundForceRatio;
        centerOfMassOffsetY = tuning.centerOfMassOffsetY;
        maxAngularVelocity = tuning.maxAngularVelocity;
        wheelProbeRadiusScale = tuning.wheelProbeRadiusScale;
        uprightAssist = tuning.uprightAssist;
        uprightAssistInAir = tuning.uprightAssistInAir;
        yawAssist = tuning.yawAssist;
        extraGravityInAir = tuning.extraGravityInAir;
        coyoteTime = tuning.coyoteTime;
        landingGripBlendTime = tuning.landingGripBlendTime;
        landingGripStart = tuning.landingGripStart;
        airPitchTorque = tuning.airPitchTorque;
        airYawTorque = tuning.airYawTorque;
        airRollTorque = tuning.airRollTorque;
        collisionSkin = tuning.collisionSkin;
        maxSweepIterations = tuning.maxSweepIterations;
        maxDepenetrationIterations = tuning.maxDepenetrationIterations;
        disableLegacyCollisionShell = tuning.disableLegacyCollisionShell;
        useLocalInput = tuning.useLocalInput;
    }

    public void PrimeSpawnPose()
    {
        ResolveReferences();
        if (body == null)
            return;

        float lowestHardpointY = float.PositiveInfinity;
        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.hardpoint == null)
                continue;

            lowestHardpointY = Mathf.Min(lowestHardpointY, binding.hardpoint.localPosition.y);
        }

        if (float.IsPositiveInfinity(lowestHardpointY))
            lowestHardpointY = 0.0f;

        if (VehicleSpawnUtility.TryGetGroundHeight(transform.position, 2.0f, 8.0f, out float groundY, transform))
        {
            float desiredSpringLength = GetSuspensionRestLength();
            Vector3 position = body != null ? body.position : transform.position;
            position.y = groundY + GetWheelRadius() + desiredSpringLength - lowestHardpointY;
            if (body != null)
                body.position = position;
            transform.position = position;
        }

        SyncCustomBodyState(true);
    }

    private void Awake()
    {
        ResolveReferences();
        ApplyBodySetup();
        ResetSimulationState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        PreparePrototypeHierarchy();
        ApplyBodySetup();
        ResetSimulationState();
        ResetPresentationBaseline();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private void FixedUpdate()
    {
        if (!useLocalInput)
            return;

        currentInput = ReadLocalInput(out currentNitroInput);
        Simulate(currentInput, currentNitroInput, Time.fixedDeltaTime);
    }

    private void LateUpdate()
    {
        ApplyRenderInterpolation();
        UpdateCameraTargetAnchor();
        UpdateWheelVisuals(Time.deltaTime);
        UpdateSteeringWheelVisual(Time.deltaTime);
    }

    public void SimulateTick(CarControlFrame controlFrame, float deltaTime)
    {
        VehicleInput input = new VehicleInput
        {
            throttle = controlFrame.Motor,
            steer = controlFrame.Steer,
            brake = controlFrame.Brake ? 1.0f : 0.0f,
            handbrake = controlFrame.Handbrake ? 1.0f : 0.0f
        };
        Simulate(input, controlFrame.Nitro, deltaTime);
    }

    public void SetInput(VehicleInput input)
    {
        currentInput = SanitizeInput(input);
    }

    public void SetNitro(bool nitro)
    {
        currentNitroInput = nitro;
    }

    public void SetGear(int gear)
    {
        RequestShift(ref simulationState, gear);
    }

    public void Simulate(VehicleInput input, float deltaTime)
    {
        Simulate(input, currentNitroInput, deltaTime);
    }

    private void Simulate(VehicleInput input, bool nitroInput, float deltaTime)
    {
        if (body == null || handlingConfig == null || engineConfig == null || suspensionConfig == null)
            return;

        if (deltaTime <= 0.0f)
            return;

        RestoreVisualHierarchyToSimulationPose();
        currentInput = SanitizeInput(input);

        VehicleInputs inputs = new VehicleInputs
        {
            Motor = currentInput.throttle,
            Steer = currentInput.steer,
            Brake = currentInput.brake > 0.01f,
            Handbrake = currentInput.handbrake > 0.01f
        };

        ApplyAutoBrakeFromOppositeInput(ref inputs);
        UpdateNitro(nitroInput, inputs.Motor, deltaTime);
        UpdatePowertrain(ref simulationState, inputs, deltaTime);
        UpdateSteering(ref simulationState, inputs, deltaTime);
        UpdateDriftKick(ref simulationState, inputs, deltaTime);

        float wheelRadius = GetWheelRadius();
        int driveWheelCount = CountDriveWheels();
        float totalMotorTorque = ComputeMotorTorque(simulationState, inputs.Motor);
        float perWheelDriveForce = driveWheelCount > 0
            ? ((totalMotorTorque / driveWheelCount) / Mathf.Max(0.05f, wheelRadius)) * driveForceScale
            : 0.0f;
        float brakeForce = inputs.Brake ? handlingConfig.brakePower * brakeForceScale : 0.0f;
        float rearBrakeForce = brakeForce + (inputs.Handbrake ? handlingConfig.handbrakePower * brakeForceScale : 0.0f);

        ProbeWheels(deltaTime);
        SimulateWheels(inputs, perWheelDriveForce, brakeForce, rearBrakeForce, deltaTime);
        ApplyChassisForces(inputs, deltaTime);
        IntegrateCustomBody(deltaTime);
        UpdateStateTimers(deltaTime);
    }

    private void ResolveReferences()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<BoxCollider>();
        if (bodyCollider == null)
        {
            bodyCollider = gameObject.AddComponent<BoxCollider>();
            bodyCollider.center = new Vector3(0.0f, 0.7f, 0.0f);
            bodyCollider.size = new Vector3(1.8f, 0.7f, 4.0f);
        }
        if (sourceCar == null)
            sourceCar = GetComponent<PlayerCar>();
        if (sourceCar != null)
        {
            if (handlingConfig == null)
                handlingConfig = sourceCar.HandlingConfig;
            if (engineConfig == null)
                engineConfig = sourceCar.EngineConfig;
            if (suspensionConfig == null)
                suspensionConfig = sourceCar.SuspensionConfig;
        }

        ResolveSteeringWheel();
        ResolveBodyVisualRoot();
        ResolveCameraTargetAnchor();
        BindWheels();
        RefreshBodyColliderShape();
    }

    private void ResolveSteeringWheel()
    {
        if (steeringWheel != null)
            return;

        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (!string.Equals(all[i].name, steeringWheelName, StringComparison.Ordinal))
                continue;

            steeringWheel = all[i];
            steeringWheelBaseRotation = steeringWheel.localRotation;
            return;
        }
    }

    private void ResolveBodyVisualRoot()
    {
        if (bodyVisualRoot != null)
            return;

        bodyVisualRoot = FindNamedTransform("Body");
        if (bodyVisualRoot == null)
            return;

        bodyVisualBaseLocalPosition = bodyVisualRoot.localPosition;
        bodyVisualBaseLocalRotation = bodyVisualRoot.localRotation;
    }

    private void ResolveCameraTargetAnchor()
    {
        if (cameraTargetAnchor != null)
            return;

        Transform existing = FindNamedTransform(cameraTargetName);
        if (existing != null)
        {
            cameraTargetAnchor = existing;
            return;
        }

        GameObject anchorObject = new GameObject(cameraTargetName);
        cameraTargetAnchor = anchorObject.transform;
        cameraTargetAnchor.SetParent(transform, false);
        cameraTargetAnchor.localPosition = Vector3.zero;
        cameraTargetAnchor.localRotation = Quaternion.identity;
        cameraTargetAnchor.localScale = Vector3.one;
    }

    private void BindWheels()
    {
        BindWheel(0, "FrontLeft", true, true, false);
        BindWheel(1, "FrontRight", true, true, false);
        BindWheel(2, "RearLeft", true, false, true);
        BindWheel(3, "RearRight", true, false, true);
        UpdateDriveFlags();
    }

    private void BindWheel(int index, string name, bool drive, bool steer, bool handbrake)
    {
        WheelBinding binding = wheelBindings[index];
        if (binding.hardpoint != null && binding.visualRoot != null && string.Equals(binding.name, name, StringComparison.Ordinal))
            return;

        Transform hardpoint = FindNamedTransform(name);
        Transform visualRoot = hardpoint != null ? hardpoint.Find("VisualRoot") : null;
        if (visualRoot == null && hardpoint != null)
            visualRoot = hardpoint.Find("Visual");

        wheelBindings[index] = new WheelBinding
        {
            name = name,
            hardpoint = hardpoint,
            visualRoot = visualRoot,
            drive = drive,
            steer = steer,
            handbrake = handbrake,
            baseHardpointLocalPosition = hardpoint != null ? hardpoint.localPosition : Vector3.zero,
            baseHardpointLocalRotation = hardpoint != null ? hardpoint.localRotation : Quaternion.identity,
            baseVisualRotation = ResolveBaseVisualRotation(visualRoot)
        };
    }

    private static Quaternion ResolveBaseVisualRotation(Transform visualRoot)
    {
        if (visualRoot == null)
            return Quaternion.identity;

        if (string.Equals(visualRoot.name, "VisualRoot", StringComparison.Ordinal))
            return Quaternion.identity;

        return visualRoot.localRotation;
    }

    private Transform FindNamedTransform(string targetName)
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (string.Equals(all[i].name, targetName, StringComparison.Ordinal))
                return all[i];
        }

        return null;
    }

    private void UpdateDriveFlags()
    {
        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            bool isFront = binding.steer;
            binding.drive = IsDriveWheel(isFront);
            wheelBindings[i] = binding;
        }
    }

    private bool IsDriveWheel(bool isFront)
    {
        CarControllerBase.DriveType driveType =
            engineConfig != null ? engineConfig.driveType : CarControllerBase.DriveType.Rwd;

        switch (driveType)
        {
            case CarControllerBase.DriveType.Fwd:
                return isFront;
            case CarControllerBase.DriveType.Rwd:
                return !isFront;
            default:
                return true;
        }
    }

    private void PreparePrototypeHierarchy()
    {
        if (!disableLegacyCollisionShell)
            return;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;
            if (collider.transform == transform)
                continue;

            collider.enabled = false;
        }
    }

    private void ResetPresentationBaseline()
    {
        ResolveBodyVisualRoot();
        ResolveSteeringWheel();
        ResolveCameraTargetAnchor();
        if (bodyVisualRoot != null)
        {
            bodyVisualBaseLocalPosition = bodyVisualRoot.localPosition;
            bodyVisualBaseLocalRotation = bodyVisualRoot.localRotation;
            bodyVisualRoot.localPosition = bodyVisualBaseLocalPosition;
            bodyVisualRoot.localRotation = bodyVisualBaseLocalRotation;
        }
        if (steeringWheel != null)
            steeringWheel.localRotation = steeringWheelBaseRotation;
        if (cameraTargetAnchor != null)
        {
            cameraTargetAnchor.localPosition = Vector3.zero;
            cameraTargetAnchor.localRotation = Quaternion.identity;
        }

        for (int i = 0; i < wheelBindings.Length; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.visualRoot == null)
                continue;

            if (binding.hardpoint != null)
            {
                binding.baseHardpointLocalPosition = binding.hardpoint.localPosition;
                binding.baseHardpointLocalRotation = binding.hardpoint.localRotation;
                binding.hardpoint.localPosition = binding.baseHardpointLocalPosition;
                binding.hardpoint.localRotation = binding.baseHardpointLocalRotation;
            }
            binding.baseVisualRotation = ResolveBaseVisualRotation(binding.visualRoot);
            binding.visualRoot.localPosition = Vector3.down * GetSuspensionRestLength();
            binding.visualRoot.localRotation = binding.baseVisualRotation;
            wheelBindings[i] = binding;
        }
    }
}
