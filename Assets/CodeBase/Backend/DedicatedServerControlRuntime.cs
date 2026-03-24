using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class NetworkCarInputSource : MonoBehaviour, ICarInputSource
{
    private CarControlFrame currentFrame = CarControlFrame.CreateBrakingFrame();

    public void SetControlFrame(CarControlFrame frame)
    {
        frame.Clamp();
        currentFrame = frame;
    }

    public void ResetInput()
    {
        currentFrame = CarControlFrame.CreateBrakingFrame();
    }

    public bool TryGetControlFrame(out CarControlFrame controlFrame)
    {
        controlFrame = currentFrame;
        return true;
    }
}

[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class DedicatedServerControlRuntime : MonoBehaviour
{
    private readonly ConcurrentQueue<string> pendingLogs = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<MainThreadWorkItem> mainThreadWorkItems = new ConcurrentQueue<MainThreadWorkItem>();

    private HttpListener listener;
    private CancellationTokenSource cancellation;
    private Task serverTask;
    private DedicatedServerSettings settings;
    private DedicatedSimulationRoom activeRoom;
    private PlayerCar templateCar;
    private float defaultFixedDeltaTime;
    private int mainThreadId;
#if UNITY_SERVER
    private SimulationMode previousPhysicsSimulationMode = SimulationMode.FixedUpdate;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntime()
    {
        if (!Application.isBatchMode)
            return;

        DedicatedServerControlRuntime existing = FindFirstObjectByType<DedicatedServerControlRuntime>();
        if (existing != null)
            return;

        GameObject root = new GameObject("DedicatedServerControlRuntime");
        root.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(root);
        root.AddComponent<DedicatedServerControlRuntime>();
    }

    private void Awake()
    {
        Application.runInBackground = true;
        settings = DedicatedServerSettings.FromEnvironment();
        defaultFixedDeltaTime = Time.fixedDeltaTime;
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
#if UNITY_SERVER
        previousPhysicsSimulationMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
#endif
        CacheTemplateCar();
        StartServer();
    }

    private void Update()
    {
        PumpMainThreadQueue();
        while (pendingLogs.TryDequeue(out string line))
            Debug.Log(line, this);
    }

    private void FixedUpdate()
    {
        PumpMainThreadQueue();
        if (activeRoom == null || !activeRoom.UseAutomaticTick)
            return;

        activeRoom.Tick();
#if UNITY_SERVER
        if (Physics.simulationMode == SimulationMode.Script)
            Physics.Simulate(Time.fixedDeltaTime);
#endif
    }

    private void OnDestroy()
    {
        try
        {
            cancellation?.Cancel();
            listener?.Stop();
            listener?.Close();
        }
        catch
        {
        }

        try
        {
            activeRoom?.Dispose();
            activeRoom = null;
            Time.fixedDeltaTime = defaultFixedDeltaTime;
#if UNITY_SERVER
            Physics.simulationMode = previousPhysicsSimulationMode;
#endif
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
            pendingLogs.Enqueue($"Dedicated control API listening on {settings.HttpPrefix}");
        }
        catch (Exception ex)
        {
            Debug.LogError("DedicatedServerControlRuntime: failed to start control API. " + ex.Message, this);
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
            if (!IsAuthorized(context.Request))
            {
                await WriteJsonAsync(context.Response, 401, "{\"code\":\"UNAUTHORIZED\",\"message\":\"Invalid service token\"}");
                return;
            }

            string method = context.Request.HttpMethod.ToUpperInvariant();
            string[] segments = GetPathSegments(context.Request);

            if (segments.Length == 1 && string.Equals(segments[0], "health", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                DedicatedHealthResponse payload = await RunOnMainThreadAsync(BuildHealthResponse);
                await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                return;
            }

            if (segments.Length == 3 &&
                string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[1], "v1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[2], "rooms", StringComparison.OrdinalIgnoreCase))
            {
                if (method == "GET")
                {
                    DedicatedRoomsResponse payload = await RunOnMainThreadAsync(BuildRoomsResponse);
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }

                if (method == "POST")
                {
                    string body = await ReadBodyAsync(context.Request);
                    DedicatedCreateRoomRequest request = JsonUtility.FromJson<DedicatedCreateRoomRequest>(body);
                    ValidateCreateRoomRequest(request);
                    DedicatedRoomResponse payload = await RunOnMainThreadAsync(() => CreateRoom(request));
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }
            }

            if (segments.Length >= 4 &&
                string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[1], "v1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[2], "rooms", StringComparison.OrdinalIgnoreCase))
            {
                string matchId = Uri.UnescapeDataString(segments[3]);
                if (string.IsNullOrWhiteSpace(matchId))
                    throw new DedicatedControlException(400, "INVALID_REQUEST", "match_id is required");

                if (segments.Length == 4)
                {
                    if (method == "GET")
                    {
                        DedicatedRoomResponse payload = await RunOnMainThreadAsync(() => GetRoom(matchId));
                        await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                        return;
                    }

                    if (method == "DELETE")
                    {
                        DedicatedReleaseRoomResponse payload = await RunOnMainThreadAsync(() => ReleaseRoom(matchId));
                        await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                        return;
                    }
                }

                if (segments.Length == 5 && string.Equals(segments[4], "inputs", StringComparison.OrdinalIgnoreCase) && method == "POST")
                {
                    string body = await ReadBodyAsync(context.Request);
                    DedicatedRoomInputBatchRequest request = JsonUtility.FromJson<DedicatedRoomInputBatchRequest>(body);
                    DedicatedApplyInputsResponse payload = await RunOnMainThreadAsync(() => ApplyInputs(matchId, request));
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }

                if (segments.Length == 5 && string.Equals(segments[4], "step", StringComparison.OrdinalIgnoreCase) && method == "POST")
                {
                    string body = await ReadBodyAsync(context.Request);
                    DedicatedStepRoomRequest request = JsonUtility.FromJson<DedicatedStepRoomRequest>(body);
                    DedicatedStepRoomResponse payload = await RunOnMainThreadAsync(() => StepRoom(matchId, request));
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }

                if (segments.Length == 5 && string.Equals(segments[4], "snapshot", StringComparison.OrdinalIgnoreCase) && method == "GET")
                {
                    DedicatedRoomSnapshotResponse payload = await RunOnMainThreadAsync(() => GetSnapshot(matchId));
                    await WriteJsonAsync(context.Response, 200, JsonUtility.ToJson(payload));
                    return;
                }
            }

            await WriteJsonAsync(context.Response, 404, "{\"code\":\"NOT_FOUND\",\"message\":\"Not found\"}");
        }
        catch (DedicatedControlException ex)
        {
            await WriteJsonAsync(context.Response, ex.StatusCode, $"{{\"code\":\"{EscapeJson(ex.Code)}\",\"message\":\"{EscapeJson(ex.Message)}\"}}");
        }
        catch (Exception ex)
        {
            pendingLogs.Enqueue("Dedicated control API request failed: " + ex.Message);
            try
            {
                await WriteJsonAsync(context.Response, 500, "{\"code\":\"INTERNAL_ERROR\",\"message\":\"Internal error\"}");
            }
            catch
            {
            }
        }
    }

    private DedicatedHealthResponse BuildHealthResponse()
    {
        return new DedicatedHealthResponse
        {
            status = "ok",
            room_count = activeRoom != null ? 1 : 0,
            bind = settings.Bind,
            port = settings.Port,
            active_match_id = activeRoom != null ? activeRoom.MatchId : string.Empty,
            active_room_status = activeRoom != null ? activeRoom.Status : "idle"
        };
    }

    private DedicatedRoomsResponse BuildRoomsResponse()
    {
        DedicatedRoomsResponse response = new DedicatedRoomsResponse();
        if (activeRoom != null)
            response.items.Add(activeRoom.ToRoomResponse(settings));
        return response;
    }

    private DedicatedRoomResponse CreateRoom(DedicatedCreateRoomRequest request)
    {
        if (activeRoom != null)
        {
            if (string.Equals(activeRoom.MatchId, request.match_id, StringComparison.OrdinalIgnoreCase))
                return activeRoom.ToRoomResponse(settings);

            throw new DedicatedControlException(409, "ROOM_BUSY", "Dedicated worker already serves another match");
        }

        PlayerCar resolvedTemplate = ResolveTemplateCar();
        if (resolvedTemplate == null)
            throw new DedicatedControlException(500, "TEMPLATE_MISSING", "PlayerCar template was not found in the loaded scene");

        int tickRate = Mathf.Clamp(request.tick_rate <= 0 ? 30 : request.tick_rate, 10, 120);
        Time.fixedDeltaTime = 1.0f / tickRate;

        try
        {
            activeRoom = new DedicatedSimulationRoom(request, settings, resolvedTemplate);
        }
        catch
        {
            Time.fixedDeltaTime = defaultFixedDeltaTime;
            throw;
        }

        pendingLogs.Enqueue($"Dedicated room created for match {request.match_id} with {activeRoom.PlayerCount} players.");
        return activeRoom.ToRoomResponse(settings);
    }

    private DedicatedRoomResponse GetRoom(string matchId)
    {
        DedicatedSimulationRoom room = RequireRoom(matchId);
        return room.ToRoomResponse(settings);
    }

    private DedicatedReleaseRoomResponse ReleaseRoom(string matchId)
    {
        DedicatedSimulationRoom room = RequireRoom(matchId);
        room.Dispose();
        activeRoom = null;
        Time.fixedDeltaTime = defaultFixedDeltaTime;
        pendingLogs.Enqueue($"Dedicated room released for match {matchId}.");
        return new DedicatedReleaseRoomResponse
        {
            released = true
        };
    }

    private DedicatedApplyInputsResponse ApplyInputs(string matchId, DedicatedRoomInputBatchRequest request)
    {
        DedicatedSimulationRoom room = RequireRoom(matchId);
        int acceptedPlayers = room.ApplyInputs(request);
        return new DedicatedApplyInputsResponse
        {
            match_id = matchId,
            accepted_players = acceptedPlayers,
            server_tick = room.ServerTick,
            status = room.Status
        };
    }

    private DedicatedRoomSnapshotResponse GetSnapshot(string matchId)
    {
        DedicatedSimulationRoom room = RequireRoom(matchId);
        return room.BuildSnapshot();
    }

    private DedicatedStepRoomResponse StepRoom(string matchId, DedicatedStepRoomRequest request)
    {
        DedicatedSimulationRoom room = RequireRoom(matchId);
        int executedTicks = room.Step(request != null ? request.ticks : 1);
        DedicatedRoomSnapshotResponse snapshot = room.BuildSnapshot();
        return new DedicatedStepRoomResponse
        {
            match_id = matchId,
            status = room.Status,
            executed_ticks = executedTicks,
            server_tick = room.ServerTick,
            server_time = snapshot != null ? snapshot.server_time : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            snapshot = snapshot
        };
    }

    private DedicatedSimulationRoom RequireRoom(string matchId)
    {
        if (activeRoom == null || !string.Equals(activeRoom.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
            throw new DedicatedControlException(404, "ROOM_NOT_FOUND", "Room not found");
        return activeRoom;
    }

    private void CacheTemplateCar()
    {
        templateCar = FindScenePlayerCar(includeInactive: true);
        if (templateCar == null)
            return;

        if (templateCar.gameObject.activeSelf)
            templateCar.gameObject.SetActive(false);
    }

    private PlayerCar ResolveTemplateCar()
    {
        if (templateCar != null)
            return templateCar;

        templateCar = FindScenePlayerCar(includeInactive: true);
        if (templateCar != null && templateCar.gameObject.activeSelf)
            templateCar.gameObject.SetActive(false);
        return templateCar;
    }

    private static PlayerCar FindScenePlayerCar(bool includeInactive)
    {
        PlayerCar[] candidates = Resources.FindObjectsOfTypeAll<PlayerCar>();
        for (int i = 0; i < candidates.Length; i++)
        {
            PlayerCar candidate = candidates[i];
            if (candidate == null)
                continue;

            GameObject candidateObject = candidate.gameObject;
            if (!candidateObject.scene.IsValid())
                continue;
            if (!includeInactive && !candidateObject.activeInHierarchy)
                continue;
            if ((candidateObject.hideFlags & HideFlags.HideAndDontSave) != 0)
                continue;
            return candidate;
        }

        return null;
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

    private static void ValidateCreateRoomRequest(DedicatedCreateRoomRequest request)
    {
        if (request == null)
            throw new DedicatedControlException(400, "INVALID_REQUEST", "Request body is required");
        if (string.IsNullOrWhiteSpace(request.match_id))
            throw new DedicatedControlException(400, "INVALID_REQUEST", "match_id is required");
        if (request.players == null || request.players.Count == 0)
            throw new DedicatedControlException(400, "INVALID_REQUEST", "players are required");
    }

    private static string[] GetPathSegments(HttpListenerRequest request)
    {
        string path = request.Url != null ? request.Url.AbsolutePath : string.Empty;
        return path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        if (request == null || request.InputStream == null)
            return string.Empty;

        using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            return await reader.ReadToEndAsync();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
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

    private sealed class DedicatedControlException : Exception
    {
        public DedicatedControlException(int statusCode, string code, string message)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code ?? "INTERNAL_ERROR";
        }

        public int StatusCode { get; }
        public string Code { get; }
    }

    [Serializable]
    private sealed class DedicatedHealthResponse
    {
        public string status;
        public int room_count;
        public string bind;
        public int port;
        public string active_match_id;
        public string active_room_status;
    }

    [Serializable]
    private sealed class DedicatedRoomsResponse
    {
        public List<DedicatedRoomResponse> items = new List<DedicatedRoomResponse>();
    }

    [Serializable]
    private sealed class DedicatedRoomResponse
    {
        public string room_id;
        public string match_id;
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
    private sealed class DedicatedReleaseRoomResponse
    {
        public bool released;
    }

    [Serializable]
    private sealed class DedicatedCreateRoomRequest
    {
        public string match_id;
        public string map_id;
        public int tick_rate;
        public int broadcast_rate;
        public bool manual_tick;
        public List<DedicatedRoomPlayerRequest> players = new List<DedicatedRoomPlayerRequest>();
    }

    [Serializable]
    private sealed class DedicatedRoomPlayerRequest
    {
        public string player_id;
        public string player_name;
        public int authority_order;
        public string spawn_point_id;
        public BackendVector3 spawn_position;
        public BackendVector3 spawn_rotation;
        public BackendCarConfigPayload car_config;
    }

    [Serializable]
    private sealed class DedicatedRoomInputBatchRequest
    {
        public List<DedicatedRoomInputFrame> players = new List<DedicatedRoomInputFrame>();
    }

    [Serializable]
    private sealed class DedicatedRoomInputFrame
    {
        public string player_id;
        public int seq;
        public long client_time;
        public BackendCarControlInputPayload input;
    }

    [Serializable]
    private sealed class DedicatedApplyInputsResponse
    {
        public string match_id;
        public string status;
        public int accepted_players;
        public int server_tick;
    }

    [Serializable]
    private sealed class DedicatedStepRoomRequest
    {
        public int ticks = 1;
    }

    [Serializable]
    private sealed class DedicatedStepRoomResponse
    {
        public string match_id;
        public string status;
        public int executed_ticks;
        public int server_tick;
        public long server_time;
        public DedicatedRoomSnapshotResponse snapshot;
    }

    [Serializable]
    private sealed class DedicatedRoomSnapshotResponse
    {
        public string room_id;
        public string match_id;
        public string status;
        public int server_tick;
        public long server_time;
        public List<DedicatedSnapshotPlayerState> players = new List<DedicatedSnapshotPlayerState>();
        public List<DedicatedSnapshotDamageState> damage_states = new List<DedicatedSnapshotDamageState>();
        public List<DedicatedSnapshotCollisionEvent> collisions = new List<DedicatedSnapshotCollisionEvent>();
    }

    [Serializable]
    private sealed class DedicatedSnapshotPlayerState
    {
        public string player_id;
        public int ack_input_seq;
        public long client_time;
        public long server_received_time;
        public BackendCarControlInputPayload input;
        public BackendVector3 position;
        public BackendVector3 rotation;
        public BackendVector3 velocity;
        public BackendVector3 angular_velocity;
        public List<BackendWheelPose> wheel_states = new List<BackendWheelPose>();
        public DedicatedVehicleDebugState debug;
    }

    [Serializable]
    private sealed class DedicatedSnapshotDamageState
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
    private sealed class DedicatedSnapshotCollisionEvent
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
    private sealed class DedicatedVehicleDebugState
    {
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
        public float drift_kick_force;
        public float nitro_amount;
        public bool nitro_active;
        public bool nitro_initialized;
        public int grounded_wheels;
        public int wheel_count;
        public bool input_enabled;
        public bool sleeping;
    }

    private sealed class DedicatedServerSettings
    {
        public string Bind;
        public int Port;
        public string ControlToken;
        public string PublicHttpBaseUrl;
        public string PublicWsBaseUrl;

        public string HttpPrefix
        {
            get
            {
                string host = string.IsNullOrWhiteSpace(Bind) || Bind == "0.0.0.0" ? "*" : Bind;
                return $"http://{host}:{Port}/";
            }
        }

        public static DedicatedServerSettings FromEnvironment()
        {
            string bind = Environment.GetEnvironmentVariable("RRR_DEDICATED_BIND");
            if (string.IsNullOrWhiteSpace(bind))
                bind = "0.0.0.0";

            int port = 7777;
            string rawPort = Environment.GetEnvironmentVariable("RRR_DEDICATED_PORT");
            if (!string.IsNullOrWhiteSpace(rawPort))
                int.TryParse(rawPort, out port);
            if (port <= 0)
                port = 7777;

            string publicHttp = Environment.GetEnvironmentVariable("RRR_DEDICATED_PUBLIC_HTTP_BASE_URL");
            if (string.IsNullOrWhiteSpace(publicHttp))
                publicHttp = $"http://127.0.0.1:{port}";

            string publicWs = Environment.GetEnvironmentVariable("RRR_DEDICATED_PUBLIC_WS_BASE_URL");
            if (string.IsNullOrWhiteSpace(publicWs))
                publicWs = $"ws://127.0.0.1:{port}";

            return new DedicatedServerSettings
            {
                Bind = bind,
                Port = port,
                ControlToken = Environment.GetEnvironmentVariable("RRR_DEDICATED_CONTROL_TOKEN") ?? string.Empty,
                PublicHttpBaseUrl = publicHttp.TrimEnd('/'),
                PublicWsBaseUrl = publicWs.TrimEnd('/'),
            };
        }
    }

    private sealed class DedicatedSimulationRoom : IDisposable
    {
        private const float FallbackSpawnLift = 0.4f;
        private const float SpawnGroundProbeHeight = 3.0f;
        private const float SpawnGroundProbeDistance = 8.0f;

        private readonly GameObject rootObject;
        private readonly Dictionary<string, DedicatedRoomPlayerRuntime> playersById =
            new Dictionary<string, DedicatedRoomPlayerRuntime>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DedicatedRoomPlayerRuntime> orderedPlayers = new List<DedicatedRoomPlayerRuntime>();
        private readonly Vector3 spawnAnchorPosition;
        private readonly Quaternion spawnAnchorRotation;
        private readonly List<DedicatedSnapshotCollisionEvent> pendingCollisionEvents = new List<DedicatedSnapshotCollisionEvent>(16);
        private readonly Dictionary<string, int> recentCollisionTicks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int nextCollisionSequence = 1;

        public DedicatedSimulationRoom(DedicatedCreateRoomRequest request, DedicatedServerSettings settings, PlayerCar template)
        {
            MatchId = request.match_id;
            RoomId = request.match_id;
            MapId = string.IsNullOrWhiteSpace(request.map_id) ? "city_default" : request.map_id;
            TickRate = Mathf.Clamp(request.tick_rate <= 0 ? 30 : request.tick_rate, 10, 120);
            BroadcastRate = Mathf.Clamp(request.broadcast_rate <= 0 ? TickRate : request.broadcast_rate, 1, TickRate);
            ManualTick = request.manual_tick;
            Status = "simulating";
            RoomToken = Guid.NewGuid().ToString("N");
            SceneName = SceneManager.GetActiveScene().name;
            CreatedAtUtc = DateTime.UtcNow.ToString("O");
            rootObject = new GameObject($"DedicatedRoom_{MatchId}");
            spawnAnchorPosition = template != null ? template.transform.position : Vector3.zero;
            spawnAnchorRotation = template != null ? template.transform.rotation : Quaternion.identity;

            for (int i = 0; i < request.players.Count; i++)
            {
                DedicatedRoomPlayerRequest playerRequest = request.players[i];
                if (playerRequest == null || string.IsNullOrWhiteSpace(playerRequest.player_id))
                    continue;
                if (playersById.ContainsKey(playerRequest.player_id))
                    continue;

                DedicatedRoomPlayerRuntime runtimePlayer = CreatePlayerRuntime(playerRequest, template);
                playersById.Add(runtimePlayer.PlayerId, runtimePlayer);
                orderedPlayers.Add(runtimePlayer);
            }

            orderedPlayers.Sort(ComparePlayers);
            if (orderedPlayers.Count == 0)
                throw new DedicatedControlException(400, "INVALID_REQUEST", "players are required");
        }

        public string RoomId { get; }
        public string MatchId { get; }
        public string MapId { get; }
        public string Status { get; private set; }
        public string RoomToken { get; }
        public string SceneName { get; }
        public string CreatedAtUtc { get; }
        public int TickRate { get; }
        public int BroadcastRate { get; }
        public bool ManualTick { get; }
        public bool UseAutomaticTick => !ManualTick;
        public int ServerTick { get; private set; }
        public int PlayerCount => orderedPlayers.Count;

        public void Tick()
        {
            if (!UseAutomaticTick || !string.Equals(Status, "simulating", StringComparison.OrdinalIgnoreCase))
                return;

            PrepareTick();
            ServerTick += 1;
        }

        public int Step(int ticks)
        {
            if (!ManualTick)
                throw new DedicatedControlException(409, "MANUAL_TICK_DISABLED", "Room does not support manual ticking");

            int ticksToExecute = Mathf.Clamp(ticks <= 0 ? 1 : ticks, 1, 256);
            int executedTicks = 0;
            float deltaTime = 1.0f / Mathf.Max(1, TickRate);

            for (int tickIndex = 0; tickIndex < ticksToExecute; tickIndex++)
            {
                if (!string.Equals(Status, "simulating", StringComparison.OrdinalIgnoreCase))
                    break;

                PrepareTick();
                SimulateControllers(deltaTime);
                ServerTick += 1;
#if UNITY_SERVER
                if (Physics.simulationMode == SimulationMode.Script)
                    Physics.Simulate(deltaTime);
#else
                Physics.Simulate(deltaTime);
#endif
                executedTicks += 1;
            }

            return executedTicks;
        }

        private void PrepareTick()
        {
            for (int i = 0; i < orderedPlayers.Count; i++)
            {
                DedicatedRoomPlayerRuntime player = orderedPlayers[i];
                if (player == null || player.Car == null || player.LoadoutPayload == null || player.PendingLoadoutRefreshFrames <= 0)
                    continue;

                player.PendingLoadoutRefreshFrames -= 1;
                if (player.PendingLoadoutRefreshFrames > 0)
                    continue;

                PlayerCarLoadoutUtility.ApplySelectedLoadout(player.Car, player.LoadoutPayload);
                RegroundPlayerAfterLoadoutRefresh(player);
            }
        }

        private void SimulateControllers(float deltaTime)
        {
            for (int i = 0; i < orderedPlayers.Count; i++)
            {
                DedicatedRoomPlayerRuntime player = orderedPlayers[i];
                if (player == null || player.Controller == null)
                    continue;

                player.Controller.SimulateManualStep(ToControlFrame(player.LastInput), deltaTime);
            }
        }

        public DedicatedRoomResponse ToRoomResponse(DedicatedServerSettings serverSettings)
        {
            return new DedicatedRoomResponse
            {
                room_id = RoomId,
                match_id = MatchId,
                map_id = MapId,
                status = Status,
                room_http_url = serverSettings.PublicHttpBaseUrl + "/api/v1/rooms/" + Uri.EscapeDataString(MatchId),
                room_ws_url = string.Empty,
                room_token = RoomToken,
                scene_name = SceneName,
                player_count = PlayerCount,
                created_at = CreatedAtUtc,
                tick_rate = TickRate,
                server_tick = ServerTick,
                manual_tick = ManualTick
            };
        }

        public DedicatedRoomSnapshotResponse BuildSnapshot()
        {
            DedicatedRoomSnapshotResponse response = new DedicatedRoomSnapshotResponse
            {
                room_id = RoomId,
                match_id = MatchId,
                status = Status,
                server_tick = ServerTick,
                server_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            for (int i = 0; i < orderedPlayers.Count; i++)
            {
                DedicatedRoomPlayerRuntime player = orderedPlayers[i];
                if (player == null || player.Car == null)
                    continue;

                Rigidbody body = player.Rigidbody;
                DedicatedSnapshotPlayerState item = new DedicatedSnapshotPlayerState
                {
                    player_id = player.PlayerId,
                    ack_input_seq = player.LastInputSeq,
                    client_time = player.ClientTimeMs,
                    server_received_time = player.ServerReceivedTimeMs,
                    input = player.LastInput,
                    position = BackendVector3.FromVector3(player.Car.transform.position),
                    rotation = BackendVector3.FromVector3(player.Car.transform.eulerAngles),
                    velocity = BackendVector3.FromVector3(body != null ? body.linearVelocity : Vector3.zero),
                    angular_velocity = BackendVector3.FromVector3(body != null ? body.angularVelocity : Vector3.zero),
                    debug = BuildDebugState(player)
                };

                for (int wheelIndex = 0; wheelIndex < player.WheelBindings.Count; wheelIndex++)
                {
                    WheelVisualBinding binding = player.WheelBindings[wheelIndex];
                    if (binding == null || binding.VisualRoot == null)
                        continue;

                    item.wheel_states.Add(new BackendWheelPose
                    {
                        position = BackendVector3.FromVector3(binding.VisualRoot.localPosition),
                        rotation = BackendVector3.FromVector3(binding.VisualRoot.localRotation.eulerAngles)
                    });
                }

                response.players.Add(item);
            }

            for (int i = 0; i < orderedPlayers.Count; i++)
            {
                DedicatedRoomPlayerRuntime player = orderedPlayers[i];
                if (player == null || player.PendingDamageSnapshot == null)
                    continue;
                if (player.PendingDamageSnapshot.revision <= player.LastPublishedDamageRevision)
                    continue;

                CarDamageNetworkSnapshot damageSnapshot = player.PendingDamageSnapshot;
                response.damage_states.Add(new DedicatedSnapshotDamageState
                {
                    player_id = player.PlayerId,
                    revision = damageSnapshot.revision,
                    width = damageSnapshot.width,
                    height = damageSnapshot.height,
                    map_b64 = damageSnapshot.rawBytes != null && damageSnapshot.rawBytes.Length > 0
                        ? Convert.ToBase64String(damageSnapshot.rawBytes)
                        : string.Empty,
                    world_point = damageSnapshot.hasImpactPoint ? BackendVector3.FromVector3(damageSnapshot.worldPoint) : null,
                    world_normal = damageSnapshot.hasImpactNormal ? BackendVector3.FromVector3(damageSnapshot.worldNormal) : null
                });
                player.LastPublishedDamageRevision = damageSnapshot.revision;
                player.PendingDamageSnapshot = null;
            }

            if (pendingCollisionEvents.Count > 0)
            {
                response.collisions.AddRange(pendingCollisionEvents);
                pendingCollisionEvents.Clear();
            }

            return response;
        }

        private static DedicatedVehicleDebugState BuildDebugState(DedicatedRoomPlayerRuntime player)
        {
            CarControllerBase controller = player != null ? player.Controller : null;
            if (controller == null)
                return null;

            CarControllerSimulationState simulationState = controller.CaptureSimulationState();

            return new DedicatedVehicleDebugState
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

        public int ApplyInputs(DedicatedRoomInputBatchRequest request)
        {
            if (request == null || request.players == null || request.players.Count == 0)
                return 0;

            int acceptedPlayers = 0;
            long serverReceivedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for (int i = 0; i < request.players.Count; i++)
            {
                DedicatedRoomInputFrame input = request.players[i];
                if (input == null || string.IsNullOrWhiteSpace(input.player_id))
                    continue;
                if (!playersById.TryGetValue(input.player_id, out DedicatedRoomPlayerRuntime player))
                    continue;
                if (input.seq <= player.LastInputSeq)
                    continue;

                BackendCarControlInputPayload payload = input.input ?? new BackendCarControlInputPayload();
                player.LastInputSeq = input.seq;
                player.ClientTimeMs = input.client_time;
                player.ServerReceivedTimeMs = serverReceivedTime;
                player.LastInput = new BackendCarControlInputPayload
                {
                    throttle = Mathf.Clamp(payload.throttle, -1.0f, 1.0f),
                    steer = Mathf.Clamp(payload.steer, -1.0f, 1.0f),
                    brake = payload.brake,
                    handbrake = payload.handbrake,
                    nitro = payload.nitro
                };
                player.InputSource.SetControlFrame(new CarControlFrame
                {
                    Motor = player.LastInput.throttle,
                    Steer = player.LastInput.steer,
                    Brake = player.LastInput.brake,
                    Handbrake = player.LastInput.handbrake,
                    Nitro = player.LastInput.nitro
                });
                if (player.Rigidbody != null)
                    player.Rigidbody.WakeUp();
                acceptedPlayers += 1;
            }

            return acceptedPlayers;
        }

        public void Dispose()
        {
            Status = "released";
            for (int i = 0; i < orderedPlayers.Count; i++)
            {
                DedicatedRoomPlayerRuntime player = orderedPlayers[i];
                if (player == null || player.Car == null)
                    continue;
                UnityEngine.Object.Destroy(player.Car.gameObject);
            }

            orderedPlayers.Clear();
            playersById.Clear();

            if (rootObject != null)
                UnityEngine.Object.Destroy(rootObject);
        }

        private static void RegroundPlayerAfterLoadoutRefresh(DedicatedRoomPlayerRuntime player)
        {
            if (player == null || player.Car == null)
                return;

            Transform root = player.Car.transform;
            Physics.SyncTransforms();
            root.position = VehicleSpawnUtility.ResolveGroundedSpawnPosition(
                player.Car,
                player.RequestedSpawnPosition,
                FallbackSpawnLift,
                SpawnGroundProbeHeight,
                SpawnGroundProbeDistance,
                root);
            root.rotation = player.RequestedSpawnRotation;
            Physics.SyncTransforms();

            if (player.Rigidbody != null)
            {
                player.Rigidbody.position = root.position;
                player.Rigidbody.rotation = root.rotation;
                player.Rigidbody.linearVelocity = Vector3.zero;
                player.Rigidbody.angularVelocity = Vector3.zero;
                player.Rigidbody.WakeUp();
            }
        }

        private DedicatedRoomPlayerRuntime CreatePlayerRuntime(DedicatedRoomPlayerRequest request, PlayerCar template)
        {
            GameObject instance = UnityEngine.Object.Instantiate(template.gameObject, rootObject.transform);
            instance.name = "ServerCar_" + request.player_id;
            Vector3 requestedSpawnOffset = request.spawn_position != null ? request.spawn_position.ToVector3() : Vector3.zero;
            Vector3 requestedSpawnPosition = VehicleSpawnUtility.ResolveMatchSpawnPosition(
                requestedSpawnOffset,
                spawnAnchorPosition,
                spawnAnchorRotation);
            Quaternion requestedSpawnRotation = VehicleSpawnUtility.ResolveMatchSpawnRotation(
                request.spawn_rotation != null ? request.spawn_rotation.ToVector3() : Vector3.zero,
                spawnAnchorRotation);
            instance.transform.position = requestedSpawnPosition + Vector3.up * SpawnGroundProbeHeight;
            instance.transform.rotation = requestedSpawnRotation;

            PlayerCar playerCar = instance.GetComponent<PlayerCar>();
            if (playerCar == null)
                throw new DedicatedControlException(500, "PLAYER_CAR_MISSING", "Instantiated car is missing PlayerCar");

            CarControllerBase controller = playerCar.Controller != null
                ? playerCar.Controller
                : instance.GetComponent<CarControllerBase>();
            if (controller == null)
                throw new DedicatedControlException(500, "CAR_CONTROLLER_MISSING", "Instantiated car is missing CarControllerBase");

            NetworkCarInputSource inputSource = instance.GetComponent<NetworkCarInputSource>();
            if (inputSource == null)
                inputSource = instance.AddComponent<NetworkCarInputSource>();

            EnsureNetworkVehicleEntity(instance, request.player_id);

            controller.SetInputSource(inputSource);
            controller.SetInputEnabled(true);
            controller.SetManualSimulationEnabled(ManualTick);
            inputSource.ResetInput();

            instance.SetActive(true);
            PlayerCarSelectionPayload payload = request.car_config != null ? request.car_config.ToPlayerSelectionPayload() : null;
            PlayerCarLoadoutUtility.ApplySelectedLoadout(playerCar, payload);
            Physics.SyncTransforms();

            instance.transform.position = VehicleSpawnUtility.ResolveGroundedSpawnPosition(
                playerCar,
                requestedSpawnPosition,
                FallbackSpawnLift,
                SpawnGroundProbeHeight,
                SpawnGroundProbeDistance,
                instance.transform);
            Physics.SyncTransforms();

            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = instance.transform.position;
                body.rotation = instance.transform.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.sleepThreshold = 0.0f;
                body.WakeUp();
            }

            CarDamageController damageController = playerCar.DamageController != null
                ? playerCar.DamageController
                : instance.GetComponentInChildren<CarDamageController>(true);
            if (damageController != null)
            {
                damageController.EnsureNetworkTextureReady();
                damageController.DamageMapChanged += snapshot => HandlePlayerDamageState(request.player_id, snapshot);
                damageController.NetworkVehicleCollisionDetected += report => HandleVehicleCollision(request.player_id, report);
            }

            return new DedicatedRoomPlayerRuntime
            {
                PlayerId = request.player_id,
                PlayerName = string.IsNullOrWhiteSpace(request.player_name) ? request.player_id : request.player_name,
                AuthorityOrder = request.authority_order,
                SpawnPointId = request.spawn_point_id ?? string.Empty,
                RequestedSpawnPosition = requestedSpawnPosition,
                RequestedSpawnRotation = requestedSpawnRotation,
                Car = playerCar,
                Controller = controller,
                Rigidbody = body,
                InputSource = inputSource,
                LoadoutPayload = payload,
                PendingLoadoutRefreshFrames = 2,
                LastInput = BackendCarControlInputPayload.FromControlFrame(CarControlFrame.CreateBrakingFrame()),
                WheelBindings = ResolveWheelBindings(playerCar),
                DamageController = damageController
            };
        }

        private void HandlePlayerDamageState(string playerId, CarDamageNetworkSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(playerId))
                return;
            if (!playersById.TryGetValue(playerId, out DedicatedRoomPlayerRuntime player) || player == null)
                return;

            player.PendingDamageSnapshot = CloneDamageSnapshot(snapshot);
        }

        private void HandleVehicleCollision(string primaryPlayerId, NetworkVehicleCollisionReport report)
        {
            if (report == null || string.IsNullOrWhiteSpace(primaryPlayerId) || string.IsNullOrWhiteSpace(report.otherPlayerId))
                return;
            if (!playersById.ContainsKey(primaryPlayerId) || !playersById.ContainsKey(report.otherPlayerId))
                return;

            string pairKey = BuildCollisionPairKey(primaryPlayerId, report.otherPlayerId);
            if (recentCollisionTicks.TryGetValue(pairKey, out int lastCollisionTick) && ServerTick - lastCollisionTick <= 1)
                return;
            recentCollisionTicks[pairKey] = ServerTick;

            pendingCollisionEvents.Add(new DedicatedSnapshotCollisionEvent
            {
                sequence = nextCollisionSequence++,
                server_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                primary_player_id = primaryPlayerId,
                secondary_player_id = report.otherPlayerId,
                world_point = BackendVector3.FromVector3(report.worldPoint),
                world_normal = BackendVector3.FromVector3(report.worldNormal),
                relative_velocity = BackendVector3.FromVector3(report.relativeVelocity),
                impulse_vector = BackendVector3.FromVector3(report.impulseVector),
                impulse_magnitude = report.impulseMagnitude
            });
        }

        private static CarDamageNetworkSnapshot CloneDamageSnapshot(CarDamageNetworkSnapshot snapshot)
        {
            return new CarDamageNetworkSnapshot
            {
                revision = snapshot.revision,
                width = snapshot.width,
                height = snapshot.height,
                rawBytes = snapshot.rawBytes != null ? (byte[])snapshot.rawBytes.Clone() : Array.Empty<byte>(),
                hasImpactPoint = snapshot.hasImpactPoint,
                worldPoint = snapshot.worldPoint,
                hasImpactNormal = snapshot.hasImpactNormal,
                worldNormal = snapshot.worldNormal
            };
        }

        private static string BuildCollisionPairKey(string firstPlayerId, string secondPlayerId)
        {
            return string.Compare(firstPlayerId, secondPlayerId, StringComparison.OrdinalIgnoreCase) <= 0
                ? firstPlayerId + "|" + secondPlayerId
                : secondPlayerId + "|" + firstPlayerId;
        }

        private static void EnsureNetworkVehicleEntity(GameObject rootObject, string playerId)
        {
            if (rootObject == null)
                return;

            NetworkVehicleEntity entity = rootObject.GetComponent<NetworkVehicleEntity>();
            if (entity == null)
                entity = rootObject.AddComponent<NetworkVehicleEntity>();
            entity.Configure(playerId, false);
        }

        private static List<WheelVisualBinding> ResolveWheelBindings(PlayerCar playerCar)
        {
            List<WheelVisualBinding> result = new List<WheelVisualBinding>(4);
            if (playerCar == null)
                return result;

            WheelCollider[] colliders = playerCar.GetComponentsInChildren<WheelCollider>(true);
            Array.Sort(colliders, CompareWheelColliders);
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

                result.Add(new WheelVisualBinding
                {
                    VisualRoot = visualRoot
                });
            }

            return result;
        }

        private static int ComparePlayers(DedicatedRoomPlayerRuntime left, DedicatedRoomPlayerRuntime right)
        {
            if (left == null || right == null)
                return 0;

            int authorityCompare = left.AuthorityOrder.CompareTo(right.AuthorityOrder);
            return authorityCompare != 0
                ? authorityCompare
                : string.Compare(left.PlayerId, right.PlayerId, StringComparison.OrdinalIgnoreCase);
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

        private static CarControlFrame ToControlFrame(BackendCarControlInputPayload payload)
        {
            BackendCarControlInputPayload resolvedPayload = payload ?? BackendCarControlInputPayload.FromControlFrame(CarControlFrame.CreateBrakingFrame());
            return new CarControlFrame
            {
                Motor = resolvedPayload.throttle,
                Steer = resolvedPayload.steer,
                Brake = resolvedPayload.brake,
                Handbrake = resolvedPayload.handbrake,
                Nitro = resolvedPayload.nitro
            };
        }
    }

    private sealed class DedicatedRoomPlayerRuntime
    {
        public string PlayerId;
        public string PlayerName;
        public int AuthorityOrder;
        public string SpawnPointId;
        public Vector3 RequestedSpawnPosition;
        public Quaternion RequestedSpawnRotation;
        public PlayerCar Car;
        public CarControllerBase Controller;
        public Rigidbody Rigidbody;
        public NetworkCarInputSource InputSource;
        public CarDamageController DamageController;
        public PlayerCarSelectionPayload LoadoutPayload;
        public int PendingLoadoutRefreshFrames;
        public BackendCarControlInputPayload LastInput;
        public int LastInputSeq = -1;
        public long ClientTimeMs;
        public long ServerReceivedTimeMs;
        public List<WheelVisualBinding> WheelBindings = new List<WheelVisualBinding>();
        public CarDamageNetworkSnapshot PendingDamageSnapshot;
        public int LastPublishedDamageRevision;
    }

    private sealed class WheelVisualBinding
    {
        public Transform VisualRoot;
    }
}
