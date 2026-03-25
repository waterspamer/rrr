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

[DefaultExecutionOrder(370)]
[DisallowMultipleComponent]
public sealed class PurrVehicleDamageSync : PurrMonoBehaviour
{
    private const float LocalCollisionDedupeWindow = 0.35f;
    private const float ServerCollisionDedupeWindow = 0.05f;

    [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 0.35f;

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
            ApplySnapshotsToSpawnedEntities();
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
            UnsubscribeAll();
            isServerAuthority = false;
        }
        else
        {
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
            if (entity == null || string.IsNullOrWhiteSpace(entity.PlayerId))
                continue;

            int key = damageController.GetInstanceID();
            activeKeys.Add(key);

            if (!serverSubscriptions.ContainsKey(key) || force)
            {
                if (serverSubscriptions.TryGetValue(key, out DamageSubscription existing) && existing?.damageController != null)
                {
                    existing.damageController.DamageMapChanged -= existing.damageHandler;
                    existing.damageController.NetworkVehicleCollisionDetected -= existing.collisionHandler;
                }

                DamageSubscription subscription = new DamageSubscription
                {
                    damageController = damageController,
                    entity = entity
                };
                subscription.damageHandler = snapshot => HandleServerDamageChanged(subscription, snapshot);
                subscription.collisionHandler = report => HandleServerCollision(subscription, report);
                damageController.DamageMapChanged += subscription.damageHandler;
                damageController.NetworkVehicleCollisionDetected += subscription.collisionHandler;
                serverSubscriptions[key] = subscription;
            }

            CaptureCurrentSnapshot(entity.PlayerId, damageController);
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
            if (entity == null || string.IsNullOrWhiteSpace(entity.PlayerId))
                continue;

            CaptureCurrentSnapshot(entity.PlayerId, damageController);
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
        if (subscription == null || subscription.entity == null || string.IsNullOrWhiteSpace(subscription.entity.PlayerId))
            return;
        if (snapshot == null || snapshot.rawBytes == null || snapshot.rawBytes.Length == 0)
            return;

        string playerId = subscription.entity.PlayerId;
        CarDamageNetworkSnapshot cloned = CloneSnapshot(snapshot);
        latestSnapshotsByPlayer[playerId] = cloned;
        BroadcastDamageSnapshot(playerId, cloned);
    }

    private void HandleServerCollision(DamageSubscription subscription, NetworkVehicleCollisionReport report)
    {
        if (playersManager == null || subscription == null || subscription.entity == null || report == null)
            return;
        if (string.IsNullOrWhiteSpace(subscription.entity.PlayerId) || string.IsNullOrWhiteSpace(report.otherPlayerId))
            return;

        string pairKey = BuildCollisionPairKey(subscription.entity.PlayerId, report.otherPlayerId);
        if (recentServerCollisions.TryGetValue(pairKey, out float recentTime) &&
            Time.unscaledTime - recentTime <= ServerCollisionDedupeWindow)
        {
            return;
        }

        recentServerCollisions[pairKey] = Time.unscaledTime;
        playersManager.SendToAll(new PurrVehicleCollisionEventMessage
        {
            primaryPlayerId = subscription.entity.PlayerId,
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
        if (asServer || string.IsNullOrWhiteSpace(message.playerId))
            return;
        if (!message.TryCreateSnapshot(out CarDamageNetworkSnapshot snapshot) || snapshot == null)
            return;

        if (latestSnapshotsByPlayer.TryGetValue(message.playerId, out CarDamageNetworkSnapshot existing) &&
            existing != null &&
            existing.revision >= snapshot.revision)
        {
            return;
        }

        latestSnapshotsByPlayer[message.playerId] = snapshot;
        ApplySnapshotToEntity(message.playerId, snapshot);
    }

    private void OnCollisionMessage(PlayerID player, PurrVehicleCollisionEventMessage message, bool asServer)
    {
        if (asServer || playersManager == null || !playersManager.localPlayerId.HasValue)
            return;

        string localPlayerId = playersManager.localPlayerId.Value.ToString();
        if (!string.Equals(message.primaryPlayerId, localPlayerId, StringComparison.Ordinal))
            return;

        string pairKey = BuildCollisionPairKey(message.primaryPlayerId, message.secondaryPlayerId);
        if (recentLocalCollisions.TryGetValue(pairKey, out float recentTime) &&
            Time.unscaledTime - recentTime <= LocalCollisionDedupeWindow)
        {
            return;
        }

        if (!TryGetLocalDamageController(localPlayerId, out CarDamageController damageController) || damageController == null)
            return;

        if (damageController.ApplySyntheticCollisionDamage(
                message.worldPoint,
                message.worldNormal,
                message.relativeVelocity,
                message.impulseMagnitude,
                $"network collision {message.primaryPlayerId}->{message.secondaryPlayerId}",
                notifyNetwork: true))
        {
            recentLocalCollisions[pairKey] = Time.unscaledTime;
        }
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
            if (entity == null || !string.Equals(entity.PlayerId, playerId, StringComparison.Ordinal))
                continue;

            CarDamageController damageController = entity.GetComponent<CarDamageController>();
            if (damageController == null)
                damageController = entity.GetComponentInParent<CarDamageController>();
            if (damageController == null)
                continue;
            if (damageController.DamageRevision >= snapshot.revision)
                continue;

            damageController.ApplyNetworkDamageSnapshot(snapshot);
        }
    }

    private void UnsubscribeAll()
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

    private static bool TryGetLocalDamageController(string localPlayerId, out CarDamageController damageController)
    {
        NetworkVehicleEntity[] entities =
            FindObjectsByType<NetworkVehicleEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < entities.Length; i++)
        {
            NetworkVehicleEntity entity = entities[i];
            if (entity == null)
                continue;
            if (!entity.IsLocalPlayer && !string.Equals(entity.PlayerId, localPlayerId, StringComparison.Ordinal))
                continue;

            damageController = entity.GetComponent<CarDamageController>();
            if (damageController == null)
                damageController = entity.GetComponentInParent<CarDamageController>();
            if (damageController != null)
                return true;
        }

        damageController = null;
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
