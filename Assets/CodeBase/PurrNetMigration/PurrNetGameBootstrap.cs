using System;
using System.Collections;
using System.Reflection;
using PurrNet;
using PurrNet.Prediction;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-450)]
[DisallowMultipleComponent]
public sealed class PurrNetGameBootstrap : MonoBehaviour
{
    private const string NetworkRuntimeRootName = "PurrNetRuntime";
    private const string SceneSystemsRootName = "PredictedWorld";

    [SerializeField] private PlayerCar templateCar;
    [SerializeField] private FollowCarCamera followCamera;
    [SerializeField] private bool deactivateTemplateAfterBootstrap = true;
    [SerializeField] private bool drawConnectionOverlay = true;
    [SerializeField, Min(1.0f)] private float clientConnectTimeoutSeconds = 8.0f;

    private bool bootstrapped;
    private bool isBootstrapping;
    private PurrNetSessionSettings activeSettings;
    private NetworkManager activeNetworkManager;
    private UDPTransport activeTransport;
    private GameObject activeRuntimeRoot;
    private GameObject activeSceneSystemsRoot;
    private GameObject activeRuntimeTemplateRoot;
    private PlayerCar activeRuntimeTemplateCar;
    private PredictionManager activePredictionManager;
    private string lastClientState = "Disconnected";
    private string lastServerState = "Disconnected";
    private string lastTransportEvent = "Idle";
    private string lastDisconnectReason = "-";
    private float connectStartedAt = -1.0f;

    public void Configure(PlayerCar template, FollowCarCamera cameraController)
    {
        templateCar = template;
        followCamera = cameraController;
        ResolveReferences();

        if (deactivateTemplateAfterBootstrap && templateCar != null)
            templateCar.gameObject.SetActive(false);

        TryBeginBootstrap();
    }

    private void Start()
    {
        TryBeginBootstrap();
    }

    private void Update()
    {
        if (!bootstrapped || activeNetworkManager == null)
            return;

        if (activeSettings.Mode != PurrNetSessionMode.Client)
            return;

        if (connectStartedAt < 0.0f)
            return;

        if (activeNetworkManager.clientState == ConnectionState.Connected)
            return;

        if (Time.unscaledTime - connectStartedAt < clientConnectTimeoutSeconds)
            return;

        connectStartedAt = -1.0f;
        Debug.LogWarning(
            $"PurrNetGameBootstrap: client connect timed out. target={activeSettings.Address}:{activeSettings.Port}, state={activeNetworkManager.clientState}, transport={lastTransportEvent}",
            this);
    }

    private void OnGUI()
    {
        if (!drawConnectionOverlay || !bootstrapped || activeNetworkManager == null)
            return;

        if (!Application.isPlaying)
            return;

        GUI.color = new Color(1.0f, 1.0f, 1.0f, 0.95f);
        Rect rect = new Rect(16.0f, 16.0f, 540.0f, 124.0f);
        GUI.Box(rect, GUIContent.none);
        GUILayout.BeginArea(rect);
        GUILayout.Label(
            $"PurrNet | Mode {activeSettings.Mode} | Target {activeSettings.Address}:{activeSettings.Port} | Tick {activeSettings.TickRate}");
        GUILayout.Label(
            $"Client {lastClientState} | Server {lastServerState} | Transport {lastTransportEvent} | Disconnect {lastDisconnectReason}");
        GUILayout.Label(
            $"NM ClientState {activeNetworkManager.clientState} | NM ServerState {activeNetworkManager.serverState} | IsClient {activeNetworkManager.isClient} | IsServer {activeNetworkManager.isServer}");
        if (activeRuntimeRoot != null)
            GUILayout.Label($"RuntimeRoot activeSelf {activeRuntimeRoot.activeSelf} | activeInHierarchy {activeRuntimeRoot.activeInHierarchy} | scene {activeRuntimeRoot.scene.name}");
        if (activeSceneSystemsRoot != null)
            GUILayout.Label($"SceneRoot activeSelf {activeSceneSystemsRoot.activeSelf} | activeInHierarchy {activeSceneSystemsRoot.activeInHierarchy} | scene {activeSceneSystemsRoot.scene.name}");
        GUILayout.EndArea();
    }

    private void ResolveReferences()
    {
        if (templateCar == null)
            templateCar = FindFirstObjectByType<PlayerCar>();
        if (followCamera == null)
            followCamera = FindFirstObjectByType<FollowCarCamera>();
    }

    private void TryBeginBootstrap()
    {
        if (!Application.isPlaying)
            return;

        if (activeNetworkManager != null || isBootstrapping)
            return;

        if (!PurrNetSessionRuntime.TryGetSettings(out PurrNetSessionSettings settings))
            return;

        ResolveReferences();
        if (templateCar == null)
            return;

        bootstrapped = true;
        isBootstrapping = true;
        activeSettings = settings;
        StartCoroutine(BootstrapAfterCleanup(settings));
    }

    private void Bootstrap(PurrNetSessionSettings settings)
    {
        Debug.Log(
            $"PurrNetGameBootstrap: bootstrapping mode={settings.Mode} target={settings.Address}:{settings.Port} tick={settings.TickRate} bots={settings.SoloBotCount}",
            this);

        Scene targetScene = ResolveTargetScene();

        GameObject runtimeRoot = new GameObject(NetworkRuntimeRootName);
        MoveToScene(runtimeRoot, targetScene);
        runtimeRoot.SetActive(false);
        activeRuntimeRoot = runtimeRoot;

        UDPTransport transport = runtimeRoot.AddComponent<UDPTransport>();
        transport.address = settings.Address;
        transport.serverPort = settings.Port;
        activeTransport = transport;

        NetworkManager networkManager = runtimeRoot.AddComponent<NetworkManager>();
        networkManager.startServerFlags = StartFlags.None;
        networkManager.startClientFlags = StartFlags.None;
        networkManager.transport = transport;
        SetPrivateField(networkManager, "_dontDestroyOnLoad", false);
        SetPrivateField(networkManager, "_tickRate", settings.TickRate);
        NetworkRules networkRules = ScriptableObject.CreateInstance<NetworkRules>();
        networkRules.name = "RuntimeNetworkRules";
        SetPrivateField(networkManager, "_networkRules", networkRules);
        PlayerCar runtimeTemplateCar = CreateRuntimeVehiclePrefab();
        if (runtimeTemplateCar == null)
            throw new InvalidOperationException("PurrNetGameBootstrap: failed to create runtime vehicle prefab.");

        activeRuntimeTemplateCar = runtimeTemplateCar;
        networkManager.SetPrefabProvider(CreateNetworkPrefabs(runtimeTemplateCar.gameObject));
        activeNetworkManager = networkManager;
        PurrNetRuntimeLifecycleProbe probe = runtimeRoot.AddComponent<PurrNetRuntimeLifecycleProbe>();
        probe.Initialize(settings.Mode.ToString());

        PredictionManager predictionManager = ResolveScenePredictionManager(targetScene);
        activePredictionManager = predictionManager;
        predictionManager.predictedPrefabs = CreatePredictedPrefabs(runtimeTemplateCar.gameObject);
        SetPrivateField(predictionManager, "_physicsProvider", PredictionPhysicsProvider.UnityPhysics3D);
        SetPrivateField(predictionManager, "_updateViewMode", UpdateViewMode.LateUpdate);
        SetPrivateField(
            predictionManager,
            "_builtInSystems",
            BuiltInSystems.Physics3D |
            BuiltInSystems.Time |
            BuiltInSystems.Hierarchy |
            BuiltInSystems.Players |
            BuiltInSystems.Random);

        if (deactivateTemplateAfterBootstrap)
            templateCar.gameObject.SetActive(false);

        HookDiagnostics(networkManager, transport);
        runtimeRoot.SetActive(true);

        PurrVehicleSceneSpawner spawner = GetOrAddComponent<PurrVehicleSceneSpawner>(predictionManager.gameObject);
        spawner.Configure(runtimeTemplateCar, settings.SoloBotCount, predictionManager);
        GetOrAddComponent<PurrVehiclePlayerRoster>(predictionManager.gameObject);
        GetOrAddComponent<PurrVehicleDamageSync>(predictionManager.gameObject);

        switch (settings.Mode)
        {
            case PurrNetSessionMode.Host:
                connectStartedAt = Time.unscaledTime;
                networkManager.StartHost();
                break;
            case PurrNetSessionMode.Client:
                connectStartedAt = Time.unscaledTime;
                networkManager.StartClient();
                break;
            case PurrNetSessionMode.Server:
                networkManager.StartServer();
                break;
        }
    }

    private PredictionManager ResolveScenePredictionManager(Scene targetScene)
    {
        PredictionManager predictionManager = FindPredictionManagerInScene(targetScene);
        if (predictionManager == null)
        {
            GameObject sceneSystemsRoot = new GameObject(SceneSystemsRootName);
            MoveToScene(sceneSystemsRoot, targetScene);
            predictionManager = sceneSystemsRoot.AddComponent<PredictionManager>();
            Debug.LogWarning(
                $"PurrNetGameBootstrap: scene '{targetScene.name}' has no PredictionManager scene object. Created runtime fallback.",
                sceneSystemsRoot);
        }
        else
        {
            Debug.Log($"PurrNetGameBootstrap: using scene PredictionManager '{predictionManager.name}' in '{targetScene.name}'.", predictionManager);
        }

        activeSceneSystemsRoot = predictionManager.gameObject;
        return predictionManager;
    }

    private IEnumerator BootstrapAfterCleanup(PurrNetSessionSettings settings)
    {
        CleanupStaleNetworkManagers();
        yield return null;

        try
        {
            Bootstrap(settings);
        }
        finally
        {
            isBootstrapping = false;
        }
    }

    private PlayerCar CreateRuntimeVehiclePrefab()
    {
        if (templateCar == null)
            return null;

        GameObject runtimePrefabRoot = new GameObject("PurrVehicleRuntimePrefabRoot");
        runtimePrefabRoot.hideFlags = HideFlags.HideAndDontSave;
        runtimePrefabRoot.SetActive(false);
        DontDestroyOnLoad(runtimePrefabRoot);

        GameObject runtimePrefab = Instantiate(templateCar.gameObject, runtimePrefabRoot.transform);
        runtimePrefab.name = "PurrVehicleRuntimePrefab";
        runtimePrefab.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        runtimePrefab.SetActive(true);
        activeRuntimeTemplateRoot = runtimePrefabRoot;

        if (!runtimePrefab.TryGetComponent(out PlayerCar runtimeTemplateCar))
            return null;

        PrepareRuntimeTemplate(runtimeTemplateCar);
        return runtimeTemplateCar;
    }

    private void PrepareRuntimeTemplate(PlayerCar runtimeTemplateCar)
    {
        if (runtimeTemplateCar == null)
            return;

        GameObject runtimeTemplateObject = runtimeTemplateCar.gameObject;
        Rigidbody runtimeBody = runtimeTemplateObject.GetComponent<Rigidbody>();

        GetOrAddComponent<NetworkVehicleEntity>(runtimeTemplateObject);
        GetOrAddComponent<PurrVehicleLocalInputProvider>(runtimeTemplateObject);
        GetOrAddComponent<PurrVehicleBotInputProvider>(runtimeTemplateObject);
        GetOrAddComponent<PurrVehiclePredictedController>(runtimeTemplateObject);
        GetOrAddComponent<PurrVehicleWheelPresentation>(runtimeTemplateObject);
        GetOrAddComponent<PurrVehicleNameplate>(runtimeTemplateObject);

        SafePredictedTransform predictedTransform = GetOrAddComponent<SafePredictedTransform>(runtimeTemplateObject);
        PredictedRigidbody predictedRigidbody = GetOrAddComponent<PredictedRigidbody>(runtimeTemplateObject);
        SetPrivateField(predictedTransform, "_graphics", PurrVehicleGraphicsBindingUtility.ResolveGraphicsRoot(runtimeTemplateCar.transform));
        SetPrivateField(predictedTransform, "_interpolationSettings", CreateRuntimeInterpolationSettings());
        SetPrivateField(predictedRigidbody, "_rigidbody", runtimeBody);
        SetPrivateField(predictedRigidbody, "_eventMask", PhysicsEventMask.None);
    }

    private static TransformInterpolationSettings CreateRuntimeInterpolationSettings()
    {
        TransformInterpolationSettings settings = ScriptableObject.CreateInstance<TransformInterpolationSettings>();
        settings.hideFlags = HideFlags.HideAndDontSave;
        settings.name = "PurrVehicleRuntimeInterpolation";
        settings.useInterpolation = true;
        settings.positionInterpolation.correctionRateMinMax = new Vector2(3.3f, 10.0f);
        settings.positionInterpolation.correctionBlendMinMax = new Vector2(0.0f, 4.0f);
        settings.positionInterpolation.teleportThresholdMinMax = new Vector2(0.025f, 5.0f);
        settings.rotationInterpolation.correctionRateMinMax = new Vector2(3.3f, 10.0f);
        settings.rotationInterpolation.correctionBlendMinMax = new Vector2(5.0f, 30.0f);
        settings.rotationInterpolation.teleportThresholdMinMax = new Vector2(1.5f, 52.0f);
        return settings;
    }

    private static NetworkPrefabs CreateNetworkPrefabs(GameObject templatePrefab)
    {
        NetworkPrefabs networkPrefabs = ScriptableObject.CreateInstance<NetworkPrefabs>();
        networkPrefabs.autoGenerate = false;
        networkPrefabs.networkOnly = false;
        networkPrefabs.prefabs.Add(new NetworkPrefabs.UserPrefabData
        {
            guid = string.Empty,
            prefab = templatePrefab,
            pooled = false,
            warmupCount = 0
        });
        networkPrefabs.Refresh();
        return networkPrefabs;
    }

    private static PredictedPrefabs CreatePredictedPrefabs(GameObject templatePrefab)
    {
        PredictedPrefabs predictedPrefabs = ScriptableObject.CreateInstance<PredictedPrefabs>();
        predictedPrefabs.prefabs.Add(new PredictedPrefab
        {
            prefab = templatePrefab,
            pooling = new PoolSettings
            {
                usePooling = false,
                initialSize = 0
            }
        });
        return predictedPrefabs;
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    private static PredictionManager FindPredictionManagerInScene(Scene targetScene)
    {
        if (!targetScene.IsValid())
            return null;

        GameObject[] roots = targetScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null)
                continue;

            PredictionManager manager = roots[i].GetComponentInChildren<PredictionManager>(true);
            if (manager != null)
                return manager;
        }

        return null;
    }

    private Scene ResolveTargetScene()
    {
        if (templateCar != null && templateCar.gameObject.scene.IsValid())
            return templateCar.gameObject.scene;

        return SceneManager.GetActiveScene();
    }

    private static void MoveToScene(GameObject root, Scene targetScene)
    {
        if (root == null || !targetScene.IsValid())
            return;

        SceneManager.MoveGameObjectToScene(root, targetScene);
    }

    private void CleanupStaleNetworkManagers()
    {
        NetworkManager[] managers = Resources.FindObjectsOfTypeAll<NetworkManager>();
        if (managers == null || managers.Length == 0)
            return;

        for (int i = 0; i < managers.Length; i++)
        {
            NetworkManager manager = managers[i];
            if (manager == null)
                continue;

            GameObject runtimeObject = manager.gameObject;
            if (runtimeObject == null)
                continue;

            bool looksLikeRuntimeRoot = string.Equals(runtimeObject.name, NetworkRuntimeRootName, StringComparison.Ordinal);
            bool isMainInstance = ReferenceEquals(NetworkManager.main, manager);
            if (!looksLikeRuntimeRoot && !isMainInstance)
                continue;

            if (manager.clientState != ConnectionState.Disconnected || manager.serverState != ConnectionState.Disconnected)
                continue;

            Debug.LogWarning(
                $"PurrNetGameBootstrap: removing stale offline NetworkManager '{runtimeObject.name}' from scene '{runtimeObject.scene.name}'.",
                runtimeObject);

            if (isMainInstance)
                SetStaticField(typeof(NetworkManager), "<main>k__BackingField", null);

            Destroy(runtimeObject);
        }

        PredictionManager[] predictionManagers = Resources.FindObjectsOfTypeAll<PredictionManager>();
        if (predictionManagers == null || predictionManagers.Length == 0)
            return;

        for (int i = 0; i < predictionManagers.Length; i++)
        {
            PredictionManager predictionManager = predictionManagers[i];
            if (predictionManager == null)
                continue;

            GameObject predictionRoot = predictionManager.gameObject;
            if (predictionRoot == null)
                continue;

            if (!string.Equals(predictionRoot.name, SceneSystemsRootName, StringComparison.Ordinal))
                continue;

            Destroy(predictionRoot);
        }
    }

    private void HookDiagnostics(NetworkManager networkManager, UDPTransport transport)
    {
        if (networkManager != null)
        {
            networkManager.onClientConnectionState += HandleClientConnectionStateChanged;
            networkManager.onServerConnectionState += HandleServerConnectionStateChanged;
            networkManager.onPlayerJoined += HandlePlayerJoined;
            networkManager.onPlayerLoadedScene += HandlePlayerLoadedScene;
        }

        if (transport != null)
        {
            transport.onConnectionState += HandleTransportConnectionStateChanged;
            transport.onConnected += HandleTransportConnected;
            transport.onDisconnected += HandleTransportDisconnected;
        }
    }

    private void HandleClientConnectionStateChanged(ConnectionState state)
    {
        lastClientState = state.ToString();
        Debug.Log($"PurrNetGameBootstrap: client state -> {state}", this);
    }

    private void HandleServerConnectionStateChanged(ConnectionState state)
    {
        lastServerState = state.ToString();
        Debug.Log($"PurrNetGameBootstrap: server state -> {state}", this);
    }

    private void HandleTransportConnectionStateChanged(ConnectionState state, bool asServer)
    {
        lastTransportEvent = $"{(asServer ? "server" : "client")} state {state}";
        Debug.Log($"PurrNetGameBootstrap: transport {(asServer ? "server" : "client")} state -> {state}", this);
    }

    private void HandleTransportConnected(Connection connection, bool asServer)
    {
        lastTransportEvent = $"{(asServer ? "server" : "client")} connected {connection.connectionId}";
        lastDisconnectReason = "-";
        Debug.Log(
            $"PurrNetGameBootstrap: transport {(asServer ? "server" : "client")} connected, conn={connection.connectionId}",
            this);
    }

    private void HandleTransportDisconnected(Connection connection, DisconnectReason reason, bool asServer)
    {
        lastTransportEvent = $"{(asServer ? "server" : "client")} disconnected {connection.connectionId}";
        lastDisconnectReason = reason.ToString();
        Debug.LogWarning(
            $"PurrNetGameBootstrap: transport {(asServer ? "server" : "client")} disconnected, conn={connection.connectionId}, reason={reason}",
            this);
    }

    private void HandlePlayerJoined(PlayerID player, bool isReconnect, bool asServer)
    {
        Debug.Log($"PurrNetGameBootstrap: player joined {player} reconnect={isReconnect} asServer={asServer}", this);
    }

    private void HandlePlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
    {
        Debug.Log($"PurrNetGameBootstrap: player {player} loaded scene {scene} asServer={asServer}", this);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null || string.IsNullOrWhiteSpace(fieldName))
            return;

        Type current = target.GetType();
        while (current != null)
        {
            FieldInfo field = current.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            current = current.BaseType;
        }
    }

    private static void SetStaticField(Type targetType, string fieldName, object value)
    {
        if (targetType == null || string.IsNullOrWhiteSpace(fieldName))
            return;

        FieldInfo field = targetType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null)
            field.SetValue(null, value);
    }
}

[AddComponentMenu("PurrDiction/Safe Predicted Transform")]
public sealed class SafePredictedTransform : PredictedTransform
{
    protected override void UpdateView(PredictedTransformState viewState, PredictedTransformState? verified)
    {
        Transform targetGraphics = graphics;
        if (targetGraphics == null || !updateGraphics)
            return;

        targetGraphics.SetPositionAndRotation(viewState.unityPosition, viewState.unityRotation);
    }
}

public static class PurrVehicleGraphicsBindingUtility
{
    private const string CollisionBodyRootName = "BodyCollisionRoot";
    private static readonly FieldInfo GraphicsField =
        typeof(PredictedTransform).GetField("_graphics", BindingFlags.Instance | BindingFlags.NonPublic);

    public static Transform RefreshGraphicsBinding(Component component, SafePredictedTransform predictedTransform)
    {
        if (component == null)
            return null;

        return RefreshGraphicsBinding(component.transform, predictedTransform);
    }

    public static Transform RefreshGraphicsBinding(Transform root, SafePredictedTransform predictedTransform)
    {
        if (root == null || predictedTransform == null)
            return null;

        Transform graphicsRoot = ResolveGraphicsRoot(root);
        if (GraphicsField != null)
            GraphicsField.SetValue(predictedTransform, graphicsRoot);

        return graphicsRoot;
    }

    public static Transform ResolveGraphicsRoot(Transform root)
    {
        if (root == null)
            return null;

        Transform body = root.Find("Body");
        Transform collisionBodyRoot = root.Find(CollisionBodyRootName);
        bool hasDetachedCollisionBody = collisionBodyRoot != null && collisionBodyRoot.GetComponentInChildren<Collider>(true) != null;
        if (body != null)
        {
            Transform bodyRoot = body.Find("Root");
            if (hasDetachedCollisionBody)
            {
                if (IsDetachedBodyGraphicsRoot(bodyRoot))
                    return bodyRoot;

                if (IsDetachedBodyGraphicsRoot(body))
                    return body;
            }

            if (IsValidGraphicsRoot(bodyRoot))
                return bodyRoot;

            if (IsValidGraphicsRoot(body))
                return body;

            Transform bestBodyCandidate = FindBestGraphicsRoot(body);
            if (bestBodyCandidate != null)
                return bestBodyCandidate;
        }

        return FindBestGraphicsRoot(root);
    }

    private static bool IsValidGraphicsRoot(Transform candidate)
    {
        if (candidate == null)
            return false;

        if (IsExcludedGraphicsCandidate(candidate))
            return false;

        if (candidate.GetComponentInChildren<Collider>(true) != null)
            return false;

        if (candidate.GetComponentInChildren<Rigidbody>(true) != null)
            return false;

        if (candidate.GetComponentInChildren<PredictedIdentity>(true) != null)
            return false;

        return candidate.GetComponentInChildren<Renderer>(true) != null;
    }

    private static bool IsDetachedBodyGraphicsRoot(Transform candidate)
    {
        return candidate != null &&
               !IsExcludedGraphicsCandidate(candidate) &&
               candidate.GetComponentInChildren<Renderer>(true) != null;
    }

    private static Transform FindBestGraphicsRoot(Transform searchRoot)
    {
        if (searchRoot == null)
            return null;

        Transform[] all = searchRoot.GetComponentsInChildren<Transform>(true);
        Transform best = null;
        int bestRendererCount = -1;
        int bestDepth = int.MaxValue;

        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (!IsValidGraphicsRoot(candidate))
                continue;

            int rendererCount = candidate.GetComponentsInChildren<Renderer>(true).Length;
            int depth = GetTransformDepth(searchRoot, candidate);
            if (rendererCount > bestRendererCount || (rendererCount == bestRendererCount && depth < bestDepth))
            {
                best = candidate;
                bestRendererCount = rendererCount;
                bestDepth = depth;
            }
        }

        return best;
    }

    private static int GetTransformDepth(Transform searchRoot, Transform candidate)
    {
        int depth = 0;
        Transform current = candidate;
        while (current != null && current != searchRoot)
        {
            depth += 1;
            current = current.parent;
        }

        return depth;
    }

    private static bool IsExcludedGraphicsCandidate(Transform candidate)
    {
        if (candidate == null)
            return true;

        if (candidate.GetComponentInParent<WheelCollider>() != null)
            return true;

        Transform current = candidate;
        while (current != null)
        {
            if (current.name == CollisionBodyRootName ||
                current.name == "FrontLeft" ||
                current.name == "FrontRight" ||
                current.name == "RearLeft" ||
                current.name == "RearRight")
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }
}

[DisallowMultipleComponent]
public sealed class PurrNetRuntimeLifecycleProbe : MonoBehaviour
{
    private string runtimeMode = "unknown";

    public void Initialize(string mode)
    {
        runtimeMode = string.IsNullOrWhiteSpace(mode) ? "unknown" : mode;
    }

    private void OnEnable()
    {
        Debug.Log($"PurrNetRuntimeLifecycleProbe: enabled mode={runtimeMode} scene={gameObject.scene.name}", this);
    }

    private void OnDisable()
    {
        Debug.LogWarning($"PurrNetRuntimeLifecycleProbe: disabled mode={runtimeMode} scene={gameObject.scene.name}", this);
    }

    private void OnDestroy()
    {
        Debug.LogWarning($"PurrNetRuntimeLifecycleProbe: destroyed mode={runtimeMode} scene={gameObject.scene.name}", this);
    }
}
