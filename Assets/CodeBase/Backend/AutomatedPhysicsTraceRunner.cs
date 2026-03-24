using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(12000)]
[DisallowMultipleComponent]
public sealed class AutomatedPhysicsTraceRunner : MonoBehaviour
{
    private const string AutorunEnvVar = "RRR_PHYSICS_TRACE_AUTORUN";
    private const string AutorunArg = "-rrrPhysicsTraceAutorun";
    private const string OutputEnvVar = "RRR_PHYSICS_TRACE_OUTPUT";
    private const string OutputArg = "-rrrPhysicsTraceOutput";
    private const string LabelEnvVar = "RRR_PHYSICS_TRACE_LABEL";
    private const string LabelArg = "-rrrPhysicsTraceLabel";
    private const string TickCountEnvVar = "RRR_PHYSICS_TRACE_TICKS";
    private const string TickCountArg = "-rrrPhysicsTraceTicks";

    private readonly List<WheelBinding> wheelBindings = new List<WheelBinding>(4);

    private string configuredOutputPath;
    private string configuredLabel;
    private int configuredTickCount = 480;
    private bool initializationAttempted;
    private bool initialized;
    private bool completed;
    private string resolvedOutputPath;
    private PlayerCar playerCar;
    private CarControllerBase controller;
    private Rigidbody body;
    private float originalFixedDeltaTime;
    private bool fixedDeltaTimeOverridden;
    private SimulationMode originalSimulationMode = SimulationMode.FixedUpdate;
    private bool simulationModeOverridden;
    private TraceSession traceSession;

    private sealed class WheelBinding
    {
        public Transform VisualRoot;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!TryResolveConfig(out string outputPath, out string label, out int tickCount))
            return;

        AutomatedPhysicsTraceRunner existing = FindFirstObjectByType<AutomatedPhysicsTraceRunner>();
        if (existing != null)
        {
            existing.Configure(outputPath, label, tickCount);
            return;
        }

        GameObject root = new GameObject("AutomatedPhysicsTraceRunner");
        DontDestroyOnLoad(root);
        AutomatedPhysicsTraceRunner runner = root.AddComponent<AutomatedPhysicsTraceRunner>();
        runner.Configure(outputPath, label, tickCount);
    }

    public void Configure(string outputPath, string label, int tickCount)
    {
        configuredOutputPath = outputPath;
        configuredLabel = label;
        configuredTickCount = Mathf.Clamp(tickCount <= 0 ? 480 : tickCount, 30, 10000);
    }

    private void Update()
    {
        if (completed)
            return;

        if (!initialized)
        {
            if (!TryInitialize())
                return;
        }

        try
        {
            RunScenario();
        }
        catch (Exception ex)
        {
            Debug.LogError("AutomatedPhysicsTraceRunner: failed to run automated trace. " + ex, this);
        }
        finally
        {
            RestoreSimulationSettings();
            completed = true;
            Application.Quit(0);
        }
    }

    private void OnDestroy()
    {
        RestoreSimulationSettings();
    }

    private bool TryInitialize()
    {
        if (initializationAttempted && !initialized)
            return false;

        initializationAttempted = true;
        playerCar = FindFirstObjectByType<PlayerCar>();
        if (playerCar == null)
            return false;

        controller = playerCar.Controller != null ? playerCar.Controller : playerCar.GetComponent<CarControllerBase>();
        body = playerCar.GetComponent<Rigidbody>();
        if (controller == null || body == null)
            return false;

        RefreshWheelBindings();
        resolvedOutputPath = ResolveOutputPath(configuredOutputPath);
        Application.runInBackground = true;

        originalFixedDeltaTime = Time.fixedDeltaTime;
        fixedDeltaTimeOverridden = true;
        Time.fixedDeltaTime = Mathf.Max(0.0001f, Time.fixedDeltaTime);

        originalSimulationMode = Physics.simulationMode;
        simulationModeOverridden = true;
        Physics.simulationMode = SimulationMode.Script;

        controller.SetManualSimulationEnabled(true);
        controller.SetInputEnabled(true);

        traceSession = new TraceSession
        {
            format_version = 1,
            label = string.IsNullOrWhiteSpace(configuredLabel) ? "automated-physics-trace" : configuredLabel,
            created_at = DateTime.UtcNow.ToString("O"),
            scene_name = SceneManager.GetActiveScene().name,
            map_id = ResolveMapId(),
            player_id = ResolvePlayerId(),
            tick_rate = Mathf.Max(1, Mathf.RoundToInt(1.0f / Mathf.Max(0.0001f, Time.fixedDeltaTime))),
            fixed_delta_time = Time.fixedDeltaTime,
            spawn_position = BackendVector3.FromVector3(playerCar.transform.position),
            spawn_rotation = BackendVector3.FromVector3(playerCar.transform.eulerAngles),
            car_config = ResolveCarConfig(),
            initial_state = CaptureState(),
            initial_debug = CaptureDebugState()
        };

        initialized = true;
        return true;
    }

    private void RunScenario()
    {
        if (traceSession == null || controller == null)
            return;

        int tickCount = Mathf.Max(1, configuredTickCount);
        float deltaTime = Time.fixedDeltaTime;

        for (int tick = 0; tick < tickCount; tick++)
        {
            CarControlFrame frame = EvaluateScenario(tick, tickCount);
            frame.Clamp();
            controller.SimulateManualStep(frame, deltaTime);
            Physics.Simulate(deltaTime);
            Physics.SyncTransforms();

            traceSession.frames.Add(new TraceFrame
            {
                tick = tick,
                seq = tick + 1,
                client_time = tick,
                fixed_delta_time = deltaTime,
                input = BackendCarControlInputPayload.FromControlFrame(frame),
                state = CaptureState(),
                debug = CaptureDebugState()
            });
        }

        string directory = Path.GetDirectoryName(resolvedOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resolvedOutputPath, JsonUtility.ToJson(traceSession, true));
        Debug.Log("AutomatedPhysicsTraceRunner: trace saved to " + resolvedOutputPath, this);
    }

    private static CarControlFrame EvaluateScenario(int tick, int totalTicks)
    {
        if (tick < 30)
            return CarControlFrame.CreateBrakingFrame();

        if (tick < 150)
        {
            return new CarControlFrame
            {
                Motor = 1.0f,
                Steer = 0.0f,
                Brake = false,
                Handbrake = false,
                Nitro = false
            };
        }

        if (tick < 240)
        {
            return new CarControlFrame
            {
                Motor = 1.0f,
                Steer = 0.45f,
                Brake = false,
                Handbrake = false,
                Nitro = false
            };
        }

        if (tick < 320)
        {
            return new CarControlFrame
            {
                Motor = 0.75f,
                Steer = -0.35f,
                Brake = false,
                Handbrake = false,
                Nitro = tick >= 270 && tick < 305
            };
        }

        if (tick < 390)
        {
            return new CarControlFrame
            {
                Motor = 0.25f,
                Steer = 0.7f,
                Brake = false,
                Handbrake = true,
                Nitro = false
            };
        }

        if (tick < totalTicks - 20)
        {
            return new CarControlFrame
            {
                Motor = -0.2f,
                Steer = 0.0f,
                Brake = true,
                Handbrake = false,
                Nitro = false
            };
        }

        return CarControlFrame.CreateBrakingFrame();
    }

    private BackendPlayerStateSnapshot CaptureState()
    {
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

    private void RestoreSimulationSettings()
    {
        if (controller != null)
            controller.SetManualSimulationEnabled(false);

        if (simulationModeOverridden)
        {
            Physics.simulationMode = originalSimulationMode;
            simulationModeOverridden = false;
        }

        if (fixedDeltaTimeOverridden)
        {
            Time.fixedDeltaTime = originalFixedDeltaTime;
            fixedDeltaTimeOverridden = false;
        }
    }

    private static bool TryResolveConfig(out string outputPath, out string label, out int tickCount)
    {
        outputPath = Environment.GetEnvironmentVariable(OutputEnvVar);
        label = Environment.GetEnvironmentVariable(LabelEnvVar);
        tickCount = 480;

        string autorunValue = Environment.GetEnvironmentVariable(AutorunEnvVar);
        if (string.IsNullOrWhiteSpace(autorunValue))
            autorunValue = TryGetArgumentValue(AutorunArg);
        if (string.IsNullOrWhiteSpace(autorunValue))
            autorunValue = "0";

        if (string.Equals(autorunValue, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(autorunValue, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(autorunValue, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = TryGetArgumentValue(OutputArg);
        if (string.IsNullOrWhiteSpace(label))
            label = TryGetArgumentValue(LabelArg);

        string tickCountValue = Environment.GetEnvironmentVariable(TickCountEnvVar);
        if (string.IsNullOrWhiteSpace(tickCountValue))
            tickCountValue = TryGetArgumentValue(TickCountArg);
        if (!string.IsNullOrWhiteSpace(tickCountValue))
            int.TryParse(tickCountValue, out tickCount);

        return !string.IsNullOrWhiteSpace(outputPath);
    }

    private static string TryGetArgumentValue(string argumentName)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], argumentName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                return "1";

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

        string fileName = "automated-physics-trace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
        return Path.Combine(expandedPath, fileName);
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

    private static BackendCarConfigPayload ResolveCarConfig()
    {
        string playerId = ResolvePlayerId();
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
    private sealed class TraceSession
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
        public List<TraceFrame> frames = new List<TraceFrame>(4096);
    }

    [Serializable]
    private sealed class TraceFrame
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
