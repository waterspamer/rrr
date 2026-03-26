using System;
using System.Collections.Generic;
using System.Globalization;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Prediction;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

[System.Serializable]
public struct PurrVehicleLoadoutMessage : IPackedAuto
{
    public string payloadJson;

    public static PurrVehicleLoadoutMessage FromPayload(PlayerCarSelectionPayload payload)
    {
        return new PurrVehicleLoadoutMessage
        {
            payloadJson = SerializePayload(payload)
        };
    }

    public PlayerCarSelectionPayload ToPayload()
    {
        return DeserializePayload(payloadJson);
    }

    private static string SerializePayload(PlayerCarSelectionPayload payload)
    {
        return payload != null ? JsonUtility.ToJson(payload) : string.Empty;
    }

    private static PlayerCarSelectionPayload DeserializePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            return JsonUtility.FromJson<PlayerCarSelectionPayload>(payloadJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PurrVehicleLoadoutMessage: failed to deserialize payload. {ex.Message}");
            return null;
        }
    }
}

[Serializable]
public sealed class PurrVehicleSpawnerPlayerObserverRecord
{
    public string playerId;
    public bool isBot;
    public bool queued;
    public bool spawned;
    public int spawnSlot = -1;
    public string spawnPointId;
    public Vector3 spawnPosition;
    public Vector3 spawnRotationEuler;
    public string lastSpawnFailureReason;
    public PlayerCarSelectionPayload loadout;
}

[Serializable]
public sealed class PurrVehicleSpawnerObserverSnapshot
{
    public string sceneName;
    public string activeSceneId;
    public string templateCarName;
    public int soloBotTarget;
    public int queuedPlayers;
    public int spawnedPlayers;
    public int trackedBotPlayers;
    public int pendingBotCreates;
    public bool hasSceneId;
    public bool predictionManagerReady;
    public bool predictionManagerSpawned;
    public bool hierarchyReady;
    public bool scenePlayersReady;
    public bool playersManagerReady;
    public bool isServerSpawner;
    public bool isClientPublisher;
    public bool localLoadoutPublished;
    public bool transientSoloCleanupEnabled;
    public float soloIdleTimeoutSeconds;
    public float soloLifecyclePollIntervalSeconds;
    public bool soloSessionActive;
    public string soloSessionHumanPlayerId;
    public float soloSessionActiveForSeconds;
    public float secondsSinceLastHumanSeen;
    public float secondsSinceLastMeaningfulInput;
    public float secondsUntilIdleClose;
    public string soloSessionStatus;
    public string lastSoloSessionCloseReason;
    public float secondsSinceLastSoloSessionClose;
    public string networkClientState;
    public string networkServerState;
    public string lastWaitReason;
    public List<PurrVehicleSpawnerPlayerObserverRecord> players = new List<PurrVehicleSpawnerPlayerObserverRecord>();
}

[DisallowMultipleComponent]
public sealed class PurrVehicleSceneSpawner : PurrMonoBehaviour
{
    [SerializeField] private PlayerCar templateCar;
    [SerializeField] private PredictionManager predictionManager;
    [SerializeField, Range(0, 8)] private int soloBotCount;
    [SerializeField, Min(2.0f)] private float spawnSpacing = 7.0f;
    [SerializeField, Min(0.5f)] private float spawnLift = 1.5f;
    [SerializeField, Min(1.0f)] private float groundProbeHeight = 8.0f;
    [SerializeField, Min(1.0f)] private float groundProbeDistance = 20.0f;
    [SerializeField] private bool autoCloseTransientSoloSession = true;
    [SerializeField, Min(5.0f)] private float soloSessionIdleTimeoutSeconds = 30.0f;
    [SerializeField, Min(0.1f)] private float soloSessionLifecyclePollIntervalSeconds = 0.5f;

    private readonly Dictionary<PlayerID, PredictedObjectID> spawnedPlayers = new Dictionary<PlayerID, PredictedObjectID>();
    private readonly Dictionary<PlayerID, PlayerCarSelectionPayload> playerLoadouts = new Dictionary<PlayerID, PlayerCarSelectionPayload>();
    private readonly HashSet<PlayerID> queuedPlayers = new HashSet<PlayerID>();
    private readonly List<PlayerID> botPlayers = new List<PlayerID>();
    private readonly List<PlayerID> spawnBuffer = new List<PlayerID>();
    private readonly Dictionary<string, PurrVehicleSpawnerPlayerObserverRecord> observerPlayerRecords =
        new Dictionary<string, PurrVehicleSpawnerPlayerObserverRecord>(StringComparer.Ordinal);

    private NetworkManager activeManager;
    private ScenePlayersModule scenePlayers;
    private PlayersManager playersManager;
    private SceneID activeSceneId;
    private bool hasSceneId;
    private bool isServerSpawner;
    private bool isClientPublisher;
    private bool localLoadoutPublished;
    private bool isEnsuringBots;
    private int pendingBotCreates;
    private float nextWaitDiagnosticAt;
    private float nextSoloLifecycleCheckAt;
    private bool soloSessionActive;
    private float soloSessionActivatedAt = -1.0f;
    private float lastHumanSeenAt = -1.0f;
    private float lastMeaningfulHumanInputAt = -1.0f;
    private float lastSoloSessionClosedAt = -1.0f;
    private string lastWaitReason = string.Empty;
    private string soloSessionHumanPlayerId = string.Empty;
    private string soloSessionStatus = "idle";
    private string lastSoloSessionCloseReason = string.Empty;

    public void Configure(PlayerCar template, int botCount, PredictionManager world)
    {
        templateCar = template;
        predictionManager = world;
        soloBotCount = Mathf.Max(0, botCount);
    }

    private void Update()
    {
        if (isClientPublisher)
            TryPublishLocalLoadout();

        if (isServerSpawner)
        {
            TrySpawnQueuedPlayers();
            UpdateSoloSessionLifecycle();
        }
    }

    public override void Subscribe(NetworkManager manager, bool asServer)
    {
        if (templateCar == null)
            return;

        activeManager = manager;
        ResolveReferences();
        if (playersManager != null)
            playersManager.Subscribe<PurrVehicleLoadoutMessage>(OnLoadoutMessage);

        if (!asServer)
        {
            isClientPublisher = true;
            Debug.Log($"PurrVehicleSceneSpawner: client subscribe scene='{gameObject.scene.name}' template='{templateCar.name}'", this);
            return;
        }

        isServerSpawner = true;
        Debug.Log($"PurrVehicleSceneSpawner: server subscribe scene='{gameObject.scene.name}' template='{templateCar.name}'", this);

        ApplyServerEnvironmentOverrides();
        manager.onPlayerLoadedScene += OnPlayerLoadedScene;
        manager.onPlayerLeft += OnPlayerLeft;

        CacheSceneId(manager);
        QueueExistingPlayers();
        EnsureBots();
        TrySpawnQueuedPlayers();
    }

    public override void Unsubscribe(NetworkManager manager, bool asServer)
    {
        if (playersManager != null)
            playersManager.Unsubscribe<PurrVehicleLoadoutMessage>(OnLoadoutMessage);

        if (asServer)
        {
            manager.onPlayerLoadedScene -= OnPlayerLoadedScene;
            manager.onPlayerLeft -= OnPlayerLeft;
            isServerSpawner = false;
        }
        else
        {
            isClientPublisher = false;
            localLoadoutPublished = false;
        }

        activeManager = null;
    }

    private void ResolveReferences()
    {
        if (predictionManager == null)
            predictionManager = GetComponent<PredictionManager>();

        if (activeManager != null)
        {
            if (scenePlayers == null)
            {
                if (!activeManager.TryGetModule(out scenePlayers, true))
                    activeManager.TryGetModule(out scenePlayers, false);
            }
            if (playersManager == null)
            {
                if (!activeManager.TryGetModule(out playersManager, true))
                    activeManager.TryGetModule(out playersManager, false);
            }
        }
    }

    private void CacheSceneId(NetworkManager manager)
    {
        hasSceneId = manager != null && manager.TryGetSceneID(gameObject.scene, out activeSceneId);
        Debug.Log(
            hasSceneId
                ? $"PurrVehicleSceneSpawner: active scene id is {activeSceneId} for '{gameObject.scene.name}'."
                : $"PurrVehicleSceneSpawner: failed to resolve scene id for '{gameObject.scene.name}'.",
            this);
    }

    private void QueueExistingPlayers()
    {
        if (!hasSceneId || scenePlayers == null)
            return;

        if (!scenePlayers.TryGetPlayersInScene(activeSceneId, out IReadOnlyList<PlayerID> players) || players == null)
            return;

        for (int i = 0; i < players.Count; i++)
            QueueSpawn(players[i]);
    }

    private void EnsureBots()
    {
        if (soloBotCount <= 0 || playersManager == null || scenePlayers == null || !hasSceneId || isEnsuringBots)
            return;

        isEnsuringBots = true;
        try
        {
            while (botPlayers.Count + pendingBotCreates < soloBotCount)
            {
                pendingBotCreates += 1;
                PlayerID bot;
                try
                {
                    bot = playersManager.CreateBot();
                }
                finally
                {
                    pendingBotCreates = Mathf.Max(0, pendingBotCreates - 1);
                }

                if (!botPlayers.Contains(bot))
                    botPlayers.Add(bot);

                if (!scenePlayers.IsPlayerLoadedInScene(bot, activeSceneId))
                    scenePlayers.AddPlayerToScene(bot, activeSceneId);

                QueueSpawn(bot);
                Debug.Log($"PurrVehicleSceneSpawner: created bot {bot} in scene {activeSceneId}.", this);
            }
        }
        finally
        {
            isEnsuringBots = false;
        }
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
    {
        if (!asServer)
            return;

        if (!hasSceneId)
        {
            activeSceneId = scene;
            hasSceneId = true;
            Debug.Log($"PurrVehicleSceneSpawner: late-bound active scene id to {activeSceneId}.", this);
        }

        if (scene != activeSceneId)
            return;

        QueueSpawn(player);
        if (!player.isBot)
            EnsureBots();
        TrySpawnQueuedPlayers();
    }

    private void OnPlayerLeft(PlayerID player, bool asServer)
    {
        if (!asServer)
            return;

        RemoveTrackedPlayer(player);
    }

    private void QueueSpawn(PlayerID player)
    {
        if (spawnedPlayers.ContainsKey(player))
            return;

        queuedPlayers.Add(player);
        PurrVehicleSpawnerPlayerObserverRecord record = GetOrCreateObserverRecord(player);
        record.queued = true;
        record.spawned = false;
    }

    private void TrySpawnQueuedPlayers()
    {
        ResolveReferences();

        if (!hasSceneId || queuedPlayers.Count == 0)
            return;

        if (predictionManager == null || predictionManager.hierarchy == null || scenePlayers == null || !predictionManager.isSpawned)
        {
            MaybeLogWaitReason();
            return;
        }

        spawnBuffer.Clear();
        foreach (PlayerID player in queuedPlayers)
            spawnBuffer.Add(player);
        spawnBuffer.Sort(CompareSpawnPriority);

        for (int i = 0; i < spawnBuffer.Count; i++)
        {
            PlayerID player = spawnBuffer[i];
            if (TrySpawnPlayer(player))
                queuedPlayers.Remove(player);
        }
    }

    private bool TrySpawnPlayer(PlayerID player)
    {
        if (!hasSceneId || templateCar == null || spawnedPlayers.ContainsKey(player))
        {
            SetObserverSpawnFailure(player, !hasSceneId ? "scene_id_unresolved" : templateCar == null ? "template_missing" : "already_spawned");
            return false;
        }

        if (!scenePlayers.IsPlayerLoadedInScene(player, activeSceneId))
        {
            SetObserverSpawnFailure(player, "player_not_loaded_in_scene");
            return false;
        }

        int spawnSlot = ComputeSpawnSlot(player);
        Vector3 requestedSpawnPosition = templateCar.transform.position +
                                         templateCar.transform.right * (spawnSpacing * spawnSlot) +
                                         Vector3.up * spawnLift;
        Quaternion requestedSpawnRotation = templateCar.transform.rotation;

        if (!predictionManager.hierarchy.TryCreate(templateCar.gameObject, requestedSpawnPosition, requestedSpawnRotation, out PredictedObjectID objectId, player))
        {
            SetObserverSpawnFailure(player, "hierarchy_create_failed");
            Debug.LogWarning($"PurrVehicleSceneSpawner: failed to create predicted vehicle for player {player}.", this);
            return false;
        }

        if (!predictionManager.hierarchy.TryGetGameObject(objectId, out GameObject instance) || instance == null)
        {
            SetObserverSpawnFailure(player, "runtime_object_missing");
            Debug.LogWarning($"PurrVehicleSceneSpawner: hierarchy created player {player}, but runtime object was not found.", this);
            predictionManager.hierarchy.Delete(objectId);
            return false;
        }

        if (!instance.TryGetComponent(out PlayerCar spawnedCar))
        {
            SetObserverSpawnFailure(player, "player_car_missing");
            Debug.LogWarning($"PurrVehicleSceneSpawner: spawned object for player {player} has no PlayerCar.", instance);
            predictionManager.hierarchy.Delete(objectId);
            return false;
        }

        TryApplyLoadout(player, spawnedCar, instance);

        Vector3 groundedSpawnPosition = VehicleSpawnUtility.ResolveGroundedSpawnPosition(
            spawnedCar,
            requestedSpawnPosition,
            spawnLift,
            groundProbeHeight,
            groundProbeDistance,
            instance.transform);
        instance.transform.SetPositionAndRotation(groundedSpawnPosition, requestedSpawnRotation);

        NetworkVehicleEntity entity = instance.GetComponent<NetworkVehicleEntity>();
        if (entity == null)
            entity = instance.AddComponent<NetworkVehicleEntity>();
        entity.Configure(player.ToString(), false);

        spawnedPlayers[player] = objectId;
        UpdateObserverSpawned(player, groundedSpawnPosition, requestedSpawnRotation.eulerAngles, spawnSlot);
        Debug.Log(
            $"PurrVehicleSceneSpawner: spawned predicted vehicle '{instance.name}' for player {player} at {groundedSpawnPosition} in scene {activeSceneId}.",
            instance);
        return true;
    }

    private void MaybeLogWaitReason()
    {
        if (Time.unscaledTime < nextWaitDiagnosticAt)
            return;

        nextWaitDiagnosticAt = Time.unscaledTime + 1.0f;
        lastWaitReason =
            $"hasSceneId={hasSceneId} queued={queuedPlayers.Count} predictionManager={(predictionManager != null)} predictionSpawned={(predictionManager != null && predictionManager.isSpawned)} hierarchy={(predictionManager != null && predictionManager.hierarchy != null)} scenePlayers={(scenePlayers != null)}";
        Debug.LogWarning($"PurrVehicleSceneSpawner: waiting for spawn. {lastWaitReason}", this);
    }

    private void ApplyServerEnvironmentOverrides()
    {
        autoCloseTransientSoloSession = ReadBoolEnvironmentVariable(
            "RRR_PURRNET_AUTO_CLOSE_SOLO_SESSION",
            autoCloseTransientSoloSession);
        soloSessionIdleTimeoutSeconds = ReadFloatEnvironmentVariable(
            "RRR_PURRNET_SOLO_IDLE_TIMEOUT_SEC",
            soloSessionIdleTimeoutSeconds,
            5.0f,
            600.0f);
        soloSessionLifecyclePollIntervalSeconds = ReadFloatEnvironmentVariable(
            "RRR_PURRNET_SOLO_IDLE_POLL_SEC",
            soloSessionLifecyclePollIntervalSeconds,
            0.1f,
            5.0f);
    }

    private void UpdateSoloSessionLifecycle()
    {
        if (!autoCloseTransientSoloSession || soloBotCount <= 0)
            return;

        if (Time.unscaledTime < nextSoloLifecycleCheckAt)
            return;

        nextSoloLifecycleCheckAt = Time.unscaledTime + soloSessionLifecyclePollIntervalSeconds;
        ResolveReferences();
        if (playersManager == null)
            return;

        float now = Time.unscaledTime;
        int humanPlayers = 0;
        string activeHumanPlayerId = string.Empty;

        IReadOnlyList<PlayerID> connectedPlayers = playersManager.players;
        for (int i = 0; i < connectedPlayers.Count; i++)
        {
            PlayerID player = connectedPlayers[i];
            if (player.isServer || player.isBot)
                continue;

            humanPlayers += 1;
            activeHumanPlayerId = player.ToString();
            lastHumanSeenAt = now;

            if (!soloSessionActive)
            {
                soloSessionActive = true;
                soloSessionActivatedAt = now;
                lastMeaningfulHumanInputAt = now;
                soloSessionStatus = "active";
                soloSessionHumanPlayerId = activeHumanPlayerId;
                lastSoloSessionCloseReason = string.Empty;
                Debug.Log($"PurrVehicleSceneSpawner: transient solo session activated for player {activeHumanPlayerId}.", this);
            }
            else if (string.IsNullOrWhiteSpace(soloSessionHumanPlayerId))
            {
                soloSessionHumanPlayerId = activeHumanPlayerId;
            }

            if (TryGetLastAppliedInput(player, out CarControlFrame frame) && IsMeaningfulInput(frame))
                lastMeaningfulHumanInputAt = now;
        }

        if (!soloSessionActive)
        {
            soloSessionStatus = "idle";
            return;
        }

        if (humanPlayers == 0)
        {
            CloseTransientSoloSession("player_disconnected");
            return;
        }

        if (humanPlayers > 1)
        {
            soloSessionStatus = "cleanup_suppressed_multiple_humans";
            return;
        }

        if (lastMeaningfulHumanInputAt < 0.0f)
            lastMeaningfulHumanInputAt = lastHumanSeenAt >= 0.0f ? lastHumanSeenAt : now;

        float secondsUntilIdleClose = soloSessionIdleTimeoutSeconds - Mathf.Max(0.0f, now - lastMeaningfulHumanInputAt);
        if (secondsUntilIdleClose <= 0.0f)
        {
            CloseTransientSoloSession("idle_timeout");
            return;
        }

        soloSessionHumanPlayerId = activeHumanPlayerId;
        soloSessionStatus = "active";
    }

    private bool TryGetLastAppliedInput(PlayerID player, out CarControlFrame frame)
    {
        frame = default;
        if (predictionManager == null || predictionManager.hierarchy == null)
            return false;

        if (!spawnedPlayers.TryGetValue(player, out PredictedObjectID objectId))
            return false;

        if (!predictionManager.hierarchy.TryGetGameObject(objectId, out GameObject instance) || instance == null)
            return false;

        PurrVehicleSimulationBridge bridge = instance.GetComponent<PurrVehicleSimulationBridge>();
        if (bridge != null && bridge.HasController)
        {
            frame = bridge.LastAppliedControlFrame;
            frame.Clamp();
            return true;
        }

        CarControllerBase controller = null;
        if (instance.TryGetComponent(out PlayerCar spawnedCar) && spawnedCar != null)
            controller = spawnedCar.Controller;
        if (controller == null)
            controller = instance.GetComponent<CarControllerBase>();
        if (controller == null)
            return false;

        frame = controller.LastAppliedControlFrame;
        frame.Clamp();
        return true;
    }

    private void CloseTransientSoloSession(string reason)
    {
        ResolveReferences();
        if (playersManager == null)
            return;

        IReadOnlyList<PlayerID> players = playersManager.players;
        List<PlayerID> kickList = new List<PlayerID>(players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            PlayerID player = players[i];
            if (!player.isServer)
                kickList.Add(player);
        }

        for (int i = 0; i < kickList.Count; i++)
            playersManager.KickPlayer(kickList[i]);

        HashSet<PlayerID> lingeringPlayers = new HashSet<PlayerID>();
        foreach (PlayerID player in spawnedPlayers.Keys)
            lingeringPlayers.Add(player);
        foreach (PlayerID player in queuedPlayers)
            lingeringPlayers.Add(player);
        for (int i = 0; i < botPlayers.Count; i++)
            lingeringPlayers.Add(botPlayers[i]);
        foreach (PlayerID player in lingeringPlayers)
            RemoveTrackedPlayer(player);

        queuedPlayers.Clear();
        playerLoadouts.Clear();
        botPlayers.Clear();
        observerPlayerRecords.Clear();

        soloSessionActive = false;
        soloSessionStatus = "closed";
        lastSoloSessionCloseReason = reason ?? string.Empty;
        lastSoloSessionClosedAt = Time.unscaledTime;
        soloSessionActivatedAt = -1.0f;
        lastHumanSeenAt = -1.0f;
        lastMeaningfulHumanInputAt = -1.0f;
        soloSessionHumanPlayerId = string.Empty;
        Debug.LogWarning($"PurrVehicleSceneSpawner: transient solo session closed. reason={lastSoloSessionCloseReason}", this);
    }

    private void RemoveTrackedPlayer(PlayerID player)
    {
        queuedPlayers.Remove(player);
        playerLoadouts.Remove(player);
        botPlayers.Remove(player);

        if (spawnedPlayers.Remove(player, out PredictedObjectID objectId))
            DeleteSpawnedObject(objectId);

        RemoveObserverRecord(player);
    }

    private void DeleteSpawnedObject(PredictedObjectID objectId)
    {
        GameObject runtimeObject = null;
        if (predictionManager != null && predictionManager.hierarchy != null)
        {
            predictionManager.hierarchy.TryGetGameObject(objectId, out runtimeObject);
            predictionManager.hierarchy.Delete(objectId);
        }

        if (runtimeObject == null || runtimeObject == gameObject || runtimeObject == templateCar?.gameObject)
            return;

        runtimeObject.SetActive(false);
        Destroy(runtimeObject);
    }

    private static bool IsMeaningfulInput(CarControlFrame frame)
    {
        return Mathf.Abs(frame.Motor) > 0.05f ||
               Mathf.Abs(frame.Steer) > 0.05f ||
               frame.Brake ||
               frame.Handbrake ||
               frame.Nitro;
    }

    private void TryPublishLocalLoadout()
    {
        if (localLoadoutPublished || playersManager == null || !playersManager.localPlayerId.HasValue)
            return;

        if (!PlayerCarSelection.TryGetPayload(out PlayerCarSelectionPayload payload) || payload == null)
            return;

        PurrVehicleLoadoutMessage message = PurrVehicleLoadoutMessage.FromPayload(payload);
        playersManager.SendToServer(message, Channel.ReliableOrdered);
        localLoadoutPublished = true;
        Debug.Log(
            $"PurrVehicleSceneSpawner: published local loadout '{payload.loadoutName}' for player {playersManager.localPlayerId.Value}.",
            this);
    }

    private void OnLoadoutMessage(PlayerID player, PurrVehicleLoadoutMessage message, bool asServer)
    {
        if (!asServer)
            return;

        PlayerCarSelectionPayload payload = message.ToPayload();
        if (payload == null)
            return;

        playerLoadouts[player] = payload;
        UpdateObserverLoadout(player, payload);
        Debug.Log($"PurrVehicleSceneSpawner: received loadout '{payload.loadoutName}' for player {player}.", this);

        if (!player.isBot && !player.isServer)
            MirrorHumanLoadoutToBots(payload);

        if (!spawnedPlayers.TryGetValue(player, out PredictedObjectID objectId))
            return;

        if (!predictionManager.hierarchy.TryGetGameObject(objectId, out GameObject instance) || instance == null)
            return;

        if (!instance.TryGetComponent(out PlayerCar spawnedCar))
            return;

        if (TryApplyLoadout(player, spawnedCar, instance))
            RegroundSpawnedCar(spawnedCar, instance);
    }

    private bool TryApplyLoadout(PlayerID player, PlayerCar spawnedCar, GameObject instance)
    {
        if (spawnedCar == null || !TryResolveLoadoutPayload(player, out PlayerCarSelectionPayload payload) || payload == null)
            return false;

        if (player.isBot)
        {
            PlayerCarSelectionPayload botPayload = ClonePayload(payload);
            if (botPayload != null)
            {
                payload = botPayload;
                playerLoadouts[player] = botPayload;
            }
        }

        CarLoadoutConfig loadout = PlayerCarLoadoutUtility.ApplySelectedLoadout(spawnedCar, payload);
        if (spawnedCar.DamageController != null)
            spawnedCar.DamageController.ResetDamageState(notifyNetwork: false);
        if (instance.TryGetComponent(out SafePredictedTransform predictedTransform))
            PurrVehicleGraphicsBindingUtility.RefreshGraphicsBinding(instance.transform, predictedTransform);
        UpdateObserverLoadout(player, payload);
        Debug.Log(
            $"PurrVehicleSceneSpawner: applied loadout '{(loadout != null ? loadout.name : payload.loadoutName)}' to player {player}.",
            instance);
        return true;
    }

    private void RegroundSpawnedCar(PlayerCar spawnedCar, GameObject instance)
    {
        if (spawnedCar == null || instance == null)
            return;

        Vector3 groundedSpawnPosition = VehicleSpawnUtility.ResolveGroundedSpawnPosition(
            spawnedCar,
            instance.transform.position,
            spawnLift,
            groundProbeHeight,
            groundProbeDistance,
            instance.transform);
        instance.transform.SetPositionAndRotation(groundedSpawnPosition, instance.transform.rotation);
    }

    private bool TryResolveLoadoutPayload(PlayerID player, out PlayerCarSelectionPayload payload)
    {
        if (playerLoadouts.TryGetValue(player, out payload) && payload != null)
            return true;

        if (!player.isBot)
        {
            payload = null;
            return false;
        }

        foreach (KeyValuePair<PlayerID, PlayerCarSelectionPayload> pair in playerLoadouts)
        {
            if (pair.Key.isBot || pair.Key.isServer || pair.Value == null)
                continue;

            payload = pair.Value;
            return true;
        }

        payload = null;
        return false;
    }

    private void MirrorHumanLoadoutToBots(PlayerCarSelectionPayload payload)
    {
        if (payload == null || botPlayers.Count == 0)
            return;

        for (int i = 0; i < botPlayers.Count; i++)
        {
            PlayerID bot = botPlayers[i];
            PlayerCarSelectionPayload botPayload = ClonePayload(payload);
            if (botPayload == null)
                continue;

            playerLoadouts[bot] = botPayload;
            UpdateObserverLoadout(bot, botPayload);

            if (!spawnedPlayers.TryGetValue(bot, out PredictedObjectID objectId))
                continue;

            if (predictionManager == null || predictionManager.hierarchy == null)
                continue;

            if (!predictionManager.hierarchy.TryGetGameObject(objectId, out GameObject instance) || instance == null)
                continue;

            if (!instance.TryGetComponent(out PlayerCar spawnedCar))
                continue;

            if (TryApplyLoadout(bot, spawnedCar, instance))
                RegroundSpawnedCar(spawnedCar, instance);
        }
    }

    private int ComputeSpawnSlot(PlayerID player)
    {
        int humanCount = 0;
        int botCount = 0;
        foreach (PlayerID spawnedPlayer in spawnedPlayers.Keys)
        {
            if (spawnedPlayer.isBot)
                botCount += 1;
            else
                humanCount += 1;
        }

        return player.isBot ? humanCount + botCount : humanCount;
    }

    private static int CompareSpawnPriority(PlayerID a, PlayerID b)
    {
        if (a.isBot != b.isBot)
            return a.isBot ? 1 : -1;

        return a.id.value.CompareTo(b.id.value);
    }

    public PurrVehicleSpawnerObserverSnapshot CaptureObserverState()
    {
        ResolveReferences();
        float now = Time.unscaledTime;
        float secondsSinceLastMeaningfulInput = soloSessionActive && lastMeaningfulHumanInputAt >= 0.0f
            ? Mathf.Max(0.0f, now - lastMeaningfulHumanInputAt)
            : -1.0f;
        float secondsUntilIdleClose = soloSessionActive && lastMeaningfulHumanInputAt >= 0.0f
            ? Mathf.Max(0.0f, soloSessionIdleTimeoutSeconds - secondsSinceLastMeaningfulInput)
            : -1.0f;

        PurrVehicleSpawnerObserverSnapshot snapshot = new PurrVehicleSpawnerObserverSnapshot
        {
            sceneName = gameObject.scene.name,
            activeSceneId = hasSceneId ? activeSceneId.ToString() : string.Empty,
            templateCarName = templateCar != null ? templateCar.name : string.Empty,
            soloBotTarget = soloBotCount,
            queuedPlayers = queuedPlayers.Count,
            spawnedPlayers = spawnedPlayers.Count,
            trackedBotPlayers = botPlayers.Count,
            pendingBotCreates = pendingBotCreates,
            hasSceneId = hasSceneId,
            predictionManagerReady = predictionManager != null,
            predictionManagerSpawned = predictionManager != null && predictionManager.isSpawned,
            hierarchyReady = predictionManager != null && predictionManager.hierarchy != null,
            scenePlayersReady = scenePlayers != null,
            playersManagerReady = playersManager != null,
            isServerSpawner = isServerSpawner,
            isClientPublisher = isClientPublisher,
            localLoadoutPublished = localLoadoutPublished,
            transientSoloCleanupEnabled = autoCloseTransientSoloSession && soloBotCount > 0,
            soloIdleTimeoutSeconds = soloSessionIdleTimeoutSeconds,
            soloLifecyclePollIntervalSeconds = soloSessionLifecyclePollIntervalSeconds,
            soloSessionActive = soloSessionActive,
            soloSessionHumanPlayerId = soloSessionHumanPlayerId ?? string.Empty,
            soloSessionActiveForSeconds = soloSessionActive && soloSessionActivatedAt >= 0.0f
                ? Mathf.Max(0.0f, now - soloSessionActivatedAt)
                : 0.0f,
            secondsSinceLastHumanSeen = soloSessionActive && lastHumanSeenAt >= 0.0f
                ? Mathf.Max(0.0f, now - lastHumanSeenAt)
                : -1.0f,
            secondsSinceLastMeaningfulInput = secondsSinceLastMeaningfulInput,
            secondsUntilIdleClose = secondsUntilIdleClose,
            soloSessionStatus = soloSessionStatus ?? string.Empty,
            lastSoloSessionCloseReason = lastSoloSessionCloseReason ?? string.Empty,
            secondsSinceLastSoloSessionClose = lastSoloSessionClosedAt >= 0.0f
                ? Mathf.Max(0.0f, now - lastSoloSessionClosedAt)
                : -1.0f,
            networkClientState = activeManager != null ? activeManager.clientState.ToString() : string.Empty,
            networkServerState = activeManager != null ? activeManager.serverState.ToString() : string.Empty,
            lastWaitReason = lastWaitReason ?? string.Empty
        };

        foreach (PurrVehicleSpawnerPlayerObserverRecord record in observerPlayerRecords.Values)
        {
            if (record == null)
                continue;

            snapshot.players.Add(CloneObserverRecord(record));
        }

        snapshot.players.Sort(CompareObserverRecords);
        return snapshot;
    }

    private static int CompareObserverRecords(PurrVehicleSpawnerPlayerObserverRecord a, PurrVehicleSpawnerPlayerObserverRecord b)
    {
        if (a == null && b == null)
            return 0;
        if (a == null)
            return 1;
        if (b == null)
            return -1;
        if (a.isBot != b.isBot)
            return a.isBot ? 1 : -1;
        return string.CompareOrdinal(a.playerId, b.playerId);
    }

    private PurrVehicleSpawnerPlayerObserverRecord GetOrCreateObserverRecord(PlayerID player)
    {
        string playerId = player.ToString();
        if (!observerPlayerRecords.TryGetValue(playerId, out PurrVehicleSpawnerPlayerObserverRecord record) || record == null)
        {
            record = new PurrVehicleSpawnerPlayerObserverRecord
            {
                playerId = playerId,
                isBot = player.isBot,
                spawnPointId = string.Empty,
                lastSpawnFailureReason = string.Empty
            };
            observerPlayerRecords[playerId] = record;
        }

        record.isBot = player.isBot;
        return record;
    }

    private void RemoveObserverRecord(PlayerID player)
    {
        observerPlayerRecords.Remove(player.ToString());
    }

    private void SetObserverSpawnFailure(PlayerID player, string reason)
    {
        PurrVehicleSpawnerPlayerObserverRecord record = GetOrCreateObserverRecord(player);
        record.queued = true;
        record.spawned = false;
        record.lastSpawnFailureReason = reason ?? string.Empty;
    }

    private void UpdateObserverSpawned(PlayerID player, Vector3 spawnPosition, Vector3 spawnRotationEuler, int spawnSlot)
    {
        PurrVehicleSpawnerPlayerObserverRecord record = GetOrCreateObserverRecord(player);
        record.queued = false;
        record.spawned = true;
        record.spawnSlot = spawnSlot;
        record.spawnPointId = $"purr_slot_{spawnSlot}";
        record.spawnPosition = spawnPosition;
        record.spawnRotationEuler = spawnRotationEuler;
        record.lastSpawnFailureReason = string.Empty;
        if (playerLoadouts.TryGetValue(player, out PlayerCarSelectionPayload payload) && payload != null)
            record.loadout = ClonePayload(payload);
    }

    private void UpdateObserverLoadout(PlayerID player, PlayerCarSelectionPayload payload)
    {
        PurrVehicleSpawnerPlayerObserverRecord record = GetOrCreateObserverRecord(player);
        record.loadout = ClonePayload(payload);
    }

    private static PurrVehicleSpawnerPlayerObserverRecord CloneObserverRecord(PurrVehicleSpawnerPlayerObserverRecord source)
    {
        if (source == null)
            return null;

        return new PurrVehicleSpawnerPlayerObserverRecord
        {
            playerId = source.playerId,
            isBot = source.isBot,
            queued = source.queued,
            spawned = source.spawned,
            spawnSlot = source.spawnSlot,
            spawnPointId = source.spawnPointId,
            spawnPosition = source.spawnPosition,
            spawnRotationEuler = source.spawnRotationEuler,
            lastSpawnFailureReason = source.lastSpawnFailureReason,
            loadout = ClonePayload(source.loadout)
        };
    }

    private static PlayerCarSelectionPayload ClonePayload(PlayerCarSelectionPayload payload)
    {
        if (payload == null)
            return null;

        string json = JsonUtility.ToJson(payload);
        return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<PlayerCarSelectionPayload>(json);
    }

    private static bool ReadBoolEnvironmentVariable(string name, bool fallback)
    {
        string raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "on":
                return true;
            case "0":
            case "false":
            case "no":
            case "off":
                return false;
            default:
                return fallback;
        }
    }

    private static float ReadFloatEnvironmentVariable(string name, float fallback, float min, float max)
    {
        string raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            return fallback;

        return Mathf.Clamp(parsed, min, max);
    }
}
