using PurrNet;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerCar))]
public sealed class PurrVehiclePlayerIdentity : PlayerIdentity<PurrVehiclePlayerIdentity>
{
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private FollowCarCamera followCamera;
    [SerializeField] private string graphicsRootName = "Body";
    [SerializeField] private SyncVar<string> loadoutJson = new SyncVar<string>(string.Empty, ownerAuth: true);

    private bool loadoutSubscribed;
    private bool localLoadoutPublished;

    private void Awake()
    {
        ResolveReferences();
        SubscribeLoadout();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    protected override void OnSpawned()
    {
        base.OnSpawned();
        ResolveReferences();
        Debug.Log($"PurrVehiclePlayerIdentity: spawned '{name}' owner={owner} isOwner={isOwner} isServer={isServer}", this);
        RefreshOwnershipBindings();
    }

    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        base.OnOwnerChanged(oldOwner, newOwner, asServer);
        ResolveReferences();
        Debug.Log($"PurrVehiclePlayerIdentity: owner changed '{name}' {oldOwner} -> {newOwner} asServer={asServer} isOwner={isOwner}", this);

        if (!gameObject.activeInHierarchy)
            return;

        RefreshOwnershipBindings();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (loadoutSubscribed)
        {
            loadoutJson.onChanged -= HandleLoadoutJsonChanged;
            loadoutSubscribed = false;
        }
    }

    private void SubscribeLoadout()
    {
        if (loadoutSubscribed)
            return;

        loadoutJson.onChanged += HandleLoadoutJsonChanged;
        loadoutSubscribed = true;
    }

    private void HandleLoadoutJsonChanged(string json)
    {
        if (playerCar == null || string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            PlayerCarSelectionPayload payload = JsonUtility.FromJson<PlayerCarSelectionPayload>(json);
            if (payload != null)
                PlayerCarLoadoutUtility.ApplySelectedLoadout(playerCar, payload);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("PurrVehiclePlayerIdentity: failed to apply loadout sync. " + ex.Message, this);
        }
    }

    private void PublishLocalLoadoutIfNeeded()
    {
        if (!isOwner || localLoadoutPublished)
            return;

        if (!PlayerCarSelection.TryGetPayload(out PlayerCarSelectionPayload payload) || payload == null)
            return;

        string serialized = JsonUtility.ToJson(BuildNetworkLoadoutPayload(payload));
        if (string.IsNullOrWhiteSpace(serialized))
            return;

        loadoutJson.value = serialized;
        loadoutJson.FlushImmediately();
        localLoadoutPublished = true;
        Debug.Log($"PurrVehiclePlayerIdentity: published compact loadout ({serialized.Length} chars) for '{name}'.", this);
        HandleLoadoutJsonChanged(serialized);
    }

    private static PlayerCarSelectionPayload BuildNetworkLoadoutPayload(PlayerCarSelectionPayload payload)
    {
        if (payload == null)
            return null;

        return new PlayerCarSelectionPayload
        {
            version = payload.version,
            loadoutName = payload.loadoutName,
            bodySetOptionIndex = payload.bodySetOptionIndex,
            engineIndex = payload.engineIndex,
            suspensionIndex = payload.suspensionIndex,
            paintIndex = payload.paintIndex,
            hasPaint = payload.hasPaint,
            paint = payload.paint
        };
    }

    private void ApplyLocalViewBindings()
    {
        if (!isOwner || !gameObject.activeInHierarchy)
            return;

        if (followCamera == null)
            followCamera = FindFirstObjectByType<FollowCarCamera>();

        if (followCamera == null)
            return;

        Transform target = playerCar != null ? playerCar.transform : transform;

        if (target != null)
            followCamera.SetTarget(target);
    }

    private void RefreshOwnershipBindings()
    {
        ApplyNetworkVehicleEntity();
        ApplyLocalViewBindings();
        PublishLocalLoadoutIfNeeded();
    }

    private void ApplyNetworkVehicleEntity()
    {
        NetworkVehicleEntity entity = GetComponent<NetworkVehicleEntity>();
        if (entity == null)
            entity = gameObject.AddComponent<NetworkVehicleEntity>();

        string playerId = owner.HasValue ? owner.Value.ToString() : "server";
        entity.Configure(playerId, isOwner);
    }

    private Transform ResolveGraphicsTarget()
    {
        if (string.IsNullOrWhiteSpace(graphicsRootName))
            return playerCar != null ? playerCar.transform : transform;

        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == graphicsRootName)
                return all[i];
        }

        return playerCar != null ? playerCar.transform : transform;
    }

    private void ResolveReferences()
    {
        if (playerCar == null)
            playerCar = GetComponent<PlayerCar>();
        if (followCamera == null)
            followCamera = FindFirstObjectByType<FollowCarCamera>();
    }
}
