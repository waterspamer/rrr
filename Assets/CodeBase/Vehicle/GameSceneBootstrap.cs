using UnityEngine;

public class GameSceneBootstrap : MonoBehaviour
{
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private FollowCarCamera followCamera;
    [SerializeField] private MultiplayerMatchRuntime multiplayerMatchRuntime;
    [SerializeField] private bool ensureFollowCamera = true;
    [SerializeField] private bool lockCursor = true;
    private PlayerCarSelectionPayload cachedPayload;
    private bool hasCachedPayload;

    private void Awake()
    {
        if (playerCar == null)
            playerCar = FindFirstObjectByType<PlayerCar>();

        if (playerCar == null)
        {
            Debug.LogError("GameSceneBootstrap: PlayerCar not found in scene. Add PlayerCar to Game scene.", this);
            return;
        }

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        hasCachedPayload = TryResolveInitialPayload(out cachedPayload);
    }

    private void Start()
    {
        if (playerCar == null)
            return;

        ApplySelectedLoadout();
        StartCoroutine(ApplySelectedLoadoutDeferred());
        EnsureLocalNetworkVehicleEntity();
        NotifyBackendMatchLoaded();
        EnsureMultiplayerRuntime();
    }

    private void ApplySelectedLoadout()
    {
        if (playerCar == null)
            return;

        if (!hasCachedPayload &&
            PlayerCarSelection.SelectedCarConfig == null &&
            PlayerCarSelection.SelectedHandling == null &&
            PlayerCarSelection.SelectedBodySet == null &&
            PlayerCarSelection.SelectedEngine == null &&
            PlayerCarSelection.SelectedSuspension == null)
        {
            if (ensureFollowCamera)
                EnsureFollowCamera(playerCar.transform);
            return;
        }

        CarLoadoutConfig loadout = ResolveLoadout(cachedPayload);
        PlayerCarConfig carConfig = loadout != null && loadout.PlayerCarConfig != null
            ? loadout.PlayerCarConfig
            : PlayerCarSelection.SelectedCarConfig;
        VehicleSettings handling = ResolveHandling(loadout, cachedPayload);
        BodySetConfig bodySet = ResolveBodySet(loadout, cachedPayload);
        EngineGearboxConfig engine = ResolveEngine(loadout, cachedPayload);
        SuspensionConfig suspension = ResolveSuspension(loadout, cachedPayload);

        playerCar.OverrideLoadout(
            carConfig,
            handling,
            bodySet,
            engine,
            suspension,
            ResolveCustomizations(cachedPayload));

        if (TryResolvePaint(loadout, cachedPayload, out Color paint))
            playerCar.SetPaint(paint);

        if (ensureFollowCamera)
            EnsureFollowCamera(playerCar.transform);
    }

    private System.Collections.IEnumerator ApplySelectedLoadoutDeferred()
    {
        yield return null;
        yield return null;

        ApplySelectedLoadout();
    }

    private static bool TryResolveInitialPayload(out PlayerCarSelectionPayload payload)
    {
        if (TryResolveBackendMatchPayload(out payload))
            return true;

        return PlayerCarSelection.TryGetPayload(out payload);
    }

    private static bool TryResolveBackendMatchPayload(out PlayerCarSelectionPayload payload)
    {
        payload = null;

        BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
        BackendSessionResponse session = Backend.Client.Session;
        if (matchInfo == null || matchInfo.players == null || session == null || string.IsNullOrWhiteSpace(session.player_id))
            return false;

        for (int i = 0; i < matchInfo.players.Count; i++)
        {
            BackendMatchPlayerInfo player = matchInfo.players[i];
            if (player == null || !string.Equals(player.player_id, session.player_id, System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (player.car_config == null)
                continue;

            payload = player.car_config.ToPlayerSelectionPayload();
            return payload != null;
        }

        return false;
    }

    private static CarLoadoutConfig ResolveLoadout(PlayerCarSelectionPayload payload)
    {
        return CarLoadoutResolver.Resolve(payload);
    }

    private static VehicleSettings ResolveHandling(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload)
    {
        if (loadout != null && loadout.HandlingConfig != null)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.handlingName) ||
                string.Equals(loadout.HandlingConfig.name, payload.handlingName, System.StringComparison.OrdinalIgnoreCase))
                return loadout.HandlingConfig;
        }

        if (PlayerCarSelection.SelectedHandling != null &&
            (payload == null || string.IsNullOrWhiteSpace(payload.handlingName) ||
             string.Equals(PlayerCarSelection.SelectedHandling.name, payload.handlingName, System.StringComparison.OrdinalIgnoreCase)))
        {
            return PlayerCarSelection.SelectedHandling;
        }

        return loadout != null ? loadout.HandlingConfig : PlayerCarSelection.SelectedHandling;
    }

    private static BodySetConfig ResolveBodySet(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload)
    {
        return CarLoadoutResolver.ResolveBodySet(loadout, payload, PlayerCarSelection.SelectedBodySet);
    }

    private static EngineGearboxConfig ResolveEngine(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload)
    {
        return CarLoadoutResolver.ResolveEngine(loadout, payload, PlayerCarSelection.SelectedEngine);
    }

    private static SuspensionConfig ResolveSuspension(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload)
    {
        return CarLoadoutResolver.ResolveSuspension(loadout, payload, PlayerCarSelection.SelectedSuspension);
    }

    private static System.Collections.Generic.List<CarCustomizationSelection> ResolveCustomizations(PlayerCarSelectionPayload payload)
    {
        if (payload == null || payload.customizations == null || payload.customizations.Count == 0)
            return PlayerCarSelection.SelectedCustomizations;

        var resolved = new System.Collections.Generic.List<CarCustomizationSelection>(payload.customizations.Count);
        for (int i = 0; i < payload.customizations.Count; i++)
        {
            PlayerCarCustomizationPayload customization = payload.customizations[i];
            if (customization == null || string.IsNullOrWhiteSpace(customization.selectorPath))
                continue;

            resolved.Add(new CarCustomizationSelection(customization.selectorPath, customization.variantName));
        }

        return resolved;
    }

    private static bool TryResolvePaint(CarLoadoutConfig loadout, PlayerCarSelectionPayload payload, out Color paint)
    {
        paint = PlayerCarSelection.SelectedPaint;
        if (payload == null)
            return PlayerCarSelection.HasPaint;

        if (loadout != null && loadout.PaintOptions != null && payload.paintIndex >= 0 && payload.paintIndex < loadout.PaintOptions.Count)
        {
            PaintConfig config = CarLoadoutResolver.ResolvePaint(loadout, payload, loadout.PaintOptions[payload.paintIndex]);
            if (config != null)
            {
                paint = config.Color;
                return true;
            }
        }

        if (payload.hasPaint)
        {
            paint = payload.paint.ToColor();
            return true;
        }

        return PlayerCarSelection.HasPaint;
    }

    private void EnsureFollowCamera(Transform target)
    {
        if (target == null)
            return;

        if (followCamera == null)
            followCamera = FindFirstObjectByType<FollowCarCamera>();

        if (followCamera != null)
            followCamera.SetTarget(target);
        else
            Debug.LogWarning("GameSceneBootstrap: FollowCarCamera not found in scene.", this);
    }

    private async void NotifyBackendMatchLoaded()
    {
        try
        {
            BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
            if (matchInfo == null || string.IsNullOrWhiteSpace(matchInfo.match_id))
                return;

            if (!Backend.Client.IsRealtimeConnected)
                await Backend.Client.ConnectRealtimeAsync();

            await Backend.Client.SendMatchLoadedAsync(matchInfo.match_id);
        }
        catch
        {
        }
    }

    private void EnsureMultiplayerRuntime()
    {
        BackendMatchInfo matchInfo = Backend.Client.CurrentMatchInfo;
        if (matchInfo == null || string.IsNullOrWhiteSpace(matchInfo.match_id))
            return;

        if (multiplayerMatchRuntime == null)
            multiplayerMatchRuntime = FindFirstObjectByType<MultiplayerMatchRuntime>();

        if (multiplayerMatchRuntime == null)
            multiplayerMatchRuntime = gameObject.AddComponent<MultiplayerMatchRuntime>();
    }

    private void EnsureLocalNetworkVehicleEntity()
    {
        if (playerCar == null)
            return;

        NetworkVehicleEntity entity = playerCar.GetComponent<NetworkVehicleEntity>();
        if (entity == null)
            entity = playerCar.gameObject.AddComponent<NetworkVehicleEntity>();

        string playerId = Backend.Client.Session != null ? Backend.Client.Session.player_id : "local_player";
        entity.Configure(playerId, true);
    }
}
