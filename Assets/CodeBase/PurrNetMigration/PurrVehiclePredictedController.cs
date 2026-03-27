using PurrNet;
using PurrNet.Prediction;
using UnityEngine;

[DefaultExecutionOrder(1100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerCar))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SafePredictedTransform))]
[RequireComponent(typeof(PredictedRigidbody))]
public sealed class PurrVehiclePredictedController : PredictedIdentity<PurrVehiclePredictedInput, PurrVehiclePredictedControllerState>
{
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private SafePredictedTransform predictedTransform;
    [SerializeField] private PurrVehicleSimulationBridge simulationBridge;
    [SerializeField] private Rigidbody body;
    [SerializeField] private CarDamageController damageController;
    [SerializeField] private PurrVehicleLocalInputProvider localInputProvider;
    [SerializeField] private PurrVehicleBotInputProvider botInputProvider;

    private int lastAppliedDamageRevision = int.MinValue;
    private string lastAppliedLocalLoadoutSignature;

    private void Awake()
    {
        ResolveReferences();
        PrepareController();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        if (simulationBridge == null || !simulationBridge.RequiresExternalPresentation)
            return;

        simulationBridge.UpdateExternalPresentation(ResolvePresentationRoot(), Time.deltaTime);
    }

    protected override PurrVehiclePredictedControllerState GetInitialState()
    {
        ResolveReferences();
        return simulationBridge != null ? simulationBridge.CapturePredictedState(damageController) : default;
    }

    protected override void GetUnityState(ref PurrVehiclePredictedControllerState state)
    {
        ResolveReferences();
        ConfigureAuthorityState();
        state = simulationBridge != null ? simulationBridge.CapturePredictedState(damageController) : default;
    }

    protected override void SetUnityState(PurrVehiclePredictedControllerState state)
    {
        ResolveReferences();
        PrepareController();
        ConfigureAuthorityState();

        simulationBridge?.ApplyPredictedState(state);

        ApplyDamageState(state);
    }

    protected override void SimulationStart()
    {
        ResolveReferences();
        PrepareController();
        ConfigureAuthorityState();
    }

    protected override void LateAwake()
    {
        base.LateAwake();
        ResolveReferences();
        PrepareController();
        ConfigureAuthorityState();
        RefreshViewBindings();
    }

    protected override void Simulate(PurrVehiclePredictedInput input, ref PurrVehiclePredictedControllerState state, float delta)
    {
        ResolveReferences();
        PrepareController();
        ConfigureAuthorityState();

        if (simulationBridge == null || !simulationBridge.HasController)
            return;

        simulationBridge.ApplyPredictedState(state);
        simulationBridge.SimulatePredicted(input.ToControlFrame(), delta);
    }

    protected override void GetFinalInput(ref PurrVehiclePredictedInput input)
    {
        if (ShouldUseBotInput())
        {
            botInputProvider?.GetFinalInput(ref input);
            return;
        }

        if (IsOwner())
            localInputProvider?.GetFinalInput(ref input);
    }

    protected override void UpdateInput(ref PurrVehiclePredictedInput input)
    {
        if (ShouldUseBotInput())
        {
            botInputProvider?.UpdateInput(ref input);
            return;
        }

        if (IsOwner())
            localInputProvider?.UpdateInput(ref input);
    }

    protected override void ModifyExtrapolatedInput(ref PurrVehiclePredictedInput input)
    {
        input.handbrake = false;
        input.nitro = false;
        input.Clamp();
    }

    protected override void SanitizeInput(ref PurrVehiclePredictedInput input)
    {
        input.Clamp();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        simulationBridge?.CleanupPrediction();

        if (damageController != null)
            damageController.SetCollisionDamageEnabled(true);
    }

    public override void OnViewOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner)
    {
        base.OnViewOwnerChanged(oldOwner, newOwner);
        RefreshViewBindings();
    }

    private bool ShouldUseBotInput()
    {
        return isServer && owner.HasValue && owner.Value.isBot && botInputProvider != null;
    }

    private void ApplyDamageState(PurrVehiclePredictedControllerState state)
    {
        if (damageController == null || !state.HasDamageSnapshot)
            return;

        int currentAppliedRevision = Mathf.Max(lastAppliedDamageRevision, damageController.DamageRevision);
        if (state.damage.revision <= currentAppliedRevision)
            return;

        CarDamageNetworkSnapshot snapshot = state.CreateDamageSnapshot();
        if (snapshot == null)
            return;

        damageController.ApplyNetworkDamageSnapshot(snapshot);
        lastAppliedDamageRevision = state.damage.revision;
    }

    private void ResolveReferences()
    {
        if (playerCar == null)
            playerCar = GetComponent<PlayerCar>();
        if (predictedTransform == null)
            predictedTransform = GetComponent<SafePredictedTransform>();
        if (simulationBridge == null)
            simulationBridge = GetComponent<PurrVehicleSimulationBridge>();
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (damageController == null && playerCar != null)
            damageController = playerCar.DamageController;
        if (damageController == null)
            damageController = GetComponent<CarDamageController>();
        if (localInputProvider == null)
            localInputProvider = GetComponent<PurrVehicleLocalInputProvider>();
        if (botInputProvider == null)
            botInputProvider = GetComponent<PurrVehicleBotInputProvider>();
    }

    private void PrepareController()
    {
        if (simulationBridge == null)
            return;

        bool usePredictedSimulation = predictionManager != null;
        simulationBridge.ConfigurePrediction(usePredictedSimulation);
        ConfigurePredictionViewWriter();
    }

    private void ConfigureAuthorityState()
    {
        if (damageController != null)
            damageController.SetCollisionDamageEnabled(predictionManager == null || IsDamageAuthority());
    }

    private void RefreshViewBindings()
    {
        ResolveReferences();
        RefreshPredictionView();

        NetworkVehicleEntity entity = playerCar != null ? playerCar.GetComponent<NetworkVehicleEntity>() : null;
        if (playerCar != null && entity == null)
            entity = playerCar.gameObject.AddComponent<NetworkVehicleEntity>();

        if (entity != null)
            entity.Configure(owner.HasValue ? owner.Value.ToString() : "server", IsOwner());

        if (!IsOwner() || playerCar == null)
            return;

        TryApplyLocalOwnedLoadout();

        FollowCarCamera followCamera = FindFirstObjectByType<FollowCarCamera>();
        if (followCamera != null)
            followCamera.SetTarget(ResolveCameraTarget());
    }

    private void TryApplyLocalOwnedLoadout()
    {
        if (!PlayerCarSelection.TryGetPayload(out PlayerCarSelectionPayload payload) || payload == null)
            return;

        string signature = BuildLoadoutSignature(payload);
        if (signature == lastAppliedLocalLoadoutSignature)
            return;

        PlayerCarLoadoutUtility.ApplySelectedLoadout(playerCar, payload);
        damageController?.ResetDamageState(notifyNetwork: false);
        lastAppliedDamageRevision = int.MinValue;
        RefreshPredictionView();
        lastAppliedLocalLoadoutSignature = signature;
        Debug.Log($"PurrVehiclePredictedController: applied local owner loadout '{payload.loadoutName}'.", this);
    }

    private static string BuildLoadoutSignature(PlayerCarSelectionPayload payload)
    {
        if (payload == null)
            return string.Empty;

        return $"{payload.loadoutName}|{payload.bodySetOptionIndex}|{payload.engineIndex}|{payload.suspensionIndex}|{payload.paintIndex}";
    }

    private Transform ResolveCameraTarget()
    {
        if (simulationBridge != null && simulationBridge.CameraTarget != null)
            return simulationBridge.CameraTarget;

        return playerCar != null ? playerCar.transform : transform;
    }

    private Transform ResolvePresentationRoot()
    {
        Transform graphicsTarget = predictedTransform != null ? predictedTransform.graphics : null;
        if (graphicsTarget != null)
            return graphicsTarget;

        return RefreshPredictionView();
    }

    private Transform RefreshPredictionView()
    {
        if (predictedTransform == null)
            return null;

        Transform graphicsRoot = PurrVehicleGraphicsBindingUtility.RefreshGraphicsBinding(this, predictedTransform);
        ConfigurePredictionViewWriter();
        return graphicsRoot;
    }

    private void ConfigurePredictionViewWriter()
    {
        if (predictedTransform == null)
            return;

        bool usesArcadePresentation = simulationBridge != null && simulationBridge.UsesArcadeController;
        predictedTransform.updateGraphics = !usesArcadePresentation;
    }

    private bool IsDamageAuthority()
    {
        if (IsOwner())
            return true;

        if (!isServer)
            return false;

        if (!owner.HasValue)
            return true;

        return owner.Value.isBot || owner.Value.isServer;
    }
}

// Legacy shim kept for serialized compatibility. Presentation is now driven from PurrVehiclePredictedController.
public sealed class PurrVehicleWheelPresentation : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

[DisallowMultipleComponent]
public sealed class PurrVehicleSimulationBridge : MonoBehaviour
{
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private CarControllerBase legacyController;
    [SerializeField] private ArcadePrototypeCarController arcadeController;
    [SerializeField] private Rigidbody body;
    [SerializeField] private string lastConfiguredSignature;
    [SerializeField] private PurrVehicleSimulationBackend configuredBackend;

    public bool HasController => UsesArcadeController || UsesLegacyController;
    public bool UsesArcadeController =>
        ResolveRequestedBackend() == PurrVehicleSimulationBackend.ArcadePrototype &&
        arcadeController != null &&
        arcadeController.isActiveAndEnabled;
    public bool UsesLegacyController => !UsesArcadeController && legacyController != null;
    public PurrVehicleSimulationBackend ActiveBackend => UsesArcadeController
        ? PurrVehicleSimulationBackend.ArcadePrototype
        : UsesLegacyController ? PurrVehicleSimulationBackend.LegacyController : PurrVehicleSimulationBackend.None;
    public CarControllerBase LegacyController => legacyController;
    public ArcadePrototypeCarController ArcadeController => arcadeController;
    public bool RequiresExternalPresentation => UsesLegacyController && legacyController.ExternalPresentationEnabled;
    public Transform CameraTarget => UsesArcadeController && arcadeController.CameraTarget != null
        ? arcadeController.CameraTarget
        : playerCar != null ? playerCar.transform : transform;
    public float SpeedKph => UsesArcadeController ? arcadeController.SpeedKph : UsesLegacyController ? legacyController.SpeedKph : 0.0f;
    public int CurrentGear => UsesArcadeController ? arcadeController.CurrentGear : UsesLegacyController ? legacyController.CurrentGear : 0;
    public float CurrentRpm => UsesArcadeController ? arcadeController.CurrentRpm : UsesLegacyController ? legacyController.CurrentRpm : 0.0f;
    public float NitroAmount => UsesArcadeController ? arcadeController.NitroAmount : UsesLegacyController ? legacyController.NitroAmount : 0.0f;
    public bool NitroActive => UsesArcadeController ? arcadeController.NitroActive : UsesLegacyController && legacyController.NitroActive;
    public int GroundedWheelCount => UsesArcadeController ? arcadeController.GroundedWheels : UsesLegacyController ? legacyController.GroundedWheelCount : 0;
    public int WheelCount => UsesArcadeController ? arcadeController.WheelCountValue : UsesLegacyController ? legacyController.WheelCount : 0;
    public Vector3 LinearVelocity => UsesArcadeController
        ? arcadeController.LinearVelocity
        : body != null ? body.linearVelocity : Vector3.zero;
    public Vector3 AngularVelocity => UsesArcadeController
        ? arcadeController.AngularVelocity
        : body != null ? body.angularVelocity : Vector3.zero;
    public bool IsSleeping => UsesArcadeController
        ? arcadeController.IsBodySleeping
        : body != null && body.IsSleeping();
    public float LastMotorTorque => UsesLegacyController ? legacyController.LastMotorTorque : 0.0f;
    public float LastBrakeTorque => UsesLegacyController ? legacyController.LastBrakeTorque : 0.0f;
    public float LastRearBrakeTorque => UsesLegacyController ? legacyController.LastRearBrakeTorque : 0.0f;
    public bool InputEnabled => UsesLegacyController ? legacyController.InputEnabled : false;
    public bool PhysicsSimulationEnabled => UsesLegacyController ? legacyController.PhysicsSimulationEnabled : true;
    public bool ManualSimulationEnabled => UsesLegacyController ? legacyController.ManualSimulationEnabled : true;
    public CarControlFrame LastAppliedControlFrame => UsesArcadeController
        ? arcadeController.LastAppliedControlFrame
        : UsesLegacyController ? legacyController.LastAppliedControlFrame : default;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    public void ResolveReferences()
    {
        if (playerCar == null)
            playerCar = GetComponent<PlayerCar>();
        if (legacyController == null && playerCar != null)
            legacyController = playerCar.Controller;
        if (legacyController == null)
            legacyController = GetComponent<CarControllerBase>();
        if (arcadeController == null)
            arcadeController = GetComponent<ArcadePrototypeCarController>();
        if (body == null)
            body = GetComponent<Rigidbody>();
    }

    public void ConfigurePrediction(bool usePredictedSimulation)
    {
        ResolveReferences();
        EnsureRequestedControllerConfigured();

        if (UsesArcadeController)
        {
            ConfigureLegacyForArcadeMode();
            arcadeController.SetUseLocalInput(!usePredictedSimulation);
        }
        else if (UsesLegacyController)
        {
            legacyController.SetManualSimulationEnabled(usePredictedSimulation);
            legacyController.SetExternalPresentationEnabled(usePredictedSimulation);
            legacyController.SetInputEnabled(true);
            legacyController.SetPhysicsSimulationEnabled(true);
        }

        if (body != null)
            body.interpolation = usePredictedSimulation ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
    }

    public void CleanupPrediction()
    {
        ResolveReferences();

        if (UsesLegacyController)
        {
            legacyController.SetManualSimulationEnabled(false);
            legacyController.SetExternalPresentationEnabled(false);
        }

        if (arcadeController != null)
        {
            arcadeController.SetUseLocalInput(true);
            arcadeController.enabled = UsesArcadeController;
        }
    }

    public void UpdateExternalPresentation(Transform presentationRoot, float deltaTime)
    {
        ResolveReferences();
        if (!UsesLegacyController || !legacyController.ExternalPresentationEnabled)
            return;

        legacyController.UpdateVehiclePresentation(presentationRoot, deltaTime);
    }

    public PurrVehiclePredictedControllerState CapturePredictedState(CarDamageController damageController)
    {
        ResolveReferences();
        return PurrVehiclePredictedControllerState.Capture(this, damageController);
    }

    public void ApplyPredictedState(PurrVehiclePredictedControllerState state)
    {
        ResolveReferences();
        EnsureRequestedControllerConfigured();

        switch ((PurrVehicleSimulationBackend)state.simulationBackend)
        {
            case PurrVehicleSimulationBackend.ArcadePrototype:
                arcadeController?.ApplyState(state.arcadeSimulation);
                break;
            case PurrVehicleSimulationBackend.LegacyController:
                legacyController?.ApplySimulationState(state.legacySimulation);
                break;
        }
    }

    public void SimulatePredicted(CarControlFrame controlFrame, float deltaTime)
    {
        ResolveReferences();
        EnsureRequestedControllerConfigured();

        if (UsesArcadeController)
        {
            arcadeController.SimulateTick(controlFrame, deltaTime);
            return;
        }

        if (UsesLegacyController)
            legacyController.SimulateManualStep(controlFrame, deltaTime);
    }

    public bool TryCaptureLegacySimulation(out CarControllerSimulationState state)
    {
        state = default;
        if (!UsesLegacyController || legacyController == null)
            return false;

        state = legacyController.CaptureSimulationState();
        return true;
    }

    public bool TryCaptureArcadeSimulation(out ArcadePrototypeCarController.VehicleState state)
    {
        state = default;
        if (!UsesArcadeController || arcadeController == null)
            return false;

        state = arcadeController.CaptureState();
        return true;
    }

    private PurrVehicleSimulationBackend ResolveRequestedBackend()
    {
        ResolveReferences();
        if (playerCar == null || playerCar.Config == null)
            return PurrVehicleSimulationBackend.LegacyController;

        return playerCar.Config.DirectMultiplayerSimulationBackend == DirectMultiplayerSimulationBackend.ArcadePrototype
            ? PurrVehicleSimulationBackend.ArcadePrototype
            : PurrVehicleSimulationBackend.LegacyController;
    }

    private void EnsureRequestedControllerConfigured()
    {
        ResolveReferences();
        PurrVehicleSimulationBackend requestedBackend = ResolveRequestedBackend();

        if (requestedBackend == PurrVehicleSimulationBackend.ArcadePrototype && arcadeController == null)
            arcadeController = gameObject.AddComponent<ArcadePrototypeCarController>();

        string signature = BuildConfigurationSignature(requestedBackend);
        bool needsRefresh = configuredBackend != requestedBackend || !string.Equals(lastConfiguredSignature, signature);
        if (!needsRefresh)
            return;

        if (requestedBackend == PurrVehicleSimulationBackend.ArcadePrototype && arcadeController != null)
        {
            arcadeController.enabled = true;
            arcadeController.ConfigureFromPlayerCar(playerCar);
            if (playerCar != null && playerCar.Config != null && playerCar.Config.UseArcadePrototypeControllerTuning)
                arcadeController.ApplyRuntimeTuning(playerCar.Config.ArcadePrototypeController);
        }
        else if (arcadeController != null)
        {
            arcadeController.enabled = false;
        }

        configuredBackend = requestedBackend;
        lastConfiguredSignature = signature;
    }

    private void ConfigureLegacyForArcadeMode()
    {
        if (legacyController == null)
            return;

        legacyController.SetManualSimulationEnabled(true);
        legacyController.SetExternalPresentationEnabled(true);
        legacyController.SetInputEnabled(false);
        legacyController.SetPhysicsSimulationEnabled(false);
    }

    private string BuildConfigurationSignature(PurrVehicleSimulationBackend backend)
    {
        string configName = playerCar != null && playerCar.Config != null ? playerCar.Config.name : string.Empty;
        string handlingName = playerCar != null && playerCar.HandlingConfig != null ? playerCar.HandlingConfig.name : string.Empty;
        string engineName = playerCar != null && playerCar.EngineConfig != null ? playerCar.EngineConfig.name : string.Empty;
        string suspensionName = playerCar != null && playerCar.SuspensionConfig != null ? playerCar.SuspensionConfig.name : string.Empty;
        bool hasArcadeTuning = playerCar != null && playerCar.Config != null && playerCar.Config.UseArcadePrototypeControllerTuning;
        return $"{(byte)backend}|{configName}|{handlingName}|{engineName}|{suspensionName}|{hasArcadeTuning}";
    }
}
