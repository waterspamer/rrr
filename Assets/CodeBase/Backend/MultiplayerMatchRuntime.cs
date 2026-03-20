using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class MultiplayerMatchRuntime : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerCar localPlayerCar;
    [SerializeField] private NetworkPlayerSpawnManager spawnManager;

    [Header("Networking")]
    [SerializeField, Min(1.0f)] private float inputSendRate = 30.0f;
    [SerializeField, Min(1.0f)] private float pingRate = 2.0f;

    [Header("Remote Players")]
    [SerializeField] private Material remoteFallbackMaterial;
    [SerializeField] private Color remoteFallbackColor = new Color(0.22f, 0.88f, 1.0f, 0.82f);
    [SerializeField, Min(0.01f)] private float remoteInterpolationBackTime = 0.10f;
    [SerializeField, Min(0.0f)] private float remoteExtrapolationLimit = 0.08f;
    [SerializeField, Min(2)] private int remoteSnapshotBufferSize = 32;
    [SerializeField, Min(0.5f)] private float remoteTeleportDistance = 12.0f;

    private readonly Dictionary<string, RemotePlayerProxy> remotePlayers = new Dictionary<string, RemotePlayerProxy>(StringComparer.OrdinalIgnoreCase);
    private float nextInputSendTime;
    private float nextPingTime;
    private int inputSequence;
    private string localPlayerId;
    private string activeMatchId;
    private bool matchSetupRequested;
    private CarDamageController localDamageController;
    private readonly List<LocalWheelBinding> localWheelBindings = new List<LocalWheelBinding>(4);

    private sealed class LocalWheelBinding
    {
        public WheelCollider Collider;
        public Transform VisualRoot;
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
        Backend.Client.MatchInfoChanged -= HandleMatchInfoChanged;
        Backend.Client.RealtimeErrorReceived -= HandleRealtimeErrorReceived;
        BindLocalDamageController(null);
    }

    private void Update()
    {
        if (!IsMultiplayerActive())
            return;

        ApplyLocalSpawnIfReady();

        if (Time.unscaledTime >= nextInputSendTime)
        {
            nextInputSendTime = Time.unscaledTime + (1.0f / Mathf.Max(1.0f, inputSendRate));
            _ = SendLocalStateAsync();
        }

        if (Time.unscaledTime >= nextPingTime)
        {
            nextPingTime = Time.unscaledTime + (1.0f / Mathf.Max(1.0f, pingRate));
            _ = SendPingAsync();
        }

        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        foreach (RemotePlayerProxy proxy in remotePlayers.Values)
            proxy.Tick(Time.unscaledTimeAsDouble, deltaTime, remoteInterpolationBackTime, remoteExtrapolationLimit, remoteTeleportDistance);
    }

    private bool IsMultiplayerActive()
    {
        BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
        if (matchInfo == null || string.IsNullOrWhiteSpace(matchInfo.match_id))
            return false;

        activeMatchId = matchInfo.match_id;
        localPlayerId = Backend.Client.Session != null ? Backend.Client.Session.player_id : null;
        CacheMatchPlayers(matchInfo.players);
        if ((matchInfo.players == null || matchInfo.players.Count == 0) && !matchSetupRequested)
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

    private async Task SendLocalStateAsync()
    {
        if (!IsMultiplayerActive())
            return;

        try
        {
            await EnsureRealtimeReadyAsync();
            await Backend.Client.SendPlayerStateAsync(activeMatchId, ++inputSequence, CaptureLocalState());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("MultiplayerMatchRuntime: failed to send player state. " + ex.Message, this);
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

        BackendPlayerStateSnapshot snapshot = new BackendPlayerStateSnapshot
        {
            position = BackendVector3.FromVector3(root.position),
            rotation = BackendVector3.FromVector3(root.eulerAngles),
            velocity = BackendVector3.FromVector3(body != null ? body.linearVelocity : Vector3.zero)
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

    private void RefreshLocalBindings()
    {
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();

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
            localDamageController.DamageMapChanged -= HandleLocalDamageMapChanged;

        localDamageController = nextController;
        if (localDamageController != null)
        {
            localDamageController.EnsureNetworkTextureReady();
            localDamageController.DamageMapChanged -= HandleLocalDamageMapChanged;
            localDamageController.DamageMapChanged += HandleLocalDamageMapChanged;
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
        ApplyLocalSpawnIfReady(force: true);
        EnsureRemotePlayersSpawned();
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
                    continue;

                RemotePlayerProxy proxy = GetOrCreateRemotePlayer(playerState);
                proxy.SetTargetState(
                    state.server_time,
                    playerState.client_time,
                    playerState.server_received_time,
                    playerState.PositionVector,
                    Quaternion.Euler(playerState.RotationVector),
                    playerState.VelocityVector,
                    playerState.wheel_states);
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
            return;

        RemotePlayerProxy proxy = GetOrCreateRemotePlayer(message.player_id);
        proxy?.ApplyDamage(message);
    }

    private void HandleLocalDamageMapChanged(CarDamageNetworkSnapshot snapshot)
    {
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

    private async Task EnsureMatchSetupAsync()
    {
        if (matchSetupRequested || string.IsNullOrWhiteSpace(activeMatchId))
            return;

        matchSetupRequested = true;
        try
        {
            BackendMatchInfo info = await Backend.Client.GetMatchAsync(activeMatchId);
            CacheMatchPlayers(info != null ? info.players : null);
            ApplyLocalSpawnIfReady(force: true);
            EnsureRemotePlayersSpawned();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("MultiplayerMatchRuntime: failed to fetch match setup. " + ex.Message, this);
        }
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

            public RemoteSnapshot(double localTime, Vector3 position, Quaternion rotation, Vector3 velocity)
            {
                LocalTime = localTime;
                Position = position;
                Rotation = rotation;
                Velocity = velocity;
            }
        }

        private readonly GameObject root;
        private readonly Transform transform;
        private readonly Material fallbackMaterial;
        private readonly Color fallbackColor;
        private readonly int snapshotBufferSize;
        private readonly List<RemoteWheelBinding> wheelBindings = new List<RemoteWheelBinding>(4);
        private readonly List<RemoteSnapshot> snapshots = new List<RemoteSnapshot>(32);
        private readonly string playerId;
        private bool visualReady;
        private CarDamageController damageController;
        private DamageManager damageManager;
        private int appliedDamageRevision;

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
            this.playerId = playerId;
            this.fallbackMaterial = fallbackMaterial;
            this.fallbackColor = fallbackColor;
            this.snapshotBufferSize = Math.Max(2, snapshotBufferSize);

            if (matchPlayer != null && matchPlayer.HasSpawnAssignment)
            {
                transform.position = matchPlayer.SpawnPositionVector;
                transform.rotation = Quaternion.Euler(matchPlayer.SpawnRotationVector);
                snapshots.Add(new RemoteSnapshot(Time.unscaledTimeAsDouble, transform.position, transform.rotation, Vector3.zero));
            }

            EnsureVisual(matchPlayer, lobbyPlayer, fallbackCarConfig);
        }

        public void EnsureVisual(BackendCarConfigPayload fallbackCarConfig)
        {
            if (visualReady)
                return;

            EnsureVisual(null, null, fallbackCarConfig);
        }

        public void SetTargetState(
            long matchServerTimeMs,
            long clientTimeMs,
            long serverReceivedTimeMs,
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity,
            List<BackendWheelPose> wheelStates)
        {
            EnsureVisual(null);
            PushSnapshot(ResolveSnapshotLocalTime(matchServerTimeMs, clientTimeMs, serverReceivedTimeMs), position, rotation, velocity);
            ApplyWheelStates(wheelStates);
        }

        public void Tick(double localNow, float deltaTime, float interpolationBackTime, float extrapolationLimit, float teleportDistance)
        {
            if (snapshots.Count == 0)
                return;

            double renderTime = localNow - Mathf.Max(0.01f, interpolationBackTime);
            Vector3 desiredPosition;
            Quaternion desiredRotation;
            EvaluateSnapshot(renderTime, extrapolationLimit, out desiredPosition, out desiredRotation);

            if (Vector3.Distance(transform.position, desiredPosition) >= teleportDistance)
            {
                transform.SetPositionAndRotation(desiredPosition, desiredRotation);
                return;
            }

            transform.SetPositionAndRotation(desiredPosition, desiredRotation);
        }

        public void Dispose()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
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

        private void PushSnapshot(double localTime, Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            if (snapshots.Count > 0)
            {
                RemoteSnapshot newest = snapshots[snapshots.Count - 1];
                if (localTime <= newest.LocalTime)
                    localTime = newest.LocalTime + 0.0001d;
            }

            snapshots.Add(new RemoteSnapshot(localTime, position, rotation, velocity));
            if (snapshots.Count > snapshotBufferSize)
                snapshots.RemoveRange(0, snapshots.Count - snapshotBufferSize);
        }

        private static double ResolveSnapshotLocalTime(long matchServerTimeMs, long clientTimeMs, long serverReceivedTimeMs)
        {
            double localNow = Time.unscaledTimeAsDouble;
            if (matchServerTimeMs <= 0)
                return localNow;

            if (serverReceivedTimeMs > 0 && matchServerTimeMs >= serverReceivedTimeMs)
            {
                double serverAgeSec = (matchServerTimeMs - serverReceivedTimeMs) / 1000.0d;
                return localNow - Math.Max(0.0d, serverAgeSec);
            }

            if (clientTimeMs > 0)
                return localNow;

            return localNow;
        }

        private void EvaluateSnapshot(double renderTime, float extrapolationLimit, out Vector3 position, out Quaternion rotation)
        {
            if (snapshots.Count == 1)
            {
                RemoteSnapshot only = snapshots[0];
                double dt = Math.Min(Math.Max(0.0d, renderTime - only.LocalTime), extrapolationLimit);
                position = only.Position + only.Velocity * (float)dt;
                rotation = only.Rotation;
                return;
            }

            while (snapshots.Count >= 2 && snapshots[1].LocalTime <= renderTime - 0.5d)
                snapshots.RemoveAt(0);

            RemoteSnapshot oldest = snapshots[0];
            if (renderTime <= oldest.LocalTime)
            {
                position = oldest.Position;
                rotation = oldest.Rotation;
                return;
            }

            for (int i = 0; i < snapshots.Count - 1; i++)
            {
                RemoteSnapshot from = snapshots[i];
                RemoteSnapshot to = snapshots[i + 1];
                if (renderTime > to.LocalTime)
                    continue;

                float t = (float)((renderTime - from.LocalTime) / Math.Max(0.0001d, to.LocalTime - from.LocalTime));
                position = Vector3.LerpUnclamped(from.Position, to.Position, t);
                rotation = Quaternion.SlerpUnclamped(from.Rotation, to.Rotation, t);
                return;
            }

            RemoteSnapshot latest = snapshots[snapshots.Count - 1];
            double extrapolation = Math.Min(Math.Max(0.0d, renderTime - latest.LocalTime), extrapolationLimit);
            position = latest.Position + latest.Velocity * (float)extrapolation;
            rotation = latest.Rotation;
        }

        private void EnsureVisual(BackendMatchPlayerInfo matchPlayer, BackendLobbyPlayer lobbyPlayer, BackendCarConfigPayload fallbackCarConfig)
        {
            if (visualReady)
                return;

            visualReady =
                TryCreateVisualFromMatchConfig(root.transform, matchPlayer) ||
                TryCreateVisualFromLobbyConfig(root.transform, lobbyPlayer) ||
                TryCreateVisual(root.transform, fallbackCarConfig, playerId);

            if (!visualReady)
            {
                CreateFallbackVisual(root.transform, fallbackMaterial, fallbackColor);
                visualReady = true;
            }

            if (visualReady && damageController == null)
            {
                damageController = root.GetComponentInChildren<CarDamageController>(true);
                damageManager = root.GetComponentInChildren<DamageManager>(true);
                if (damageController != null)
                    damageController.EnsureNetworkTextureReady();
            }

            if (visualReady && wheelBindings.Count == 0)
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
            if (carConfig == null || string.IsNullOrWhiteSpace(carConfig.loadout_name))
                return false;

            CarLoadoutConfig loadout = CarLoadoutResolver.Resolve(carConfig.ToPlayerSelectionPayload());
            if (loadout == null || loadout.PlayerCarConfig == null || loadout.PlayerCarConfig.Visual == null)
                return false;

            GameObject bodyPrefab = loadout.PlayerCarConfig.Visual.bodyPrefab;
            if (bodyPrefab == null)
                return false;

            GameObject bodyInstance = UnityEngine.Object.Instantiate(bodyPrefab, parent);
            bodyInstance.name = "Body";
            bodyInstance.transform.localPosition = Vector3.zero;
            bodyInstance.transform.localRotation = Quaternion.identity;
            bodyInstance.transform.localScale = Vector3.one;
            ApplyRemoteBodySet(loadout, bodyInstance.transform, carConfig);
            ApplyRemoteCustomizations(bodyInstance.transform, carConfig);
            EnsureRemotePhysics(parent.gameObject, bodyInstance, loadout.PlayerCarConfig);
            EnsureRemoteDamage(parent.gameObject, bodyInstance, loadout.PlayerCarConfig, loadout.PlayerCarConfig.Visual != null ? loadout.PlayerCarConfig.Visual.bodyPrefab : null);
            EnsureNetworkEntity(parent.gameObject, playerId);
            StripGameplayComponents(bodyInstance, keepBodyColliders: true);
            ApplyRemotePaint(bodyInstance, carConfig);
            AttachRemoteWheels(parent, loadout, carConfig, null);
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

        private void ApplyWheelStates(List<BackendWheelPose> wheelStates)
        {
            if (wheelStates == null || wheelStates.Count == 0 || wheelBindings.Count == 0)
                return;

            int count = Mathf.Min(wheelBindings.Count, wheelStates.Count);
            for (int i = 0; i < count; i++)
            {
                RemoteWheelBinding binding = wheelBindings[i];
                BackendWheelPose pose = wheelStates[i];
                if (binding == null || binding.VisualRoot == null || pose == null)
                    continue;

                binding.VisualRoot.localPosition = pose.position != null ? pose.position.ToVector3() : binding.VisualRoot.localPosition;
                binding.VisualRoot.localRotation = pose.rotation != null ? Quaternion.Euler(pose.rotation.ToVector3()) : binding.VisualRoot.localRotation;
            }
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
                Transform wheelRoot = transform.Find(names[i]);
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
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            if (bodyInstance != null && bodyInstance.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                Renderer[] renderers = bodyInstance.GetComponentsInChildren<Renderer>(true);
                if (renderers != null && renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);

                    BoxCollider collider = bodyInstance.AddComponent<BoxCollider>();
                    collider.center = bodyInstance.transform.InverseTransformPoint(bounds.center);
                    Vector3 scale = bodyInstance.transform.lossyScale;
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
