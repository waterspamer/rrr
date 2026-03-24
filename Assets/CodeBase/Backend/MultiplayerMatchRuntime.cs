using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(250)]
[DisallowMultipleComponent]
public sealed class MultiplayerMatchRuntime : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerCar localPlayerCar;
    [SerializeField] private NetworkPlayerSpawnManager spawnManager;

    [Header("Networking")]
    [SerializeField, Min(1.0f)] private float inputSendRate = 30.0f;
    [SerializeField, Min(1.0f)] private float pingRate = 2.0f;
    [SerializeField, Min(0.1f)] private float matchSetupRetryDelay = 1.0f;

    [Header("Remote Players")]
    [SerializeField] private Material remoteFallbackMaterial;
    [SerializeField] private Color remoteFallbackColor = new Color(0.22f, 0.88f, 1.0f, 0.82f);
    [SerializeField, Min(0.01f)] private float remoteInterpolationBackTime = 0.10f;
    [SerializeField, Min(0.0f)] private float remoteExtrapolationLimit = 0.08f;
    [SerializeField, Min(2)] private int remoteSnapshotBufferSize = 32;
    [SerializeField, Min(0.5f)] private float remoteTeleportDistance = 12.0f;
    [SerializeField, Min(0.01f)] private float remoteCollisionStaleTimeout = 0.18f;
    [SerializeField, Min(0.0f)] private float remoteCollisionRecoveryDelay = 0.35f;
    [SerializeField, Min(0.0f)] private float remotePresentationSnapDistance = 0.2f;
    [SerializeField, Min(0.0f)] private float remotePresentationSnapRotation = 3.0f;
    [SerializeField, Min(0.0f)] private float remotePresentationCorrectionSmooth = 18.0f;
    [SerializeField, Min(0.0f)] private float remotePresentationMaxOffset = 2.0f;
    [SerializeField, Min(0.0f)] private float maxDepenetrationVelocity = 7.5f;
    [SerializeField] private bool forceAuthoritativeLocalPlayer = true;

    [Header("Local Prediction")]
    [SerializeField] private bool enableLocalPrediction = true;
    [SerializeField, Min(4)] private int localPredictionHistorySize = 128;
    [SerializeField, Min(0.0f)] private float localPredictionPositionDeadzone = 0.6f;
    [SerializeField, Min(0.0f)] private float localPredictionRotationDeadzone = 8.0f;
    [SerializeField, Min(0.0f)] private float localPredictionHardPositionDeadzone = 2.0f;
    [SerializeField, Min(0.0f)] private float localPredictionHardRotationDeadzone = 18.0f;
    [SerializeField, Min(1)] private int localPredictionSoftCorrectionConfirmations = 2;
    [SerializeField, Min(0)] private int localPredictionMaxReplaySteps = 32;
    [SerializeField, Min(0.0f)] private float localPredictionCameraSmoothTime = 0.05f;
    [SerializeField, Min(0.0f)] private float localPredictionCameraRotationSmooth = 12.0f;
    [SerializeField, Min(0.0f)] private float localPredictionCameraCorrectionSmoothTime = 0.12f;
    [SerializeField, Min(0.0f)] private float localPredictionCameraCorrectionRotationSmooth = 12.0f;
    [SerializeField, Min(0.0f)] private float localPredictionCameraCorrectionMaxDistance = 3.5f;
    [SerializeField, Min(0.0f)] private float localPredictionCameraCorrectionMaxRotation = 22.0f;
    [SerializeField] private bool enableLocalPresentationLayer = true;

    [Header("Debug")]
    [SerializeField] private bool drawNetworkPoseGizmos = true;
    [SerializeField, Min(0.05f)] private float poseGizmoMarkerRadius = 0.18f;
    [SerializeField, Min(0.1f)] private float poseGizmoAxisLength = 1.4f;
    [SerializeField] private Color poseGizmoServerColor = new Color(1.0f, 0.45f, 0.12f, 1.0f);
    [SerializeField] private Color poseGizmoClientColor = new Color(0.1f, 0.95f, 0.45f, 1.0f);
    [SerializeField] private bool drawNetworkInputOverlay = true;
    [SerializeField] private Vector2 inputOverlayScreenOffset = new Vector2(18.0f, 18.0f);
    [SerializeField, Min(320.0f)] private float inputOverlayWidth = 520.0f;

    private readonly Dictionary<string, RemotePlayerProxy> remotePlayers = new Dictionary<string, RemotePlayerProxy>(StringComparer.OrdinalIgnoreCase);
    private float nextInputSendTime;
    private float nextPingTime;
    private int inputSequence;
    private string localPlayerId;
    private string activeMatchId;
    private bool matchSetupRequestInFlight;
    private double lastMatchSetupAttemptLocalTime;
    private CarDamageController localDamageController;
    private readonly List<LocalWheelBinding> localWheelBindings = new List<LocalWheelBinding>(4);
    private readonly List<LocalAuthoritativeSnapshot> localAuthoritativeSnapshots = new List<LocalAuthoritativeSnapshot>(32);
    private readonly List<LocalWheelPoseState> localInterpolatedWheelStates = new List<LocalWheelPoseState>(4);
    private readonly List<LocalPredictedInputSample> localPredictedInputs = new List<LocalPredictedInputSample>(128);
    private readonly Dictionary<string, float> recentLocalPairCollisions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private const float RemoteCollisionDedupeWindow = 0.35f;
    private bool applicationHasFocus = true;
    private float focusRecoveryUntil;
    private bool localAuthoritativeMode;
    private bool localControllerModeOverridden;
    private bool localControllerEnabledBeforeOverride = true;
    private bool localControllerInputEnabledBeforeOverride = true;
    private bool localControllerPhysicsEnabledBeforeOverride = true;
    private bool localBodyModeOverridden;
    private bool localBodyKinematicBeforeOverride;
    private bool localBodyUseGravityBeforeOverride = true;
    private RigidbodyInterpolation localBodyInterpolationBeforeOverride = RigidbodyInterpolation.None;
    private VehicleServerSyncState localVehicleSyncState;
    private FollowCarCamera followCarCamera;
    private Transform localPredictionCameraTarget;
    private Vector3 localPredictionCameraVelocity;
    private Quaternion localPredictionCameraRotation = Quaternion.identity;
    private bool localPredictionCameraInitialized;
    private Vector3 localPredictionCameraCorrectionOffset;
    private Vector3 localPredictionCameraCorrectionVelocity;
    private Quaternion localPredictionCameraCorrectionRotationOffset = Quaternion.identity;
    private Transform localPresentationRoot;
    private Transform localPresentationSourceBody;
    private readonly List<LocalPresentationTransformBinding> localPresentationBodyBindings = new List<LocalPresentationTransformBinding>(64);
    private readonly List<LocalPresentationWheelBinding> localPresentationWheelBindings = new List<LocalPresentationWheelBinding>(4);
    private readonly List<LocalPresentationHiddenRendererState> localPresentationHiddenRenderers = new List<LocalPresentationHiddenRendererState>(16);
    private SimulationMode localPhysicsSimulationModeBeforeOverride = SimulationMode.FixedUpdate;
    private bool localPhysicsSimulationModeOverridden;
    private PendingLocalReconciliation pendingLocalReconciliation;
    private float localFixedDeltaTimeBeforeOverride = 0.02f;
    private bool localFixedDeltaTimeOverridden;
    private bool localSnapshotTimelineInitialized;
    private double localSnapshotServerToLocalOffset;
    private double localSnapshotTimelineLastSampleLocalTime;
    private bool localServerPoseAvailable;
    private Vector3 localServerPosition;
    private Quaternion localServerRotation = Quaternion.identity;
    private Vector3 localServerVelocity;
    private Vector3 localServerAngularVelocity;
    private int localServerWheelStateCount;
    private BackendVehicleDebugState localServerVehicleDebug;
    private double localServerSnapshotReceivedLocalTime;
    private long localServerSnapshotServerTimeMs;
    private InputDebugSnapshot localClientInputDebug;
    private InputDebugSnapshot localServerInputDebug;
    private int localPredictionCorrectionCandidateSequence = -1;
    private int localPredictionCorrectionConsecutiveCount;
    private float localPredictionAckMatchedPositionError;
    private float localPredictionAckMatchedRotationError;
    private float localPredictionLastAppliedCorrectionDistance;
    private float localPredictionLastAppliedCorrectionRotation;
    private GUIStyle inputOverlayBoxStyle;
    private GUIStyle inputOverlayTitleStyle;
    private GUIStyle inputOverlayHeaderStyle;
    private GUIStyle inputOverlayLabelStyle;
    private GUIStyle inputOverlayValueStyle;
    private const double SnapshotTimelineHardResetThresholdSeconds = 0.25d;
    private const double SnapshotTimelineMaxCorrectionPerSampleSeconds = 0.012d;
    private const double SnapshotTimelineFilterStrength = 10.0d;

    private sealed class LocalWheelBinding
    {
        public WheelCollider Collider;
        public Transform VisualRoot;
    }

    private struct LocalWheelPoseState
    {
        public bool HasPosition;
        public Vector3 Position;
        public bool HasRotation;
        public Quaternion Rotation;
    }

    private sealed class LocalAuthoritativeSnapshot
    {
        public double LocalTime;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public List<LocalWheelPoseState> WheelStates = new List<LocalWheelPoseState>(4);
    }

    private sealed class LocalPredictedInputSample
    {
        public int Sequence;
        public CarControlFrame Input;
        public ServerSyncedComponentState PredictedState;
    }

    private sealed class PendingLocalReconciliation
    {
        public int AcknowledgedSequence;
        public ServerSyncedComponentState AuthoritativeState;
    }

    private sealed class LocalPresentationTransformBinding
    {
        public Transform Source;
        public Transform Clone;
        public Renderer SourceRenderer;
        public Renderer CloneRenderer;
        public MeshFilter SourceMeshFilter;
        public MeshFilter CloneMeshFilter;
        public SkinnedMeshRenderer SourceSkinnedMesh;
        public SkinnedMeshRenderer CloneSkinnedMesh;
    }

    private sealed class LocalPresentationWheelBinding
    {
        public Transform Source;
        public Transform Clone;
    }

    private struct LocalPresentationHiddenRendererState
    {
        public Renderer Renderer;
        public bool ForceRenderingOffBefore;
    }

    private struct InputDebugSnapshot
    {
        public bool Available;
        public int Sequence;
        public long ClientTimeMs;
        public long ServerReceivedTimeMs;
        public float Throttle;
        public float Steer;
        public bool Brake;
        public bool Handbrake;
        public bool Nitro;
    }

    private struct RemoteOverlayDebugSnapshot
    {
        public bool Available;
        public string PlayerId;
        public Vector3 ClientPosition;
        public Quaternion ClientRotation;
        public Vector3 ServerPosition;
        public Quaternion ServerRotation;
        public Vector3 ServerVelocity;
        public Vector3 ServerAngularVelocity;
        public int ServerWheelStateCount;
        public int ClientWheelVisualCount;
        public InputDebugSnapshot ServerInput;
        public BackendVehicleDebugState ServerVehicleDebug;
        public double SnapshotAgeMs;
    }

    private struct LocalVehicleOverlaySnapshot
    {
        public bool Available;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public int WheelVisualCount;
        public float SpeedKph;
        public int CurrentGear;
        public int RequestedGear;
        public float CurrentRpm;
        public float ShiftTimer;
        public int ShiftState;
        public float MotorTorque;
        public float BrakeTorque;
        public float RearBrakeTorque;
        public float SteerAngle;
        public float NitroAmount;
        public bool NitroActive;
        public bool NitroInitialized;
        public int GroundedWheels;
        public int WheelCount;
        public bool InputEnabled;
        public bool Sleeping;
    }

    private bool IsUsingLocalPrediction()
    {
        return localAuthoritativeMode && enableLocalPrediction;
    }

    private bool IsUsingLocalPresentationLayer()
    {
        return IsUsingLocalPrediction() && enableLocalPresentationLayer;
    }

    private void Awake()
    {
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();
        if (spawnManager == null)
            spawnManager = FindFirstObjectByType<NetworkPlayerSpawnManager>();
        if (spawnManager == null)
            spawnManager = gameObject.AddComponent<NetworkPlayerSpawnManager>();
    }

    private async void OnEnable()
    {
        Backend.Client.MatchStateReceived -= HandleMatchStateReceived;
        Backend.Client.MatchStateReceived += HandleMatchStateReceived;
        Backend.Client.DamageStateReceived -= HandleDamageStateReceived;
        Backend.Client.DamageStateReceived += HandleDamageStateReceived;
        Backend.Client.CollisionEventReceived -= HandleCollisionEventReceived;
        Backend.Client.CollisionEventReceived += HandleCollisionEventReceived;

        Backend.Client.MatchInfoChanged -= HandleMatchInfoChanged;
        Backend.Client.MatchInfoChanged += HandleMatchInfoChanged;

        Backend.Client.RealtimeErrorReceived -= HandleRealtimeErrorReceived;
        Backend.Client.RealtimeErrorReceived += HandleRealtimeErrorReceived;

        RefreshLocalBindings();
        await EnsureRealtimeReadyAsync();
    }

    private void OnDisable()
    {
        Backend.Client.MatchStateReceived -= HandleMatchStateReceived;
        Backend.Client.DamageStateReceived -= HandleDamageStateReceived;
        Backend.Client.CollisionEventReceived -= HandleCollisionEventReceived;
        Backend.Client.MatchInfoChanged -= HandleMatchInfoChanged;
        Backend.Client.RealtimeErrorReceived -= HandleRealtimeErrorReceived;
        BindLocalDamageController(null);
        ClearLocalPoseGizmo();
        ClearInputDebugState();
        RestoreLocalControllerMode();
    }

    private void OnDrawGizmos()
    {
        if (!drawNetworkPoseGizmos || !Application.isPlaying)
            return;

        if (poseGizmoMarkerRadius <= 0.0f || poseGizmoAxisLength <= 0.0f)
            return;

        if (localPlayerCar != null && localServerPoseAvailable)
        {
            DrawPosePairGizmo(
                localPlayerCar.transform.position,
                localPlayerCar.transform.rotation,
                localServerPosition,
                localServerRotation,
                poseGizmoClientColor,
                poseGizmoServerColor,
                poseGizmoMarkerRadius,
                poseGizmoAxisLength);
        }

        foreach (RemotePlayerProxy proxy in remotePlayers.Values)
        {
            if (proxy == null ||
                !proxy.TryGetPoseGizmoState(
                    out Vector3 clientPosition,
                    out Quaternion clientRotation,
                    out Vector3 serverPosition,
                    out Quaternion serverRotation))
            {
                continue;
            }

            DrawPosePairGizmo(
                clientPosition,
                clientRotation,
                serverPosition,
                serverRotation,
                poseGizmoClientColor,
                poseGizmoServerColor,
                poseGizmoMarkerRadius,
                poseGizmoAxisLength);
        }
    }

    private void OnGUI()
    {
        if (!drawNetworkInputOverlay || !Application.isPlaying)
            return;

        BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
        if (matchInfo == null || string.IsNullOrWhiteSpace(matchInfo.match_id))
            return;

        LocalVehicleOverlaySnapshot localClientVehicle = CaptureLocalClientVehicleOverlaySnapshot();
        bool hasRemoteDebug = TryGetPrimaryRemoteOverlayDebugSnapshot(out RemoteOverlayDebugSnapshot remoteDebug);

        if (!localClientInputDebug.Available &&
            !localServerInputDebug.Available &&
            !localClientVehicle.Available &&
            !hasRemoteDebug)
        return;

        EnsureInputOverlayStyles();

        float width = Mathf.Max(760.0f, inputOverlayWidth);
        float rowHeight = 18.0f;
        float height = hasRemoteDebug ? 820.0f : 620.0f;
        Rect area = new Rect(inputOverlayScreenOffset.x, inputOverlayScreenOffset.y, width, height);
        GUI.Box(area, GUIContent.none, inputOverlayBoxStyle);

        Rect titleRect = new Rect(area.x + 12.0f, area.y + 10.0f, area.width - 24.0f, 22.0f);
        string modeLabel = localAuthoritativeMode
            ? (IsUsingLocalPrediction() ? "Authority Mode: Prediction" : "Authority Mode: Remote Drive")
            : "Authority Mode: Client Drive";
        GUI.Label(titleRect, "Sync Debug HUD  |  " + modeLabel, inputOverlayTitleStyle);

        float labelX = area.x + 14.0f;
        float clientX = area.x + 212.0f;
        float serverX = area.x + 474.0f;
        float valueWidth = 236.0f;
        float y = area.y + 38.0f;

        if (matchInfo != null)
        {
            DrawOverlaySingleRow(labelX, y, "Match", matchInfo.match_id + "  |  " + matchInfo.map_id);
            y += rowHeight;
            DrawOverlaySingleRow(
                labelX,
                y,
                "Room",
                matchInfo.room_status + "  |  tick " + matchInfo.tick_rate.ToString() + "  |  players " + (matchInfo.players != null ? matchInfo.players.Count.ToString() : "0"));
            y += rowHeight + 6.0f;
        }

        DrawOverlaySectionHeader(labelX, clientX, serverX, valueWidth, y, "Local Input");
        y += rowHeight + 2.0f;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Seq", FormatInputSequence(localClientInputDebug), FormatInputSequence(localServerInputDebug));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "SeqGap", "-", FormatSequenceGap(localClientInputDebug, localServerInputDebug));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "AckAgeMs", "-", FormatAckAgeMilliseconds(localClientInputDebug, localServerInputDebug));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Throttle", FormatInputFloat(localClientInputDebug.Throttle, localClientInputDebug.Available), FormatInputFloat(localServerInputDebug.Throttle, localServerInputDebug.Available));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Steer", FormatInputFloat(localClientInputDebug.Steer, localClientInputDebug.Available), FormatInputFloat(localServerInputDebug.Steer, localServerInputDebug.Available));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Brake", FormatInputBool(localClientInputDebug.Brake, localClientInputDebug.Available), FormatInputBool(localServerInputDebug.Brake, localServerInputDebug.Available));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Handbrake", FormatInputBool(localClientInputDebug.Handbrake, localClientInputDebug.Available), FormatInputBool(localServerInputDebug.Handbrake, localServerInputDebug.Available));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Nitro", FormatInputBool(localClientInputDebug.Nitro, localClientInputDebug.Available), FormatInputBool(localServerInputDebug.Nitro, localServerInputDebug.Available));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "ClientTime", FormatInputLong(localClientInputDebug.ClientTimeMs, localClientInputDebug.Available), FormatInputLong(localServerInputDebug.ClientTimeMs, localServerInputDebug.Available));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "ServerRecv", FormatInputLong(localClientInputDebug.ServerReceivedTimeMs, localClientInputDebug.Available), FormatInputLong(localServerInputDebug.ServerReceivedTimeMs, localServerInputDebug.Available));
        y += rowHeight + 8.0f;

        DrawOverlaySectionHeader(labelX, clientX, serverX, valueWidth, y, "Local State");
        y += rowHeight + 2.0f;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Pos", FormatVector3(localClientVehicle.Position, localClientVehicle.Available), FormatVector3(localServerPosition, localServerPoseAvailable));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Vel", FormatVector3(localClientVehicle.Velocity, localClientVehicle.Available), FormatVector3(localServerVelocity, localServerPoseAvailable));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "AngVel", FormatVector3(localClientVehicle.AngularVelocity, localClientVehicle.Available), FormatVector3(localServerAngularVelocity, localServerPoseAvailable));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "SpeedKph", FormatAvailableFloat(localClientVehicle.SpeedKph, localClientVehicle.Available), FormatAvailableFloat(localServerVehicleDebug != null ? localServerVehicleDebug.speed_kph : localServerVelocity.magnitude * 3.6f, localServerPoseAvailable));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Gear", FormatGear(localClientVehicle.CurrentGear, localClientVehicle.RequestedGear, localClientVehicle.Available), FormatGear(localServerVehicleDebug != null ? localServerVehicleDebug.current_gear : 0, localServerVehicleDebug != null ? localServerVehicleDebug.requested_gear : 0, localServerVehicleDebug != null));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "RPM", FormatAvailableFloat(localClientVehicle.CurrentRpm, localClientVehicle.Available), FormatAvailableFloat(localServerVehicleDebug != null ? localServerVehicleDebug.current_rpm : 0.0f, localServerVehicleDebug != null));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Torque", FormatTorqueSet(localClientVehicle.MotorTorque, localClientVehicle.BrakeTorque, localClientVehicle.RearBrakeTorque, localClientVehicle.Available), FormatTorqueSet(localServerVehicleDebug != null ? localServerVehicleDebug.motor_torque : 0.0f, localServerVehicleDebug != null ? localServerVehicleDebug.brake_torque : 0.0f, localServerVehicleDebug != null ? localServerVehicleDebug.rear_brake_torque : 0.0f, localServerVehicleDebug != null));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Steer/Nitro", FormatSteerNitro(localClientVehicle.SteerAngle, localClientVehicle.NitroAmount, localClientVehicle.NitroActive, localClientVehicle.Available), FormatSteerNitro(localServerVehicleDebug != null ? localServerVehicleDebug.steer_angle : 0.0f, localServerVehicleDebug != null ? localServerVehicleDebug.nitro_amount : 0.0f, localServerVehicleDebug != null && localServerVehicleDebug.nitro_active, localServerVehicleDebug != null));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Ground/Wheels", FormatGroundWheels(localClientVehicle.GroundedWheels, localClientVehicle.WheelCount, localClientVehicle.Available), FormatGroundWheels(localServerVehicleDebug != null ? localServerVehicleDebug.grounded_wheels : 0, localServerVehicleDebug != null ? localServerVehicleDebug.wheel_count : localServerWheelStateCount, localServerPoseAvailable));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Input/Sleep", FormatInputSleep(localClientVehicle.InputEnabled, localClientVehicle.Sleeping, localClientVehicle.Available), FormatInputSleep(localServerVehicleDebug != null && localServerVehicleDebug.input_enabled, localServerVehicleDebug != null && localServerVehicleDebug.sleeping, localServerVehicleDebug != null));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "PosErr", "-", FormatAvailableFloat(localClientVehicle.Available && localServerPoseAvailable ? Vector3.Distance(localClientVehicle.Position, localServerPosition) : 0.0f, localClientVehicle.Available && localServerPoseAvailable));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "RotErr", "-", FormatAvailableFloat(localClientVehicle.Available && localServerPoseAvailable ? Quaternion.Angle(localClientVehicle.Rotation, localServerRotation) : 0.0f, localClientVehicle.Available && localServerPoseAvailable));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "VelErr", "-", FormatAvailableFloat(localClientVehicle.Available && localServerPoseAvailable ? Vector3.Distance(localClientVehicle.Velocity, localServerVelocity) : 0.0f, localClientVehicle.Available && localServerPoseAvailable));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "SnapAgeMs", "-", FormatAvailableFloat(localServerSnapshotReceivedLocalTime > 0.0d ? (float)((Time.unscaledTimeAsDouble - localServerSnapshotReceivedLocalTime) * 1000.0d) : 0.0f, localServerSnapshotReceivedLocalTime > 0.0d));
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "AckMatchErr", FormatAckMatchError(localPredictionAckMatchedPositionError, localPredictionAckMatchedRotationError), "-");
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Reconcile", FormatAckMatchError(localPredictionLastAppliedCorrectionDistance, localPredictionLastAppliedCorrectionRotation), "-");
        y += rowHeight;
        DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "CorrGate", FormatCorrectionGate(localPredictionCorrectionConsecutiveCount, localPredictionSoftCorrectionConfirmations), "-");
        y += rowHeight + 8.0f;

        if (hasRemoteDebug)
        {
            DrawOverlaySectionHeader(labelX, clientX, serverX, valueWidth, y, "Remote  |  " + remoteDebug.PlayerId);
            y += rowHeight + 2.0f;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Pos", FormatVector3(remoteDebug.ClientPosition, true), FormatVector3(remoteDebug.ServerPosition, true));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Yaw", FormatAvailableFloat(remoteDebug.ClientRotation.eulerAngles.y, true), FormatAvailableFloat(remoteDebug.ServerRotation.eulerAngles.y, true));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "PosErr", "-", FormatAvailableFloat(Vector3.Distance(remoteDebug.ClientPosition, remoteDebug.ServerPosition), true));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "RotErr", "-", FormatAvailableFloat(Quaternion.Angle(remoteDebug.ClientRotation, remoteDebug.ServerRotation), true));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Vel", "-", FormatVector3(remoteDebug.ServerVelocity, true));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "WheelVis/State", remoteDebug.ClientWheelVisualCount.ToString(), remoteDebug.ServerWheelStateCount.ToString());
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "SpeedKph", "-", FormatAvailableFloat(remoteDebug.ServerVehicleDebug != null ? remoteDebug.ServerVehicleDebug.speed_kph : remoteDebug.ServerVelocity.magnitude * 3.6f, true));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Ground/Wheels", "-", FormatGroundWheels(remoteDebug.ServerVehicleDebug != null ? remoteDebug.ServerVehicleDebug.grounded_wheels : 0, remoteDebug.ServerVehicleDebug != null ? remoteDebug.ServerVehicleDebug.wheel_count : remoteDebug.ServerWheelStateCount, true));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "Input/Sleep", "-", FormatInputSleep(remoteDebug.ServerVehicleDebug != null && remoteDebug.ServerVehicleDebug.input_enabled, remoteDebug.ServerVehicleDebug != null && remoteDebug.ServerVehicleDebug.sleeping, remoteDebug.ServerVehicleDebug != null));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "ServerInput", "-", FormatInputSummary(remoteDebug.ServerInput));
            y += rowHeight;
            DrawInputOverlayRow(labelX, clientX, serverX, valueWidth, y, "SnapAgeMs", "-", FormatAvailableFloat((float)remoteDebug.SnapshotAgeMs, remoteDebug.SnapshotAgeMs >= 0.0d));
        }
    }

    private void Update()
    {
        if (!IsMultiplayerActive())
        {
            ClearInputDebugState();
            ClearLocalPoseGizmo();
            return;
        }

        ApplyLocalSpawnIfReady();
        UpdateLocalInputDebugState();

        if (localAuthoritativeMode)
        {
            if (IsUsingLocalPrediction())
                TickLocalPredictionCameraTarget(Time.unscaledDeltaTime);
            else
                TickLocalAuthoritativeState(Time.unscaledTimeAsDouble);
        }

        if (!IsUsingLocalPrediction() && Time.unscaledTime >= nextInputSendTime)
        {
            nextInputSendTime = Time.unscaledTime + (1.0f / Mathf.Max(1.0f, inputSendRate));
            _ = SendLocalInputAsync();
        }

        if (Time.unscaledTime >= nextPingTime)
        {
            nextPingTime = Time.unscaledTime + (1.0f / Mathf.Max(1.0f, pingRate));
            _ = SendPingAsync();
        }

        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        bool collisionsAllowed = applicationHasFocus && Time.unscaledTime >= focusRecoveryUntil;
        foreach (RemotePlayerProxy proxy in remotePlayers.Values)
            proxy.Tick(
                Time.unscaledTimeAsDouble,
                deltaTime,
                remoteInterpolationBackTime,
                remoteExtrapolationLimit,
                remoteTeleportDistance,
                remoteCollisionStaleTimeout,
                remoteCollisionRecoveryDelay,
                collisionsAllowed,
                remotePresentationSnapDistance,
                remotePresentationSnapRotation,
                remotePresentationCorrectionSmooth,
                remotePresentationMaxOffset);
    }

    private void FixedUpdate()
    {
        if (!IsMultiplayerActive() || !IsUsingLocalPrediction())
            return;

        SendLocalPredictedInputTick();
    }

    private void LateUpdate()
    {
        if (IsUsingLocalPresentationLayer())
            TickLocalPresentationLayer();
        else
            DestroyLocalPresentationLayer();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        applicationHasFocus = hasFocus;
        focusRecoveryUntil = hasFocus ? Time.unscaledTime + remoteCollisionRecoveryDelay : float.PositiveInfinity;

        foreach (RemotePlayerProxy proxy in remotePlayers.Values)
            proxy.SetCollisionEnabled(false);
    }

    private void OnApplicationPause(bool paused)
    {
        OnApplicationFocus(!paused);
    }

    private bool IsMultiplayerActive()
    {
        BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
        if (matchInfo == null || string.IsNullOrWhiteSpace(matchInfo.match_id))
            return false;

        activeMatchId = matchInfo.match_id;
        localPlayerId = Backend.Client.Session != null ? Backend.Client.Session.player_id : null;
        CacheMatchPlayers(matchInfo.players);
        if (ShouldFetchMatchSetup(matchInfo))
            _ = EnsureMatchSetupAsync();
        return !string.IsNullOrWhiteSpace(localPlayerId);
    }

    private async Task EnsureRealtimeReadyAsync()
    {
        try
        {
            if (Backend.Client.Session == null)
                return;

            if (!Backend.Client.IsRealtimeConnected)
                await Backend.Client.ConnectRealtimeAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("MultiplayerMatchRuntime: failed to ensure realtime connection. " + ex.Message, this);
        }
    }

    private async Task SendLocalInputAsync()
    {
        int sequence = ++inputSequence;
        await SendLocalInputAsync(
            sequence,
            CaptureLocalInput(),
            localAuthoritativeMode ? null : CaptureLocalState());
    }

    private async Task SendLocalInputAsync(int sequence, BackendCarControlInputPayload input, BackendPlayerStateSnapshot state)
    {
        if (!IsMultiplayerActive())
            return;

        try
        {
            await EnsureRealtimeReadyAsync();
            await Backend.Client.SendPlayerInputAsync(
                activeMatchId,
                sequence,
                input,
                state);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("MultiplayerMatchRuntime: failed to send player input. " + ex.Message, this);
        }
    }

    private async Task SendPingAsync()
    {
        if (!IsMultiplayerActive())
            return;

        try
        {
            await EnsureRealtimeReadyAsync();
            await Backend.Client.SendPingAsync();
        }
        catch
        {
        }
    }

    private async Task SendDamageStateAsync(BackendDamageStateMessage message)
    {
        if (message == null || !IsMultiplayerActive())
            return;

        try
        {
            await EnsureRealtimeReadyAsync();
            await Backend.Client.SendDamageStateAsync(message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("MultiplayerMatchRuntime: failed to send damage state. " + ex.Message, this);
        }
    }

    private BackendPlayerStateSnapshot CaptureLocalState()
    {
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();
        RefreshLocalBindings();

        Transform root = localPlayerCar != null ? localPlayerCar.transform : transform;
        Rigidbody body = localPlayerCar != null ? localPlayerCar.GetComponent<Rigidbody>() : null;
        if (body != null)
            body.maxDepenetrationVelocity = maxDepenetrationVelocity;

        BackendPlayerStateSnapshot snapshot = new BackendPlayerStateSnapshot
        {
            position = BackendVector3.FromVector3(root.position),
            rotation = BackendVector3.FromVector3(root.eulerAngles),
            velocity = BackendVector3.FromVector3(body != null ? body.linearVelocity : Vector3.zero),
            angular_velocity = BackendVector3.FromVector3(body != null ? body.angularVelocity : Vector3.zero)
        };

        for (int i = 0; i < localWheelBindings.Count; i++)
        {
            LocalWheelBinding binding = localWheelBindings[i];
            if (binding == null || binding.Collider == null || binding.VisualRoot == null)
                continue;
            snapshot.wheel_states.Add(new BackendWheelPose
            {
                position = BackendVector3.FromVector3(binding.VisualRoot.localPosition),
                rotation = BackendVector3.FromVector3(binding.VisualRoot.localRotation.eulerAngles)
            });
        }

        return snapshot;
    }

    private BackendCarControlInputPayload CaptureLocalInput()
    {
        CarControlFrame frame = IsUsingLocalPrediction()
            ? ReadControllerControlFrame()
            : localAuthoritativeMode
            ? ReadAuthoritativeLocalControlFrame()
            : ReadControllerControlFrame();
        frame.Clamp();
        return BackendCarControlInputPayload.FromControlFrame(frame);
    }

    private void SendLocalPredictedInputTick()
    {
        RefreshLocalBindings();
        if (localPlayerCar == null || localVehicleSyncState == null)
            return;

        CarControllerBase controller = localPlayerCar.Controller;
        if (controller == null)
            return;

        ApplyPendingLocalReconciliation();

        int sequence = ++inputSequence;
        CarControlFrame frame = controller.CaptureControlFrame();
        controller.SimulateManualStep(frame, Time.fixedDeltaTime);
        SimulatePhysicsStep(Time.fixedDeltaTime);

        ServerSyncedComponentState predictedState = localVehicleSyncState.CaptureState();
        if (predictedState != null)
        {
            localPredictedInputs.Add(new LocalPredictedInputSample
            {
                Sequence = sequence,
                Input = frame,
                PredictedState = predictedState.DeepClone()
            });
            TrimLocalPredictedInputs(int.MaxValue);
        }

        _ = SendLocalInputAsync(sequence, BackendCarControlInputPayload.FromControlFrame(frame), null);
    }

    private void TrimLocalPredictedInputs(int acknowledgedSequence)
    {
        if (acknowledgedSequence != int.MaxValue)
            localPredictedInputs.RemoveAll(sample => sample != null && sample.Sequence <= acknowledgedSequence);

        int maxSamples = Mathf.Max(8, localPredictionHistorySize);
        if (localPredictedInputs.Count > maxSamples)
            localPredictedInputs.RemoveRange(0, localPredictedInputs.Count - maxSamples);
    }

    private LocalPredictedInputSample FindPredictedInputSample(int acknowledgedSequence)
    {
        if (acknowledgedSequence <= 0 || localPredictedInputs.Count == 0)
            return null;

        LocalPredictedInputSample candidate = null;
        for (int i = 0; i < localPredictedInputs.Count; i++)
        {
            LocalPredictedInputSample sample = localPredictedInputs[i];
            if (sample == null || sample.Sequence > acknowledgedSequence)
                break;
            candidate = sample;
        }

        return candidate;
    }

    private void ApplyPendingLocalReconciliation()
    {
        if (pendingLocalReconciliation == null || localVehicleSyncState == null || localPlayerCar == null)
            return;

        CarControllerBase controller = localPlayerCar.Controller;
        if (controller == null)
        {
            pendingLocalReconciliation = null;
            return;
        }

        Transform localTransform = localPlayerCar.transform;
        Vector3 preReconcilePosition = localTransform.position;
        Quaternion preReconcileRotation = localTransform.rotation;

        List<LocalPredictedInputSample> replaySamples = new List<LocalPredictedInputSample>();
        for (int i = 0; i < localPredictedInputs.Count; i++)
        {
            LocalPredictedInputSample sample = localPredictedInputs[i];
            if (sample == null || sample.Sequence <= pendingLocalReconciliation.AcknowledgedSequence)
                continue;
            replaySamples.Add(sample);
        }

        int maxReplaySteps = Mathf.Max(0, localPredictionMaxReplaySteps);
        bool replayOverflow = maxReplaySteps > 0 && replaySamples.Count > maxReplaySteps;

        localVehicleSyncState.ApplyState(pendingLocalReconciliation.AuthoritativeState);
        Rigidbody body = localPlayerCar.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.maxDepenetrationVelocity = maxDepenetrationVelocity;
            body.WakeUp();
        }

        Physics.SyncTransforms();

        if (replayOverflow)
        {
            RecordLocalPredictionAppliedCorrection(preReconcilePosition, preReconcileRotation, localTransform.position, localTransform.rotation);
            localPredictedInputs.Clear();
            pendingLocalReconciliation = null;
            return;
        }

        for (int i = 0; i < replaySamples.Count; i++)
        {
            LocalPredictedInputSample sample = replaySamples[i];
            controller.SimulateManualStep(sample.Input, Time.fixedDeltaTime);
            SimulatePhysicsStep(Time.fixedDeltaTime);
            sample.PredictedState = localVehicleSyncState.CaptureState();
        }

        localPredictedInputs.Clear();
        localPredictedInputs.AddRange(replaySamples);
        RecordLocalPredictionAppliedCorrection(preReconcilePosition, preReconcileRotation, localTransform.position, localTransform.rotation);
        pendingLocalReconciliation = null;
    }

    private void SimulatePhysicsStep(float deltaTime)
    {
        if (deltaTime <= 0.0f)
            deltaTime = Time.fixedDeltaTime;

        if (Physics.simulationMode == SimulationMode.Script)
            Physics.Simulate(deltaTime);

        Physics.SyncTransforms();
    }

    private void RefreshLocalBindings()
    {
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();

        if (localPlayerCar != null)
        {
            localVehicleSyncState = localPlayerCar.GetComponent<VehicleServerSyncState>();
            if (localVehicleSyncState == null)
                localVehicleSyncState = localPlayerCar.gameObject.AddComponent<VehicleServerSyncState>();
        }
        else
        {
            localVehicleSyncState = null;
        }

        CarDamageController nextDamageController = localPlayerCar != null
            ? (localPlayerCar.DamageController != null ? localPlayerCar.DamageController : localPlayerCar.GetComponentInChildren<CarDamageController>(true))
            : null;
        BindLocalDamageController(nextDamageController);

        WheelCollider[] colliders = localPlayerCar != null ? localPlayerCar.GetComponentsInChildren<WheelCollider>(true) : null;
        if (colliders == null || colliders.Length == 0)
        {
            localWheelBindings.Clear();
            return;
        }

        if (localWheelBindings.Count == colliders.Length)
            return;

        Array.Sort(colliders, CompareWheelColliders);
        localWheelBindings.Clear();
        for (int i = 0; i < colliders.Length; i++)
        {
            WheelCollider collider = colliders[i];
            if (collider == null)
                continue;

            Transform visualRoot = collider.transform.Find("VisualRoot");
            if (visualRoot == null)
                visualRoot = collider.transform.Find("Visual");
            if (visualRoot == null)
                continue;

            localWheelBindings.Add(new LocalWheelBinding
            {
                Collider = collider,
                VisualRoot = visualRoot
            });
        }
    }

    private void BindLocalDamageController(CarDamageController nextController)
    {
        if (ReferenceEquals(localDamageController, nextController))
            return;

        if (localDamageController != null)
        {
            localDamageController.DamageMapChanged -= HandleLocalDamageMapChanged;
            localDamageController.NetworkVehicleCollisionDetected -= HandleLocalVehicleCollisionDetected;
        }

        localDamageController = nextController;
        if (localDamageController != null)
        {
            localDamageController.EnsureNetworkTextureReady();
            localDamageController.DamageMapChanged -= HandleLocalDamageMapChanged;
            localDamageController.DamageMapChanged += HandleLocalDamageMapChanged;
            localDamageController.NetworkVehicleCollisionDetected -= HandleLocalVehicleCollisionDetected;
            localDamageController.NetworkVehicleCollisionDetected += HandleLocalVehicleCollisionDetected;
        }
    }

    private static int CompareWheelColliders(WheelCollider left, WheelCollider right)
    {
        if (left == null || right == null)
            return 0;

        Vector3 leftLocal = left.transform.localPosition;
        Vector3 rightLocal = right.transform.localPosition;
        int zCompare = rightLocal.z.CompareTo(leftLocal.z);
        return zCompare != 0 ? zCompare : leftLocal.x.CompareTo(rightLocal.x);
    }

    private void HandleMatchInfoChanged(BackendMatchInfo matchInfo)
    {
        if (matchInfo == null || string.IsNullOrWhiteSpace(matchInfo.match_id))
            return;

        activeMatchId = matchInfo.match_id;
        localPlayerId = Backend.Client.Session != null ? Backend.Client.Session.player_id : localPlayerId;
        CacheMatchPlayers(matchInfo.players);
        ConfigureLocalAuthorityMode(matchInfo);
        ApplyLocalSpawnIfReady();
        EnsureRemotePlayersSpawned();
        if (ShouldFetchMatchSetup(matchInfo))
            _ = EnsureMatchSetupAsync();
    }

    private void HandleMatchStateReceived(BackendMatchStateMessage state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.match_id))
            return;
        if (!string.IsNullOrWhiteSpace(activeMatchId) &&
            !string.Equals(activeMatchId, state.match_id, StringComparison.OrdinalIgnoreCase))
            return;

        HashSet<string> seenPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (state.players != null)
        {
            for (int i = 0; i < state.players.Count; i++)
            {
                BackendMatchPlayerState playerState = state.players[i];
                if (playerState == null || string.IsNullOrWhiteSpace(playerState.player_id))
                    continue;

                seenPlayers.Add(playerState.player_id);
                if (string.Equals(playerState.player_id, localPlayerId, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateServerInputDebugState(playerState);
                    if (localAuthoritativeMode)
                    {
                        if (IsUsingLocalPrediction())
                            ReconcileLocalPredictedState(state.server_time, playerState);
                        else
                            QueueLocalAuthoritativeState(state.server_time, playerState);
                    }
                    continue;
                }

                RemotePlayerProxy proxy = GetOrCreateRemotePlayer(playerState);
                proxy.SetTargetState(
                    state.server_time,
                    playerState.PositionVector,
                    Quaternion.Euler(playerState.RotationVector),
                    playerState.VelocityVector,
                    playerState.AngularVelocityVector,
                    playerState.wheel_states);
                proxy.UpdateServerDebugState(state.server_time, playerState);
            }
        }

        List<string> toRemove = null;
        foreach (KeyValuePair<string, RemotePlayerProxy> pair in remotePlayers)
        {
            if (seenPlayers.Contains(pair.Key))
                continue;

            if (toRemove == null)
                toRemove = new List<string>();
            toRemove.Add(pair.Key);
        }

        if (toRemove == null)
            return;

        for (int i = 0; i < toRemove.Count; i++)
        {
            string playerId = toRemove[i];
            if (!remotePlayers.TryGetValue(playerId, out RemotePlayerProxy proxy))
                continue;

            proxy.Dispose();
            remotePlayers.Remove(playerId);
        }
    }

    private void HandleDamageStateReceived(BackendDamageStateMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.match_id) || string.IsNullOrWhiteSpace(message.player_id))
            return;
        if (!string.IsNullOrWhiteSpace(activeMatchId) &&
            !string.Equals(activeMatchId, message.match_id, StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(message.player_id, localPlayerId, StringComparison.OrdinalIgnoreCase))
        {
            if (!localAuthoritativeMode)
                return;

            RefreshLocalBindings();
            if (localDamageController == null || string.IsNullOrWhiteSpace(message.map_b64))
                return;

            byte[] rawBytes;
            try
            {
                rawBytes = Convert.FromBase64String(message.map_b64);
            }
            catch
            {
                return;
            }

            localDamageController.ApplyNetworkDamageSnapshot(new CarDamageNetworkSnapshot
            {
                revision = message.revision,
                width = message.width,
                height = message.height,
                rawBytes = rawBytes,
                hasImpactPoint = message.world_point != null,
                worldPoint = message.WorldPointVector,
                hasImpactNormal = message.world_normal != null,
                worldNormal = message.WorldNormalVector
            });
            return;
        }

        RemotePlayerProxy proxy = GetOrCreateRemotePlayer(message.player_id);
        proxy?.ApplyDamage(message);
    }

    private void HandleLocalDamageMapChanged(CarDamageNetworkSnapshot snapshot)
    {
        if (localAuthoritativeMode)
            return;
        if (!IsMultiplayerActive() || snapshot == null || snapshot.rawBytes == null || snapshot.rawBytes.Length == 0)
            return;

        BackendDamageStateMessage message = new BackendDamageStateMessage
        {
            match_id = activeMatchId,
            player_id = localPlayerId,
            revision = snapshot.revision,
            width = snapshot.width,
            height = snapshot.height,
            map_b64 = Convert.ToBase64String(snapshot.rawBytes),
            world_point = snapshot.hasImpactPoint ? BackendVector3.FromVector3(snapshot.worldPoint) : null,
            world_normal = snapshot.hasImpactNormal ? BackendVector3.FromVector3(snapshot.worldNormal) : null
        };

        _ = SendDamageStateAsync(message);
        Debug.Log("MultiplayerMatchRuntime: queued damage_state revision " + message.revision + " for " + localPlayerId, this);
    }

    private void HandleLocalVehicleCollisionDetected(NetworkVehicleCollisionReport report)
    {
        if (localAuthoritativeMode)
            return;
        if (report == null || string.IsNullOrWhiteSpace(report.otherPlayerId) || string.IsNullOrWhiteSpace(localPlayerId))
            return;

        string pairKey = BuildPairKey(localPlayerId, report.otherPlayerId);
        recentLocalPairCollisions[pairKey] = Time.unscaledTime;

        BackendCollisionEventMessage message = new BackendCollisionEventMessage
        {
            match_id = activeMatchId,
            primary_player_id = localPlayerId,
            secondary_player_id = report.otherPlayerId,
            world_point = BackendVector3.FromVector3(report.worldPoint),
            world_normal = BackendVector3.FromVector3(report.worldNormal),
            relative_velocity = BackendVector3.FromVector3(report.relativeVelocity),
            impulse_vector = BackendVector3.FromVector3(report.impulseVector),
            impulse_magnitude = report.impulseMagnitude
        };

        _ = SendCollisionEventAsync(message);
    }

    private void HandleCollisionEventReceived(BackendCollisionEventMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.match_id))
            return;
        if (!string.IsNullOrWhiteSpace(activeMatchId) &&
            !string.Equals(activeMatchId, message.match_id, StringComparison.OrdinalIgnoreCase))
            return;
        if (string.IsNullOrWhiteSpace(localPlayerId))
            return;

        if (!string.Equals(message.primary_player_id, localPlayerId, StringComparison.OrdinalIgnoreCase))
            return;

        string otherPlayerId = message.secondary_player_id;
        string pairKey = BuildPairKey(localPlayerId, otherPlayerId);
        if (recentLocalPairCollisions.TryGetValue(pairKey, out float recentTime) &&
            Time.unscaledTime - recentTime <= RemoteCollisionDedupeWindow)
        {
            return;
        }

        RefreshLocalBindings();
        if (localDamageController == null)
            return;

        Vector3 relativeVelocity = message.RelativeVelocityVector;
        Vector3 normal = message.WorldNormalVector;
        if (localDamageController.ApplySyntheticCollisionDamage(
                message.WorldPointVector,
                normal,
                relativeVelocity,
                message.impulse_magnitude,
                $"network collision {message.primary_player_id}->{message.secondary_player_id}",
                notifyNetwork: true))
        {
            recentLocalPairCollisions[pairKey] = Time.unscaledTime;
        }

    }

    private void HandleRealtimeErrorReceived(BackendRealtimeErrorMessage error)
    {
        if (error == null || string.IsNullOrWhiteSpace(error.message))
            return;

        Debug.LogWarning("MultiplayerMatchRuntime: " + error.message, this);
    }

    private void CacheMatchPlayers(IReadOnlyList<BackendMatchPlayerInfo> players)
    {
        if (spawnManager == null)
            return;

        spawnManager.CachePlayers(players);
    }

    private void ApplyLocalSpawnIfReady(bool force = false)
    {
        if (spawnManager == null || string.IsNullOrWhiteSpace(localPlayerId))
            return;

        spawnManager.ApplyLocalSpawn(localPlayerId, force);
    }

    private async Task EnsureMatchSetupAsync(bool force = false)
    {
        if (string.IsNullOrWhiteSpace(activeMatchId) || matchSetupRequestInFlight)
            return;

        if (!force && !ShouldFetchMatchSetup(Backend.Client.CurrentMatchInfo))
            return;

        double localNow = Time.unscaledTimeAsDouble;
        if (!force &&
            lastMatchSetupAttemptLocalTime > 0.0d &&
            localNow - lastMatchSetupAttemptLocalTime < Math.Max(0.1f, matchSetupRetryDelay))
        {
            return;
        }

        matchSetupRequestInFlight = true;
        lastMatchSetupAttemptLocalTime = localNow;
        try
        {
            BackendMatchInfo info = await Backend.Client.GetMatchAsync(activeMatchId);
            CacheMatchPlayers(info != null ? info.players : null);
            ConfigureLocalAuthorityMode(info);
            ApplyLocalSpawnIfReady();
            EnsureRemotePlayersSpawned();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("MultiplayerMatchRuntime: failed to fetch match setup. " + ex.Message, this);
        }
        finally
        {
            matchSetupRequestInFlight = false;
        }
    }

    private bool ShouldFetchMatchSetup(BackendMatchInfo matchInfo)
    {
        if (string.IsNullOrWhiteSpace(activeMatchId))
            return false;

        if (matchInfo == null)
            return true;

        if (!string.Equals(matchInfo.match_id, activeMatchId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (matchInfo.tick_rate <= 0)
            return true;

        if (matchInfo.players == null || matchInfo.players.Count == 0)
            return true;

        return false;
    }

    private void EnsureRemotePlayersSpawned()
    {
        if (spawnManager == null)
            return;

        BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
        if (matchInfo == null || matchInfo.players == null)
            return;

        for (int i = 0; i < matchInfo.players.Count; i++)
        {
            BackendMatchPlayerInfo player = matchInfo.players[i];
            if (player == null || string.IsNullOrWhiteSpace(player.player_id))
                continue;
            if (string.Equals(player.player_id, localPlayerId, StringComparison.OrdinalIgnoreCase))
                continue;

            GetOrCreateRemotePlayer(player.player_id, player.car_config);
        }
    }

    private RemotePlayerProxy GetOrCreateRemotePlayer(BackendMatchPlayerState playerState)
    {
        if (playerState == null)
            return null;

        BackendMatchPlayerInfo matchPlayer = spawnManager != null ? spawnManager.FindPlayer(playerState.player_id) : null;
        return GetOrCreateRemotePlayer(playerState.player_id, matchPlayer != null ? matchPlayer.car_config : null);
    }

    private RemotePlayerProxy GetOrCreateRemotePlayer(string playerId, BackendCarConfigPayload fallbackCarConfig = null)
    {
        if (remotePlayers.TryGetValue(playerId, out RemotePlayerProxy existing))
        {
            existing.EnsureVisual(fallbackCarConfig);
            return existing;
        }

        BackendMatchPlayerInfo matchPlayer = spawnManager != null ? spawnManager.FindPlayer(playerId) : null;
        BackendLobbyPlayer lobbyPlayer = FindLobbyPlayer(playerId);
        RemotePlayerProxy created = new RemotePlayerProxy(
            playerId,
            matchPlayer,
            lobbyPlayer,
            fallbackCarConfig,
            remoteFallbackMaterial,
            remoteFallbackColor,
            remoteSnapshotBufferSize,
            spawnManager != null ? spawnManager.RemotePlayersRoot : null);
        remotePlayers.Add(playerId, created);
        return created;
    }

    private static BackendLobbyPlayer FindLobbyPlayer(string playerId)
    {
        BackendLobbyDetails lobby = Backend.Client.CurrentLobby;
        if (lobby == null || lobby.players == null)
            return null;

        for (int i = 0; i < lobby.players.Count; i++)
        {
            BackendLobbyPlayer player = lobby.players[i];
            if (player != null && string.Equals(player.player_id, playerId, StringComparison.OrdinalIgnoreCase))
                return player;
        }

        return null;
    }

    private async Task SendCollisionEventAsync(BackendCollisionEventMessage message)
    {
        if (message == null || !IsMultiplayerActive())
            return;

        try
        {
            await EnsureRealtimeReadyAsync();
            await Backend.Client.SendCollisionEventAsync(message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("MultiplayerMatchRuntime: failed to send collision event. " + ex.Message, this);
        }
    }

    private static string BuildPairKey(string firstPlayerId, string secondPlayerId)
    {
        if (string.IsNullOrWhiteSpace(firstPlayerId) || string.IsNullOrWhiteSpace(secondPlayerId))
            return string.Empty;

        return string.Compare(firstPlayerId, secondPlayerId, StringComparison.OrdinalIgnoreCase) <= 0
            ? firstPlayerId + "|" + secondPlayerId
            : secondPlayerId + "|" + firstPlayerId;
    }

    private CarControlFrame ReadControllerControlFrame()
    {
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();

        CarControllerBase controller = localPlayerCar != null ? localPlayerCar.Controller : null;
        return controller != null ? controller.LastAppliedControlFrame : default;
    }

    private static CarControlFrame ReadAuthoritativeLocalControlFrame()
    {
        CarControlFrame frame = default;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            frame = new CarControlFrame
            {
                Motor = (Keyboard.current.wKey.isPressed ? 1.0f : 0.0f) +
                        (Keyboard.current.sKey.isPressed ? -1.0f : 0.0f),
                Steer = (Keyboard.current.dKey.isPressed ? 1.0f : 0.0f) +
                        (Keyboard.current.aKey.isPressed ? -1.0f : 0.0f),
                Brake = false,
                Handbrake = Keyboard.current.spaceKey.isPressed,
                Nitro = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed
            };
            frame.Clamp();
            return frame;
        }
#else
        frame = new CarControlFrame
        {
            Motor = Input.GetAxis("Vertical"),
            Steer = Input.GetAxis("Horizontal"),
            Brake = false,
            Handbrake = Input.GetKey(KeyCode.Space),
            Nitro = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
        };
        frame.Clamp();
        return frame;
#endif
        return frame;
    }

    private void ConfigureLocalAuthorityMode(BackendMatchInfo matchInfo)
    {
        bool shouldUseAuthoritativeMode = forceAuthoritativeLocalPlayer &&
                                          matchInfo != null &&
                                          IsAuthoritativeRoom(matchInfo.room_status);

        bool modeChanged = localAuthoritativeMode != shouldUseAuthoritativeMode;
        localAuthoritativeMode = shouldUseAuthoritativeMode;
        UpdateLocalPredictionFixedDeltaTime(matchInfo);
        if (!modeChanged)
            return;

        RefreshLocalBindings();

        CarControllerBase controller = localPlayerCar != null ? localPlayerCar.Controller : null;
        Rigidbody body = localPlayerCar != null ? localPlayerCar.GetComponent<Rigidbody>() : null;

        if (localAuthoritativeMode)
        {
            localAuthoritativeSnapshots.Clear();
            localInterpolatedWheelStates.Clear();
            localPredictedInputs.Clear();
            ResetLocalPredictionReconciliationState();
            ResetLocalSnapshotTimeline();

            if (controller != null && !localControllerModeOverridden)
            {
                localControllerEnabledBeforeOverride = controller.enabled;
                localControllerInputEnabledBeforeOverride = controller.InputEnabled;
                localControllerPhysicsEnabledBeforeOverride = controller.PhysicsSimulationEnabled;
                localControllerModeOverridden = true;
            }

            if (controller != null)
            {
                if (IsUsingLocalPrediction())
                {
                    controller.enabled = true;
                    controller.SetInputEnabled(true);
                    controller.SetPhysicsSimulationEnabled(true);
                    controller.SetManualSimulationEnabled(true);
                }
                else
                {
                    // Fallback visual puppet path for rooms where local prediction is disabled.
                    controller.SetManualSimulationEnabled(false);
                    controller.SetInputEnabled(false);
                    controller.SetPhysicsSimulationEnabled(false);
                    controller.enabled = false;
                }
            }

            if (body != null && !localBodyModeOverridden)
            {
                localBodyKinematicBeforeOverride = body.isKinematic;
                localBodyUseGravityBeforeOverride = body.useGravity;
                localBodyInterpolationBeforeOverride = body.interpolation;
                localBodyModeOverridden = true;
            }

            if (body != null)
            {
                if (IsUsingLocalPrediction())
                {
                    body.isKinematic = false;
                    body.useGravity = true;
                    body.interpolation = RigidbodyInterpolation.None;
                    body.WakeUp();
                }
                else
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.interpolation = RigidbodyInterpolation.None;
                }

                body.maxDepenetrationVelocity = maxDepenetrationVelocity;
            }

            if (IsUsingLocalPrediction() && !localPhysicsSimulationModeOverridden)
            {
                localPhysicsSimulationModeBeforeOverride = Physics.simulationMode;
                localPhysicsSimulationModeOverridden = true;
                Physics.simulationMode = SimulationMode.Script;
            }
            else if (!IsUsingLocalPrediction() && localPhysicsSimulationModeOverridden)
            {
                Physics.simulationMode = localPhysicsSimulationModeBeforeOverride;
                localPhysicsSimulationModeOverridden = false;
            }

            if (IsUsingLocalPrediction())
            {
                EnsureLocalPredictionCameraTarget();
                EnsureLocalPresentationLayer();
            }
            else
            {
                DestroyLocalPredictionCameraTarget();
                DestroyLocalPresentationLayer();
            }
        }
        else
        {
            localAuthoritativeSnapshots.Clear();
            localInterpolatedWheelStates.Clear();
            localPredictedInputs.Clear();
            ResetLocalPredictionReconciliationState();
            ResetLocalSnapshotTimeline();
            ClearLocalPoseGizmo();

            if (controller != null && localControllerModeOverridden)
            {
                controller.enabled = localControllerEnabledBeforeOverride;
                controller.SetInputEnabled(localControllerInputEnabledBeforeOverride);
                controller.SetPhysicsSimulationEnabled(localControllerPhysicsEnabledBeforeOverride);
                controller.SetManualSimulationEnabled(false);
                localControllerModeOverridden = false;
            }

            if (body != null && localBodyModeOverridden)
            {
                body.isKinematic = localBodyKinematicBeforeOverride;
                body.useGravity = localBodyUseGravityBeforeOverride;
                body.interpolation = localBodyInterpolationBeforeOverride;
                localBodyModeOverridden = false;
            }

            if (localPhysicsSimulationModeOverridden)
            {
                Physics.simulationMode = localPhysicsSimulationModeBeforeOverride;
                localPhysicsSimulationModeOverridden = false;
            }

            DestroyLocalPredictionCameraTarget();
            DestroyLocalPresentationLayer();
        }
    }

    private void RestoreLocalControllerMode()
    {
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();

        CarControllerBase controller = localPlayerCar != null ? localPlayerCar.Controller : null;
        Rigidbody body = localPlayerCar != null ? localPlayerCar.GetComponent<Rigidbody>() : null;
        if (controller != null)
        {
            controller.enabled = localControllerEnabledBeforeOverride;
            controller.SetInputEnabled(localControllerInputEnabledBeforeOverride);
            controller.SetPhysicsSimulationEnabled(localControllerPhysicsEnabledBeforeOverride);
            controller.SetManualSimulationEnabled(false);
        }

        if (body != null && localBodyModeOverridden)
        {
            body.isKinematic = localBodyKinematicBeforeOverride;
            body.useGravity = localBodyUseGravityBeforeOverride;
            body.interpolation = localBodyInterpolationBeforeOverride;
        }

        localControllerModeOverridden = false;
        localBodyModeOverridden = false;
        localAuthoritativeSnapshots.Clear();
        localInterpolatedWheelStates.Clear();
        localPredictedInputs.Clear();
        ResetLocalPredictionReconciliationState();
        ResetLocalSnapshotTimeline();
        ClearLocalPoseGizmo();
        if (localPhysicsSimulationModeOverridden)
        {
            Physics.simulationMode = localPhysicsSimulationModeBeforeOverride;
            localPhysicsSimulationModeOverridden = false;
        }
        if (localFixedDeltaTimeOverridden)
        {
            Time.fixedDeltaTime = localFixedDeltaTimeBeforeOverride;
            localFixedDeltaTimeOverridden = false;
        }
        DestroyLocalPredictionCameraTarget();
        DestroyLocalPresentationLayer();
        localAuthoritativeMode = false;
    }

    private void ResetLocalPredictionReconciliationState()
    {
        pendingLocalReconciliation = null;
        localPredictionCorrectionCandidateSequence = -1;
        localPredictionCorrectionConsecutiveCount = 0;
        localPredictionAckMatchedPositionError = 0.0f;
        localPredictionAckMatchedRotationError = 0.0f;
        localPredictionLastAppliedCorrectionDistance = 0.0f;
        localPredictionLastAppliedCorrectionRotation = 0.0f;
        ResetLocalPredictionCameraCorrection();
    }

    private void ResetLocalPredictionCameraCorrection()
    {
        localPredictionCameraCorrectionOffset = Vector3.zero;
        localPredictionCameraCorrectionVelocity = Vector3.zero;
        localPredictionCameraCorrectionRotationOffset = Quaternion.identity;
    }

    private void ResetLocalPredictionCorrectionCandidate()
    {
        localPredictionCorrectionCandidateSequence = -1;
        localPredictionCorrectionConsecutiveCount = 0;
    }

    private bool EnsureLocalPresentationLayer()
    {
        if (!IsUsingLocalPresentationLayer())
            return false;

        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();
        if (localPlayerCar == null)
        {
            DestroyLocalPresentationLayer();
            return false;
        }

        RefreshLocalBindings();

        Transform sourceBody = localPlayerCar.transform.Find("Body");
        if (ShouldRebuildLocalPresentationLayer(sourceBody))
            RebuildLocalPresentationLayer(sourceBody);

        return localPresentationRoot != null;
    }

    private bool ShouldRebuildLocalPresentationLayer(Transform sourceBody)
    {
        if (localPresentationRoot == null)
            return true;

        if (localPresentationSourceBody != sourceBody)
            return true;

        if (sourceBody != null && localPresentationBodyBindings.Count == 0)
            return true;

        if (sourceBody == null && localPresentationBodyBindings.Count > 0)
            return true;

        if (localPresentationWheelBindings.Count != localWheelBindings.Count)
            return true;

        for (int i = 0; i < localPresentationBodyBindings.Count; i++)
        {
            LocalPresentationTransformBinding binding = localPresentationBodyBindings[i];
            if (binding == null || binding.Source == null || binding.Clone == null)
                return true;
        }

        for (int i = 0; i < localPresentationWheelBindings.Count; i++)
        {
            LocalPresentationWheelBinding binding = localPresentationWheelBindings[i];
            if (binding == null || binding.Source != localWheelBindings[i].VisualRoot || binding.Clone == null)
                return true;
        }

        return false;
    }

    private void RebuildLocalPresentationLayer(Transform sourceBody)
    {
        DestroyLocalPresentationLayer();

        if (localPlayerCar == null)
            return;

        GameObject rootObject = new GameObject("LocalPresentationLayer");
        localPresentationRoot = rootObject.transform;
        localPresentationRoot.SetParent(localPlayerCar.transform, false);
        localPresentationRoot.localPosition = Vector3.zero;
        localPresentationRoot.localRotation = Quaternion.identity;
        localPresentationRoot.localScale = Vector3.one;
        localPresentationSourceBody = sourceBody;

        if (sourceBody != null)
        {
            GameObject bodyClone = Instantiate(sourceBody.gameObject, localPresentationRoot);
            bodyClone.name = "PresentationBody";
            bodyClone.transform.localPosition = sourceBody.localPosition;
            bodyClone.transform.localRotation = sourceBody.localRotation;
            bodyClone.transform.localScale = sourceBody.localScale;
            StripLocalPresentationClone(bodyClone);
            BuildLocalPresentationBodyBindings(sourceBody, bodyClone.transform);
        }

        for (int i = 0; i < localWheelBindings.Count; i++)
        {
            LocalWheelBinding binding = localWheelBindings[i];
            if (binding == null || binding.VisualRoot == null)
                continue;

            GameObject wheelClone = Instantiate(binding.VisualRoot.gameObject, localPresentationRoot);
            wheelClone.name = (binding.Collider != null ? binding.Collider.name : binding.VisualRoot.name) + "_Presentation";
            StripLocalPresentationClone(wheelClone);
            localPresentationWheelBindings.Add(new LocalPresentationWheelBinding
            {
                Source = binding.VisualRoot,
                Clone = wheelClone.transform
            });
        }

        CacheLocalPresentationHiddenRenderers(sourceBody);
        SetLocalPresentationHiddenRenderers(true);
        TickLocalPresentationLayer();
    }

    private void BuildLocalPresentationBodyBindings(Transform sourceRoot, Transform cloneRoot)
    {
        localPresentationBodyBindings.Clear();
        if (sourceRoot == null || cloneRoot == null)
            return;

        var pending = new Queue<(Transform Source, Transform Clone)>();
        pending.Enqueue((sourceRoot, cloneRoot));
        while (pending.Count > 0)
        {
            (Transform source, Transform clone) = pending.Dequeue();
            var binding = new LocalPresentationTransformBinding
            {
                Source = source,
                Clone = clone,
                SourceRenderer = source != null ? source.GetComponent<Renderer>() : null,
                CloneRenderer = clone != null ? clone.GetComponent<Renderer>() : null,
                SourceMeshFilter = source != null ? source.GetComponent<MeshFilter>() : null,
                CloneMeshFilter = clone != null ? clone.GetComponent<MeshFilter>() : null,
                SourceSkinnedMesh = source != null ? source.GetComponent<SkinnedMeshRenderer>() : null,
                CloneSkinnedMesh = clone != null ? clone.GetComponent<SkinnedMeshRenderer>() : null
            };
            localPresentationBodyBindings.Add(binding);
            SyncLocalPresentationBodyBinding(binding);
            if (binding.SourceRenderer != null && binding.CloneRenderer != null)
                binding.CloneRenderer.materials = binding.SourceRenderer.materials;

            int childCount = Mathf.Min(source.childCount, clone.childCount);
            for (int i = 0; i < childCount; i++)
                pending.Enqueue((source.GetChild(i), clone.GetChild(i)));
        }
    }

    private void CacheLocalPresentationHiddenRenderers(Transform sourceBody)
    {
        localPresentationHiddenRenderers.Clear();
        var seenRenderers = new HashSet<Renderer>();

        void CacheRenderersFrom(Transform source)
        {
            if (source == null)
                return;

            Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !seenRenderers.Add(renderer))
                    continue;

                localPresentationHiddenRenderers.Add(new LocalPresentationHiddenRendererState
                {
                    Renderer = renderer,
                    ForceRenderingOffBefore = renderer.forceRenderingOff
                });
            }
        }

        CacheRenderersFrom(sourceBody);
        for (int i = 0; i < localWheelBindings.Count; i++)
        {
            LocalWheelBinding binding = localWheelBindings[i];
            CacheRenderersFrom(binding != null ? binding.VisualRoot : null);
        }
    }

    private void SetLocalPresentationHiddenRenderers(bool hidden)
    {
        for (int i = 0; i < localPresentationHiddenRenderers.Count; i++)
        {
            LocalPresentationHiddenRendererState state = localPresentationHiddenRenderers[i];
            if (state.Renderer == null)
                continue;

            state.Renderer.forceRenderingOff = hidden || state.ForceRenderingOffBefore;
        }
    }

    private void TickLocalPresentationLayer()
    {
        if (!EnsureLocalPresentationLayer() || localPlayerCar == null || localPresentationRoot == null)
            return;

        Transform root = localPlayerCar.transform;
        localPresentationRoot.localPosition = Quaternion.Inverse(root.rotation) * localPredictionCameraCorrectionOffset;
        localPresentationRoot.localRotation = localPredictionCameraCorrectionRotationOffset;
        localPresentationRoot.localScale = Vector3.one;

        for (int i = 0; i < localPresentationBodyBindings.Count; i++)
            SyncLocalPresentationBodyBinding(localPresentationBodyBindings[i]);

        for (int i = 0; i < localPresentationWheelBindings.Count; i++)
            SyncLocalPresentationWheelBinding(root, localPresentationWheelBindings[i]);
    }

    private void SyncLocalPresentationBodyBinding(LocalPresentationTransformBinding binding)
    {
        if (binding == null || binding.Source == null || binding.Clone == null)
            return;

        bool activeSelf = binding.Source.gameObject.activeSelf;
        if (binding.Clone.gameObject.activeSelf != activeSelf)
            binding.Clone.gameObject.SetActive(activeSelf);
        if (!activeSelf)
            return;

        binding.Clone.localPosition = binding.Source.localPosition;
        binding.Clone.localRotation = binding.Source.localRotation;
        binding.Clone.localScale = binding.Source.localScale;

        if (binding.SourceMeshFilter != null &&
            binding.CloneMeshFilter != null &&
            binding.CloneMeshFilter.sharedMesh != binding.SourceMeshFilter.sharedMesh)
        {
            binding.CloneMeshFilter.sharedMesh = binding.SourceMeshFilter.sharedMesh;
        }

        if (binding.SourceSkinnedMesh != null && binding.CloneSkinnedMesh != null)
        {
            if (binding.CloneSkinnedMesh.sharedMesh != binding.SourceSkinnedMesh.sharedMesh)
                binding.CloneSkinnedMesh.sharedMesh = binding.SourceSkinnedMesh.sharedMesh;
            binding.CloneSkinnedMesh.enabled = binding.SourceSkinnedMesh.enabled;
        }

        if (binding.SourceRenderer != null && binding.CloneRenderer != null)
            binding.CloneRenderer.enabled = binding.SourceRenderer.enabled;
    }

    private static void SyncLocalPresentationWheelBinding(Transform root, LocalPresentationWheelBinding binding)
    {
        if (root == null || binding == null || binding.Source == null || binding.Clone == null)
            return;

        binding.Clone.gameObject.SetActive(binding.Source.gameObject.activeSelf);
        if (!binding.Clone.gameObject.activeSelf)
            return;

        binding.Clone.localPosition = root.InverseTransformPoint(binding.Source.position);
        binding.Clone.localRotation = Quaternion.Inverse(root.rotation) * binding.Source.rotation;
        binding.Clone.localScale = binding.Source.lossyScale;
    }

    private void DestroyLocalPresentationLayer()
    {
        SetLocalPresentationHiddenRenderers(false);
        localPresentationHiddenRenderers.Clear();
        localPresentationSourceBody = null;
        localPresentationBodyBindings.Clear();
        localPresentationWheelBindings.Clear();

        if (localPresentationRoot != null)
            Destroy(localPresentationRoot.gameObject);

        localPresentationRoot = null;
    }

    private static void StripLocalPresentationClone(GameObject root)
    {
        if (root == null)
            return;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.Destroy(collider);
        foreach (WheelCollider wheelCollider in root.GetComponentsInChildren<WheelCollider>(true))
            UnityEngine.Object.Destroy(wheelCollider);
        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            UnityEngine.Object.Destroy(body);
        foreach (Joint joint in root.GetComponentsInChildren<Joint>(true))
            UnityEngine.Object.Destroy(joint);
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            UnityEngine.Object.Destroy(behaviour);
        foreach (AudioSource audioSource in root.GetComponentsInChildren<AudioSource>(true))
            UnityEngine.Object.Destroy(audioSource);
        foreach (ParticleSystem particleSystem in root.GetComponentsInChildren<ParticleSystem>(true))
            UnityEngine.Object.Destroy(particleSystem);
    }

    private void ResetLocalSnapshotTimeline()
    {
        localSnapshotTimelineInitialized = false;
        localSnapshotServerToLocalOffset = 0.0d;
        localSnapshotTimelineLastSampleLocalTime = 0.0d;
    }

    private void EnsureInputOverlayStyles()
    {
        if (inputOverlayBoxStyle == null)
        {
            inputOverlayBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 10, 10)
            };
        }

        if (inputOverlayTitleStyle == null)
        {
            inputOverlayTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13
            };
        }

        if (inputOverlayHeaderStyle == null)
        {
            inputOverlayHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
        }

        if (inputOverlayLabelStyle == null)
        {
            inputOverlayLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft
            };
        }

        if (inputOverlayValueStyle == null)
        {
            inputOverlayValueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                richText = false
            };
        }
    }

    private void DrawInputOverlayRow(
        float labelX,
        float clientX,
        float serverX,
        float valueWidth,
        float y,
        string label,
        string clientValue,
        string serverValue)
    {
        GUI.Label(new Rect(labelX, y, 140.0f, 18.0f), label, inputOverlayLabelStyle);
        GUI.Label(new Rect(clientX, y, valueWidth, 18.0f), clientValue, inputOverlayValueStyle);
        GUI.Label(new Rect(serverX, y, valueWidth, 18.0f), serverValue, inputOverlayValueStyle);
    }

    private void DrawOverlaySectionHeader(
        float labelX,
        float clientX,
        float serverX,
        float valueWidth,
        float y,
        string title)
    {
        GUI.Label(new Rect(labelX, y, 180.0f, 18.0f), title, inputOverlayHeaderStyle);
        GUI.Label(new Rect(clientX, y, valueWidth, 18.0f), "Client", inputOverlayHeaderStyle);
        GUI.Label(new Rect(serverX, y, valueWidth, 18.0f), "Server", inputOverlayHeaderStyle);
    }

    private void DrawOverlaySingleRow(float x, float y, string label, string value)
    {
        GUI.Label(new Rect(x, y, 96.0f, 18.0f), label, inputOverlayLabelStyle);
        GUI.Label(new Rect(x + 74.0f, y, Mathf.Max(300.0f, inputOverlayWidth - 96.0f), 18.0f), value, inputOverlayValueStyle);
    }

    private LocalVehicleOverlaySnapshot CaptureLocalClientVehicleOverlaySnapshot()
    {
        LocalVehicleOverlaySnapshot snapshot = default;
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();
        if (localPlayerCar == null)
            return snapshot;

        CarControllerBase controller = localPlayerCar.Controller != null
            ? localPlayerCar.Controller
            : localPlayerCar.GetComponent<CarControllerBase>();
        Rigidbody body = localPlayerCar.GetComponent<Rigidbody>();
        CarControllerSimulationState simulationState = controller != null ? controller.CaptureSimulationState() : default;

        snapshot.Available = controller != null || body != null;
        snapshot.Position = localPlayerCar.transform.position;
        snapshot.Rotation = localPlayerCar.transform.rotation;
        snapshot.Velocity = body != null ? body.linearVelocity : Vector3.zero;
        snapshot.AngularVelocity = body != null ? body.angularVelocity : Vector3.zero;
        snapshot.WheelVisualCount = localWheelBindings.Count;
        snapshot.SpeedKph = controller != null ? controller.SpeedKph : snapshot.Velocity.magnitude * 3.6f;
        snapshot.CurrentGear = controller != null ? controller.CurrentGear : 0;
        snapshot.RequestedGear = simulationState.requestedGear;
        snapshot.CurrentRpm = controller != null ? controller.CurrentRpm : 0.0f;
        snapshot.ShiftTimer = simulationState.shiftTimer;
        snapshot.ShiftState = simulationState.shiftState;
        snapshot.MotorTorque = controller != null ? controller.LastMotorTorque : 0.0f;
        snapshot.BrakeTorque = controller != null ? controller.LastBrakeTorque : 0.0f;
        snapshot.RearBrakeTorque = controller != null ? controller.LastRearBrakeTorque : 0.0f;
        snapshot.SteerAngle = controller != null ? controller.LastSteerAngle : simulationState.currentSteerAngle;
        snapshot.NitroAmount = simulationState.nitroAmount;
        snapshot.NitroActive = simulationState.nitroActive;
        snapshot.NitroInitialized = simulationState.nitroInitialized;
        snapshot.GroundedWheels = controller != null ? controller.GroundedWheelCount : 0;
        snapshot.WheelCount = controller != null ? controller.WheelCount : localWheelBindings.Count;
        snapshot.InputEnabled = controller != null && controller.InputEnabled;
        snapshot.Sleeping = controller != null && controller.IsRigidBodySleeping;
        return snapshot;
    }

    private bool TryGetPrimaryRemoteOverlayDebugSnapshot(out RemoteOverlayDebugSnapshot snapshot)
    {
        foreach (RemotePlayerProxy proxy in remotePlayers.Values)
        {
            if (proxy != null && proxy.TryGetOverlayDebugSnapshot(out snapshot))
                return true;
        }

        snapshot = default;
        return false;
    }

    private void UpdateLocalInputDebugState()
    {
        BackendCarControlInputPayload input = CaptureLocalInput();
        localClientInputDebug = CreateInputDebugSnapshot(
            inputSequence,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            0L,
            input);
    }

    private void UpdateServerInputDebugState(BackendMatchPlayerState playerState)
    {
        localServerInputDebug = CreateInputDebugSnapshot(
            playerState != null ? playerState.ack_input_seq : -1,
            playerState != null ? playerState.client_time : 0L,
            playerState != null ? playerState.server_received_time : 0L,
            playerState != null ? playerState.input : null);
    }

    private void ClearInputDebugState()
    {
        localClientInputDebug = default;
        localServerInputDebug = default;
    }

    private static InputDebugSnapshot CreateInputDebugSnapshot(
        int sequence,
        long clientTimeMs,
        long serverReceivedTimeMs,
        BackendCarControlInputPayload input)
    {
        return new InputDebugSnapshot
        {
            Available = input != null,
            Sequence = sequence,
            ClientTimeMs = clientTimeMs,
            ServerReceivedTimeMs = serverReceivedTimeMs,
            Throttle = input != null ? input.throttle : 0.0f,
            Steer = input != null ? input.steer : 0.0f,
            Brake = input != null && input.brake,
            Handbrake = input != null && input.handbrake,
            Nitro = input != null && input.nitro
        };
    }

    private static string FormatInputSequence(InputDebugSnapshot snapshot)
    {
        return snapshot.Available ? snapshot.Sequence.ToString() : "-";
    }

    private static string FormatInputFloat(float value, bool available)
    {
        return available ? value.ToString("0.000") : "-";
    }

    private static string FormatInputBool(bool value, bool available)
    {
        return available ? (value ? "1" : "0") : "-";
    }

    private static string FormatInputLong(long value, bool available)
    {
        return available ? value.ToString() : "-";
    }

    private static string FormatSequenceGap(InputDebugSnapshot client, InputDebugSnapshot server)
    {
        if (!client.Available || !server.Available)
            return "-";

        return Mathf.Max(0, client.Sequence - server.Sequence).ToString();
    }

    private static string FormatAckAgeMilliseconds(InputDebugSnapshot client, InputDebugSnapshot server)
    {
        if (!client.Available || !server.Available || client.ClientTimeMs <= 0L || server.ClientTimeMs <= 0L)
            return "-";

        long delta = Math.Max(0L, client.ClientTimeMs - server.ClientTimeMs);
        return delta.ToString();
    }

    private static string FormatVector3(Vector3 value, bool available)
    {
        return available
            ? string.Format("{0,7:0.00} {1,7:0.00} {2,7:0.00}", value.x, value.y, value.z)
            : "-";
    }

    private static string FormatAvailableFloat(float value, bool available)
    {
        return available ? value.ToString("0.00") : "-";
    }

    private static string FormatGear(int currentGear, int requestedGear, bool available)
    {
        return available ? currentGear.ToString() + " / " + requestedGear.ToString() : "-";
    }

    private static string FormatTorqueSet(float motorTorque, float brakeTorque, float rearBrakeTorque, bool available)
    {
        return available
            ? "M " + motorTorque.ToString("0") + "  B " + brakeTorque.ToString("0") + "  RB " + rearBrakeTorque.ToString("0")
            : "-";
    }

    private static string FormatSteerNitro(float steerAngle, float nitroAmount, bool nitroActive, bool available)
    {
        return available
            ? "S " + steerAngle.ToString("0.0") + "  N " + nitroAmount.ToString("0.00") + "  A " + (nitroActive ? "1" : "0")
            : "-";
    }

    private static string FormatGroundWheels(int groundedWheels, int wheelCount, bool available)
    {
        return available ? groundedWheels.ToString() + " / " + wheelCount.ToString() : "-";
    }

    private static string FormatInputSleep(bool inputEnabled, bool sleeping, bool available)
    {
        return available ? "I " + (inputEnabled ? "1" : "0") + "  S " + (sleeping ? "1" : "0") : "-";
    }

    private static string FormatInputSummary(InputDebugSnapshot snapshot)
    {
        return snapshot.Available
            ? "seq " + snapshot.Sequence +
              "  t " + snapshot.Throttle.ToString("0.00") +
              "  s " + snapshot.Steer.ToString("0.00") +
              "  bhn " + (snapshot.Brake ? "1" : "0") + (snapshot.Handbrake ? "1" : "0") + (snapshot.Nitro ? "1" : "0")
            : "-";
    }

    private static string FormatAckMatchError(float positionError, float rotationError)
    {
        return "P " + positionError.ToString("0.00") + "  R " + rotationError.ToString("0.00");
    }

    private static string FormatCorrectionGate(int consecutiveCount, int requiredConfirmations)
    {
        return consecutiveCount.ToString() + " / " + Mathf.Max(1, requiredConfirmations).ToString();
    }

    private void UpdateLocalServerState(long matchServerTimeMs, BackendMatchPlayerState playerState)
    {
        if (playerState == null)
            return;

        localServerPoseAvailable = true;
        localServerPosition = playerState.PositionVector;
        localServerRotation = Quaternion.Euler(playerState.RotationVector);
        localServerVelocity = playerState.VelocityVector;
        localServerAngularVelocity = playerState.AngularVelocityVector;
        localServerWheelStateCount = playerState.wheel_states != null ? playerState.wheel_states.Count : 0;
        localServerVehicleDebug = playerState.debug;
        localServerSnapshotReceivedLocalTime = Time.unscaledTimeAsDouble;
        localServerSnapshotServerTimeMs = matchServerTimeMs;
    }

    private void ClearLocalPoseGizmo()
    {
        localServerPoseAvailable = false;
        localServerPosition = Vector3.zero;
        localServerRotation = Quaternion.identity;
        localServerVelocity = Vector3.zero;
        localServerAngularVelocity = Vector3.zero;
        localServerWheelStateCount = 0;
        localServerVehicleDebug = null;
        localServerSnapshotReceivedLocalTime = 0.0d;
        localServerSnapshotServerTimeMs = 0L;
    }

    private static void DrawPosePairGizmo(
        Vector3 clientPosition,
        Quaternion clientRotation,
        Vector3 serverPosition,
        Quaternion serverRotation,
        Color clientColor,
        Color serverColor,
        float markerRadius,
        float axisLength)
    {
        Color linkColor = Color.Lerp(clientColor, serverColor, 0.5f);
        Gizmos.color = linkColor;
        Gizmos.DrawLine(serverPosition, clientPosition);
        DrawPoseGizmo(serverPosition, serverRotation, serverColor, markerRadius, axisLength, true);
        DrawPoseGizmo(clientPosition, clientRotation, clientColor, markerRadius, axisLength, false);
    }

    private static void DrawPoseGizmo(
        Vector3 position,
        Quaternion rotation,
        Color color,
        float markerRadius,
        float axisLength,
        bool wireMarker)
    {
        Gizmos.color = color;
        Vector3 markerSize = Vector3.one * (markerRadius * 2.0f);
        if (wireMarker)
            Gizmos.DrawWireCube(position, markerSize);
        else
            Gizmos.DrawSphere(position, markerRadius);

        Gizmos.DrawLine(position, position + rotation * (Vector3.forward * axisLength));
        Gizmos.DrawLine(position, position + rotation * (Vector3.right * (axisLength * 0.8f)));
        Gizmos.DrawLine(position, position + rotation * (Vector3.up * (axisLength * 0.65f)));
    }

    private void UpdateLocalPredictionFixedDeltaTime(BackendMatchInfo matchInfo)
    {
        bool shouldOverride = IsUsingLocalPrediction() && matchInfo != null && matchInfo.tick_rate > 0;
        if (!shouldOverride)
        {
            if (localFixedDeltaTimeOverridden)
            {
                Time.fixedDeltaTime = localFixedDeltaTimeBeforeOverride;
                localFixedDeltaTimeOverridden = false;
            }

            return;
        }

        float desiredFixedDeltaTime = 1.0f / Mathf.Clamp(matchInfo.tick_rate, 1, 240);
        if (!localFixedDeltaTimeOverridden)
        {
            localFixedDeltaTimeBeforeOverride = Time.fixedDeltaTime;
            localFixedDeltaTimeOverridden = true;
        }

        if (!Mathf.Approximately(Time.fixedDeltaTime, desiredFixedDeltaTime))
            Time.fixedDeltaTime = desiredFixedDeltaTime;
    }

    private void ReconcileLocalPredictedState(long matchServerTimeMs, BackendMatchPlayerState playerState)
    {
        if (playerState == null)
            return;

        UpdateLocalServerState(matchServerTimeMs, playerState);
        RefreshLocalBindings();
        if (localVehicleSyncState == null)
            return;

        LocalPredictedInputSample acknowledgedSample = FindPredictedInputSample(playerState.ack_input_seq);
        if (acknowledgedSample == null || acknowledgedSample.PredictedState == null)
        {
            ResetLocalPredictionCorrectionCandidate();
            TrimLocalPredictedInputs(playerState.ack_input_seq);
            return;
        }

        ServerSyncedComponentState authoritativeState = localVehicleSyncState.CreateState(playerState);
        ServerSyncedComponentState predictedState = acknowledgedSample.PredictedState.DeepClone();
        if (authoritativeState == null || predictedState == null)
        {
            ResetLocalPredictionCorrectionCandidate();
            return;
        }

        float positionError = VehicleServerSyncState.ComputePositionError(authoritativeState, predictedState);
        float rotationError = VehicleServerSyncState.ComputeRotationErrorDegrees(authoritativeState, predictedState);
        localPredictionAckMatchedPositionError = positionError;
        localPredictionAckMatchedRotationError = rotationError;

        if (positionError <= localPredictionPositionDeadzone && rotationError <= localPredictionRotationDeadzone)
        {
            ResetLocalPredictionCorrectionCandidate();
            TrimLocalPredictedInputs(playerState.ack_input_seq);
            return;
        }

        bool requiresImmediateCorrection =
            positionError >= Mathf.Max(localPredictionPositionDeadzone, localPredictionHardPositionDeadzone) ||
            rotationError >= Mathf.Max(localPredictionRotationDeadzone, localPredictionHardRotationDeadzone);

        if (!requiresImmediateCorrection)
        {
            if (playerState.ack_input_seq != localPredictionCorrectionCandidateSequence)
            {
                localPredictionCorrectionCandidateSequence = playerState.ack_input_seq;
                localPredictionCorrectionConsecutiveCount++;
            }

            if (localPredictionCorrectionConsecutiveCount < Mathf.Max(1, localPredictionSoftCorrectionConfirmations))
            {
                TrimLocalPredictedInputs(playerState.ack_input_seq);
                return;
            }
        }
        else
        {
            ResetLocalPredictionCorrectionCandidate();
        }

        if (pendingLocalReconciliation != null &&
            playerState.ack_input_seq <= pendingLocalReconciliation.AcknowledgedSequence)
        {
            return;
        }

        ResetLocalPredictionCorrectionCandidate();
        pendingLocalReconciliation = new PendingLocalReconciliation
        {
            AcknowledgedSequence = playerState.ack_input_seq,
            AuthoritativeState = authoritativeState.DeepClone()
        };
    }

    private void TickLocalPredictionCameraTarget(float deltaTime)
    {
        if (!EnsureLocalPredictionCameraTarget() || localPlayerCar == null)
            return;

        Transform localTransform = localPlayerCar.transform;
        TickLocalPredictionCameraCorrection(deltaTime);
        Vector3 desiredPosition = localTransform.position + localPredictionCameraCorrectionOffset;
        Quaternion desiredRotation = localTransform.rotation * localPredictionCameraCorrectionRotationOffset;

        if (!localPredictionCameraInitialized)
        {
            localPredictionCameraTarget.position = desiredPosition;
            localPredictionCameraTarget.rotation = desiredRotation;
            localPredictionCameraRotation = desiredRotation;
            localPredictionCameraVelocity = Vector3.zero;
            localPredictionCameraInitialized = true;
            return;
        }

        if (localPredictionCameraSmoothTime > 0.0f)
        {
            localPredictionCameraTarget.position = Vector3.SmoothDamp(
                localPredictionCameraTarget.position,
                desiredPosition,
                ref localPredictionCameraVelocity,
                localPredictionCameraSmoothTime,
                Mathf.Infinity,
                Mathf.Max(0.0001f, deltaTime));
        }
        else
        {
            localPredictionCameraTarget.position = desiredPosition;
        }

        if (localPredictionCameraRotationSmooth > 0.0f)
        {
            float t = 1.0f - Mathf.Exp(-localPredictionCameraRotationSmooth * Mathf.Max(0.0001f, deltaTime));
            localPredictionCameraRotation = Quaternion.Slerp(localPredictionCameraRotation, desiredRotation, t);
        }
        else
        {
            localPredictionCameraRotation = desiredRotation;
        }

        localPredictionCameraTarget.rotation = localPredictionCameraRotation;
    }

    private bool EnsureLocalPredictionCameraTarget()
    {
        if (!IsUsingLocalPrediction())
            return false;

        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();
        if (localPlayerCar == null)
            return false;

        if (followCarCamera == null)
            followCarCamera = FindFirstObjectByType<FollowCarCamera>();

        if (localPredictionCameraTarget == null)
        {
            GameObject targetRoot = new GameObject("LocalPredictionCameraTarget");
            localPredictionCameraTarget = targetRoot.transform;
            localPredictionCameraInitialized = false;
        }

        if (followCarCamera != null)
            followCarCamera.SetTarget(localPredictionCameraTarget);

        return true;
    }

    private void DestroyLocalPredictionCameraTarget()
    {
        if (followCarCamera == null)
            followCarCamera = FindFirstObjectByType<FollowCarCamera>();

        if (followCarCamera != null && localPlayerCar != null)
            followCarCamera.SetTarget(localPlayerCar.transform);

        if (localPredictionCameraTarget != null)
            Destroy(localPredictionCameraTarget.gameObject);

        localPredictionCameraTarget = null;
        localPredictionCameraVelocity = Vector3.zero;
        localPredictionCameraRotation = Quaternion.identity;
        localPredictionCameraInitialized = false;
        ResetLocalPredictionCameraCorrection();
    }

    private void RecordLocalPredictionAppliedCorrection(
        Vector3 preReconcilePosition,
        Quaternion preReconcileRotation,
        Vector3 postReconcilePosition,
        Quaternion postReconcileRotation)
    {
        localPredictionLastAppliedCorrectionDistance = Vector3.Distance(preReconcilePosition, postReconcilePosition);
        localPredictionLastAppliedCorrectionRotation = Quaternion.Angle(preReconcileRotation, postReconcileRotation);

        if (localPredictionLastAppliedCorrectionDistance <= 0.0001f &&
            localPredictionLastAppliedCorrectionRotation <= 0.01f)
        {
            return;
        }

        Vector3 correctionOffset = preReconcilePosition - postReconcilePosition;
        if (localPredictionCameraCorrectionMaxDistance > 0.0f)
            correctionOffset = Vector3.ClampMagnitude(correctionOffset, localPredictionCameraCorrectionMaxDistance);

        localPredictionCameraCorrectionOffset += correctionOffset;
        if (localPredictionCameraCorrectionMaxDistance > 0.0f)
            localPredictionCameraCorrectionOffset = Vector3.ClampMagnitude(localPredictionCameraCorrectionOffset, localPredictionCameraCorrectionMaxDistance);

        Quaternion correctionRotationOffset = Quaternion.Inverse(postReconcileRotation) * preReconcileRotation;
        float correctionAngle = Quaternion.Angle(Quaternion.identity, correctionRotationOffset);
        if (localPredictionCameraCorrectionMaxRotation > 0.0f && correctionAngle > localPredictionCameraCorrectionMaxRotation)
        {
            float t = localPredictionCameraCorrectionMaxRotation / Mathf.Max(0.0001f, correctionAngle);
            correctionRotationOffset = Quaternion.Slerp(Quaternion.identity, correctionRotationOffset, t);
        }

        localPredictionCameraCorrectionRotationOffset =
            correctionRotationOffset * localPredictionCameraCorrectionRotationOffset;
    }

    private void TickLocalPredictionCameraCorrection(float deltaTime)
    {
        float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
        if (localPredictionCameraCorrectionSmoothTime > 0.0f)
        {
            localPredictionCameraCorrectionOffset = Vector3.SmoothDamp(
                localPredictionCameraCorrectionOffset,
                Vector3.zero,
                ref localPredictionCameraCorrectionVelocity,
                localPredictionCameraCorrectionSmoothTime,
                Mathf.Infinity,
                safeDeltaTime);
        }
        else
        {
            localPredictionCameraCorrectionOffset = Vector3.zero;
            localPredictionCameraCorrectionVelocity = Vector3.zero;
        }

        if (localPredictionCameraCorrectionOffset.sqrMagnitude <= 0.000001f)
        {
            localPredictionCameraCorrectionOffset = Vector3.zero;
            localPredictionCameraCorrectionVelocity = Vector3.zero;
        }

        if (localPredictionCameraCorrectionRotationSmooth > 0.0f)
        {
            float t = 1.0f - Mathf.Exp(-localPredictionCameraCorrectionRotationSmooth * safeDeltaTime);
            localPredictionCameraCorrectionRotationOffset = Quaternion.Slerp(
                localPredictionCameraCorrectionRotationOffset,
                Quaternion.identity,
                t);
        }
        else
        {
            localPredictionCameraCorrectionRotationOffset = Quaternion.identity;
        }

        if (Quaternion.Angle(localPredictionCameraCorrectionRotationOffset, Quaternion.identity) <= 0.05f)
            localPredictionCameraCorrectionRotationOffset = Quaternion.identity;
    }

    private void QueueLocalAuthoritativeState(long matchServerTimeMs, BackendMatchPlayerState playerState)
    {
        if (playerState == null)
            return;

        UpdateLocalServerState(matchServerTimeMs, playerState);
        double snapshotTime = ResolveSnapshotLocalTime(
            matchServerTimeMs,
            ref localSnapshotTimelineInitialized,
            ref localSnapshotServerToLocalOffset,
            ref localSnapshotTimelineLastSampleLocalTime);
        PushLocalAuthoritativeSnapshot(snapshotTime, playerState);
    }

    private void PushLocalAuthoritativeSnapshot(double localTime, BackendMatchPlayerState playerState)
    {
        if (playerState == null)
            return;

        if (localAuthoritativeSnapshots.Count > 0)
        {
            LocalAuthoritativeSnapshot newest = localAuthoritativeSnapshots[localAuthoritativeSnapshots.Count - 1];
            if (localTime <= newest.LocalTime)
                localTime = newest.LocalTime + 0.0001d;
        }

        localAuthoritativeSnapshots.Add(new LocalAuthoritativeSnapshot
        {
            LocalTime = localTime,
            Position = playerState.PositionVector,
            Rotation = Quaternion.Euler(playerState.RotationVector),
            Velocity = playerState.VelocityVector,
            AngularVelocity = playerState.AngularVelocityVector,
            WheelStates = CloneLocalWheelStates(playerState.wheel_states)
        });

        int maxSnapshots = Mathf.Max(2, remoteSnapshotBufferSize);
        if (localAuthoritativeSnapshots.Count > maxSnapshots)
            localAuthoritativeSnapshots.RemoveRange(0, localAuthoritativeSnapshots.Count - maxSnapshots);
    }

    private void TickLocalAuthoritativeState(double localNow)
    {
        if (!localAuthoritativeMode || localAuthoritativeSnapshots.Count == 0)
            return;

        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();
        if (localPlayerCar == null)
            return;

        RefreshLocalBindings();

        double renderTime = localNow - Mathf.Max(0.01f, Mathf.Min(0.05f, remoteInterpolationBackTime));
        EvaluateLocalAuthoritativeSnapshot(
            renderTime,
            remoteExtrapolationLimit,
            out Vector3 position,
            out Quaternion rotation,
            localInterpolatedWheelStates);
        ApplyLocalAuthoritativeState(position, rotation, localInterpolatedWheelStates);
    }

    private void EvaluateLocalAuthoritativeSnapshot(
        double renderTime,
        float extrapolationLimit,
        out Vector3 position,
        out Quaternion rotation,
        List<LocalWheelPoseState> wheelStateBuffer)
    {
        wheelStateBuffer.Clear();

        if (localAuthoritativeSnapshots.Count == 1)
        {
            LocalAuthoritativeSnapshot only = localAuthoritativeSnapshots[0];
            double dt = Math.Min(Math.Max(0.0d, renderTime - only.LocalTime), extrapolationLimit);
            position = only.Position + only.Velocity * (float)dt;
            rotation = only.AngularVelocity.sqrMagnitude > 0.0001f
                ? only.Rotation * Quaternion.Euler(only.AngularVelocity * Mathf.Rad2Deg * (float)dt)
                : only.Rotation;
            CopyLocalWheelStates(only.WheelStates, wheelStateBuffer);
            return;
        }

        while (localAuthoritativeSnapshots.Count >= 2 && localAuthoritativeSnapshots[1].LocalTime <= renderTime - 0.5d)
            localAuthoritativeSnapshots.RemoveAt(0);

        LocalAuthoritativeSnapshot oldest = localAuthoritativeSnapshots[0];
        if (renderTime <= oldest.LocalTime)
        {
            position = oldest.Position;
            rotation = oldest.Rotation;
            CopyLocalWheelStates(oldest.WheelStates, wheelStateBuffer);
            return;
        }

        for (int i = 0; i < localAuthoritativeSnapshots.Count - 1; i++)
        {
            LocalAuthoritativeSnapshot from = localAuthoritativeSnapshots[i];
            LocalAuthoritativeSnapshot to = localAuthoritativeSnapshots[i + 1];
            if (renderTime > to.LocalTime)
                continue;

            float t = (float)((renderTime - from.LocalTime) / Math.Max(0.0001d, to.LocalTime - from.LocalTime));
            position = InterpolateSnapshotPosition(
                from.Position,
                from.Velocity,
                from.LocalTime,
                to.Position,
                to.Velocity,
                to.LocalTime,
                t);
            rotation = Quaternion.SlerpUnclamped(from.Rotation, to.Rotation, t);
            InterpolateLocalWheelStates(from.WheelStates, to.WheelStates, t, wheelStateBuffer);
            return;
        }

        LocalAuthoritativeSnapshot latest = localAuthoritativeSnapshots[localAuthoritativeSnapshots.Count - 1];
        double extrapolation = Math.Min(Math.Max(0.0d, renderTime - latest.LocalTime), extrapolationLimit);
        position = latest.Position + latest.Velocity * (float)extrapolation;
        rotation = latest.AngularVelocity.sqrMagnitude > 0.0001f
            ? latest.Rotation * Quaternion.Euler(latest.AngularVelocity * Mathf.Rad2Deg * (float)extrapolation)
            : latest.Rotation;
        CopyLocalWheelStates(latest.WheelStates, wheelStateBuffer);
    }

    private void ApplyLocalAuthoritativeState(Vector3 position, Quaternion rotation, IReadOnlyList<LocalWheelPoseState> wheelStates)
    {
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();
        if (localPlayerCar == null)
            return;

        Rigidbody body = localPlayerCar.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.maxDepenetrationVelocity = maxDepenetrationVelocity;
            if (body.isKinematic)
            {
                body.position = position;
                body.rotation = rotation;
            }
            else
            {
                body.position = position;
                body.rotation = rotation;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        else
        {
            Transform root = localPlayerCar.transform;
            root.position = position;
            root.rotation = rotation;
        }

        ApplyLocalWheelStates(wheelStates);
    }

    private void ApplyLocalWheelStates(List<BackendWheelPose> wheelStates)
    {
        if (wheelStates == null || wheelStates.Count == 0 || localWheelBindings.Count == 0)
            return;

        int count = Mathf.Min(localWheelBindings.Count, wheelStates.Count);
        for (int i = 0; i < count; i++)
        {
            LocalWheelBinding binding = localWheelBindings[i];
            BackendWheelPose pose = wheelStates[i];
            if (binding == null || binding.VisualRoot == null || pose == null)
                continue;

            if (pose.position != null)
                binding.VisualRoot.localPosition = pose.position.ToVector3();
            if (pose.rotation != null)
                binding.VisualRoot.localRotation = Quaternion.Euler(pose.rotation.ToVector3());
        }
    }

    private void ApplyLocalWheelStates(IReadOnlyList<LocalWheelPoseState> wheelStates)
    {
        if (wheelStates == null || wheelStates.Count == 0 || localWheelBindings.Count == 0)
            return;

        int count = Mathf.Min(localWheelBindings.Count, wheelStates.Count);
        for (int i = 0; i < count; i++)
        {
            LocalWheelBinding binding = localWheelBindings[i];
            LocalWheelPoseState pose = wheelStates[i];
            if (binding == null || binding.VisualRoot == null)
                continue;

            if (pose.HasPosition)
                binding.VisualRoot.localPosition = pose.Position;
            if (pose.HasRotation)
                binding.VisualRoot.localRotation = pose.Rotation;
        }
    }

    private static List<LocalWheelPoseState> CloneLocalWheelStates(List<BackendWheelPose> wheelStates)
    {
        List<LocalWheelPoseState> result = new List<LocalWheelPoseState>(wheelStates != null ? wheelStates.Count : 0);
        if (wheelStates == null)
            return result;

        for (int i = 0; i < wheelStates.Count; i++)
        {
            BackendWheelPose pose = wheelStates[i];
            LocalWheelPoseState item = default;
            if (pose != null && pose.position != null)
            {
                item.HasPosition = true;
                item.Position = pose.position.ToVector3();
            }

            if (pose != null && pose.rotation != null)
            {
                item.HasRotation = true;
                item.Rotation = Quaternion.Euler(pose.rotation.ToVector3());
            }

            result.Add(item);
        }

        return result;
    }

    private static void CopyLocalWheelStates(IReadOnlyList<LocalWheelPoseState> source, List<LocalWheelPoseState> target)
    {
        target.Clear();
        if (source == null)
            return;

        for (int i = 0; i < source.Count; i++)
            target.Add(source[i]);
    }

    private static void InterpolateLocalWheelStates(
        IReadOnlyList<LocalWheelPoseState> from,
        IReadOnlyList<LocalWheelPoseState> to,
        float t,
        List<LocalWheelPoseState> target)
    {
        target.Clear();
        int count = Mathf.Max(from != null ? from.Count : 0, to != null ? to.Count : 0);
        for (int i = 0; i < count; i++)
        {
            LocalWheelPoseState result = default;
            bool hasFrom = from != null && i < from.Count;
            bool hasTo = to != null && i < to.Count;
            if (hasFrom && hasTo && from[i].HasPosition && to[i].HasPosition)
            {
                result.HasPosition = true;
                result.Position = Vector3.LerpUnclamped(from[i].Position, to[i].Position, t);
            }
            else if (hasTo && to[i].HasPosition)
            {
                result.HasPosition = true;
                result.Position = to[i].Position;
            }
            else if (hasFrom && from[i].HasPosition)
            {
                result.HasPosition = true;
                result.Position = from[i].Position;
            }

            if (hasFrom && hasTo && from[i].HasRotation && to[i].HasRotation)
            {
                result.HasRotation = true;
                result.Rotation = Quaternion.SlerpUnclamped(from[i].Rotation, to[i].Rotation, t);
            }
            else if (hasTo && to[i].HasRotation)
            {
                result.HasRotation = true;
                result.Rotation = to[i].Rotation;
            }
            else if (hasFrom && from[i].HasRotation)
            {
                result.HasRotation = true;
                result.Rotation = from[i].Rotation;
            }

            target.Add(result);
        }
    }

    private static Vector3 InterpolateSnapshotPosition(
        Vector3 fromPosition,
        Vector3 fromVelocity,
        double fromTime,
        Vector3 toPosition,
        Vector3 toVelocity,
        double toTime,
        float t)
    {
        float clampedT = Mathf.Clamp01(t);
        float duration = (float)Math.Max(0.0001d, toTime - fromTime);
        float t2 = clampedT * clampedT;
        float t3 = t2 * clampedT;
        Vector3 tangentFrom = fromVelocity * duration;
        Vector3 tangentTo = toVelocity * duration;
        return (2.0f * t3 - 3.0f * t2 + 1.0f) * fromPosition +
               (t3 - 2.0f * t2 + clampedT) * tangentFrom +
               (-2.0f * t3 + 3.0f * t2) * toPosition +
               (t3 - t2) * tangentTo;
    }

    private static double ResolveSnapshotLocalTime(
        long matchServerTimeMs,
        ref bool timelineInitialized,
        ref double snapshotServerToLocalOffset,
        ref double lastSampleLocalTime)
    {
        double localNow = Time.unscaledTimeAsDouble;
        if (matchServerTimeMs <= 0)
            return localNow;

        double serverTime = matchServerTimeMs / 1000.0d;
        double measuredOffset = localNow - serverTime;
        if (!timelineInitialized)
        {
            snapshotServerToLocalOffset = measuredOffset;
            lastSampleLocalTime = localNow;
            timelineInitialized = true;
            return serverTime + snapshotServerToLocalOffset;
        }

        double sampleDt = lastSampleLocalTime > 0.0d
            ? Math.Max(0.0001d, localNow - lastSampleLocalTime)
            : 0.0001d;
        lastSampleLocalTime = localNow;

        double offsetError = measuredOffset - snapshotServerToLocalOffset;
        if (Math.Abs(offsetError) >= SnapshotTimelineHardResetThresholdSeconds)
        {
            snapshotServerToLocalOffset = measuredOffset;
        }
        else
        {
            double alpha = 1.0d - Math.Exp(-SnapshotTimelineFilterStrength * sampleDt);
            double correction = offsetError * alpha;
            correction = Math.Max(
                -SnapshotTimelineMaxCorrectionPerSampleSeconds,
                Math.Min(SnapshotTimelineMaxCorrectionPerSampleSeconds, correction));
            snapshotServerToLocalOffset += correction;
        }

        return serverTime + snapshotServerToLocalOffset;
    }

    private static bool IsAuthoritativeRoom(string roomStatus)
    {
        if (string.IsNullOrWhiteSpace(roomStatus))
            return false;

        switch (roomStatus.Trim().ToLowerInvariant())
        {
            case "allocated":
            case "ready":
            case "reserved":
            case "simulating":
                return true;
            default:
                return false;
        }
    }

    private sealed class RemotePlayerProxy
    {
        private sealed class RemoteWheelBinding
        {
            public Transform VisualRoot;
        }

        private readonly struct RemoteSnapshot
        {
            public readonly double LocalTime;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Velocity;
            public readonly Vector3 AngularVelocity;
            public readonly LocalWheelPoseState[] WheelStates;

            public RemoteSnapshot(
                double localTime,
                Vector3 position,
                Quaternion rotation,
                Vector3 velocity,
                Vector3 angularVelocity,
                LocalWheelPoseState[] wheelStates)
            {
                LocalTime = localTime;
                Position = position;
                Rotation = rotation;
                Velocity = velocity;
                AngularVelocity = angularVelocity;
                WheelStates = wheelStates ?? Array.Empty<LocalWheelPoseState>();
            }
        }

        private readonly GameObject root;
        private readonly Transform transform;
        private readonly Transform presentationRoot;
        private Rigidbody physicsBody;
        private readonly Material fallbackMaterial;
        private readonly Color fallbackColor;
        private readonly int snapshotBufferSize;
        private readonly List<RemoteWheelBinding> wheelBindings = new List<RemoteWheelBinding>(4);
        private readonly List<RemoteSnapshot> snapshots = new List<RemoteSnapshot>(32);
        private readonly List<LocalWheelPoseState> interpolatedWheelStates = new List<LocalWheelPoseState>(4);
        private Collider[] bodyColliders;
        private readonly string playerId;
        private bool visualReady;
        private bool fallbackVisualActive;
        private CarDamageController damageController;
        private DamageManager damageManager;
        private int appliedDamageRevision;
        private double lastSnapshotLocalTime;
        private double collisionRecoveryUntil;
        private bool collisionEnabled;
        private bool snapshotTimelineInitialized;
        private double snapshotServerToLocalOffset;
        private double snapshotTimelineLastSampleLocalTime;
        private bool serverPoseAvailable;
        private Vector3 serverPosePosition;
        private Quaternion serverPoseRotation = Quaternion.identity;
        private Vector3 lastServerVelocity;
        private Vector3 lastServerAngularVelocity;
        private int lastServerWheelStateCount;
        private BackendVehicleDebugState lastServerVehicleDebug;
        private InputDebugSnapshot lastServerInputDebug;
        private double lastServerSnapshotReceivedLocalTime;
        private Vector3 presentationCorrectionPosition;
        private Quaternion presentationCorrectionRotation = Quaternion.identity;

        public RemotePlayerProxy(
            string playerId,
            BackendMatchPlayerInfo matchPlayer,
            BackendLobbyPlayer lobbyPlayer,
            BackendCarConfigPayload fallbackCarConfig,
            Material fallbackMaterial,
            Color fallbackColor,
            int snapshotBufferSize,
            Transform remoteRoot)
        {
            root = new GameObject("RemotePlayer_" + playerId);
            if (remoteRoot != null)
                root.transform.SetParent(remoteRoot, false);
            transform = root.transform;
            GameObject presentation = new GameObject("Presentation");
            presentation.transform.SetParent(transform, false);
            presentationRoot = presentation.transform;
            this.playerId = playerId;
            this.fallbackMaterial = fallbackMaterial;
            this.fallbackColor = fallbackColor;
            this.snapshotBufferSize = Math.Max(2, snapshotBufferSize);

            if (matchPlayer != null && matchPlayer.HasSpawnAssignment)
            {
                transform.position = matchPlayer.SpawnPositionVector;
                transform.rotation = Quaternion.Euler(matchPlayer.SpawnRotationVector);
                serverPoseAvailable = true;
                serverPosePosition = transform.position;
                serverPoseRotation = transform.rotation;
                snapshots.Add(new RemoteSnapshot(
                    Time.unscaledTimeAsDouble,
                    transform.position,
                    transform.rotation,
                    Vector3.zero,
                    Vector3.zero,
                    Array.Empty<LocalWheelPoseState>()));
            }

            EnsureVisual(matchPlayer, lobbyPlayer, fallbackCarConfig);
        }

        public void EnsureVisual(BackendCarConfigPayload fallbackCarConfig)
        {
            EnsureVisual(null, null, fallbackCarConfig);
        }

        public void SetTargetState(
            long matchServerTimeMs,
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            Vector3 angularVelocity,
            List<BackendWheelPose> wheelStates)
        {
            EnsureVisual(null);
            double snapshotTime = ResolveSnapshotLocalTime(
                matchServerTimeMs,
                ref snapshotTimelineInitialized,
                ref snapshotServerToLocalOffset,
                ref snapshotTimelineLastSampleLocalTime);
            if (lastSnapshotLocalTime > 0.0d && snapshotTime - lastSnapshotLocalTime > 0.25d)
                collisionRecoveryUntil = snapshotTime + 0.35d;
            lastSnapshotLocalTime = snapshotTime;
            serverPoseAvailable = true;
            serverPosePosition = position;
            serverPoseRotation = rotation;
            PushSnapshot(snapshotTime, position, rotation, velocity, angularVelocity, ConvertWheelStates(wheelStates));
        }

        public void UpdateServerDebugState(long matchServerTimeMs, BackendMatchPlayerState playerState)
        {
            if (playerState == null)
                return;

            lastServerVelocity = playerState.VelocityVector;
            lastServerAngularVelocity = playerState.AngularVelocityVector;
            lastServerWheelStateCount = playerState.wheel_states != null ? playerState.wheel_states.Count : 0;
            lastServerVehicleDebug = playerState.debug;
            lastServerInputDebug = CreateInputDebugSnapshot(
                playerState.ack_input_seq,
                playerState.client_time,
                playerState.server_received_time,
                playerState.input);
            lastServerSnapshotReceivedLocalTime = Time.unscaledTimeAsDouble;
        }

        public void Tick(
            double localNow,
            float deltaTime,
            float interpolationBackTime,
            float extrapolationLimit,
            float teleportDistance,
            float staleTimeout,
            float recoveryDelay,
            bool allowCollisions,
            float presentationSnapDistance,
            float presentationSnapRotation,
            float presentationCorrectionSmooth,
            float presentationMaxOffset)
        {
            if (snapshots.Count == 0)
            {
                SetCollisionEnabled(false);
                return;
            }

            bool freshState = lastSnapshotLocalTime > 0.0d && localNow - lastSnapshotLocalTime <= staleTimeout;
            if (!freshState)
                collisionRecoveryUntil = Math.Max(collisionRecoveryUntil, localNow + recoveryDelay);
            SetCollisionEnabled(allowCollisions && freshState && localNow >= collisionRecoveryUntil);

            double renderTime = localNow - Mathf.Max(0.01f, interpolationBackTime);
            Vector3 desiredPosition;
            Quaternion desiredRotation;
            EvaluateSnapshot(
                renderTime,
                extrapolationLimit,
                out desiredPosition,
                out desiredRotation,
                interpolatedWheelStates);

            Vector3 previousPosition = transform.position;
            Quaternion previousRotation = transform.rotation;

            if (Vector3.Distance(transform.position, desiredPosition) >= teleportDistance)
            {
                if (physicsBody != null)
                {
                    physicsBody.position = desiredPosition;
                    physicsBody.rotation = desiredRotation;
                    collisionRecoveryUntil = Math.Max(collisionRecoveryUntil, localNow + recoveryDelay);
                    SetCollisionEnabled(false);
                }
                else
                {
                    transform.SetPositionAndRotation(desiredPosition, desiredRotation);
                }

                AccumulatePresentationCorrection(
                    previousPosition,
                    previousRotation,
                    desiredPosition,
                    desiredRotation,
                    presentationSnapDistance,
                    presentationSnapRotation,
                    presentationMaxOffset);
                TickPresentationCorrection(deltaTime, presentationCorrectionSmooth);
                return;
            }

            if (physicsBody != null && physicsBody.isKinematic)
            {
                physicsBody.position = desiredPosition;
                physicsBody.rotation = desiredRotation;
            }
            else
            {
                transform.SetPositionAndRotation(desiredPosition, desiredRotation);
            }

            AccumulatePresentationCorrection(
                previousPosition,
                previousRotation,
                desiredPosition,
                desiredRotation,
                presentationSnapDistance,
                presentationSnapRotation,
                presentationMaxOffset);
            TickPresentationCorrection(deltaTime, presentationCorrectionSmooth);
            ApplyWheelStates(interpolatedWheelStates);
        }

        public void Dispose()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
        }

        public bool TryGetPoseGizmoState(
            out Vector3 clientPosition,
            out Quaternion clientRotation,
            out Vector3 serverPosition,
            out Quaternion serverRotation)
        {
            clientPosition = transform != null ? transform.position : Vector3.zero;
            clientRotation = transform != null ? transform.rotation : Quaternion.identity;
            serverPosition = serverPosePosition;
            serverRotation = serverPoseRotation;
            return serverPoseAvailable && transform != null;
        }

        public bool TryGetOverlayDebugSnapshot(out RemoteOverlayDebugSnapshot snapshot)
        {
            snapshot = default;
            if (!serverPoseAvailable || transform == null)
                return false;

            snapshot.Available = true;
            snapshot.PlayerId = playerId;
            snapshot.ClientPosition = transform.position;
            snapshot.ClientRotation = transform.rotation;
            snapshot.ServerPosition = serverPosePosition;
            snapshot.ServerRotation = serverPoseRotation;
            snapshot.ServerVelocity = lastServerVelocity;
            snapshot.ServerAngularVelocity = lastServerAngularVelocity;
            snapshot.ServerWheelStateCount = lastServerWheelStateCount;
            snapshot.ClientWheelVisualCount = wheelBindings.Count;
            snapshot.ServerInput = lastServerInputDebug;
            snapshot.ServerVehicleDebug = lastServerVehicleDebug;
            snapshot.SnapshotAgeMs = lastServerSnapshotReceivedLocalTime > 0.0d
                ? (Time.unscaledTimeAsDouble - lastServerSnapshotReceivedLocalTime) * 1000.0d
                : 0.0d;
            return true;
        }

        public void ApplyDamage(BackendDamageStateMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.map_b64))
                return;
            if (message.revision <= appliedDamageRevision)
                return;

            EnsureVisual(null);
            RefreshDamageBindings();
            if (damageController == null)
            {
                Debug.LogWarning("MultiplayerMatchRuntime: remote damage controller missing for " + playerId, root);
                return;
            }

            byte[] rawBytes;
            try
            {
                rawBytes = Convert.FromBase64String(message.map_b64);
            }
            catch
            {
                return;
            }

            damageController.ApplyNetworkDamageSnapshot(new CarDamageNetworkSnapshot
            {
                revision = message.revision,
                width = message.width,
                height = message.height,
                rawBytes = rawBytes,
                hasImpactPoint = message.world_point != null,
                worldPoint = message.WorldPointVector,
                hasImpactNormal = message.world_normal != null,
                worldNormal = message.WorldNormalVector
            }, damageManager);
            appliedDamageRevision = message.revision;
        }

        private void PushSnapshot(
            double localTime,
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            Vector3 angularVelocity,
            LocalWheelPoseState[] wheelStates)
        {
            if (snapshots.Count > 0)
            {
                RemoteSnapshot newest = snapshots[snapshots.Count - 1];
                if (localTime <= newest.LocalTime)
                    localTime = newest.LocalTime + 0.0001d;
            }

            snapshots.Add(new RemoteSnapshot(localTime, position, rotation, velocity, angularVelocity, wheelStates));
            if (snapshots.Count > snapshotBufferSize)
                snapshots.RemoveRange(0, snapshots.Count - snapshotBufferSize);
        }

        private void EvaluateSnapshot(
            double renderTime,
            float extrapolationLimit,
            out Vector3 position,
            out Quaternion rotation,
            List<LocalWheelPoseState> wheelStateBuffer)
        {
            if (snapshots.Count == 1)
            {
                RemoteSnapshot only = snapshots[0];
                double dt = Math.Min(Math.Max(0.0d, renderTime - only.LocalTime), extrapolationLimit);
                position = only.Position + only.Velocity * (float)dt;
                rotation = only.AngularVelocity.sqrMagnitude > 0.0001f
                    ? only.Rotation * Quaternion.Euler(only.AngularVelocity * Mathf.Rad2Deg * (float)dt)
                    : only.Rotation;
                CopyLocalWheelStates(only.WheelStates, wheelStateBuffer);
                return;
            }

            while (snapshots.Count >= 2 && snapshots[1].LocalTime <= renderTime - 0.5d)
                snapshots.RemoveAt(0);

            RemoteSnapshot oldest = snapshots[0];
            if (renderTime <= oldest.LocalTime)
            {
                position = oldest.Position;
                rotation = oldest.Rotation;
                CopyLocalWheelStates(oldest.WheelStates, wheelStateBuffer);
                return;
            }

            for (int i = 0; i < snapshots.Count - 1; i++)
            {
                RemoteSnapshot from = snapshots[i];
                RemoteSnapshot to = snapshots[i + 1];
                if (renderTime > to.LocalTime)
                    continue;

                float t = (float)((renderTime - from.LocalTime) / Math.Max(0.0001d, to.LocalTime - from.LocalTime));
                position = InterpolateSnapshotPosition(
                    from.Position,
                    from.Velocity,
                    from.LocalTime,
                    to.Position,
                    to.Velocity,
                    to.LocalTime,
                    t);
                rotation = Quaternion.SlerpUnclamped(from.Rotation, to.Rotation, t);
                InterpolateLocalWheelStates(from.WheelStates, to.WheelStates, t, wheelStateBuffer);
                return;
            }

            RemoteSnapshot latest = snapshots[snapshots.Count - 1];
            double extrapolation = Math.Min(Math.Max(0.0d, renderTime - latest.LocalTime), extrapolationLimit);
            position = latest.Position + latest.Velocity * (float)extrapolation;
            rotation = latest.AngularVelocity.sqrMagnitude > 0.0001f
                ? latest.Rotation * Quaternion.Euler(latest.AngularVelocity * Mathf.Rad2Deg * (float)extrapolation)
                : latest.Rotation;
            CopyLocalWheelStates(latest.WheelStates, wheelStateBuffer);
        }

        private static LocalWheelPoseState[] ConvertWheelStates(List<BackendWheelPose> wheelStates)
        {
            return CloneLocalWheelStates(wheelStates).ToArray();
        }

        private void EnsureVisual(BackendMatchPlayerInfo matchPlayer, BackendLobbyPlayer lobbyPlayer, BackendCarConfigPayload fallbackCarConfig)
        {
            if (visualReady && !fallbackVisualActive)
                return;

            if (TryResolveConfiguredVisualSource(
                matchPlayer,
                lobbyPlayer,
                fallbackCarConfig,
                out BackendCarConfigPayload resolvedCarConfig,
                out string resolvedPlayerId,
                out CarLoadoutConfig resolvedLoadout))
            {
                if (fallbackVisualActive)
                    RebuildFromFallbackVisual();

                if (!visualReady)
                {
                    visualReady = CreateResolvedVisual(
                        root,
                        presentationRoot,
                        resolvedLoadout,
                        resolvedCarConfig,
                        resolvedPlayerId);
                    fallbackVisualActive = !visualReady;
                }
            }

            if (!visualReady)
            {
                CreateFallbackVisual(presentationRoot, fallbackMaterial, fallbackColor);
                visualReady = true;
                fallbackVisualActive = true;
            }

            RefreshVisualBindings();
        }

        private bool TryResolveConfiguredVisualSource(
            BackendMatchPlayerInfo matchPlayer,
            BackendLobbyPlayer lobbyPlayer,
            BackendCarConfigPayload fallbackCarConfig,
            out BackendCarConfigPayload resolvedCarConfig,
            out string resolvedPlayerId,
            out CarLoadoutConfig resolvedLoadout)
        {
            if (matchPlayer != null && TryResolveVisualLoadout(matchPlayer.car_config, out resolvedLoadout))
            {
                resolvedCarConfig = matchPlayer.car_config;
                resolvedPlayerId = matchPlayer.player_id;
                return true;
            }

            if (lobbyPlayer != null && TryResolveVisualLoadout(lobbyPlayer.car_config, out resolvedLoadout))
            {
                resolvedCarConfig = lobbyPlayer.car_config;
                resolvedPlayerId = lobbyPlayer.player_id;
                return true;
            }

            if (TryResolveVisualLoadout(fallbackCarConfig, out resolvedLoadout))
            {
                resolvedCarConfig = fallbackCarConfig;
                resolvedPlayerId = playerId;
                return true;
            }

            resolvedCarConfig = null;
            resolvedPlayerId = null;
            resolvedLoadout = null;
            return false;
        }

        private void RebuildFromFallbackVisual()
        {
            SetCollisionEnabled(false);
            for (int i = presentationRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(presentationRoot.GetChild(i).gameObject);

            visualReady = false;
            fallbackVisualActive = false;
            physicsBody = null;
            bodyColliders = null;
            damageController = null;
            damageManager = null;
            wheelBindings.Clear();
            presentationCorrectionPosition = Vector3.zero;
            presentationCorrectionRotation = Quaternion.identity;
            presentationRoot.localPosition = Vector3.zero;
            presentationRoot.localRotation = Quaternion.identity;
        }

        private void RefreshVisualBindings()
        {
            if (!visualReady)
                return;

            if (damageController == null)
            {
                damageController = root.GetComponentInChildren<CarDamageController>(true);
                damageManager = root.GetComponentInChildren<DamageManager>(true);
                if (damageController != null)
                    damageController.EnsureNetworkTextureReady();
            }

            if (physicsBody == null)
            {
                physicsBody = root.GetComponent<Rigidbody>();
                if (physicsBody != null)
                    physicsBody.maxDepenetrationVelocity = 7.5f;
            }

            if (bodyColliders == null)
                bodyColliders = root.GetComponentsInChildren<Collider>(true);

            if (wheelBindings.Count == 0)
                ResolveWheelBindings();
        }

        private static bool TryCreateVisualFromMatchConfig(Transform parent, BackendMatchPlayerInfo matchPlayer)
        {
            if (matchPlayer == null)
                return false;

            return TryCreateVisual(parent, matchPlayer.car_config, matchPlayer.player_id);
        }

        private static bool TryCreateVisualFromLobbyConfig(Transform parent, BackendLobbyPlayer lobbyPlayer)
        {
            if (lobbyPlayer == null)
                return false;

            return TryCreateVisual(parent, lobbyPlayer.car_config, lobbyPlayer.player_id);
        }

        private static bool TryCreateVisual(Transform parent, BackendCarConfigPayload carConfig, string playerId)
        {
            if (!TryResolveVisualLoadout(carConfig, out CarLoadoutConfig loadout))
                return false;

            return CreateResolvedVisual(parent.gameObject, parent, loadout, carConfig, playerId);
        }

        private static bool TryResolveVisualLoadout(BackendCarConfigPayload carConfig, out CarLoadoutConfig loadout)
        {
            loadout = null;
            if (carConfig == null || string.IsNullOrWhiteSpace(carConfig.loadout_name))
                return false;

            loadout = CarLoadoutResolver.Resolve(carConfig.ToPlayerSelectionPayload());
            if (loadout == null || loadout.PlayerCarConfig == null || loadout.PlayerCarConfig.Visual == null)
                return false;

            GameObject bodyPrefab = loadout.PlayerCarConfig.Visual.bodyPrefab;
            if (bodyPrefab == null)
                return false;
            return true;
        }

        private static bool CreateResolvedVisual(GameObject rootObject, Transform visualParent, CarLoadoutConfig loadout, BackendCarConfigPayload carConfig, string playerId)
        {
            GameObject bodyPrefab = loadout.PlayerCarConfig.Visual.bodyPrefab;
            GameObject bodyInstance = UnityEngine.Object.Instantiate(bodyPrefab, visualParent);
            bodyInstance.name = "Body";
            bodyInstance.transform.localPosition = Vector3.zero;
            bodyInstance.transform.localRotation = Quaternion.identity;
            bodyInstance.transform.localScale = Vector3.one;
            ApplyRemoteBodySet(loadout, bodyInstance.transform, carConfig);
            ApplyRemoteCustomizations(bodyInstance.transform, carConfig);
            EnsureRemotePhysics(rootObject, bodyInstance, loadout.PlayerCarConfig);
            EnsureRemoteDamage(rootObject, bodyInstance, loadout.PlayerCarConfig, loadout.PlayerCarConfig.Visual != null ? loadout.PlayerCarConfig.Visual.bodyPrefab : null);
            EnsureNetworkEntity(rootObject, playerId);
            StripGameplayComponents(bodyInstance, keepBodyColliders: false);
            ApplyRemotePaint(bodyInstance, carConfig);
            AttachRemoteWheels(visualParent, loadout, carConfig, null);
            return true;
        }

        private static void ApplyRemoteBodySet(CarLoadoutConfig loadout, Transform bodyRoot, BackendCarConfigPayload carConfig)
        {
            if (loadout == null || bodyRoot == null || carConfig == null || loadout.BodySets == null || loadout.BodySets.Count == 0)
                return;

            int optionIndex = carConfig.body_set_option_index;
            bool includeStock = loadout.IncludeStockBodyOption || loadout.BodySets.Count == 0;
            if (includeStock && optionIndex <= 0)
                return;
            if (!includeStock && optionIndex < 0)
                return;

            int bodySetIndex = includeStock ? optionIndex - 1 : optionIndex;
            if (bodySetIndex < 0 || bodySetIndex >= loadout.BodySets.Count)
                return;

            BodySetConfig bodySet = loadout.BodySets[bodySetIndex];
            if (bodySet == null || bodySet.BodySetPrefab == null)
                return;

            GameObject instance = UnityEngine.Object.Instantiate(bodySet.BodySetPrefab, bodyRoot);
            instance.name = bodySet.BodySetPrefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }

        private static void ApplyRemoteCustomizations(Transform bodyRoot, BackendCarConfigPayload carConfig)
        {
            if (bodyRoot == null || carConfig == null || carConfig.customizations == null || carConfig.customizations.Count == 0)
                return;

            List<CarCustomizationSelection> selections = new List<CarCustomizationSelection>(carConfig.customizations.Count);
            for (int i = 0; i < carConfig.customizations.Count; i++)
            {
                BackendCarCustomizationPayload payload = carConfig.customizations[i];
                if (payload == null || string.IsNullOrWhiteSpace(payload.selector_path))
                    continue;

                selections.Add(new CarCustomizationSelection(payload.selector_path, payload.variant_name));
            }

            CarCustomizationUtility.ApplySelections(bodyRoot, selections);
        }

        private void ApplyWheelStates(IReadOnlyList<LocalWheelPoseState> wheelStates)
        {
            if (wheelStates == null || wheelStates.Count == 0 || wheelBindings.Count == 0)
                return;

            int count = Mathf.Min(wheelBindings.Count, wheelStates.Count);
            for (int i = 0; i < count; i++)
            {
                RemoteWheelBinding binding = wheelBindings[i];
                LocalWheelPoseState pose = wheelStates[i];
                if (binding == null || binding.VisualRoot == null)
                    continue;

                if (pose.HasPosition)
                    binding.VisualRoot.localPosition = pose.Position;
                if (pose.HasRotation)
                    binding.VisualRoot.localRotation = pose.Rotation;
            }
        }

        private void AccumulatePresentationCorrection(
            Vector3 previousPosition,
            Quaternion previousRotation,
            Vector3 currentPosition,
            Quaternion currentRotation,
            float snapDistance,
            float snapRotation,
            float maxOffset)
        {
            if (presentationRoot == null)
                return;

            float positionDelta = Vector3.Distance(previousPosition, currentPosition);
            float rotationDelta = Quaternion.Angle(previousRotation, currentRotation);
            if (positionDelta < snapDistance && rotationDelta < snapRotation)
                return;

            Quaternion correctionRotation = Quaternion.Inverse(currentRotation) * previousRotation;
            Vector3 correctionPosition = Quaternion.Inverse(currentRotation) * (previousPosition - currentPosition);
            presentationCorrectionPosition = correctionPosition + correctionRotation * presentationCorrectionPosition;
            presentationCorrectionRotation = correctionRotation * presentationCorrectionRotation;

            float clampedMaxOffset = Mathf.Max(0.0f, maxOffset);
            if (clampedMaxOffset > 0.0f && presentationCorrectionPosition.magnitude > clampedMaxOffset)
                presentationCorrectionPosition = presentationCorrectionPosition.normalized * clampedMaxOffset;
        }

        private void TickPresentationCorrection(float deltaTime, float correctionSmooth)
        {
            if (presentationRoot == null)
                return;

            float dt = Mathf.Max(0.0001f, deltaTime);
            float t = correctionSmooth > 0.0f
                ? 1.0f - Mathf.Exp(-correctionSmooth * dt)
                : 1.0f;
            presentationCorrectionPosition = Vector3.Lerp(presentationCorrectionPosition, Vector3.zero, t);
            presentationCorrectionRotation = Quaternion.Slerp(presentationCorrectionRotation, Quaternion.identity, t);

            if (presentationCorrectionPosition.sqrMagnitude <= 0.000001f)
                presentationCorrectionPosition = Vector3.zero;
            if (Quaternion.Angle(presentationCorrectionRotation, Quaternion.identity) <= 0.05f)
                presentationCorrectionRotation = Quaternion.identity;

            presentationRoot.localPosition = presentationCorrectionPosition;
            presentationRoot.localRotation = presentationCorrectionRotation;
        }

        private static void AttachRemoteWheels(Transform parent, CarLoadoutConfig loadout, BackendCarConfigPayload carConfig, List<RemoteWheelBinding> wheelBindings)
        {
            if (parent == null || loadout == null || loadout.PlayerCarConfig == null || loadout.PlayerCarConfig.Visual == null)
                return;

            PlayerCarVisualSettings visual = loadout.PlayerCarConfig.Visual;
            if (visual.wheelPrefab == null)
                return;

            float wheelHeight = visual.wheelHeight;
            SuspensionConfig suspension = GetSuspensionConfig(loadout, carConfig);
            if (suspension != null && suspension.applyVisualRideHeight)
                wheelHeight = suspension.visualWheelHeight;

            float halfWheelBase = Mathf.Max(0.2f, visual.wheelBase) * 0.5f;
            float halfAxle = Mathf.Max(0.2f, visual.axleWidth) * 0.5f;
            float frontZ = visual.zOffset + halfWheelBase;
            float rearZ = visual.zOffset - halfWheelBase;

            CreateRemoteWheel(parent, visual.wheelPrefab, "FrontLeft", new Vector3(-halfAxle, wheelHeight, frontZ), false, wheelBindings);
            CreateRemoteWheel(parent, visual.wheelPrefab, "FrontRight", new Vector3(halfAxle, wheelHeight, frontZ), true, wheelBindings);
            CreateRemoteWheel(parent, visual.wheelPrefab, "RearLeft", new Vector3(-halfAxle, wheelHeight, rearZ), false, wheelBindings);
            CreateRemoteWheel(parent, visual.wheelPrefab, "RearRight", new Vector3(halfAxle, wheelHeight, rearZ), true, wheelBindings);
        }

        private static SuspensionConfig GetSuspensionConfig(CarLoadoutConfig loadout, BackendCarConfigPayload carConfig)
        {
            if (loadout == null || loadout.SuspensionConfigs == null || loadout.SuspensionConfigs.Count == 0)
                return null;

            int index = carConfig != null ? carConfig.suspension_index : loadout.DefaultSuspensionIndex;
            index = Mathf.Clamp(index, 0, loadout.SuspensionConfigs.Count - 1);
            return loadout.SuspensionConfigs[index];
        }

        private static void CreateRemoteWheel(Transform parent, GameObject wheelPrefab, string name, Vector3 localPosition, bool mirrorRightSide, List<RemoteWheelBinding> wheelBindings)
        {
            GameObject wheelRoot = new GameObject(name);
            wheelRoot.transform.SetParent(parent, false);
            wheelRoot.transform.localPosition = localPosition;
            wheelRoot.transform.localRotation = Quaternion.identity;
            wheelRoot.transform.localScale = Vector3.one;

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(wheelRoot.transform, false);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;
            visualRoot.transform.localScale = Vector3.one;

            GameObject wheelInstance = UnityEngine.Object.Instantiate(wheelPrefab, visualRoot.transform);
            wheelInstance.name = "WheelMesh";
            wheelInstance.transform.localPosition = Vector3.zero;
            wheelInstance.transform.localRotation = mirrorRightSide
                ? Quaternion.Euler(0.0f, 0.0f, 180.0f)
                : Quaternion.identity;
            wheelInstance.transform.localScale = Vector3.one;
            StripGameplayComponents(wheelInstance, keepBodyColliders: false);
            if (wheelBindings != null)
                wheelBindings.Add(new RemoteWheelBinding { VisualRoot = visualRoot.transform });
        }

        private static void ApplyRemotePaint(GameObject bodyInstance, BackendCarConfigPayload carConfig)
        {
            if (bodyInstance == null || carConfig == null || !carConfig.has_paint)
                return;

            Renderer[] renderers = bodyInstance.GetComponentsInChildren<Renderer>(true);
            if (renderers == null)
                return;

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            Color color = carConfig.paint.ToColor();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(block);
                block.SetColor("_MainColor", color);
                renderer.SetPropertyBlock(block);
            }
        }

        private static void StripGameplayComponents(GameObject root, bool keepBodyColliders)
        {
            if (!keepBodyColliders)
            {
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                    UnityEngine.Object.Destroy(colliders[i]);
            }

            Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
                UnityEngine.Object.Destroy(rigidbodies[i]);

            CarControllerBase[] controllers = root.GetComponentsInChildren<CarControllerBase>(true);
            for (int i = 0; i < controllers.Length; i++)
                UnityEngine.Object.Destroy(controllers[i]);

            WheelCollider[] wheelColliders = root.GetComponentsInChildren<WheelCollider>(true);
            for (int i = 0; i < wheelColliders.Length; i++)
                UnityEngine.Object.Destroy(wheelColliders[i]);

            Joint[] joints = root.GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
                UnityEngine.Object.Destroy(joints[i]);
        }

        private void ResolveWheelBindings()
        {
            wheelBindings.Clear();
            string[] names = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
            for (int i = 0; i < names.Length; i++)
            {
                Transform wheelRoot = presentationRoot != null ? presentationRoot.Find(names[i]) : null;
                if (wheelRoot == null)
                    continue;

                Transform visualRoot = wheelRoot.Find("VisualRoot");
                if (visualRoot == null)
                    continue;

                wheelBindings.Add(new RemoteWheelBinding { VisualRoot = visualRoot });
            }
        }

        private static void CreateFallbackVisual(Transform parent, Material fallbackMaterial, Color fallbackColor)
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            primitive.name = "RemoteFallback";
            primitive.transform.SetParent(parent, false);
            primitive.transform.localScale = new Vector3(1.3f, 0.8f, 2.7f);
            primitive.transform.localPosition = Vector3.up * 0.9f;
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);

            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (fallbackMaterial != null)
                    renderer.sharedMaterial = fallbackMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", fallbackColor);
                block.SetColor("_BaseColor", fallbackColor);
                renderer.SetPropertyBlock(block);
            }
        }

        private static void EnsureRemotePhysics(GameObject rootObject, GameObject bodyInstance, PlayerCarConfig playerConfig)
        {
            if (rootObject == null)
                return;

            Rigidbody body = rootObject.GetComponent<Rigidbody>();
            if (body == null)
                body = rootObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.maxDepenetrationVelocity = 7.5f;

            if (bodyInstance != null)
            {
                Renderer[] renderers = bodyInstance.GetComponentsInChildren<Renderer>(true);
                if (renderers != null && renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);

                    BoxCollider collider = rootObject.GetComponent<BoxCollider>();
                    if (collider == null)
                        collider = rootObject.AddComponent<BoxCollider>();
                    collider.center = rootObject.transform.InverseTransformPoint(bounds.center);
                    Vector3 scale = rootObject.transform.lossyScale;
                    collider.size = new Vector3(
                        bounds.size.x / Mathf.Max(0.0001f, scale.x),
                        bounds.size.y / Mathf.Max(0.0001f, scale.y),
                        bounds.size.z / Mathf.Max(0.0001f, scale.z));
                }
            }
        }

        private static void EnsureRemoteDamage(GameObject rootObject, GameObject bodyInstance, PlayerCarConfig playerConfig, GameObject sourceBodyPrefab)
        {
            if (rootObject == null || playerConfig == null)
                return;

            DamageManager damageManager = rootObject.GetComponent<DamageManager>();
            if (damageManager == null)
                damageManager = rootObject.AddComponent<DamageManager>();

            CarDamageController damageController = rootObject.GetComponent<CarDamageController>();
            if (damageController == null)
                damageController = rootObject.AddComponent<CarDamageController>();

            damageController.ApplyDamageSettings(playerConfig.Damage);
            Renderer runtimeTargetRenderer = ResolveRuntimeTargetRenderer(bodyInstance, sourceBodyPrefab, playerConfig.Damage.targetRenderer);
            Renderer[] renderers = bodyInstance != null ? bodyInstance.GetComponentsInChildren<Renderer>(true) : null;
            Material[] materials = renderers != null && renderers.Length > 0
                ? CollectRuntimeMaterials(renderers)
                : null;
            damageController.OverrideRuntimeTargets(
                runtimeTargetRenderer,
                renderers,
                materials);
            damageController.InitializeFromBody(bodyInstance);
        }

        private void RefreshDamageBindings()
        {
            if (damageController == null)
                return;

            Transform body = transform.Find("Body");
            GameObject bodyObject = body != null ? body.gameObject : root;
            Renderer[] renderers = bodyObject.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return;

            damageController.OverrideRuntimeTargets(
                renderers[0],
                renderers,
                CollectRuntimeMaterials(renderers));
            damageController.EnsureNetworkTextureReady();
        }

        public void SetCollisionEnabled(bool enabled)
        {
            if (collisionEnabled == enabled && bodyColliders != null)
                return;

            collisionEnabled = enabled;
            if (bodyColliders == null)
                return;

            for (int i = 0; i < bodyColliders.Length; i++)
            {
                Collider collider = bodyColliders[i];
                if (collider == null)
                    continue;
                collider.enabled = enabled;
            }
        }

        private static Renderer ResolveRuntimeTargetRenderer(GameObject runtimeBodyInstance, GameObject sourceBodyPrefab, Renderer sourceRenderer)
        {
            if (runtimeBodyInstance == null)
                return null;
            if (sourceRenderer == null || sourceBodyPrefab == null)
                return runtimeBodyInstance.GetComponentInChildren<Renderer>(true);

            string relativePath = BuildRelativePath(sourceRenderer.transform, sourceBodyPrefab.transform);
            if (string.IsNullOrWhiteSpace(relativePath))
                return runtimeBodyInstance.GetComponentInChildren<Renderer>(true);

            Transform runtimeTarget = runtimeBodyInstance.transform.Find(relativePath);
            if (runtimeTarget == null)
                return runtimeBodyInstance.GetComponentInChildren<Renderer>(true);

            Renderer mapped = runtimeTarget.GetComponent<Renderer>();
            return mapped != null ? mapped : runtimeBodyInstance.GetComponentInChildren<Renderer>(true);
        }

        private static string BuildRelativePath(Transform target, Transform root)
        {
            if (target == null || root == null)
                return null;
            if (target == root)
                return string.Empty;

            List<string> parts = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            if (current != root)
                return null;

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static Material[] CollectRuntimeMaterials(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                return null;

            List<Material> result = new List<Material>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.materials == null)
                    continue;

                Material[] materials = renderer.materials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null)
                        continue;
                    result.Add(material);
                }
            }

            return result.Count > 0 ? result.ToArray() : null;
        }

        private static void EnsureNetworkEntity(GameObject rootObject, string playerId)
        {
            if (rootObject == null)
                return;

            NetworkVehicleEntity entity = rootObject.GetComponent<NetworkVehicleEntity>();
            if (entity == null)
                entity = rootObject.AddComponent<NetworkVehicleEntity>();
            entity.Configure(playerId, false);
        }
    }
}
