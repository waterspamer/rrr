using System.Collections.Generic;
using UnityEngine;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody))]
public abstract partial class CarControllerBase : MonoBehaviour
{
    public enum DriveType
    {
        Fwd,
        Rwd,
        Awd
    }

    [System.Serializable]
    public class GearboxSettings
    {
        [Min(0.1f)] public float finalDrive = 3.42f;
        [Min(0.1f)] public float reverseRatio = 3.1f;
        public List<float> forwardGears = new List<float> { 3.1f, 2.2f, 1.6f, 1.2f, 1.0f };
        [Min(0.01f)] public float shiftDuration = 0.2f;
        public bool automatic = true;
        [Min(500.0f)] public float upshiftRpm = 6000.0f;
        [Min(500.0f)] public float downshiftRpm = 2500.0f;
        public bool allowAutoReverse = true;
    }

    [System.Serializable]
    public class EngineSettings
    {
        [Min(400.0f)] public float idleRpm = 900.0f;
        [Min(1000.0f)] public float maxRpm = 7000.0f;
        [Range(1.0f, 25.0f)] public float rpmResponse = 8.0f;
    }

    [Header("Powertrain")]
    [SerializeField] private VehicleSettings settings;
    [SerializeField] private DriveType driveType = DriveType.Rwd;
    [SerializeField, Range(60.0f, 3000.0f)] private float horsepower = 320.0f;
    [SerializeField] private GearboxSettings gearbox = new GearboxSettings();
    [SerializeField] private EngineSettings engine = new EngineSettings();
    [SerializeField] private bool autoCreateHud = true;

    [Header("Handling")]
    [SerializeField, Range(5.0f, 60.0f)] private float maxSteerAngle = 28.0f;
    [SerializeField, Range(1.0f, 20.0f)] private float steerResponse = 8.0f;
    [SerializeField] private AnimationCurve steerBySpeed = new AnimationCurve(
        new Keyframe(0.0f, 1.0f),
        new Keyframe(50.0f, 0.7f),
        new Keyframe(120.0f, 0.4f));
    [SerializeField, Range(500.0f, 8000.0f)] private float brakePower = 2200.0f;
    [SerializeField, Range(500.0f, 12000.0f)] private float handbrakePower = 4000.0f;
    [SerializeField, Range(0.5f, 5.0f)] private float forwardFriction = 1.8f;
    [SerializeField, Range(0.5f, 5.0f)] private float sidewaysFriction = 2.0f;
    [SerializeField, Range(0.05f, 1.0f)] private float handbrakeFrictionMultiplier = 0.35f;
    [SerializeField] private AnimationCurve handbrakeSidewaysBySpeed = new AnimationCurve(
        new Keyframe(0.0f, 1.0f),
        new Keyframe(40.0f, 0.7f),
        new Keyframe(90.0f, 0.4f));
    [SerializeField, Range(0.0f, 1.0f)] private float driftKickSteerThreshold = 0.2f;
    [SerializeField, Range(0.0f, 20000.0f)] private float driftKickMaxForce = 6000.0f;
    [SerializeField, Range(0.0f, 5.0f)] private float driftKickRearOffset = 1.2f;
    [SerializeField, Range(1.0f, 20.0f)] private float driftKickResponse = 6.0f;
    [SerializeField] private AnimationCurve driftKickBySpeed = new AnimationCurve(
        new Keyframe(0.0f, 0.0f),
        new Keyframe(30.0f, 0.6f),
        new Keyframe(80.0f, 1.0f));
    [SerializeField, Range(0.0f, 10.0f)] private float lateralStability = 4.0f;
    [SerializeField, Range(0.0f, 10.0f)] private float yawStability = 2.5f;

    [Header("WheelCollider Curves")]
    [SerializeField, Range(0.1f, 5.0f)] private float forwardExtremumSlip = 1.0f;
    [SerializeField, Range(0.1f, 5.0f)] private float forwardExtremumValue = 1.0f;
    [SerializeField, Range(0.1f, 5.0f)] private float forwardAsymptoteSlip = 2.0f;
    [SerializeField, Range(0.1f, 5.0f)] private float forwardAsymptoteValue = 0.5f;
    [SerializeField, Range(0.1f, 5.0f)] private float sidewaysExtremumSlip = 1.0f;
    [SerializeField, Range(0.1f, 5.0f)] private float sidewaysExtremumValue = 1.0f;
    [SerializeField, Range(0.1f, 5.0f)] private float sidewaysAsymptoteSlip = 2.0f;
    [SerializeField, Range(0.1f, 5.0f)] private float sidewaysAsymptoteValue = 0.5f;

    [Header("Wheels")]
    [SerializeField, Range(0.15f, 1.0f)] protected float wheelRadius = 0.35f;
    [SerializeField, Range(0.05f, 0.6f)] protected float wheelWidth = 0.2f;
    [SerializeField, Range(1.0f, 30.0f)] private float wheelVisualRotationSpeed = 12.0f;

    [Header("Suspension")]
    [SerializeField, Range(0.05f, 0.5f)] private float suspensionDistance = 0.2f;
    [SerializeField, Range(1.0f, 6.0f)] private float suspensionFrequency = 3.5f;
    [SerializeField, Range(0.1f, 1.0f)] private float suspensionDamping = 0.8f;
    [SerializeField, Range(0.0f, 1.0f)] private float suspensionTargetPosition = 0.5f;
    [SerializeField, Range(0.3f, 0.7f)] private float frontWeightBias = 0.55f;
    [SerializeField, Range(0.0f, 15000.0f)] private float antiRollFront = 5000.0f;
    [SerializeField, Range(0.0f, 15000.0f)] private float antiRollRear = 4500.0f;

    [Header("Chassis")]
    [SerializeField, Range(600.0f, 2500.0f)] private float mass = 1200.0f;
    [SerializeField, Range(0.0f, 1.0f)] private float centerOfMassHeight = 0.3f;
    [SerializeField, Range(0.0f, 50.0f)] private float downforce = 0.2f;
    [SerializeField, Range(0.0f, 200.0f)] private float rollingResistance = 20.0f;
    [SerializeField, Range(0.0f, 2.0f)] private float aerodynamicDrag = 0.35f;
    [SerializeField] private AnimationCurve powerCurve = new AnimationCurve(
        new Keyframe(0.0f, 0.7f),
        new Keyframe(0.45f, 1.0f),
        new Keyframe(0.75f, 0.9f),
        new Keyframe(1.0f, 0.75f));

    [Header("Steering Wheel Visual")]
    [SerializeField] private string steeringWheelName = "Steering";
    [SerializeField, Range(90.0f, 1080.0f)] private float steeringWheelMaxRotation = 540.0f;
    [SerializeField, Range(1.0f, 30.0f)] private float steeringWheelResponse = 14.0f;


    [Header("Nitro")]
    [SerializeField] private bool nitroEnabled = true;
    [SerializeField, Range(0.0f, 1.0f)] private float nitroStart = 1.0f;
    [SerializeField, Min(0.0f)] private float nitroRegenPerSecond = 0.25f;
    [SerializeField, Min(0.0f)] private float nitroDrainPerSecond = 0.5f;
    [SerializeField, Range(1.0f, 3.0f)] private float nitroPowerMultiplier = 1.25f;
    [SerializeField, Range(1.0f, 3.0f)] private float nitroRpmResponseMultiplier = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool visualizePhysics = false;
    [SerializeField] private bool visualizePhysicsInGameView = false;
    [SerializeField, Range(0.0001f, 0.05f)] private float debugForceScale = 0.002f;
    [SerializeField] private Color debugDownforceColor = new Color(0.2f, 0.7f, 1.0f);
    [SerializeField] private Color debugDragColor = new Color(1.0f, 0.3f, 0.7f);
    [SerializeField] private Color debugStabilityColor = new Color(0.3f, 1.0f, 0.4f);
    [SerializeField] private Color debugDriftColor = new Color(1.0f, 0.9f, 0.2f);

    [Header("Input")]
    [SerializeField] private MonoBehaviour inputSourceBehaviour;

    public struct Wheel
    {
        public WheelCollider Collider;
        public Transform Visual;
        public bool Drive;
        public bool Steer;
        public bool Handbrake;
    }

    protected readonly List<Wheel> wheels = new List<Wheel>();
    protected Rigidbody rb;
    private enum GearShiftState
    {
        Ready,
        Shifting
    }

    private int currentGear = 1;
    private int requestedGear = 1;
    private float currentRpm;
    private float shiftTimer;
    private float shiftTargetRpm;
    private GearShiftState shiftState = GearShiftState.Ready;
    private float currentSteerAngle;
    private Transform steeringWheel;
    private Quaternion steeringWheelBaseRotation;
    private float currentSteeringWheelAngle;
    private float currentDriftKickForce;
    private VehicleDynamics.DebugData lastDebugData;
    private static Material debugLineMaterial;
    private float nitroAmount = 1.0f;
    private bool nitroActive;
    private bool nitroInitialized;
    private bool nitroVfxInitialized;
    private ParticleSystem[] nitroVfxSystems;
    private ICarInputSource inputSource;
    private bool inputSourceWarningShown;

    public int CurrentGear => currentGear;
    public float CurrentRpm => currentRpm;
    public float ShiftTimeRemaining => shiftTimer;
    public float CurrentGearRatio => GetGearRatio(currentGear);
    public float MaxRpm => engine.maxRpm;
    public float IdleRpm => engine.idleRpm;
    public float SpeedKph => rb != null ? rb.linearVelocity.magnitude * 3.6f : 0.0f;
    public bool InputEnabled { get; private set; } = true;
    public bool PhysicsSimulationEnabled { get; private set; } = true;
    public float NitroAmount => nitroAmount;
    public bool NitroActive => nitroActive;
    public CarControlFrame LastAppliedControlFrame { get; private set; }
    public float LastMotorTorque => lastDebugData.motorTorque;
    public float LastBrakeTorque => lastDebugData.brakeTorque;
    public float LastRearBrakeTorque => lastDebugData.rearBrakeTorque;
    public float LastSteerAngle => lastDebugData.steerAngle;
    public int WheelCount => wheels.Count;
    public bool IsRigidBodySleeping => rb != null && rb.IsSleeping();
    public int GroundedWheelCount => CountGroundedWheels();
    public bool ManualSimulationEnabled { get; private set; }

    protected virtual void Awake()
    {
        ResolveInputSource();
        ApplySettings();
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (GetComponentsInChildren<WheelCollider>().Length == 0)
            BuildCar();

        if (wheels.Count == 0)
            CacheWheels();

        CacheSteeringWheel();
        UpdateCenterOfMass();
        ApplyDriveType();
        ApplySuspensionSettings();
        EnsureDefaultGears();
        currentGear = Mathf.Clamp(currentGear, -1, Mathf.Max(1, gearbox.forwardGears.Count));
        requestedGear = currentGear;
        currentRpm = engine.idleRpm;

        // HUD is now handled by UI Toolkit (GameHudController).
        _ = autoCreateHud;
        InitializeNitro();
        InitializeNitroVfx();
    }

    protected abstract void BuildCar();

    protected virtual void FixedUpdate()
    {
        if (ManualSimulationEnabled)
            return;

        SimulateStep(CaptureControlFrame(), Time.fixedDeltaTime);
    }

    public CarControlFrame CaptureControlFrame()
    {
        CarControlFrame frame = ResolveControlFrame();
        frame.Clamp();
        return frame;
    }

    public void SimulateManualStep(CarControlFrame controlFrame, float deltaTime)
    {
        SimulateStep(controlFrame, deltaTime);
    }

    public void SetManualSimulationEnabled(bool enabled)
    {
        ManualSimulationEnabled = enabled;
    }

    public CarControllerSimulationState CaptureSimulationState()
    {
        return new CarControllerSimulationState
        {
            currentGear = currentGear,
            requestedGear = requestedGear,
            currentRpm = currentRpm,
            shiftTimer = shiftTimer,
            shiftTargetRpm = shiftTargetRpm,
            shiftState = (int)shiftState,
            currentSteerAngle = currentSteerAngle,
            currentDriftKickForce = currentDriftKickForce,
            currentSteeringWheelAngle = currentSteeringWheelAngle,
            nitroAmount = nitroAmount,
            nitroActive = nitroActive,
            nitroInitialized = nitroInitialized
        };
    }

    public void ApplySimulationState(CarControllerSimulationState state)
    {
        currentGear = state.currentGear;
        requestedGear = state.requestedGear;
        currentRpm = state.currentRpm;
        shiftTimer = state.shiftTimer;
        shiftTargetRpm = state.shiftTargetRpm;
        shiftState = Enum.IsDefined(typeof(GearShiftState), state.shiftState)
            ? (GearShiftState)state.shiftState
            : GearShiftState.Ready;
        currentSteerAngle = state.currentSteerAngle;
        currentDriftKickForce = state.currentDriftKickForce;
        currentSteeringWheelAngle = state.currentSteeringWheelAngle;
        nitroAmount = state.nitroAmount;
        nitroInitialized = state.nitroInitialized;
        SetNitroActive(state.nitroActive);
    }

    private void SimulateStep(CarControlFrame controlFrame, float deltaTime)
    {
        if (!PhysicsSimulationEnabled)
        {
            LastAppliedControlFrame = CarControlFrame.CreateBrakingFrame();
            SetNitroActive(false);
            currentDriftKickForce = 0.0f;
            lastDebugData = default;
            ClearWheelDynamics();
            return;
        }

        if (deltaTime <= 0.0f)
            deltaTime = Time.fixedDeltaTime;

        controlFrame.Clamp();
        LastAppliedControlFrame = controlFrame;

        VehicleDynamics.Inputs inputs = new VehicleDynamics.Inputs
        {
            Motor = controlFrame.Motor,
            Steer = controlFrame.Steer,
            Brake = controlFrame.Brake,
            Handbrake = controlFrame.Handbrake
        };

        ApplyAutoBrakeFromOppositeInput(ref inputs, rb);
        ApplyDriveType();
        UpdateNitro(controlFrame.Nitro, inputs.Motor, deltaTime);
        UpdatePowertrain(inputs, deltaTime);
        UpdateSteering(inputs, deltaTime);
        UpdateDriftKick(inputs, deltaTime);
        float motorTorque = ComputeMotorTorque(inputs.Motor);

        VehicleDynamics.Params parameters = new VehicleDynamics.Params
        {
            brakePower = brakePower,
            handbrakePower = handbrakePower,
            forwardFriction = forwardFriction,
            sidewaysFriction = sidewaysFriction,
            handbrakeFrictionMultiplier = handbrakeFrictionMultiplier,
            downforce = downforce,
            antiRollFront = antiRollFront,
            antiRollRear = antiRollRear,
            rollingResistance = rollingResistance,
            aerodynamicDrag = aerodynamicDrag,
            lateralStability = lateralStability,
            yawStability = yawStability,
            handbrakeSidewaysBySpeed = handbrakeSidewaysBySpeed,
            speedKph = SpeedKph,
            driftKickForce = currentDriftKickForce,
            driftKickRearOffset = driftKickRearOffset,
            driftKickSteerInput = inputs.Steer,
            forwardExtremumSlip = forwardExtremumSlip,
            forwardExtremumValue = forwardExtremumValue,
            forwardAsymptoteSlip = forwardAsymptoteSlip,
            forwardAsymptoteValue = forwardAsymptoteValue,
            sidewaysExtremumSlip = sidewaysExtremumSlip,
            sidewaysExtremumValue = sidewaysExtremumValue,
            sidewaysAsymptoteSlip = sidewaysAsymptoteSlip,
            sidewaysAsymptoteValue = sidewaysAsymptoteValue
        };

        VehicleDynamics.Apply(rb, transform, wheels, inputs, motorTorque, currentSteerAngle, parameters, ref lastDebugData);
    }

    public void SetInputEnabled(bool enabled)
    {
        InputEnabled = enabled;
    }

    public void SetPhysicsSimulationEnabled(bool enabled)
    {
        PhysicsSimulationEnabled = enabled;
        if (!enabled)
        {
            SetNitroActive(false);
            currentDriftKickForce = 0.0f;
            lastDebugData = default;
            ClearWheelDynamics();
        }
    }

    public void SetInputSource(MonoBehaviour behaviour)
    {
        inputSourceBehaviour = behaviour;
        ResolveInputSource();
    }

    protected virtual void LateUpdate()
    {
        for (int i = 0; i < wheels.Count; i++)
            UpdateWheelVisual(wheels[i]);
        UpdateSteeringWheelVisual(Time.deltaTime);
    }

    private void OnDrawGizmos()
    {
        if (!visualizePhysics || !Application.isPlaying)
            return;

        Vector3 origin = transform.position + Vector3.up * 0.2f;
        DrawDebugVector(origin, lastDebugData.downforce, debugDownforceColor);
        DrawDebugVector(origin, lastDebugData.dragForce, debugDragColor);
        DrawDebugVector(origin, lastDebugData.stabilityForce, debugStabilityColor);

        if (lastDebugData.driftApplied)
            DrawDebugVector(lastDebugData.driftForcePosition, lastDebugData.driftForce, debugDriftColor);
    }

    private void OnRenderObject()
    {
        if (!visualizePhysics || !visualizePhysicsInGameView || !Application.isPlaying)
            return;

        Camera current = Camera.current;
        if (current == null || current.cameraType != CameraType.Game)
            return;

        EnsureDebugMaterial();
        if (debugLineMaterial == null)
            return;

        debugLineMaterial.SetPass(0);
        GL.Begin(GL.LINES);
        DrawDebugLineGL(transform.position + Vector3.up * 0.2f, lastDebugData.downforce, debugDownforceColor);
        DrawDebugLineGL(transform.position + Vector3.up * 0.2f, lastDebugData.dragForce, debugDragColor);
        DrawDebugLineGL(transform.position + Vector3.up * 0.2f, lastDebugData.stabilityForce, debugStabilityColor);
        if (lastDebugData.driftApplied)
            DrawDebugLineGL(lastDebugData.driftForcePosition, lastDebugData.driftForce, debugDriftColor);
        GL.End();
    }

    private void DrawDebugLineGL(Vector3 origin, Vector3 force, Color color)
    {
        if (force.sqrMagnitude <= 0.0001f)
            return;

        GL.Color(color);
        Vector3 scaled = force * debugForceScale;
        GL.Vertex(origin);
        GL.Vertex(origin + scaled);
    }

    private void DrawDebugVector(Vector3 origin, Vector3 force, Color color)
    {
        if (force.sqrMagnitude <= 0.0001f)
            return;

        Gizmos.color = color;
        Vector3 scaled = force * debugForceScale;
        Gizmos.DrawLine(origin, origin + scaled);
        Gizmos.DrawSphere(origin + scaled, 0.05f);
    }

    private static void EnsureDebugMaterial()
    {
        if (debugLineMaterial != null)
            return;

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            return;

        debugLineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        debugLineMaterial.SetInt("_ZWrite", 0);
        debugLineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        debugLineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
    }

    protected WheelCollider CreateWheelCollider(string name, Vector3 localPosition)
    {
        GameObject wheelRoot = new GameObject(name);
        wheelRoot.transform.SetParent(transform, false);
        wheelRoot.transform.localPosition = localPosition;

        WheelCollider collider = wheelRoot.AddComponent<WheelCollider>();
        collider.radius = wheelRadius;
        collider.suspensionDistance = suspensionDistance;

        return collider;
    }

    protected Transform CreateDefaultWheelVisual(Transform parent)
    {
        Transform root = new GameObject("VisualRoot").transform;
        root.SetParent(parent, false);

        Transform visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
        visual.name = "Visual";
        visual.SetParent(root, false);
        CapsuleCollider capsule = visual.GetComponent<CapsuleCollider>();
        if (capsule != null)
            Destroy(capsule);
        visual.localRotation = Quaternion.Euler(0.0f, 0.0f, 90.0f);
        return root;
    }

    protected void RegisterWheel(WheelCollider collider, Transform visual, bool drive, bool steer, bool handbrake)
    {
        wheels.Add(new Wheel
        {
            Collider = collider,
            Visual = visual,
            Drive = drive,
            Steer = steer,
            Handbrake = handbrake
        });
    }

    protected void CreateWheel(
        string name,
        Vector3 localPosition,
        Transform visual,
        bool drive,
        bool steer,
        bool handbrake,
        bool createDefaultVisual,
        bool reparentVisual)
    {
        WheelCollider collider = CreateWheelCollider(name, localPosition);
        Transform wheelVisual = visual;

        if (wheelVisual == null && createDefaultVisual)
            wheelVisual = CreateDefaultWheelVisual(collider.transform);
        else if (wheelVisual != null && reparentVisual)
            wheelVisual.SetParent(collider.transform, true);

        RegisterWheel(collider, wheelVisual, drive, steer, handbrake);
    }

    protected Transform CreateWheelWithDefaultVisual(
        string name,
        Vector3 localPosition,
        bool drive,
        bool steer,
        bool handbrake)
    {
        WheelCollider collider = CreateWheelCollider(name, localPosition);
        Transform visual = CreateDefaultWheelVisual(collider.transform);
        RegisterWheel(collider, visual, drive, steer, handbrake);
        return visual;
    }

    protected virtual void CacheWheels()
    {
        wheels.Clear();
        WheelCollider[] colliders = GetComponentsInChildren<WheelCollider>();

        for (int i = 0; i < colliders.Length; i++)
        {
            Transform visual = colliders[i].transform.Find("VisualRoot");
            if (visual == null)
                visual = colliders[i].transform.Find("Visual");
            bool isFront = colliders[i].transform.localPosition.z > 0.0f;

            RegisterWheel(
                colliders[i],
                visual,
                IsDriveWheel(isFront),
                isFront,
                !isFront);
        }
    }

    private void ApplyDriveType()
    {
        for (int i = 0; i < wheels.Count; i++)
        {
            bool isFront = wheels[i].Collider.transform.localPosition.z > 0.0f;
            Wheel wheel = wheels[i];
            wheel.Drive = IsDriveWheel(isFront);
            wheels[i] = wheel;
        }
    }

    protected virtual void UpdateWheelVisual(Wheel wheel)
    {
        if (wheel.Visual == null)
            return;

        wheel.Collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        wheel.Visual.position = pos;
        Quaternion targetRot = rot * Quaternion.Euler(0.0f, 0.0f, 90.0f);
        float t = 1.0f - Mathf.Exp(-wheelVisualRotationSpeed * Time.deltaTime);
        wheel.Visual.rotation = Quaternion.Slerp(wheel.Visual.rotation, targetRot, t);
    }

    private void CacheSteeringWheel()
    {
        steeringWheel = FindNamedTransform(steeringWheelName);
        if (steeringWheel != null)
            steeringWheelBaseRotation = steeringWheel.localRotation;
        currentSteeringWheelAngle = 0.0f;
    }

    private Transform FindNamedTransform(string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
            return null;

        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == targetName)
                return all[i];
        }

        return null;
    }

    private void UpdateSteeringWheelVisual(float deltaTime)
    {
        if (steeringWheel == null || maxSteerAngle <= 0.01f)
            return;

        float normalized = Mathf.Clamp(currentSteerAngle / maxSteerAngle, -1.0f, 1.0f);
        float targetAngle = -normalized * steeringWheelMaxRotation;
        float t = 1.0f - Mathf.Exp(-steeringWheelResponse * deltaTime);
        currentSteeringWheelAngle = Mathf.Lerp(currentSteeringWheelAngle, targetAngle, t);
        steeringWheel.localRotation = steeringWheelBaseRotation * Quaternion.Euler(0.0f, 0.0f, currentSteeringWheelAngle);
    }

    protected virtual void UpdateCenterOfMass()
    {
        rb.centerOfMass = new Vector3(0.0f, centerOfMassHeight, 0.0f);
    }

    private void ApplySuspensionSettings()
    {
        if (rb == null || wheels.Count == 0)
            return;

        float frontBias = Mathf.Clamp01(frontWeightBias);
        float frontMass = rb.mass * frontBias;
        float rearMass = rb.mass - frontMass;
        float frontPerWheel = frontMass * 0.5f;
        float rearPerWheel = rearMass * 0.5f;
        float targetPos = Mathf.Clamp01(suspensionTargetPosition);

        for (int i = 0; i < wheels.Count; i++)
        {
            WheelCollider wheelCollider = wheels[i].Collider;
            bool isFront = wheelCollider.transform.localPosition.z > 0.0f;
            float sprungMass = isFront ? frontPerWheel : rearPerWheel;

            wheelCollider.radius = wheelRadius;
            wheelCollider.suspensionDistance = suspensionDistance;
            wheelCollider.sprungMass = sprungMass;

            float spring = CalculateSpringRate(sprungMass, suspensionFrequency);
            float damper = CalculateDamperRate(spring, sprungMass, suspensionDamping);

            JointSpring springSettings = wheelCollider.suspensionSpring;
            springSettings.spring = spring;
            springSettings.damper = damper;
            springSettings.targetPosition = targetPos;
            wheelCollider.suspensionSpring = springSettings;
        }
    }

    private static float CalculateSpringRate(float sprungMass, float frequency)
    {
        float omega = 2.0f * Mathf.PI * frequency;
        return omega * omega * sprungMass;
    }

    private static float CalculateDamperRate(float springRate, float sprungMass, float dampingRatio)
    {
        return 2.0f * dampingRatio * Mathf.Sqrt(springRate * sprungMass);
    }

    private bool IsDriveWheel(bool isFront)
    {
        switch (driveType)
        {
            case DriveType.Fwd:
                return isFront;
            case DriveType.Rwd:
                return !isFront;
            default:
                return true;
        }
    }

    private void InitializeNitro()
    {
        if (nitroInitialized)
            return;

        nitroAmount = Mathf.Clamp01(nitroStart);
        nitroInitialized = true;
    }

    private void UpdateNitro(bool input, float motorInput, float deltaTime)
    {
        if (!nitroEnabled)
        {
            SetNitroActive(false);
            return;
        }

        bool wantsNitro = input && motorInput > 0.1f && InputEnabled;
        SetNitroActive(wantsNitro && nitroAmount > 0.001f);

        float delta = nitroActive ? -nitroDrainPerSecond : nitroRegenPerSecond;
        nitroAmount = Mathf.Clamp01(nitroAmount + delta * Mathf.Max(0.0f, deltaTime));
    }

    private void InitializeNitroVfx()
    {
        if (nitroVfxInitialized)
            return;

        List<ParticleSystem> matches = new List<ParticleSystem>();
        ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            if (system != null && system.gameObject.name == "NITRO_VFX")
                matches.Add(system);
        }

        nitroVfxSystems = matches.ToArray();
        SetNitroVfx(false);
        nitroVfxInitialized = true;
    }

    private void SetNitroActive(bool active)
    {
        if (nitroActive == active)
            return;

        nitroActive = active;
        SetNitroVfx(nitroActive);
    }

    private void SetNitroVfx(bool enabled)
    {
        if (nitroVfxSystems == null || nitroVfxSystems.Length == 0)
            return;

        for (int i = 0; i < nitroVfxSystems.Length; i++)
        {
            ParticleSystem system = nitroVfxSystems[i];
            if (system == null)
                continue;

            var emission = system.emission;
            emission.enabled = enabled;

            if (enabled)
            {
                if (!system.isPlaying)
                    system.Play();
            }
            else
            {
                if (system.isPlaying)
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    private void ResolveInputSource()
    {
        if (inputSourceBehaviour == null)
        {
            inputSource = GetComponent<ICarInputSource>();
            if (inputSource is MonoBehaviour sourceBehaviour)
                inputSourceBehaviour = sourceBehaviour;
            inputSourceWarningShown = false;
            return;
        }

        inputSource = inputSourceBehaviour as ICarInputSource;
        if (inputSource == null)
        {
            if (!inputSourceWarningShown)
            {
                Debug.LogWarning("CarControllerBase: assigned input source does not implement ICarInputSource.", this);
                inputSourceWarningShown = true;
            }
            return;
        }

        inputSourceWarningShown = false;
    }

    private CarControlFrame ResolveControlFrame()
    {
        if (!InputEnabled)
            return CarControlFrame.CreateBrakingFrame();

        ResolveInputSource();
        if (inputSource != null && inputSource.TryGetControlFrame(out CarControlFrame controlFrame))
        {
            controlFrame.Clamp();
            return controlFrame;
        }

        return ReadDefaultLocalControlFrame();
    }

    private static CarControlFrame ReadDefaultLocalControlFrame()
    {
        CarControlFrame frame = default;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            frame.Motor = (Keyboard.current.wKey.isPressed ? 1.0f : 0.0f) +
                          (Keyboard.current.sKey.isPressed ? -1.0f : 0.0f);
            frame.Steer = (Keyboard.current.dKey.isPressed ? 1.0f : 0.0f) +
                          (Keyboard.current.aKey.isPressed ? -1.0f : 0.0f);
            frame.Brake = false;
            frame.Handbrake = Keyboard.current.spaceKey.isPressed;
            frame.Nitro = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            frame.Clamp();
            return frame;
        }
#else
        frame.Motor = Input.GetAxis("Vertical");
        frame.Steer = Input.GetAxis("Horizontal");
        frame.Brake = false;
        frame.Handbrake = Input.GetKey(KeyCode.Space);
        frame.Nitro = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        frame.Clamp();
        return frame;
#endif

        return frame;
    }

    private int CountGroundedWheels()
    {
        int grounded = 0;
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].Collider != null && wheels[i].Collider.GetGroundHit(out _))
                grounded += 1;
        }

        return grounded;
    }

    private void ClearWheelDynamics()
    {
        for (int i = 0; i < wheels.Count; i++)
        {
            WheelCollider collider = wheels[i].Collider;
            if (collider == null)
                continue;

            collider.motorTorque = 0.0f;
            collider.brakeTorque = 0.0f;
            collider.steerAngle = 0.0f;
        }
    }

}
