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
    [SerializeField, Min(1.0f)] private float inputSendRate = 20.0f;
    [SerializeField, Min(1.0f)] private float pingRate = 2.0f;

    [Header("Remote Players")]
    [SerializeField] private Material remoteFallbackMaterial;
    [SerializeField] private Color remoteFallbackColor = new Color(0.22f, 0.88f, 1.0f, 0.82f);
    [SerializeField, Min(0.01f)] private float remotePositionLerp = 10.0f;
    [SerializeField, Min(0.01f)] private float remoteRotationLerp = 10.0f;

    private readonly Dictionary<string, RemotePlayerProxy> remotePlayers = new Dictionary<string, RemotePlayerProxy>(StringComparer.OrdinalIgnoreCase);
    private float nextInputSendTime;
    private float nextPingTime;
    private int inputSequence;
    private string localPlayerId;
    private string activeMatchId;
    private bool matchSetupRequested;

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

        Backend.Client.MatchInfoChanged -= HandleMatchInfoChanged;
        Backend.Client.MatchInfoChanged += HandleMatchInfoChanged;

        Backend.Client.RealtimeErrorReceived -= HandleRealtimeErrorReceived;
        Backend.Client.RealtimeErrorReceived += HandleRealtimeErrorReceived;

        await EnsureRealtimeReadyAsync();
    }

    private void OnDisable()
    {
        Backend.Client.MatchStateReceived -= HandleMatchStateReceived;
        Backend.Client.MatchInfoChanged -= HandleMatchInfoChanged;
        Backend.Client.RealtimeErrorReceived -= HandleRealtimeErrorReceived;
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
            proxy.Tick(deltaTime, remotePositionLerp, remoteRotationLerp);
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

    private BackendPlayerStateSnapshot CaptureLocalState()
    {
        if (localPlayerCar == null)
            localPlayerCar = FindFirstObjectByType<PlayerCar>();

        Transform root = localPlayerCar != null ? localPlayerCar.transform : transform;
        Rigidbody body = localPlayerCar != null ? localPlayerCar.GetComponent<Rigidbody>() : null;

        return new BackendPlayerStateSnapshot
        {
            position = BackendVector3.FromVector3(root.position),
            rotation = BackendVector3.FromVector3(root.eulerAngles),
            velocity = BackendVector3.FromVector3(body != null ? body.linearVelocity : Vector3.zero)
        };
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
                    playerState.PositionVector,
                    Quaternion.Euler(playerState.RotationVector),
                    playerState.VelocityVector,
                    playerState.car_config);
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

        return GetOrCreateRemotePlayer(playerState.player_id, playerState.car_config);
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
        private readonly GameObject root;
        private readonly Transform transform;
        private readonly Material fallbackMaterial;
        private readonly Color fallbackColor;
        private bool visualReady;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Vector3 currentVelocity;

        public RemotePlayerProxy(
            string playerId,
            BackendMatchPlayerInfo matchPlayer,
            BackendLobbyPlayer lobbyPlayer,
            BackendCarConfigPayload fallbackCarConfig,
            Material fallbackMaterial,
            Color fallbackColor,
            Transform remoteRoot)
        {
            root = new GameObject("RemotePlayer_" + playerId);
            if (remoteRoot != null)
                root.transform.SetParent(remoteRoot, false);
            transform = root.transform;
            this.fallbackMaterial = fallbackMaterial;
            this.fallbackColor = fallbackColor;
            targetRotation = Quaternion.identity;

            if (matchPlayer != null && matchPlayer.HasSpawnAssignment)
            {
                transform.position = matchPlayer.SpawnPositionVector;
                transform.rotation = Quaternion.Euler(matchPlayer.SpawnRotationVector);
                targetPosition = transform.position;
                targetRotation = transform.rotation;
            }

            EnsureVisual(matchPlayer, lobbyPlayer, fallbackCarConfig);
        }

        public void EnsureVisual(BackendCarConfigPayload fallbackCarConfig)
        {
            if (visualReady)
                return;

            EnsureVisual(null, null, fallbackCarConfig);
        }

        public void SetTargetState(Vector3 position, Quaternion rotation, Vector3 velocity, BackendCarConfigPayload fallbackCarConfig)
        {
            EnsureVisual(fallbackCarConfig);
            targetPosition = position;
            targetRotation = rotation;
            currentVelocity = velocity;
        }

        public void Tick(float deltaTime, float positionLerp, float rotationLerp)
        {
            float positionT = 1.0f - Mathf.Exp(-Mathf.Max(0.01f, positionLerp) * deltaTime);
            float rotationT = 1.0f - Mathf.Exp(-Mathf.Max(0.01f, rotationLerp) * deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, positionT);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationT);
        }

        public void Dispose()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
        }

        private void EnsureVisual(BackendMatchPlayerInfo matchPlayer, BackendLobbyPlayer lobbyPlayer, BackendCarConfigPayload fallbackCarConfig)
        {
            if (visualReady)
                return;

            visualReady =
                TryCreateVisualFromMatchConfig(root.transform, matchPlayer) ||
                TryCreateVisualFromLobbyConfig(root.transform, lobbyPlayer) ||
                TryCreateVisual(root.transform, fallbackCarConfig);

            if (!visualReady)
            {
                CreateFallbackVisual(root.transform, fallbackMaterial, fallbackColor);
                visualReady = true;
            }
        }

        private static bool TryCreateVisualFromMatchConfig(Transform parent, BackendMatchPlayerInfo matchPlayer)
        {
            if (matchPlayer == null)
                return false;

            return TryCreateVisual(parent, matchPlayer.car_config);
        }

        private static bool TryCreateVisualFromLobbyConfig(Transform parent, BackendLobbyPlayer lobbyPlayer)
        {
            if (lobbyPlayer == null)
                return false;

            return TryCreateVisual(parent, lobbyPlayer.car_config);
        }

        private static bool TryCreateVisual(Transform parent, BackendCarConfigPayload carConfig)
        {
            if (carConfig == null || string.IsNullOrWhiteSpace(carConfig.loadout_name))
                return false;

            CarLoadoutConfig[] loadouts = Resources.LoadAll<CarLoadoutConfig>("Vehicles");
            for (int i = 0; i < loadouts.Length; i++)
            {
                CarLoadoutConfig loadout = loadouts[i];
                if (loadout == null || loadout.PlayerCarConfig == null || loadout.PlayerCarConfig.Visual == null)
                    continue;
                if (!string.Equals(loadout.name, carConfig.loadout_name, StringComparison.OrdinalIgnoreCase))
                    continue;

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
                StripGameplayComponents(bodyInstance);
                ApplyRemotePaint(bodyInstance, carConfig);
                return true;
            }

            return false;
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

        private static void StripGameplayComponents(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                UnityEngine.Object.Destroy(colliders[i]);

            Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
                UnityEngine.Object.Destroy(rigidbodies[i]);

            CarControllerBase[] controllers = root.GetComponentsInChildren<CarControllerBase>(true);
            for (int i = 0; i < controllers.Length; i++)
                UnityEngine.Object.Destroy(controllers[i]);
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
    }
}
