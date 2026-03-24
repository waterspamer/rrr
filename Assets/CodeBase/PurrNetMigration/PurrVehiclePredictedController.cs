using PurrNet;
using PurrNet.Prediction;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerCar))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PredictedTransform))]
[RequireComponent(typeof(PredictedRigidbody))]
public sealed class PurrVehiclePredictedController : PredictedIdentity<PurrVehiclePredictedInput, PurrVehiclePredictedControllerState>
{
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private CarControllerBase controller;
    [SerializeField] private Rigidbody body;
    [SerializeField] private CarDamageController damageController;
    [SerializeField] private PurrVehicleLocalInputProvider localInputProvider;
    [SerializeField] private PurrVehicleBotInputProvider botInputProvider;

    private int lastAppliedDamageRevision = int.MinValue;

    private void Awake()
    {
        ResolveReferences();
        PrepareController();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    protected override PurrVehiclePredictedControllerState GetInitialState()
    {
        ResolveReferences();
        return PurrVehiclePredictedControllerState.Capture(controller, damageController);
    }

    protected override void GetUnityState(ref PurrVehiclePredictedControllerState state)
    {
        ResolveReferences();
        ConfigureAuthorityState();
        state = PurrVehiclePredictedControllerState.Capture(controller, damageController);
    }

    protected override void SetUnityState(PurrVehiclePredictedControllerState state)
    {
        ResolveReferences();
        PrepareController();
        ConfigureAuthorityState();

        if (controller != null)
            controller.ApplySimulationState(state.ToSimulationState());

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

        if (controller == null)
            return;

        controller.ApplySimulationState(state.ToSimulationState());
        controller.SimulateManualStep(input.ToControlFrame(), delta);
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

        if (controller != null)
            controller.SetManualSimulationEnabled(false);

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

        if (state.damage.revision == lastAppliedDamageRevision)
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
        if (controller == null && playerCar != null)
            controller = playerCar.Controller;
        if (controller == null)
            controller = GetComponent<CarControllerBase>();
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
        if (controller == null)
            return;

        controller.SetManualSimulationEnabled(true);
        controller.SetInputEnabled(true);
        controller.SetPhysicsSimulationEnabled(true);

        if (body != null)
            body.interpolation = RigidbodyInterpolation.None;
    }

    private void ConfigureAuthorityState()
    {
        if (damageController != null)
            damageController.SetCollisionDamageEnabled(isServer);
    }

    private void RefreshViewBindings()
    {
        ResolveReferences();

        NetworkVehicleEntity entity = playerCar != null ? playerCar.GetComponent<NetworkVehicleEntity>() : null;
        if (playerCar != null && entity == null)
            entity = playerCar.gameObject.AddComponent<NetworkVehicleEntity>();

        if (entity != null)
            entity.Configure(owner.HasValue ? owner.Value.ToString() : "server", IsOwner());

        if (!IsOwner() || playerCar == null)
            return;

        FollowCarCamera followCamera = FindFirstObjectByType<FollowCarCamera>();
        if (followCamera != null)
            followCamera.SetTarget(playerCar.transform);
    }
}
