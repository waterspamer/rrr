using UnityEngine;

public class GameSceneBootstrap : MonoBehaviour
{
    private const string PurrNetBootstrapRootName = "PurrNetSceneBootstrap";

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

        if (ShouldUsePurrNetMigration())
        {
            EnsurePurrNetBootstrap();
            return;
        }

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

        PlayerCarLoadoutUtility.ApplySelectedLoadout(playerCar, cachedPayload);

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

    private static bool ShouldUsePurrNetMigration()
    {
        return PurrNetSessionRuntime.IsEnabled;
    }

    private void EnsurePurrNetBootstrap()
    {
        PurrNetGameBootstrap bootstrap = FindFirstObjectByType<PurrNetGameBootstrap>();
        if (bootstrap == null)
        {
            GameObject bootstrapRoot = new GameObject(PurrNetBootstrapRootName);
            DontDestroyOnLoad(bootstrapRoot);
            bootstrap = bootstrapRoot.AddComponent<PurrNetGameBootstrap>();
        }

        bootstrap.Configure(playerCar, followCamera);
    }
}
