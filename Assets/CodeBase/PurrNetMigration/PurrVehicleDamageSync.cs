using System;
using System.Collections.Generic;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

[Serializable]
public struct PurrVehicleDamageStateMessage : IPackedAuto
{
    public string playerId;
    public int revision;
    public int width;
    public int height;
    public string rawBytesBase64;
    public bool hasImpactPoint;
    public Vector3 worldPoint;
    public bool hasImpactNormal;
    public Vector3 worldNormal;

    public static PurrVehicleDamageStateMessage FromSnapshot(string playerId, CarDamageNetworkSnapshot snapshot)
    {
        return new PurrVehicleDamageStateMessage
        {
            playerId = playerId ?? string.Empty,
            revision = snapshot != null ? snapshot.revision : 0,
            width = snapshot != null ? snapshot.width : 0,
            height = snapshot != null ? snapshot.height : 0,
            rawBytesBase64 = snapshot != null && snapshot.rawBytes != null && snapshot.rawBytes.Length > 0
                ? Convert.ToBase64String(snapshot.rawBytes)
                : string.Empty,
            hasImpactPoint = snapshot != null && snapshot.hasImpactPoint,
            worldPoint = snapshot != null ? snapshot.worldPoint : Vector3.zero,
            hasImpactNormal = snapshot != null && snapshot.hasImpactNormal,
            worldNormal = snapshot != null ? snapshot.worldNormal : Vector3.up
        };
    }

    public bool TryCreateSnapshot(out CarDamageNetworkSnapshot snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(rawBytesBase64) || width <= 0 || height <= 0)
            return false;

        try
        {
            snapshot = new CarDamageNetworkSnapshot
            {
                revision = revision,
                width = width,
                height = height,
                rawBytes = Convert.FromBase64String(rawBytesBase64),
                hasImpactPoint = hasImpactPoint,
                worldPoint = worldPoint,
                hasImpactNormal = hasImpactNormal,
                worldNormal = worldNormal
            };
            return snapshot.rawBytes != null && snapshot.rawBytes.Length > 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PurrVehicleDamageStateMessage: failed to decode damage snapshot for '{playerId}'. {ex.Message}");
            return false;
        }
    }
}

[Serializable]
public struct PurrVehicleCollisionEventMessage : IPackedAuto
{
    public string primaryPlayerId;
    public string secondaryPlayerId;
    public Vector3 worldPoint;
    public Vector3 worldNormal;
    public Vector3 relativeVelocity;
    public Vector3 impulseVector;
    public float impulseMagnitude;
}

[Serializable]
public struct PurrVehicleDamageConfigMessage : IPackedAuto
{
    public string configJson;

    public static PurrVehicleDamageConfigMessage FromConfig(CarDamageRuntimeTuning config)
    {
        return new PurrVehicleDamageConfigMessage
        {
            configJson = config != null ? JsonUtility.ToJson(config) : string.Empty
        };
    }

    public CarDamageRuntimeTuning ToConfig()
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            CarDamageRuntimeTuning config = JsonUtility.FromJson<CarDamageRuntimeTuning>(configJson);
            config?.Validate();
            return config;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PurrVehicleDamageConfigMessage: failed to deserialize config. {ex.Message}");
            return null;
        }
    }
}

[DefaultExecutionOrder(360)]
[DisallowMultipleComponent]
public sealed class PurrVehicleDamageConfigSync : PurrMonoBehaviour
{
    [SerializeField] private PlayerCar templateCar;
    [SerializeField, Min(0.05f)] private float refreshIntervalSeconds = 0.25f;

    private readonly Dictionary<int, int> appliedRevisionsByController = new Dictionary<int, int>();

    private NetworkManager activeManager;
    private PlayersManager playersManager;
    private bool isServerAuthority;
    private float nextRefreshAt;
    private CarDamageRuntimeTuning currentConfig;

    public void Configure(PlayerCar template)
    {
        templateCar = template;
        EnsureDefaultConfig();
    }

    public bool TryGetCurrentConfig(out CarDamageRuntimeTuning config)
    {
        EnsureDefaultConfig();
        if (currentConfig == null)
        {
            config = null;
            return false;
        }

        config = currentConfig.Clone();
        return config != null;
    }

    public bool TryUpdateServerConfig(CarDamageRuntimeTuning requested, string source = "observer_admin")
    {
        if (!isServerAuthority || requested == null)
            return false;

        CarDamageRuntimeTuning next = requested.Clone();
        if (next == null)
            return false;

        int previousRevision = currentConfig != null ? currentConfig.revision : 0;
        next.revision = previousRevision + 1;
        next.updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        next.source = string.IsNullOrWhiteSpace(source) ? "observer_admin" : source.Trim();
        next.Validate();

        currentConfig = next;
        ApplyConfigToActiveDamageControllers(force: true);
        BroadcastCurrentConfig();
        return true;
    }

    private void Update()
    {
        ResolveReferences();
        EnsureDefaultConfig();

        if (Time.unscaledTime < nextRefreshAt)
            return;

        nextRefreshAt = Time.unscaledTime + refreshIntervalSeconds;
        ApplyConfigToActiveDamageControllers(force: false);
    }

    public override void Subscribe(NetworkManager manager, bool asServer)
    {
        activeManager = manager;
        ResolveReferences();

        if (playersManager != null)
            playersManager.Subscribe<PurrVehicleDamageConfigMessage>(OnConfigMessage);

        if (asServer)
        {
            isServerAuthority = true;
            manager.onPlayerLoadedScene += OnPlayerLoadedScene;
            EnsureDefaultConfig();
            ApplyConfigToActiveDamageControllers(force: true);
            BroadcastCurrentConfig();
            return;
        }

    }

    public override void Unsubscribe(NetworkManager manager, bool asServer)
    {
        if (playersManager != null)
            playersManager.Unsubscribe<PurrVehicleDamageConfigMessage>(OnConfigMessage);

        if (asServer)
        {
            manager.onPlayerLoadedScene -= OnPlayerLoadedScene;
            isServerAuthority = false;
        }

        activeManager = null;
    }

    private void ResolveReferences()
    {
        if (activeManager == null)
            return;

        if (playersManager == null)
        {
            if (!activeManager.TryGetModule(out playersManager, true))
                activeManager.TryGetModule(out playersManager, false);
        }
    }

    private void EnsureDefaultConfig()
    {
        if (currentConfig != null)
            return;

        currentConfig = CaptureDefaultConfig();
        if (currentConfig == null)
            return;

        currentConfig.revision = Mathf.Max(1, currentConfig.revision);
        if (currentConfig.updatedAtUnixMs <= 0)
            currentConfig.updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (string.IsNullOrWhiteSpace(currentConfig.source))
            currentConfig.source = "car_config";
        currentConfig.Validate();
    }

    private CarDamageRuntimeTuning CaptureDefaultConfig()
    {
        if (templateCar == null)
            templateCar = FindFirstObjectByType<PlayerCar>(FindObjectsInactive.Include);

        if (templateCar != null)
        {
            if (templateCar.Config != null && templateCar.Config.Damage != null)
                return CarDamageRuntimeTuning.FromSettings(templateCar.Config.Damage);

            if (templateCar.DamageController != null)
                return templateCar.DamageController.CaptureRuntimeTuning();
        }

        CarDamageController firstController = FindFirstObjectByType<CarDamageController>(FindObjectsInactive.Include);
        return firstController != null ? firstController.CaptureRuntimeTuning() : null;
    }

    private void ApplyConfigToActiveDamageControllers(bool force)
    {
        if (currentConfig == null)
            return;

        CarDamageController[] controllers =
            FindObjectsByType<CarDamageController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HashSet<int> activeKeys = new HashSet<int>();

        for (int i = 0; i < controllers.Length; i++)
        {
            CarDamageController controller = controllers[i];
            if (controller == null || !controller.gameObject.activeInHierarchy)
                continue;

            int key = controller.GetInstanceID();
            activeKeys.Add(key);

            if (!force &&
                appliedRevisionsByController.TryGetValue(key, out int appliedRevision) &&
                appliedRevision == currentConfig.revision)
            {
                CarDamageRuntimeTuning activeTuning = controller.CaptureRuntimeTuning();
                if (activeTuning != null && activeTuning.IsEquivalentTo(currentConfig))
                    continue;
            }

            controller.ApplyRuntimeTuning(currentConfig);
            appliedRevisionsByController[key] = currentConfig.revision;
        }

        if (appliedRevisionsByController.Count == 0)
            return;

        List<int> staleKeys = new List<int>();
        foreach (KeyValuePair<int, int> pair in appliedRevisionsByController)
        {
            if (!activeKeys.Contains(pair.Key))
                staleKeys.Add(pair.Key);
        }

        for (int i = 0; i < staleKeys.Count; i++)
            appliedRevisionsByController.Remove(staleKeys[i]);
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
    {
        if (!asServer)
            return;

        SendCurrentConfig(player);
    }

    private void OnConfigMessage(PlayerID player, PurrVehicleDamageConfigMessage message, bool asServer)
    {
        if (asServer)
            return;

        CarDamageRuntimeTuning config = message.ToConfig();
        if (config == null)
            return;

        if (currentConfig != null && currentConfig.revision >= config.revision)
            return;

        currentConfig = config;
        ApplyConfigToActiveDamageControllers(force: true);
    }

    private void BroadcastCurrentConfig()
    {
        if (playersManager == null || currentConfig == null || !isServerAuthority)
            return;

        playersManager.SendToAll(PurrVehicleDamageConfigMessage.FromConfig(currentConfig), Channel.ReliableOrdered);
    }

    private void SendCurrentConfig(PlayerID player)
    {
        if (playersManager == null || currentConfig == null || !isServerAuthority)
            return;

        playersManager.Send(player, PurrVehicleDamageConfigMessage.FromConfig(currentConfig), Channel.ReliableOrdered);
    }
}

[DefaultExecutionOrder(370)]
[DisallowMultipleComponent]
public sealed class PurrVehicleDamageSync : PurrMonoBehaviour
{
    private const float ServerCollisionDedupeWindow = 0.05f;

    [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 0.35f;

    private readonly Dictionary<int, DamageSubscription> clientSubscriptions = new Dictionary<int, DamageSubscription>();
    private readonly Dictionary<int, DamageSubscription> serverSubscriptions = new Dictionary<int, DamageSubscription>();
    private readonly Dictionary<string, CarDamageNetworkSnapshot> latestSnapshotsByPlayer =
        new Dictionary<string, CarDamageNetworkSnapshot>(StringComparer.Ordinal);
    private readonly Dictionary<string, float> recentServerCollisions =
        new Dictionary<string, float>(StringComparer.Ordinal);
    private readonly Dictionary<string, float> recentLocalCollisions =
        new Dictionary<string, float>(StringComparer.Ordinal);

    private NetworkManager activeManager;
    private PlayersManager playersManager;
    private bool isServerAuthority;
    private bool isClientReplica;
    private float nextRefreshAt;

    private sealed class DamageSubscription
    {
        public CarDamageController damageController;
        public NetworkVehicleEntity entity;
        public string playerId;
        public Action<CarDamageNetworkSnapshot> damageHandler;
        public Action<NetworkVehicleCollisionReport> collisionHandler;
    }

    private void Update()
    {
        ResolveReferences();

        if (Time.unscaledTime < nextRefreshAt)
            return;

        nextRefreshAt = Time.unscaledTime + refreshIntervalSeconds;
        if (isServerAuthority)
            RefreshServerSubscriptions();
        if (isClientReplica)
        {
            RefreshClientSubscriptions();
            ApplySnapshotsToSpawnedEntities();
        }
    }

    public override void Subscribe(NetworkManager manager, bool asServer)
    {
        activeManager = manager;
        ResolveReferences();

        if (playersManager != null)
        {
            playersManager.Subscribe<PurrVehicleDamageStateMessage>(OnDamageMessage);
            playersManager.Subscribe<PurrVehicleCollisionEventMessage>(OnCollisionMessage);
        }

        if (asServer)
        {
            isServerAuthority = true;
            manager.onPlayerLoadedScene += OnPlayerLoadedScene;
            RefreshServerSubscriptions(force: true);
            CaptureCurrentSnapshots();
            return;
        }

        isClientReplica = true;
    }

    public override void Unsubscribe(NetworkManager manager, bool asServer)
    {
        if (playersManager != null)
        {
            playersManager.Unsubscribe<PurrVehicleDamageStateMessage>(OnDamageMessage);
            playersManager.Unsubscribe<PurrVehicleCollisionEventMessage>(OnCollisionMessage);
        }

        if (asServer)
        {
            manager.onPlayerLoadedScene -= OnPlayerLoadedScene;
            UnsubscribeServer();
            isServerAuthority = false;
        }
        else
        {
            UnsubscribeClient();
            isClientReplica = false;
        }

        activeManager = null;
    }

    private void ResolveReferences()
    {
        if (activeManager == null)
            return;

        if (playersManager == null)
        {
            if (!activeManager.TryGetModule(out playersManager, true))
                activeManager.TryGetModule(out playersManager, false);
        }
    }

    private void RefreshServerSubscriptions(bool force = false)
    {
        if (!isServerAuthority)
            return;

        CarDamageController[] controllers =
            FindObjectsByType<CarDamageController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HashSet<int> activeKeys = new HashSet<int>();

        for (int i = 0; i < controllers.Length; i++)
        {
            CarDamageController damageController = controllers[i];
            if (damageController == null || !damageController.gameObject.activeInHierarchy)
                continue;

            NetworkVehicleEntity entity = damageController.GetComponent<NetworkVehicleEntity>();
            if (entity == null)
                entity = damageController.GetComponentInParent<NetworkVehicleEntity>();
            if (entity == null)
                continue;

            if (!TryResolvePlayerId(damageController, entity, out string resolvedPlayerId))
                continue;

            int key = damageController.GetInstanceID();
            activeKeys.Add(key);

            bool needsRefresh =
                !serverSubscriptions.TryGetValue(key, out DamageSubscription existing) ||
                existing == null ||
                force ||
                existing.damageController != damageController ||
                existing.entity != entity ||
                !string.Equals(existing.playerId, resolvedPlayerId, StringComparison.Ordinal);

            if (needsRefresh)
            {
                if (existing?.damageController != null)
                {
                    existing.damageController.DamageMapChanged -= existing.damageHandler;
                    existing.damageController.NetworkVehicleCollisionDetected -= existing.collisionHandler;
                }

                DamageSubscription subscription = new DamageSubscription
                {
                    damageController = damageController,
                    entity = entity,
                    playerId = resolvedPlayerId
                };
                subscription.damageHandler = snapshot => HandleServerDamageChanged(subscription, snapshot);
                subscription.collisionHandler = report => HandleServerCollision(subscription, report);
                damageController.DamageMapChanged += subscription.damageHandler;
                damageController.NetworkVehicleCollisionDetected += subscription.collisionHandler;
                serverSubscriptions[key] = subscription;
            }

            CaptureCurrentSnapshot(resolvedPlayerId, damageController);
        }

        List<int> staleKeys = new List<int>();
        foreach (KeyValuePair<int, DamageSubscription> pair in serverSubscriptions)
        {
            if (!activeKeys.Contains(pair.Key))
                staleKeys.Add(pair.Key);
        }

        for (int i = 0; i < staleKeys.Count; i++)
        {
            if (!serverSubscriptions.TryGetValue(staleKeys[i], out DamageSubscription stale))
                continue;

            if (stale.damageController != null)
            {
                stale.damageController.DamageMapChanged -= stale.damageHandler;
                stale.damageController.NetworkVehicleCollisionDetected -= stale.collisionHandler;
            }
            serverSubscriptions.Remove(staleKeys[i]);
        }
    }

    private void RefreshClientSubscriptions(bool force = false)
    {
        if (!isClientReplica || playersManager == null || !playersManager.localPlayerId.HasValue)
            return;

        string localPlayerId = playersManager.localPlayerId.Value.ToString();
        CarDamageController[] controllers =
            FindObjectsByType<CarDamageController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HashSet<int> activeKeys = new HashSet<int>();

        for (int i = 0; i < controllers.Length; i++)
        {
            CarDamageController damageController = controllers[i];
            if (damageController == null || !damageController.gameObject.activeInHierarchy)
                continue;

            NetworkVehicleEntity entity = damageController.GetComponent<NetworkVehicleEntity>();
            if (entity == null)
                entity = damageController.GetComponentInParent<NetworkVehicleEntity>();
            if (entity == null)
                continue;
            if (!TryResolvePlayerId(damageController, entity, out string resolvedPlayerId))
                continue;
            if (!IsLocalOwnedEntity(entity, damageController, localPlayerId))
                continue;

            int key = damageController.GetInstanceID();
            activeKeys.Add(key);

            bool needsRefresh =
                !clientSubscriptions.TryGetValue(key, out DamageSubscription existing) ||
                existing == null ||
                force ||
                existing.damageController != damageController ||
                existing.entity != entity ||
                !string.Equals(existing.playerId, resolvedPlayerId, StringComparison.Ordinal);

            if (needsRefresh)
            {
                if (existing?.damageController != null)
                {
                    existing.damageController.DamageMapChanged -= existing.damageHandler;
                    existing.damageController.NetworkVehicleCollisionDetected -= existing.collisionHandler;
                }

                DamageSubscription subscription = new DamageSubscription
                {
                    damageController = damageController,
                    entity = entity,
                    playerId = resolvedPlayerId
                };
                subscription.damageHandler = snapshot => HandleClientDamageChanged(subscription, snapshot);
                subscription.collisionHandler = report => HandleClientCollision(subscription, report);
                damageController.DamageMapChanged += subscription.damageHandler;
                damageController.NetworkVehicleCollisionDetected += subscription.collisionHandler;
                clientSubscriptions[key] = subscription;
            }
        }

        List<int> staleKeys = new List<int>();
        foreach (KeyValuePair<int, DamageSubscription> pair in clientSubscriptions)
        {
            if (!activeKeys.Contains(pair.Key))
                staleKeys.Add(pair.Key);
        }

        for (int i = 0; i < staleKeys.Count; i++)
        {
            if (!clientSubscriptions.TryGetValue(staleKeys[i], out DamageSubscription stale))
                continue;

            if (stale.damageController != null)
            {
                stale.damageController.DamageMapChanged -= stale.damageHandler;
                stale.damageController.NetworkVehicleCollisionDetected -= stale.collisionHandler;
            }

            clientSubscriptions.Remove(staleKeys[i]);
        }
    }

    private void CaptureCurrentSnapshots()
    {
        CarDamageController[] controllers =
            FindObjectsByType<CarDamageController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            CarDamageController damageController = controllers[i];
            if (damageController == null || !damageController.gameObject.activeInHierarchy)
                continue;

            NetworkVehicleEntity entity = damageController.GetComponent<NetworkVehicleEntity>();
            if (entity == null)
                entity = damageController.GetComponentInParent<NetworkVehicleEntity>();
            if (entity == null)
                continue;

            if (TryResolvePlayerId(damageController, entity, out string resolvedPlayerId))
                CaptureCurrentSnapshot(resolvedPlayerId, damageController);
        }
    }

    private void CaptureCurrentSnapshot(string playerId, CarDamageController damageController)
    {
        if (string.IsNullOrWhiteSpace(playerId) || damageController == null)
            return;

        damageController.EnsureNetworkTextureReady();
        if (!damageController.TryCaptureDamageSnapshot(out CarDamageNetworkSnapshot snapshot) || snapshot == null)
            return;

        latestSnapshotsByPlayer[playerId] = CloneSnapshot(snapshot);
    }

    private void HandleServerDamageChanged(DamageSubscription subscription, CarDamageNetworkSnapshot snapshot)
    {
        if (subscription == null || subscription.entity == null || string.IsNullOrWhiteSpace(subscription.playerId))
            return;
        if (snapshot == null || snapshot.rawBytes == null || snapshot.rawBytes.Length == 0)
            return;
        if (latestSnapshotsByPlayer.TryGetValue(subscription.playerId, out CarDamageNetworkSnapshot existing) &&
            existing != null &&
            existing.revision >= snapshot.revision)
        {
            return;
        }

        string playerId = subscription.playerId;
        CarDamageNetworkSnapshot cloned = CloneSnapshot(snapshot);
        latestSnapshotsByPlayer[playerId] = cloned;
        BroadcastDamageSnapshot(playerId, cloned);
    }

    private void HandleClientDamageChanged(DamageSubscription subscription, CarDamageNetworkSnapshot snapshot)
    {
        if (playersManager == null || subscription == null || string.IsNullOrWhiteSpace(subscription.playerId))
            return;
        if (snapshot == null || snapshot.rawBytes == null || snapshot.rawBytes.Length == 0)
            return;

        CarDamageNetworkSnapshot cloned = CloneSnapshot(snapshot);
        latestSnapshotsByPlayer[subscription.playerId] = cloned;
        playersManager.SendToServer(PurrVehicleDamageStateMessage.FromSnapshot(subscription.playerId, cloned), Channel.ReliableOrdered);
    }

    private void HandleServerCollision(DamageSubscription subscription, NetworkVehicleCollisionReport report)
    {
        if (subscription == null || subscription.entity == null || report == null)
            return;
        if (string.IsNullOrWhiteSpace(subscription.playerId) || string.IsNullOrWhiteSpace(report.otherPlayerId))
            return;

        RelayCollisionToClients(new PurrVehicleCollisionEventMessage
        {
            primaryPlayerId = subscription.playerId,
            secondaryPlayerId = report.otherPlayerId,
            worldPoint = report.worldPoint,
            worldNormal = report.worldNormal,
            relativeVelocity = report.relativeVelocity,
            impulseVector = report.impulseVector,
            impulseMagnitude = report.impulseMagnitude
        });
    }

    private void HandleClientCollision(DamageSubscription subscription, NetworkVehicleCollisionReport report)
    {
        if (playersManager == null || subscription == null || string.IsNullOrWhiteSpace(subscription.playerId) || report == null)
            return;
        if (string.IsNullOrWhiteSpace(report.otherPlayerId))
            return;

        string pairKey = BuildCollisionPairKey(subscription.playerId, report.otherPlayerId);
        recentLocalCollisions[pairKey] = Time.unscaledTime;
        playersManager.SendToServer(new PurrVehicleCollisionEventMessage
        {
            primaryPlayerId = subscription.playerId,
            secondaryPlayerId = report.otherPlayerId,
            worldPoint = report.worldPoint,
            worldNormal = report.worldNormal,
            relativeVelocity = report.relativeVelocity,
            impulseVector = report.impulseVector,
            impulseMagnitude = report.impulseMagnitude
        }, Channel.ReliableOrdered);
    }

    private void BroadcastDamageSnapshot(string playerId, CarDamageNetworkSnapshot snapshot)
    {
        if (playersManager == null || string.IsNullOrWhiteSpace(playerId) || snapshot == null || snapshot.rawBytes == null || snapshot.rawBytes.Length == 0)
            return;

        playersManager.SendToAll(PurrVehicleDamageStateMessage.FromSnapshot(playerId, snapshot), Channel.ReliableOrdered);
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
    {
        if (!asServer || playersManager == null)
            return;

        foreach (KeyValuePair<string, CarDamageNetworkSnapshot> pair in latestSnapshotsByPlayer)
        {
            CarDamageNetworkSnapshot snapshot = pair.Value;
            if (snapshot == null || snapshot.rawBytes == null || snapshot.rawBytes.Length == 0)
                continue;

            playersManager.Send(player, PurrVehicleDamageStateMessage.FromSnapshot(pair.Key, snapshot), Channel.ReliableOrdered);
        }
    }

    private void OnDamageMessage(PlayerID player, PurrVehicleDamageStateMessage message, bool asServer)
    {
        if (string.IsNullOrWhiteSpace(message.playerId))
            return;
        if (!message.TryCreateSnapshot(out CarDamageNetworkSnapshot snapshot) || snapshot == null)
            return;

        if (latestSnapshotsByPlayer.TryGetValue(message.playerId, out CarDamageNetworkSnapshot existing) &&
            existing != null &&
            existing.revision >= snapshot.revision)
        {
            return;
        }

        CarDamageNetworkSnapshot cloned = CloneSnapshot(snapshot);
        latestSnapshotsByPlayer[message.playerId] = cloned;

        if (asServer)
        {
            ApplySnapshotToEntity(message.playerId, cloned);
            BroadcastDamageSnapshot(message.playerId, cloned);
            return;
        }

        ApplySnapshotToEntity(message.playerId, cloned);
    }

    private void OnCollisionMessage(PlayerID player, PurrVehicleCollisionEventMessage message, bool asServer)
    {
        if (asServer)
        {
            RelayCollisionToClients(message);
            return;
        }

        if (playersManager == null || !playersManager.localPlayerId.HasValue)
            return;

        string localPlayerId = playersManager.localPlayerId.Value.ToString();
        if (!string.Equals(message.primaryPlayerId, localPlayerId, StringComparison.Ordinal))
            return;

        string pairKey = BuildCollisionPairKey(message.primaryPlayerId, message.secondaryPlayerId);
        // The owner already applied collision damage and camera shake locally.
        // Replaying the relayed server collision causes duplicate shake and can double-apply impact damage.
        recentLocalCollisions[pairKey] = Time.unscaledTime;
    }

    private void ApplySnapshotsToSpawnedEntities()
    {
        foreach (KeyValuePair<string, CarDamageNetworkSnapshot> pair in latestSnapshotsByPlayer)
            ApplySnapshotToEntity(pair.Key, pair.Value);
    }

    private static void ApplySnapshotToEntity(string playerId, CarDamageNetworkSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(playerId) || snapshot == null)
            return;

        NetworkVehicleEntity[] entities =
            FindObjectsByType<NetworkVehicleEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < entities.Length; i++)
        {
            NetworkVehicleEntity entity = entities[i];
            if (entity == null)
                continue;

            CarDamageController damageController = entity.GetComponent<CarDamageController>();
            if (damageController == null)
                damageController = entity.GetComponentInParent<CarDamageController>();
            if (damageController == null)
                continue;
            if (!MatchesPlayerId(entity, damageController, playerId))
                continue;
            if (damageController.DamageRevision >= snapshot.revision)
                continue;

            damageController.ApplyNetworkDamageSnapshot(snapshot);
        }
    }

    private void RelayCollisionToClients(PurrVehicleCollisionEventMessage message)
    {
        if (playersManager == null || string.IsNullOrWhiteSpace(message.primaryPlayerId) || string.IsNullOrWhiteSpace(message.secondaryPlayerId))
            return;

        string pairKey = BuildCollisionPairKey(message.primaryPlayerId, message.secondaryPlayerId);
        if (recentServerCollisions.TryGetValue(pairKey, out float recentTime) &&
            Time.unscaledTime - recentTime <= ServerCollisionDedupeWindow)
        {
            return;
        }

        recentServerCollisions[pairKey] = Time.unscaledTime;
        playersManager.SendToAll(message, Channel.ReliableOrdered);
    }

    private void UnsubscribeServer()
    {
        foreach (DamageSubscription subscription in serverSubscriptions.Values)
        {
            if (subscription != null && subscription.damageController != null)
            {
                subscription.damageController.DamageMapChanged -= subscription.damageHandler;
                subscription.damageController.NetworkVehicleCollisionDetected -= subscription.collisionHandler;
            }
        }

        serverSubscriptions.Clear();
    }

    private void UnsubscribeClient()
    {
        foreach (DamageSubscription subscription in clientSubscriptions.Values)
        {
            if (subscription != null && subscription.damageController != null)
            {
                subscription.damageController.DamageMapChanged -= subscription.damageHandler;
                subscription.damageController.NetworkVehicleCollisionDetected -= subscription.collisionHandler;
            }
        }

        clientSubscriptions.Clear();
    }

    private static bool TryResolvePlayerId(CarDamageController damageController, NetworkVehicleEntity entity, out string playerId)
    {
        if (TryResolvePredictedOwnerId(damageController != null ? damageController.transform : entity != null ? entity.transform : null, out playerId))
            return true;

        if (entity != null && !string.IsNullOrWhiteSpace(entity.PlayerId))
        {
            playerId = entity.PlayerId;
            return true;
        }

        playerId = null;
        return false;
    }

    private static bool IsLocalOwnedEntity(NetworkVehicleEntity entity, CarDamageController damageController, string localPlayerId)
    {
        if (entity != null && entity.IsLocalPlayer)
            return true;

        return MatchesPlayerId(entity, damageController, localPlayerId);
    }

    private static bool MatchesPlayerId(NetworkVehicleEntity entity, CarDamageController damageController, string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return false;

        if (entity != null && string.Equals(entity.PlayerId, playerId, StringComparison.Ordinal))
            return true;

        return TryResolvePredictedOwnerId(
            damageController != null ? damageController.transform : entity != null ? entity.transform : null,
            out string resolvedPlayerId) &&
            string.Equals(resolvedPlayerId, playerId, StringComparison.Ordinal);
    }

    private static bool TryResolvePredictedOwnerId(Transform source, out string playerId)
    {
        playerId = null;
        Transform current = source;
        while (current != null)
        {
            PurrVehiclePredictedController predictedController = current.GetComponent<PurrVehiclePredictedController>();
            if (predictedController != null && predictedController.owner.HasValue)
            {
                playerId = predictedController.owner.Value.ToString();
                return !string.IsNullOrWhiteSpace(playerId);
            }

            current = current.parent;
        }

        return false;
    }

    private static string BuildCollisionPairKey(string primaryPlayerId, string secondaryPlayerId)
    {
        if (string.IsNullOrWhiteSpace(primaryPlayerId) || string.IsNullOrWhiteSpace(secondaryPlayerId))
            return string.Empty;

        return string.CompareOrdinal(primaryPlayerId, secondaryPlayerId) <= 0
            ? primaryPlayerId + "|" + secondaryPlayerId
            : secondaryPlayerId + "|" + primaryPlayerId;
    }

    private static CarDamageNetworkSnapshot CloneSnapshot(CarDamageNetworkSnapshot snapshot)
    {
        if (snapshot == null)
            return null;

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
}
