using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(2000)]
[DisallowMultipleComponent]
public sealed class PhysicsTraceRecorder : MonoBehaviour
{
    private const string OutputEnvVar = "RRR_PHYSICS_TRACE_OUTPUT";
    private const string OutputArg = "-rrrPhysicsTraceOutput";
    private const string LabelArg = "-rrrPhysicsTraceLabel";

    private readonly List<WheelBinding> wheelBindings = new List<WheelBinding>(4);

    private string configuredOutputPath;
    private string configuredLabel;
    private string resolvedOutputPath;
    private bool traceInitialized;
    private bool traceFlushed;
    private int nextSequence = 1;
    private PlayerCar playerCar;
    private CarControllerBase controller;
    private Rigidbody body;
    private PhysicsTraceSession traceSession;

    private sealed class WheelBinding
    {
        public Transform VisualRoot;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!TryResolveBootstrapConfig(out string outputPath, out string label))
            return;

        PhysicsTraceRecorder existing = FindFirstObjectByType<PhysicsTraceRecorder>();
        if (existing != null)
        {
            existing.Configure(outputPath, label);
            return;
        }

        GameObject root = new GameObject("PhysicsTraceRecorder");
        DontDestroyOnLoad(root);
        PhysicsTraceRecorder recorder = root.AddComponent<PhysicsTraceRecorder>();
        recorder.Configure(outputPath, label);
    }

    public void Configure(string outputPath, string label)
    {
        configuredOutputPath = outputPath;
        configuredLabel = label;
    }

    private void FixedUpdate()
    {
        if (!EnsureInitialized())
            return;
        if (playerCar == null || controller == null)
            return;

        traceSession.frames.Add(CaptureFrame());
    }

    private void OnDisable()
    {
        FlushTrace();
    }

    private void OnApplicationQuit()
    {
        FlushTrace();
    }

    private bool EnsureInitialized()
    {
        if (traceInitialized)
            return true;
        if (string.IsNullOrWhiteSpace(configuredOutputPath))
            return false;

        playerCar = FindFirstObjectByType<PlayerCar>();
        if (playerCar == null)
            return false;

        controller = playerCar.Controller != null ? playerCar.Controller : playerCar.GetComponent<CarControllerBase>();
        body = playerCar.GetComponent<Rigidbody>();
        if (controller == null)
            return false;

        RefreshWheelBindings();
        traceSession = CreateTraceSession();
        resolvedOutputPath = ResolveOutputPath(configuredOutputPath);
        traceInitialized = true;
        traceFlushed = false;
        Debug.Log("PhysicsTraceRecorder: recording trace to " + resolvedOutputPath, this);
        return true;
    }

    private void FlushTrace()
    {
        if (!traceInitialized || traceFlushed || traceSession == null || string.IsNullOrWhiteSpace(resolvedOutputPath))
            return;

        try
        {
            string directory = Path.GetDirectoryName(resolvedOutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(resolvedOutputPath, JsonUtility.ToJson(traceSession, true));
            traceFlushed = true;
            Debug.Log("PhysicsTraceRecorder: trace saved to " + resolvedOutputPath, this);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("PhysicsTraceRecorder: failed to write trace. " + ex.Message, this);
        }
    }

    private PhysicsTraceSession CreateTraceSession()
    {
        string playerId = ResolvePlayerId();
        return new PhysicsTraceSession
        {
            format_version = 1,
            label = string.IsNullOrWhiteSpace(configuredLabel) ? "physics-trace" : configuredLabel,
            created_at = DateTime.UtcNow.ToString("O"),
            scene_name = SceneManager.GetActiveScene().name,
            map_id = ResolveMapId(),
            player_id = playerId,
            tick_rate = Mathf.Max(1, Mathf.RoundToInt(1.0f / Mathf.Max(0.0001f, Time.fixedDeltaTime))),
            fixed_delta_time = Time.fixedDeltaTime,
            spawn_position = BackendVector3.FromVector3(playerCar.transform.position),
            spawn_rotation = BackendVector3.FromVector3(playerCar.transform.eulerAngles),
            car_config = ResolveCarConfig(playerId),
            initial_state = CaptureState(),
            initial_debug = CaptureDebugState()
        };
    }

    private PhysicsTraceFrame CaptureFrame()
    {
        CarControlFrame inputFrame = controller.LastAppliedControlFrame;
        inputFrame.Clamp();
        return new PhysicsTraceFrame
        {
            tick = nextSequence - 1,
            seq = nextSequence++,
            client_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            fixed_delta_time = Time.fixedDeltaTime,
            input = BackendCarControlInputPayload.FromControlFrame(inputFrame),
            state = CaptureState(),
            debug = CaptureDebugState()
        };
    }

    private BackendPlayerStateSnapshot CaptureState()
    {
        if (playerCar == null)
            return new BackendPlayerStateSnapshot();

        if (body == null)
            body = playerCar.GetComponent<Rigidbody>();

        RefreshWheelBindings();
        BackendPlayerStateSnapshot snapshot = new BackendPlayerStateSnapshot
        {
            position = BackendVector3.FromVector3(playerCar.transform.position),
            rotation = BackendVector3.FromVector3(playerCar.transform.eulerAngles),
            velocity = BackendVector3.FromVector3(body != null ? body.linearVelocity : Vector3.zero),
            angular_velocity = BackendVector3.FromVector3(body != null ? body.angularVelocity : Vector3.zero)
        };

        for (int i = 0; i < wheelBindings.Count; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding == null || binding.VisualRoot == null)
                continue;

            snapshot.wheel_states.Add(new BackendWheelPose
            {
                position = BackendVector3.FromVector3(binding.VisualRoot.localPosition),
                rotation = BackendVector3.FromVector3(binding.VisualRoot.localRotation.eulerAngles)
            });
        }

        return snapshot;
    }

    private BackendVehicleDebugState CaptureDebugState()
    {
        if (controller == null)
            return null;

        CarControllerSimulationState simulationState = controller.CaptureSimulationState();
        return new BackendVehicleDebugState
        {
            current_gear = simulationState.currentGear,
            requested_gear = simulationState.requestedGear,
            current_rpm = simulationState.currentRpm,
            shift_timer = simulationState.shiftTimer,
            shift_target_rpm = simulationState.shiftTargetRpm,
            shift_state = simulationState.shiftState,
            speed_kph = controller.SpeedKph,
            motor_torque = controller.LastMotorTorque,
            brake_torque = controller.LastBrakeTorque,
            rear_brake_torque = controller.LastRearBrakeTorque,
            steer_angle = simulationState.currentSteerAngle,
            drift_kick_force = simulationState.currentDriftKickForce,
            nitro_amount = simulationState.nitroAmount,
            nitro_active = simulationState.nitroActive,
            nitro_initialized = simulationState.nitroInitialized,
            grounded_wheels = controller.GroundedWheelCount,
            wheel_count = controller.WheelCount,
            input_enabled = controller.InputEnabled,
            sleeping = controller.IsRigidBodySleeping
        };
    }

    private void RefreshWheelBindings()
    {
        WheelCollider[] colliders = playerCar != null ? playerCar.GetComponentsInChildren<WheelCollider>(true) : null;
        if (colliders == null || colliders.Length == 0)
        {
            wheelBindings.Clear();
            return;
        }

        if (wheelBindings.Count == colliders.Length)
            return;

        Array.Sort(colliders, CompareWheelColliders);
        wheelBindings.Clear();
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

            wheelBindings.Add(new WheelBinding
            {
                VisualRoot = visualRoot
            });
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

    private static bool TryResolveBootstrapConfig(out string outputPath, out string label)
    {
        outputPath = Environment.GetEnvironmentVariable(OutputEnvVar);
        label = TryGetArgumentValue(LabelArg);

        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = TryGetArgumentValue(OutputArg);

        return !string.IsNullOrWhiteSpace(outputPath);
    }

    private static string TryGetArgumentValue(string argumentName)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], argumentName, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static string ResolveOutputPath(string configuredPath)
    {
        string expandedPath = Environment.ExpandEnvironmentVariables(configuredPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(expandedPath))
            expandedPath = "physics-trace";

        bool treatAsDirectory =
            expandedPath.EndsWith("\\", StringComparison.Ordinal) ||
            expandedPath.EndsWith("/", StringComparison.Ordinal) ||
            !Path.HasExtension(expandedPath);

        if (!Path.IsPathRooted(expandedPath))
            expandedPath = Path.Combine(Application.persistentDataPath, expandedPath);

        if (!treatAsDirectory)
            return expandedPath;

        string fileName = "physics-trace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
        return Path.Combine(expandedPath, fileName);
    }

    private static string ResolvePlayerId()
    {
        BackendSessionResponse session = Backend.Client.Session;
        if (session != null && !string.IsNullOrWhiteSpace(session.player_id))
            return session.player_id;
        return "local_player";
    }

    private static string ResolveMapId()
    {
        BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
        if (matchInfo != null && !string.IsNullOrWhiteSpace(matchInfo.map_id))
            return matchInfo.map_id;
        return SceneManager.GetActiveScene().name;
    }

    private static BackendCarConfigPayload ResolveCarConfig(string playerId)
    {
        BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
        if (matchInfo != null && matchInfo.players != null)
        {
            for (int i = 0; i < matchInfo.players.Count; i++)
            {
                BackendMatchPlayerInfo player = matchInfo.players[i];
                if (player == null || !string.Equals(player.player_id, playerId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (player.car_config != null)
                    return player.car_config;
            }
        }

        return PlayerCarSelection.TryGetPayload(out PlayerCarSelectionPayload payload)
            ? BackendCarConfigPayload.FromPlayerSelection(payload)
            : null;
    }

    [Serializable]
    private sealed class PhysicsTraceSession
    {
        public int format_version;
        public string label;
        public string created_at;
        public string scene_name;
        public string map_id;
        public string player_id;
        public int tick_rate;
        public float fixed_delta_time;
        public BackendVector3 spawn_position;
        public BackendVector3 spawn_rotation;
        public BackendCarConfigPayload car_config;
        public BackendPlayerStateSnapshot initial_state;
        public BackendVehicleDebugState initial_debug;
        public List<PhysicsTraceFrame> frames = new List<PhysicsTraceFrame>(4096);
    }

    [Serializable]
    private sealed class PhysicsTraceFrame
    {
        public int tick;
        public int seq;
        public long client_time;
        public float fixed_delta_time;
        public BackendCarControlInputPayload input;
        public BackendPlayerStateSnapshot state;
        public BackendVehicleDebugState debug;
    }
}
