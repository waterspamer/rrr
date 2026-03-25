using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Prediction;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(10020)]
[DisallowMultipleComponent]
public sealed class PurrNetDedicatedObserverRuntime : MonoBehaviour
{
    private readonly ConcurrentQueue<string> pendingLogs = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<MainThreadWorkItem> mainThreadWorkItems = new ConcurrentQueue<MainThreadWorkItem>();
    private readonly Dictionary<int, CollisionSubscription> collisionSubscriptions = new Dictionary<int, CollisionSubscription>();
    private readonly List<ObserverCollisionEvent> recentCollisions = new List<ObserverCollisionEvent>(32);

    private HttpListener listener;
    private CancellationTokenSource cancellation;
    private Task serverTask;
    private DedicatedObserverSettings settings;
    private int mainThreadId;
    private DateTime startedAtUtc;
    private float startedAtRealtime;
    private int nextCollisionSequence = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntime()
    {
        if (!Application.isBatchMode || !PurrNetSessionRuntime.IsServerMode)
            return;

        PurrNetDedicatedObserverRuntime existing = FindFirstObjectByType<PurrNetDedicatedObserverRuntime>();
        if (existing != null)
            return;

        GameObject root = new GameObject("PurrNetDedicatedObserverRuntime");
        root.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(root);
        root.AddComponent<PurrNetDedicatedObserverRuntime>();
    }

    private void Awake()
    {
        Application.runInBackground = true;
        settings = DedicatedObserverSettings.FromEnvironment();
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
        startedAtUtc = DateTime.UtcNow;
        startedAtRealtime = Time.realtimeSinceStartup;
        StartServer();
    }

    private void Update()
    {
        PumpMainThreadQueue();
        RefreshCollisionSubscriptions();

        while (pendingLogs.TryDequeue(out string line))
            Debug.Log(line, this);
    }

    private void OnDestroy()
    {
        UnsubscribeAllCollisions();

        try
        {
            cancellation?.Cancel();
            listener?.Stop();
            listener?.Close();
        }
        catch
        {
        }
    }

    private void StartServer()
    {
        try
        {
            cancellation = new CancellationTokenSource();
            listener = new HttpListener();
            listener.Prefixes.Add(settings.HttpPrefix);
            listener.Start();
            serverTask = Task.Run(() => RunLoopAsync(cancellation.Token));
            pendingLogs.Enqueue($"PurrNet dedicated observer listening on {settings.HttpPrefix}");
        }
        catch (Exception ex)
        {
            Debug.LogError("PurrNetDedicatedObserverRuntime: failed to start observer API. " + ex.Message, this);
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener != null && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested)
                    return;
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(context, token), token);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken token)
    {
        try
        {
            string method = context.Request.HttpMethod.ToUpperInvariant();
            if (method == "OPTIONS")
            {
                await WriteOptionsAsync(context.Response);
                return;
            }

            if (!IsAuthorized(context.Request))
            {
                await WriteJsonAsync(context.Response, 401, "{\"code\":\"UNAUTHORIZED\",\"message\":\"Invalid service token\"}");
                return;
            }

            string[] segments = GetPathSegments(context.Request);

            if (segments.Length == 1 && string.Equals(segments[0], "health", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                ObserverHealthResponse payload = await RunOnMainThreadAsync(BuildHealthResponse);
                await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                return;
            }

            if (segments.Length == 3 &&
                string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[1], "v1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[2], "damage-config", StringComparison.OrdinalIgnoreCase))
            {
                if (method == "GET")
                {
                    ObserverDamageConfigState payload = await RunOnMainThreadAsync(BuildDamageConfigResponse);
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }

                if (method == "PUT")
                {
                    string requestBody = await ReadRequestBodyAsync(context.Request);
                    ObserverDamageConfigState requestPayload = ParseRequestBody<ObserverDamageConfigState>(requestBody);
                    ObserverDamageConfigState payload = await RunOnMainThreadAsync(() => UpdateDamageConfig(requestPayload));
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }
            }

            if (segments.Length == 3 &&
                string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[1], "v1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[2], "rooms", StringComparison.OrdinalIgnoreCase) &&
                method == "GET")
            {
                ObserverRoomsResponse payload = await RunOnMainThreadAsync(BuildRoomsResponse);
                await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                return;
            }

            if (segments.Length >= 4 &&
                string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[1], "v1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[2], "rooms", StringComparison.OrdinalIgnoreCase))
            {
                string matchId = Uri.UnescapeDataString(segments[3]);
                ValidateRoomId(matchId);

                if (segments.Length == 4 && method == "GET")
                {
                    ObserverRoomResponse payload = await RunOnMainThreadAsync(BuildRoomResponse);
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }

                if (segments.Length == 5 &&
                    string.Equals(segments[4], "snapshot", StringComparison.OrdinalIgnoreCase) &&
                    method == "GET")
                {
                    ObserverRoomSnapshotResponse payload = await RunOnMainThreadAsync(BuildSnapshot);
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }

                if (segments.Length == 5 &&
                    string.Equals(segments[4], "damage-config", StringComparison.OrdinalIgnoreCase))
                {
                    if (method == "GET")
                    {
                        ObserverDamageConfigState payload = await RunOnMainThreadAsync(BuildDamageConfigResponse);
                        await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                        return;
                    }

                    if (method == "PUT")
                    {
                        string requestBody = await ReadRequestBodyAsync(context.Request);
                        ObserverDamageConfigState requestPayload = ParseRequestBody<ObserverDamageConfigState>(requestBody);
                        ObserverDamageConfigState payload = await RunOnMainThreadAsync(() => UpdateDamageConfig(requestPayload));
                        await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                        return;
                    }
                }
            }

            await WriteJsonAsync(context.Response, 404, "{\"code\":\"NOT_FOUND\",\"message\":\"Not found\"}");
        }
        catch (ObserverApiException ex)
        {
            await WriteJsonAsync(
                context.Response,
                ex.StatusCode,
                $"{{\"code\":\"{EscapeJson(ex.Code)}\",\"message\":\"{EscapeJson(ex.Message)}\"}}");
        }
        catch (Exception ex)
        {
            pendingLogs.Enqueue("PurrNet dedicated observer request failed: " + ex.Message);
            try
            {
                await WriteJsonAsync(context.Response, 500, "{\"code\":\"INTERNAL_ERROR\",\"message\":\"Internal error\"}");
            }
            catch
            {
            }
        }
    }

    private ObserverHealthResponse BuildHealthResponse()
    {
        ObserverSnapshotContext context = CaptureContext();
        return new ObserverHealthResponse
        {
            status = "ok",
            source = "purrnet_direct",
            room_count = context.roomVisible ? 1 : 0,
            bind = settings.Bind,
            port = settings.Port,
            active_match_id = context.roomVisible ? settings.MatchId : string.Empty,
            active_room_status = context.roomVisible ? context.status : "idle",
            scene_name = context.sceneName,
            server_state = context.serverState,
            client_state = context.clientState,
            player_count = context.playerCount,
            server_tick = context.serverTick
        };
    }

    private ObserverRoomsResponse BuildRoomsResponse()
    {
        ObserverRoomsResponse response = new ObserverRoomsResponse();
        if (CaptureContext().roomVisible)
            response.items.Add(BuildRoomResponse());
        return response;
    }

    private ObserverRoomResponse BuildRoomResponse()
    {
        ObserverSnapshotContext context = CaptureContext();
        if (!context.roomVisible)
            throw new ObserverApiException(404, "ROOM_NOT_FOUND", "Room not found");

        return new ObserverRoomResponse
        {
            room_id = settings.MatchId,
            match_id = settings.MatchId,
            source = "purrnet_direct",
            map_id = settings.MapId,
            status = context.status,
            room_http_url = settings.PublicHttpBaseUrl + "/api/v1/rooms/" + Uri.EscapeDataString(settings.MatchId),
            room_ws_url = string.Empty,
            room_token = settings.ControlToken,
            scene_name = context.sceneName,
            player_count = context.playerCount,
            created_at = startedAtUtc.ToString("O"),
            tick_rate = context.tickRate,
            server_tick = context.serverTick,
            manual_tick = false
        };
    }

    private ObserverRoomSnapshotResponse BuildSnapshot()
    {
        ObserverSnapshotContext context = CaptureContext();
        if (!context.roomVisible)
            throw new ObserverApiException(404, "ROOM_NOT_FOUND", "Room not found");

        long serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ObserverRoomSnapshotResponse response = new ObserverRoomSnapshotResponse
        {
            room_id = settings.MatchId,
            match_id = settings.MatchId,
            source = "purrnet_direct",
            map_id = settings.MapId,
            status = context.status,
            server_tick = context.serverTick,
            server_time = serverTime,
            damage_config = ObserverDamageConfigState.FromRuntime(context.damageConfig),
            observer = BuildObserverDebugState(context, serverTime)
        };

        for (int index = 0; index < context.players.Count; index++)
        {
            PlayerObserverContext playerContext = context.players[index];
            if (playerContext == null || playerContext.playerCar == null)
                continue;

            ObserverPlayerState playerState = BuildPlayerState(playerContext, index, serverTime);
            response.players.Add(playerState);

            if (playerContext.damageController == null)
                continue;

            playerContext.damageController.EnsureNetworkTextureReady();
            if (!playerContext.damageController.TryCaptureDamageSnapshot(out CarDamageNetworkSnapshot snapshot) || snapshot == null)
                continue;

            response.damage_states.Add(new ObserverDamageState
            {
                player_id = playerState.player_id,
                revision = snapshot.revision,
                width = snapshot.width,
                height = snapshot.height,
                map_b64 = snapshot.rawBytes != null && snapshot.rawBytes.Length > 0
                    ? Convert.ToBase64String(snapshot.rawBytes)
                    : string.Empty,
                world_point = snapshot.hasImpactPoint ? BackendVector3.FromVector3(snapshot.worldPoint) : null,
                world_normal = snapshot.hasImpactNormal ? BackendVector3.FromVector3(snapshot.worldNormal) : null
            });
        }

        if (recentCollisions.Count > 0)
            response.collisions.AddRange(recentCollisions);

        return response;
    }

    private ObserverDamageConfigState BuildDamageConfigResponse()
    {
        PurrVehicleDamageConfigSync sync = FindFirstObjectByType<PurrVehicleDamageConfigSync>(FindObjectsInactive.Include);
        if (sync == null || !sync.TryGetCurrentConfig(out CarDamageRuntimeTuning config) || config == null)
            throw new ObserverApiException(404, "DAMAGE_CONFIG_NOT_FOUND", "Damage config not found");

        return ObserverDamageConfigState.FromRuntime(config);
    }

    private ObserverDamageConfigState UpdateDamageConfig(ObserverDamageConfigState request)
    {
        if (request == null)
            throw new ObserverApiException(400, "INVALID_REQUEST", "damage config payload is required");

        PurrVehicleDamageConfigSync sync = FindFirstObjectByType<PurrVehicleDamageConfigSync>(FindObjectsInactive.Include);
        if (sync == null)
            throw new ObserverApiException(503, "DAMAGE_CONFIG_UNAVAILABLE", "Damage config sync is unavailable");

        if (!sync.TryUpdateServerConfig(request.ToRuntime(), "observer_admin"))
            throw new ObserverApiException(409, "CONFIG_UPDATE_REJECTED", "Damage config update was rejected");

        if (!sync.TryGetCurrentConfig(out CarDamageRuntimeTuning applied) || applied == null)
            throw new ObserverApiException(500, "CONFIG_UPDATE_FAILED", "Damage config update did not persist");

        return ObserverDamageConfigState.FromRuntime(applied);
    }

    private ObserverPlayerState BuildPlayerState(PlayerObserverContext playerContext, int index, long serverTime)
    {
        PlayerCar playerCar = playerContext.playerCar;
        Rigidbody body = playerContext.body;
        CarControllerBase controller = playerContext.controller;
        PurrVehicleSpawnerPlayerObserverRecord tracked = playerContext.tracked;
        PurrPlayerProfileData profile = playerContext.profile;
        PlayerCarSelectionPayload loadout = tracked != null ? tracked.loadout : null;
        bool isBot = tracked != null && tracked.isBot;
        string playerId = !string.IsNullOrWhiteSpace(playerContext.playerId)
            ? playerContext.playerId
            : tracked != null && !string.IsNullOrWhiteSpace(tracked.playerId)
                ? tracked.playerId
                : $"player_{index}";
        string playerName = profile != null && !string.IsNullOrWhiteSpace(profile.playerName)
            ? profile.playerName
            : isBot
                ? $"Bot {playerId}"
                : playerId;
        BackendVector3 currentPosition = BackendVector3.FromVector3(playerCar.transform.position);
        BackendVector3 currentRotation = BackendVector3.FromVector3(playerCar.transform.eulerAngles);
        BackendVector3 spawnPosition = tracked != null ? BackendVector3.FromVector3(tracked.spawnPosition) : currentPosition;
        BackendVector3 spawnRotation = tracked != null ? BackendVector3.FromVector3(tracked.spawnRotationEuler) : currentRotation;

        return new ObserverPlayerState
        {
            player_id = playerId,
            player_name = playerName,
            connection_state = tracked != null && tracked.queued && !tracked.spawned ? "queued" : "in_game",
            is_server_controlled = isBot,
            authority_order = tracked != null && tracked.spawnSlot >= 0 ? tracked.spawnSlot : index,
            spawn_point_id = tracked != null && !string.IsNullOrWhiteSpace(tracked.spawnPointId) ? tracked.spawnPointId : $"purr_slot_{index}",
            spawn_position = spawnPosition,
            spawn_rotation = spawnRotation,
            ack_input_seq = 0,
            client_time = 0,
            server_received_time = serverTime,
            input = controller != null
                ? BackendCarControlInputPayload.FromControlFrame(controller.LastAppliedControlFrame)
                : new BackendCarControlInputPayload(),
            position = currentPosition,
            rotation = currentRotation,
            velocity = BackendVector3.FromVector3(body != null ? body.linearVelocity : Vector3.zero),
            angular_velocity = BackendVector3.FromVector3(body != null ? body.angularVelocity : Vector3.zero),
            car_config = BuildCarConfigPayload(loadout, playerCar),
            debug = BuildPlayerDebugState(playerContext, tracked)
        };
    }

    private ObserverRoomDebugState BuildObserverDebugState(ObserverSnapshotContext context, long serverTime)
    {
        ObserverRoomDebugState debug = new ObserverRoomDebugState
        {
            source = "purrnet_direct",
            mode = PurrNetSessionRuntime.Current.Mode.ToString(),
            match_id = settings.MatchId,
            map_id = settings.MapId,
            scene_name = context.sceneName,
            active_scene_id = context.spawnerState != null ? context.spawnerState.activeSceneId : string.Empty,
            started_at_utc = startedAtUtc.ToString("O"),
            uptime_sec = Mathf.Max(0.0f, Time.realtimeSinceStartup - startedAtRealtime),
            room_visible = context.roomVisible,
            damage_config = ObserverDamageConfigState.FromRuntime(context.damageConfig)
        };

        debug.network.server_state = context.serverState;
        debug.network.client_state = context.clientState;
        debug.network.is_server = context.networkManager != null && context.networkManager.isServer;
        debug.network.is_client = context.networkManager != null && context.networkManager.isClient;
        debug.network.transport_type = context.transport != null ? context.transport.GetType().Name : string.Empty;
        debug.network.address = context.transport != null ? context.transport.address : string.Empty;
        debug.network.port = context.transport != null ? context.transport.serverPort : 0;
        debug.network.tick_rate = context.tickRate;
        debug.network.local_tick = context.localTick;
        debug.network.synced_tick = context.syncedTick;
        debug.network.server_time = serverTime;

        debug.prediction.has_prediction_manager = context.predictionManager != null;
        debug.prediction.prediction_spawned = context.predictionManager != null && context.predictionManager.isSpawned;
        debug.prediction.hierarchy_ready = context.predictionManager != null && context.predictionManager.hierarchy != null;

        if (context.spawnerState != null)
        {
            debug.spawner.has_scene_id = context.spawnerState.hasSceneId;
            debug.spawner.prediction_manager_ready = context.spawnerState.predictionManagerReady;
            debug.spawner.prediction_manager_spawned = context.spawnerState.predictionManagerSpawned;
            debug.spawner.hierarchy_ready = context.spawnerState.hierarchyReady;
            debug.spawner.scene_players_ready = context.spawnerState.scenePlayersReady;
            debug.spawner.players_manager_ready = context.spawnerState.playersManagerReady;
            debug.spawner.is_server_spawner = context.spawnerState.isServerSpawner;
            debug.spawner.is_client_publisher = context.spawnerState.isClientPublisher;
            debug.spawner.local_loadout_published = context.spawnerState.localLoadoutPublished;
            debug.spawner.transient_solo_cleanup_enabled = context.spawnerState.transientSoloCleanupEnabled;
            debug.spawner.solo_idle_timeout_sec = context.spawnerState.soloIdleTimeoutSeconds;
            debug.spawner.solo_lifecycle_poll_interval_sec = context.spawnerState.soloLifecyclePollIntervalSeconds;
            debug.spawner.solo_session_active = context.spawnerState.soloSessionActive;
            debug.spawner.solo_session_human_player_id = context.spawnerState.soloSessionHumanPlayerId;
            debug.spawner.solo_session_active_for_sec = context.spawnerState.soloSessionActiveForSeconds;
            debug.spawner.seconds_since_last_human_seen = context.spawnerState.secondsSinceLastHumanSeen;
            debug.spawner.seconds_since_last_meaningful_input = context.spawnerState.secondsSinceLastMeaningfulInput;
            debug.spawner.seconds_until_idle_close = context.spawnerState.secondsUntilIdleClose;
            debug.spawner.solo_session_status = context.spawnerState.soloSessionStatus;
            debug.spawner.last_solo_session_close_reason = context.spawnerState.lastSoloSessionCloseReason;
            debug.spawner.seconds_since_last_solo_session_close = context.spawnerState.secondsSinceLastSoloSessionClose;
            debug.spawner.solo_bot_target = context.spawnerState.soloBotTarget;
            debug.spawner.tracked_bot_players = context.spawnerState.trackedBotPlayers;
            debug.spawner.pending_bot_creates = context.spawnerState.pendingBotCreates;
            debug.spawner.queued_players = context.spawnerState.queuedPlayers;
            debug.spawner.spawned_players = context.spawnerState.spawnedPlayers;
            debug.spawner.template_car_name = context.spawnerState.templateCarName;
            debug.spawner.last_wait_reason = context.spawnerState.lastWaitReason;

            if (context.spawnerState.players != null)
            {
                for (int i = 0; i < context.spawnerState.players.Count; i++)
                {
                    PurrVehicleSpawnerPlayerObserverRecord tracked = context.spawnerState.players[i];
                    if (tracked == null)
                        continue;

                    debug.tracked_players.Add(new ObserverTrackedPlayerState
                    {
                        player_id = tracked.playerId,
                        is_bot = tracked.isBot,
                        queued = tracked.queued,
                        spawned = tracked.spawned,
                        spawn_slot = tracked.spawnSlot,
                        spawn_point_id = tracked.spawnPointId,
                        spawn_position = BackendVector3.FromVector3(tracked.spawnPosition),
                        spawn_rotation = BackendVector3.FromVector3(tracked.spawnRotationEuler),
                        last_spawn_failure_reason = tracked.lastSpawnFailureReason,
                        car_config = BuildCarConfigPayload(tracked.loadout, null)
                    });
                }
            }
        }

        debug.counts.active_player_cars = context.players.Count;
        debug.counts.predicted_controllers = context.players.Count;
        debug.counts.network_entities = context.players.Count;
        debug.counts.damage_controllers = context.damageControllerCount;
        debug.counts.sleeping_rigidbodies = context.sleepingRigidBodyCount;
        debug.counts.tracked_players = debug.tracked_players.Count;
        return debug;
    }

    private ObserverPlayerDebugState BuildPlayerDebugState(PlayerObserverContext playerContext, PurrVehicleSpawnerPlayerObserverRecord tracked)
    {
        PurrPlayerProfileData profile = playerContext.profile;
        PurrPlayerStatsData stats = playerContext.stats;
        ObserverPlayerDebugState debug = new ObserverPlayerDebugState
        {
            owner_player_id = playerContext.playerId,
            is_bot = tracked != null && tracked.isBot,
            queued = tracked != null && tracked.queued,
            spawned = tracked == null || tracked.spawned,
            last_spawn_failure_reason = tracked != null ? tracked.lastSpawnFailureReason : string.Empty,
            resolved_car_config_name = playerContext.playerCar != null && playerContext.playerCar.Config != null
                ? playerContext.playerCar.Config.name
                : string.Empty,
            account_player_id = profile != null ? profile.accountPlayerId : string.Empty,
            auth_provider = profile != null ? profile.authProvider : string.Empty,
            auth_state = profile != null ? profile.authState : string.Empty,
            session_id = profile != null ? profile.sessionId : string.Empty
        };

        if (tracked != null && tracked.loadout != null)
        {
            debug.loadout_name = tracked.loadout.loadoutName;
            debug.loadout_display_name = tracked.loadout.loadoutDisplayName;
        }

        if (stats != null)
        {
            if (stats.TryGetNumber("current_health", out float currentHealth))
                debug.current_health = currentHealth;
            if (stats.TryGetNumber("max_health", out float maxHealth))
                debug.max_health = maxHealth;
            if (stats.TryGetNumber("damage_ratio", out float damageRatio))
                debug.damage_ratio = damageRatio;
            if (stats.TryGetNumber("nitro", out float syncedNitroAmount))
                debug.synced_nitro_amount = syncedNitroAmount;
            if (stats.TryGetNumber("speed_kph", out float syncedSpeed))
                debug.synced_speed_kph = syncedSpeed;
            if (stats.TryGetInteger("gear", out long syncedGear))
                debug.synced_gear = (int)syncedGear;
        }

        CarControllerBase controller = playerContext.controller;
        if (controller == null)
            return debug;

        CarControllerSimulationState simulationState = controller.CaptureSimulationState();
        debug.current_gear = simulationState.currentGear;
        debug.requested_gear = simulationState.requestedGear;
        debug.current_rpm = simulationState.currentRpm;
        debug.shift_timer = simulationState.shiftTimer;
        debug.shift_target_rpm = simulationState.shiftTargetRpm;
        debug.shift_state = simulationState.shiftState;
        debug.steer_angle = simulationState.currentSteerAngle;
        debug.steering_wheel_angle = simulationState.currentSteeringWheelAngle;
        debug.drift_kick_force = simulationState.currentDriftKickForce;
        debug.nitro_amount = simulationState.nitroAmount;
        debug.nitro_active = simulationState.nitroActive;
        debug.nitro_initialized = simulationState.nitroInitialized;
        debug.speed_kph = controller.SpeedKph;
        debug.motor_torque = controller.LastMotorTorque;
        debug.brake_torque = controller.LastBrakeTorque;
        debug.rear_brake_torque = controller.LastRearBrakeTorque;
        debug.grounded_wheels = controller.GroundedWheelCount;
        debug.wheel_count = controller.WheelCount;
        debug.input_enabled = controller.InputEnabled;
        debug.physics_simulation_enabled = controller.PhysicsSimulationEnabled;
        debug.manual_simulation_enabled = controller.ManualSimulationEnabled;
        debug.sleeping = controller.IsRigidBodySleeping;

        CarDamageController damageController = playerContext.damageController;
        debug.has_damage_controller = damageController != null;
        if (damageController == null)
            return debug;

        damageController.EnsureNetworkTextureReady();
        if (!damageController.TryCaptureDamageSnapshot(out CarDamageNetworkSnapshot snapshot) || snapshot == null)
            return debug;

        debug.can_capture_damage_snapshot = true;
        debug.captured_damage_width = snapshot.width;
        debug.captured_damage_height = snapshot.height;
        debug.captured_damage_bytes = snapshot.rawBytes != null ? snapshot.rawBytes.Length : 0;
        debug.damage_revision = snapshot.revision;
        return debug;
    }

    private BackendCarConfigPayload BuildCarConfigPayload(PlayerCarSelectionPayload loadout, PlayerCar playerCar)
    {
        BackendCarConfigPayload payload = BackendCarConfigPayload.FromPlayerSelection(loadout);
        if (payload != null)
            return payload;

        PlayerCarConfig config = playerCar != null ? playerCar.Config : null;
        return new BackendCarConfigPayload
        {
            version = 1,
            loadout_name = config != null ? config.name : string.Empty,
            loadout_display_name = config != null ? config.name : string.Empty
        };
    }

    private ObserverSnapshotContext CaptureContext()
    {
        NetworkManager networkManager = FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
        UDPTransport transport = FindFirstObjectByType<UDPTransport>(FindObjectsInactive.Include);
        PredictionManager predictionManager = FindFirstObjectByType<PredictionManager>(FindObjectsInactive.Include);
        PurrVehicleSceneSpawner spawner = FindFirstObjectByType<PurrVehicleSceneSpawner>(FindObjectsInactive.Include);
        PurrVehiclePlayerRoster roster = FindFirstObjectByType<PurrVehiclePlayerRoster>(FindObjectsInactive.Include);
        PurrVehicleDamageConfigSync damageConfigSync = FindFirstObjectByType<PurrVehicleDamageConfigSync>(FindObjectsInactive.Include);
        PurrVehicleSpawnerObserverSnapshot spawnerState = spawner != null ? spawner.CaptureObserverState() : null;
        CarDamageRuntimeTuning damageConfig = null;
        if (damageConfigSync != null)
            damageConfigSync.TryGetCurrentConfig(out damageConfig);

        ObserverSnapshotContext context = new ObserverSnapshotContext
        {
            networkManager = networkManager,
            transport = transport,
            predictionManager = predictionManager,
            spawnerState = spawnerState,
            damageConfig = damageConfig != null ? damageConfig.Clone() : null,
            sceneName = SceneManager.GetActiveScene().name,
            serverState = networkManager != null ? networkManager.serverState.ToString() : "Disconnected",
            clientState = networkManager != null ? networkManager.clientState.ToString() : "Disconnected",
            status = ResolveStatus(networkManager),
            tickRate = networkManager != null && networkManager.tickModule != null
                ? networkManager.tickModule.tickRate
                : PurrNetSessionRuntime.Current.TickRate,
            localTick = networkManager != null && networkManager.tickModule != null ? (int)networkManager.tickModule.localTick : 0,
            syncedTick = networkManager != null && networkManager.tickModule != null ? (int)networkManager.tickModule.syncedTick : 0
        };
        context.roomVisible = ShouldExposeRoom(spawnerState);

        Dictionary<string, PurrVehicleSpawnerPlayerObserverRecord> trackedById =
            new Dictionary<string, PurrVehicleSpawnerPlayerObserverRecord>(StringComparer.Ordinal);
        if (spawnerState != null && spawnerState.players != null)
        {
            for (int i = 0; i < spawnerState.players.Count; i++)
            {
                PurrVehicleSpawnerPlayerObserverRecord tracked = spawnerState.players[i];
                if (tracked != null && !string.IsNullOrWhiteSpace(tracked.playerId))
                    trackedById[tracked.playerId] = tracked;
            }
        }

        PurrVehiclePredictedController[] controllers =
            FindObjectsByType<PurrVehiclePredictedController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            PurrVehiclePredictedController predicted = controllers[i];
            if (predicted == null || !predicted.gameObject.activeInHierarchy)
                continue;

            PlayerCar playerCar = predicted.GetComponent<PlayerCar>();
            if (playerCar == null)
                continue;

            NetworkVehicleEntity entity = predicted.GetComponent<NetworkVehicleEntity>();
            string playerId = entity != null ? entity.PlayerId : string.Empty;
            trackedById.TryGetValue(playerId, out PurrVehicleSpawnerPlayerObserverRecord tracked);
            PurrPlayerProfileData profile = null;
            PurrPlayerStatsData stats = null;
            if (roster != null && !string.IsNullOrWhiteSpace(playerId))
            {
                roster.TryGetProfile(playerId, out profile);
                roster.TryGetStats(playerId, out stats);
            }

            CarDamageController damageController = playerCar.DamageController;
            Rigidbody body = predicted.GetComponent<Rigidbody>();
            if (damageController != null)
                context.damageControllerCount += 1;
            if (body != null && body.IsSleeping())
                context.sleepingRigidBodyCount += 1;

            context.players.Add(new PlayerObserverContext
            {
                playerId = playerId,
                playerCar = playerCar,
                controller = playerCar.Controller != null ? playerCar.Controller : predicted.GetComponent<CarControllerBase>(),
                damageController = damageController,
                body = body,
                tracked = tracked,
                profile = profile,
                stats = stats
            });
        }

        context.players.Sort(ComparePlayerContexts);
        context.playerCount = Mathf.Max(context.players.Count, trackedById.Count);
        context.serverTick = context.localTick;
        return context;
    }

    private static int ComparePlayerContexts(PlayerObserverContext a, PlayerObserverContext b)
    {
        bool aBot = a != null && a.tracked != null && a.tracked.isBot;
        bool bBot = b != null && b.tracked != null && b.tracked.isBot;
        if (aBot != bBot)
            return aBot ? 1 : -1;
        return string.CompareOrdinal(a != null ? a.playerId : string.Empty, b != null ? b.playerId : string.Empty);
    }

    private static string ResolveStatus(NetworkManager networkManager)
    {
        if (networkManager == null)
            return "booting";

        if (networkManager.serverState == ConnectionState.Connected || networkManager.serverState == ConnectionState.Connecting)
            return "running";

        if (networkManager.serverState == ConnectionState.Disconnected && networkManager.isServer)
            return "starting";

        return networkManager.serverState.ToString().ToLowerInvariant();
    }

    private static bool ShouldExposeRoom(PurrVehicleSpawnerObserverSnapshot spawnerState)
    {
        if (spawnerState == null)
            return false;

        if (!spawnerState.transientSoloCleanupEnabled)
            return true;

        return spawnerState.soloSessionActive ||
               spawnerState.queuedPlayers > 0 ||
               spawnerState.spawnedPlayers > 0 ||
               (spawnerState.players != null && spawnerState.players.Count > 0);
    }

    private void RefreshCollisionSubscriptions()
    {
        CarDamageController[] controllers = FindObjectsByType<CarDamageController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HashSet<int> activeIds = new HashSet<int>();

        for (int i = 0; i < controllers.Length; i++)
        {
            CarDamageController damageController = controllers[i];
            if (damageController == null || !damageController.gameObject.activeInHierarchy)
                continue;

            NetworkVehicleEntity entity = damageController.GetComponent<NetworkVehicleEntity>();
            if (entity == null)
                entity = damageController.GetComponentInParent<NetworkVehicleEntity>();
            if (entity == null || string.IsNullOrWhiteSpace(entity.PlayerId))
                continue;

            int key = damageController.GetInstanceID();
            activeIds.Add(key);
            if (collisionSubscriptions.ContainsKey(key))
                continue;

            CollisionSubscription subscription = new CollisionSubscription
            {
                damageController = damageController,
                networkEntity = entity
            };
            subscription.handler = report => HandleCollisionReport(subscription, report);
            damageController.NetworkVehicleCollisionDetected += subscription.handler;
            collisionSubscriptions[key] = subscription;
        }

        List<int> stale = new List<int>();
        foreach (KeyValuePair<int, CollisionSubscription> pair in collisionSubscriptions)
        {
            if (!activeIds.Contains(pair.Key))
                stale.Add(pair.Key);
        }

        for (int i = 0; i < stale.Count; i++)
        {
            if (!collisionSubscriptions.TryGetValue(stale[i], out CollisionSubscription subscription))
                continue;

            if (subscription.damageController != null)
                subscription.damageController.NetworkVehicleCollisionDetected -= subscription.handler;
            collisionSubscriptions.Remove(stale[i]);
        }
    }

    private void UnsubscribeAllCollisions()
    {
        foreach (CollisionSubscription subscription in collisionSubscriptions.Values)
        {
            if (subscription != null && subscription.damageController != null)
                subscription.damageController.NetworkVehicleCollisionDetected -= subscription.handler;
        }

        collisionSubscriptions.Clear();
    }

    private void HandleCollisionReport(CollisionSubscription subscription, NetworkVehicleCollisionReport report)
    {
        if (subscription == null || subscription.networkEntity == null || report == null)
            return;

        recentCollisions.Add(new ObserverCollisionEvent
        {
            sequence = nextCollisionSequence++,
            server_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            primary_player_id = subscription.networkEntity.PlayerId,
            secondary_player_id = report.otherPlayerId ?? string.Empty,
            world_point = BackendVector3.FromVector3(report.worldPoint),
            world_normal = BackendVector3.FromVector3(report.worldNormal),
            relative_velocity = BackendVector3.FromVector3(report.relativeVelocity),
            impulse_vector = BackendVector3.FromVector3(report.impulseVector),
            impulse_magnitude = report.impulseMagnitude
        });

        if (recentCollisions.Count > 24)
            recentCollisions.RemoveRange(0, recentCollisions.Count - 24);
    }

    private void ValidateRoomId(string matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId))
            throw new ObserverApiException(400, "INVALID_REQUEST", "match_id is required");
        if (!string.Equals(matchId, settings.MatchId, StringComparison.OrdinalIgnoreCase))
            throw new ObserverApiException(404, "ROOM_NOT_FOUND", "Room not found");
    }

    private void PumpMainThreadQueue()
    {
        while (mainThreadWorkItems.TryDequeue(out MainThreadWorkItem item))
        {
            try
            {
                item.Completion.TrySetResult(item.Callback());
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
            }
        }
    }

    private Task<T> RunOnMainThreadAsync<T>(Func<T> callback)
    {
        if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
        {
            try
            {
                return Task.FromResult(callback());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        TaskCompletionSource<object> completion =
            new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        mainThreadWorkItems.Enqueue(new MainThreadWorkItem
        {
            Callback = () => callback(),
            Completion = completion
        });
        return AwaitMainThreadAsync<T>(completion.Task);
    }

    private static async Task<T> AwaitMainThreadAsync<T>(Task<object> task)
    {
        object result = await task;
        return result is T value ? value : default;
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(settings.ControlToken))
            return true;

        string token = request.Headers["X-RRR-Service-Token"];
        return string.Equals(token, settings.ControlToken, StringComparison.Ordinal);
    }

    private static string[] GetPathSegments(HttpListenerRequest request)
    {
        string path = request.Url != null ? request.Url.AbsolutePath : string.Empty;
        return path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
    {
        if (request == null || request.InputStream == null)
            return string.Empty;

        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            return await reader.ReadToEndAsync();
    }

    private static T ParseRequestBody<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ObserverApiException(400, "INVALID_REQUEST", "Request body is required");

        try
        {
            T payload = JsonUtility.FromJson<T>(json);
            if (payload == null)
                throw new ObserverApiException(400, "INVALID_REQUEST", "Invalid JSON body");
            return payload;
        }
        catch (ObserverApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ObserverApiException(400, "INVALID_REQUEST", "Invalid JSON body: " + ex.Message);
        }
    }

    private static Task WriteOptionsAsync(HttpListenerResponse response)
    {
        response.StatusCode = 204;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, PUT, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-RRR-Service-Token";
        response.ContentLength64 = 0;
        response.OutputStream.Close();
        return Task.CompletedTask;
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, PUT, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-RRR-Service-Token";
        response.ContentLength64 = payload.LongLength;
        await response.OutputStream.WriteAsync(payload, 0, payload.Length);
        response.OutputStream.Close();
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private sealed class MainThreadWorkItem
    {
        public Func<object> Callback;
        public TaskCompletionSource<object> Completion;
    }

    private sealed class CollisionSubscription
    {
        public CarDamageController damageController;
        public NetworkVehicleEntity networkEntity;
        public Action<NetworkVehicleCollisionReport> handler;
    }

    private sealed class PlayerObserverContext
    {
        public string playerId;
        public PlayerCar playerCar;
        public CarControllerBase controller;
        public CarDamageController damageController;
        public Rigidbody body;
        public PurrVehicleSpawnerPlayerObserverRecord tracked;
        public PurrPlayerProfileData profile;
        public PurrPlayerStatsData stats;
    }

    private sealed class ObserverSnapshotContext
    {
        public NetworkManager networkManager;
        public UDPTransport transport;
        public PredictionManager predictionManager;
        public PurrVehicleSpawnerObserverSnapshot spawnerState;
        public CarDamageRuntimeTuning damageConfig;
        public string sceneName;
        public string serverState;
        public string clientState;
        public string status;
        public int tickRate;
        public int localTick;
        public int syncedTick;
        public int serverTick;
        public int playerCount;
        public int damageControllerCount;
        public int sleepingRigidBodyCount;
        public bool roomVisible;
        public List<PlayerObserverContext> players = new List<PlayerObserverContext>();
    }

    private sealed class ObserverApiException : Exception
    {
        public ObserverApiException(int statusCode, string code, string message)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code ?? "INTERNAL_ERROR";
        }

        public int StatusCode { get; }
        public string Code { get; }
    }

    private sealed class DedicatedObserverSettings
    {
        public string Bind;
        public int Port;
        public string ControlToken;
        public string PublicHttpBaseUrl;
        public string MatchId;
        public string MapId;

        public string HttpPrefix
        {
            get
            {
                string host = string.IsNullOrWhiteSpace(Bind) || Bind == "0.0.0.0" ? "*" : Bind;
                return $"http://{host}:{Port}/";
            }
        }

        public static DedicatedObserverSettings FromEnvironment()
        {
            string bind = Environment.GetEnvironmentVariable("RRR_DEDICATED_BIND");
            if (string.IsNullOrWhiteSpace(bind))
                bind = "127.0.0.1";

            int port = 7777;
            string rawPort = Environment.GetEnvironmentVariable("RRR_DEDICATED_PORT");
            if (!string.IsNullOrWhiteSpace(rawPort))
                int.TryParse(rawPort, out port);
            if (port <= 0)
                port = 7777;

            string publicHttp = Environment.GetEnvironmentVariable("RRR_DEDICATED_PUBLIC_HTTP_BASE_URL");
            if (string.IsNullOrWhiteSpace(publicHttp))
                publicHttp = $"http://127.0.0.1:{port}";

            string matchId = Environment.GetEnvironmentVariable("RRR_PURRNET_MATCH_ID");
            if (string.IsNullOrWhiteSpace(matchId))
                matchId = "purrnet-live";

            string mapId = Environment.GetEnvironmentVariable("RRR_PURRNET_MAP_ID");
            if (string.IsNullOrWhiteSpace(mapId))
                mapId = "city_default";

            return new DedicatedObserverSettings
            {
                Bind = bind,
                Port = port,
                ControlToken = Environment.GetEnvironmentVariable("RRR_DEDICATED_CONTROL_TOKEN") ?? string.Empty,
                PublicHttpBaseUrl = publicHttp.TrimEnd('/'),
                MatchId = matchId.Trim(),
                MapId = mapId.Trim()
            };
        }
    }

    [Serializable]
    private sealed class ObserverHealthResponse
    {
        public string status;
        public string source;
        public int room_count;
        public string bind;
        public int port;
        public string active_match_id;
        public string active_room_status;
        public string scene_name;
        public string server_state;
        public string client_state;
        public int player_count;
        public int server_tick;
    }

    [Serializable]
    private sealed class ObserverRoomsResponse
    {
        public List<ObserverRoomResponse> items = new List<ObserverRoomResponse>();
    }

    [Serializable]
    private sealed class ObserverRoomResponse
    {
        public string room_id;
        public string match_id;
        public string source;
        public string map_id;
        public string status;
        public string room_http_url;
        public string room_ws_url;
        public string room_token;
        public string scene_name;
        public int player_count;
        public string created_at;
        public int tick_rate;
        public int server_tick;
        public bool manual_tick;
    }

    [Serializable]
    private sealed class ObserverRoomSnapshotResponse
    {
        public string room_id;
        public string match_id;
        public string source;
        public string map_id;
        public string status;
        public int server_tick;
        public long server_time;
        public List<ObserverPlayerState> players = new List<ObserverPlayerState>();
        public List<ObserverDamageState> damage_states = new List<ObserverDamageState>();
        public List<ObserverCollisionEvent> collisions = new List<ObserverCollisionEvent>();
        public ObserverDamageConfigState damage_config;
        public ObserverRoomDebugState observer = new ObserverRoomDebugState();
    }

    [Serializable]
    private sealed class ObserverPlayerState
    {
        public string player_id;
        public string player_name;
        public string connection_state;
        public bool is_server_controlled;
        public int authority_order;
        public string spawn_point_id;
        public BackendVector3 spawn_position;
        public BackendVector3 spawn_rotation;
        public int ack_input_seq;
        public long client_time;
        public long server_received_time;
        public BackendCarControlInputPayload input;
        public BackendVector3 position;
        public BackendVector3 rotation;
        public BackendVector3 velocity;
        public BackendVector3 angular_velocity;
        public BackendCarConfigPayload car_config;
        public ObserverPlayerDebugState debug;
    }

    [Serializable]
    private sealed class ObserverDamageState
    {
        public string player_id;
        public int revision;
        public int width;
        public int height;
        public string map_b64;
        public BackendVector3 world_point;
        public BackendVector3 world_normal;
    }

    [Serializable]
    private sealed class ObserverCollisionEvent
    {
        public int sequence;
        public long server_time;
        public string primary_player_id;
        public string secondary_player_id;
        public BackendVector3 world_point;
        public BackendVector3 world_normal;
        public BackendVector3 relative_velocity;
        public BackendVector3 impulse_vector;
        public float impulse_magnitude;
    }

    [Serializable]
    private sealed class ObserverRoomDebugState
    {
        public string source;
        public string mode;
        public string match_id;
        public string map_id;
        public string scene_name;
        public string active_scene_id;
        public string started_at_utc;
        public float uptime_sec;
        public bool room_visible;
        public ObserverDamageConfigState damage_config;
        public ObserverNetworkDebugState network = new ObserverNetworkDebugState();
        public ObserverPredictionDebugState prediction = new ObserverPredictionDebugState();
        public ObserverSpawnerDebugState spawner = new ObserverSpawnerDebugState();
        public ObserverCountsDebugState counts = new ObserverCountsDebugState();
        public List<ObserverTrackedPlayerState> tracked_players = new List<ObserverTrackedPlayerState>();
    }

    [Serializable]
    private sealed class ObserverDamageConfigState
    {
        public int version = CarDamageRuntimeTuning.CurrentVersion;
        public int revision;
        public long updated_at_unix_ms;
        public string source;
        public string obstacle_tag;
        public float impulse_to_color;
        public float max_color_step;
        public float impulse_to_radius;
        public float impulse_from_speed_factor;
        public int max_radius_cells;
        public float min_speed_for_damage_kmh;
        public float max_speed_for_damage_kmh;
        public float min_damage_scale;
        public float glancing_damage_scale;
        public float impact_alignment_power;
        public float speed_radius_boost;
        public float compute_deform_amplitude;
        public float compute_deform_direction;
        public float compute_deform_sin_frequency;
        public float compute_deform_sin_strength;
        public float compute_yield_threshold;
        public float compute_hardening;
        public float compute_max_deform;
        public bool compute_two_level_damage;
        public int compute_coarse_radius;
        public float compute_coarse_weight;
        public float compute_coarse_boost;
        public float compute_coarse_deform_meters;

        public static ObserverDamageConfigState FromRuntime(CarDamageRuntimeTuning config)
        {
            if (config == null)
                return null;

            return new ObserverDamageConfigState
            {
                version = config.version,
                revision = config.revision,
                updated_at_unix_ms = config.updatedAtUnixMs,
                source = config.source,
                obstacle_tag = config.obstacleTag,
                impulse_to_color = config.impulseToColor,
                max_color_step = config.maxColorStep,
                impulse_to_radius = config.impulseToRadius,
                impulse_from_speed_factor = config.impulseFromSpeedFactor,
                max_radius_cells = config.maxRadiusCells,
                min_speed_for_damage_kmh = config.minSpeedForDamageKmh,
                max_speed_for_damage_kmh = config.maxSpeedForDamageKmh,
                min_damage_scale = config.minDamageScale,
                glancing_damage_scale = config.glancingDamageScale,
                impact_alignment_power = config.impactAlignmentPower,
                speed_radius_boost = config.speedRadiusBoost,
                compute_deform_amplitude = config.computeDeformAmplitude,
                compute_deform_direction = config.computeDeformDirection,
                compute_deform_sin_frequency = config.computeDeformSinFrequency,
                compute_deform_sin_strength = config.computeDeformSinStrength,
                compute_yield_threshold = config.computeYieldThreshold,
                compute_hardening = config.computeHardening,
                compute_max_deform = config.computeMaxDeform,
                compute_two_level_damage = config.computeTwoLevelDamage,
                compute_coarse_radius = config.computeCoarseRadius,
                compute_coarse_weight = config.computeCoarseWeight,
                compute_coarse_boost = config.computeCoarseBoost,
                compute_coarse_deform_meters = config.computeCoarseDeformMeters
            };
        }

        public CarDamageRuntimeTuning ToRuntime()
        {
            CarDamageRuntimeTuning config = new CarDamageRuntimeTuning
            {
                version = version,
                revision = revision,
                updatedAtUnixMs = updated_at_unix_ms,
                source = source,
                obstacleTag = obstacle_tag,
                impulseToColor = impulse_to_color,
                maxColorStep = max_color_step,
                impulseToRadius = impulse_to_radius,
                impulseFromSpeedFactor = impulse_from_speed_factor,
                maxRadiusCells = max_radius_cells,
                minSpeedForDamageKmh = min_speed_for_damage_kmh,
                maxSpeedForDamageKmh = max_speed_for_damage_kmh,
                minDamageScale = min_damage_scale,
                glancingDamageScale = glancing_damage_scale,
                impactAlignmentPower = impact_alignment_power,
                speedRadiusBoost = speed_radius_boost,
                computeDeformAmplitude = compute_deform_amplitude,
                computeDeformDirection = compute_deform_direction,
                computeDeformSinFrequency = compute_deform_sin_frequency,
                computeDeformSinStrength = compute_deform_sin_strength,
                computeYieldThreshold = compute_yield_threshold,
                computeHardening = compute_hardening,
                computeMaxDeform = compute_max_deform,
                computeTwoLevelDamage = compute_two_level_damage,
                computeCoarseRadius = compute_coarse_radius,
                computeCoarseWeight = compute_coarse_weight,
                computeCoarseBoost = compute_coarse_boost,
                computeCoarseDeformMeters = compute_coarse_deform_meters
            };
            config.Validate();
            return config;
        }
    }

    [Serializable]
    private sealed class ObserverNetworkDebugState
    {
        public string server_state;
        public string client_state;
        public bool is_server;
        public bool is_client;
        public string transport_type;
        public string address;
        public int port;
        public int tick_rate;
        public int local_tick;
        public int synced_tick;
        public long server_time;
    }

    [Serializable]
    private sealed class ObserverPredictionDebugState
    {
        public bool has_prediction_manager;
        public bool prediction_spawned;
        public bool hierarchy_ready;
    }

    [Serializable]
    private sealed class ObserverSpawnerDebugState
    {
        public bool has_scene_id;
        public bool prediction_manager_ready;
        public bool prediction_manager_spawned;
        public bool hierarchy_ready;
        public bool scene_players_ready;
        public bool players_manager_ready;
        public bool is_server_spawner;
        public bool is_client_publisher;
        public bool local_loadout_published;
        public bool transient_solo_cleanup_enabled;
        public float solo_idle_timeout_sec;
        public float solo_lifecycle_poll_interval_sec;
        public bool solo_session_active;
        public string solo_session_human_player_id;
        public float solo_session_active_for_sec;
        public float seconds_since_last_human_seen;
        public float seconds_since_last_meaningful_input;
        public float seconds_until_idle_close;
        public string solo_session_status;
        public string last_solo_session_close_reason;
        public float seconds_since_last_solo_session_close;
        public int solo_bot_target;
        public int tracked_bot_players;
        public int pending_bot_creates;
        public int queued_players;
        public int spawned_players;
        public string template_car_name;
        public string last_wait_reason;
    }

    [Serializable]
    private sealed class ObserverCountsDebugState
    {
        public int active_player_cars;
        public int predicted_controllers;
        public int network_entities;
        public int damage_controllers;
        public int sleeping_rigidbodies;
        public int tracked_players;
    }

    [Serializable]
    private sealed class ObserverTrackedPlayerState
    {
        public string player_id;
        public bool is_bot;
        public bool queued;
        public bool spawned;
        public int spawn_slot;
        public string spawn_point_id;
        public BackendVector3 spawn_position;
        public BackendVector3 spawn_rotation;
        public string last_spawn_failure_reason;
        public BackendCarConfigPayload car_config;
    }

    [Serializable]
    private sealed class ObserverPlayerDebugState
    {
        public string owner_player_id;
        public bool is_bot;
        public bool queued;
        public bool spawned;
        public string last_spawn_failure_reason;
        public string loadout_name;
        public string loadout_display_name;
        public string resolved_car_config_name;
        public string account_player_id;
        public string auth_provider;
        public string auth_state;
        public string session_id;
        public float current_health;
        public float max_health;
        public float damage_ratio;
        public float synced_speed_kph;
        public int synced_gear;
        public float synced_nitro_amount;
        public int current_gear;
        public int requested_gear;
        public float current_rpm;
        public float shift_timer;
        public float shift_target_rpm;
        public int shift_state;
        public float speed_kph;
        public float motor_torque;
        public float brake_torque;
        public float rear_brake_torque;
        public float steer_angle;
        public float steering_wheel_angle;
        public float drift_kick_force;
        public float nitro_amount;
        public bool nitro_active;
        public bool nitro_initialized;
        public int grounded_wheels;
        public int wheel_count;
        public bool input_enabled;
        public bool physics_simulation_enabled;
        public bool manual_simulation_enabled;
        public bool sleeping;
        public bool has_damage_controller;
        public bool can_capture_damage_snapshot;
        public int captured_damage_width;
        public int captured_damage_height;
        public int captured_damage_bytes;
        public int damage_revision;
    }
}
