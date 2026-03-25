using PurrNet;
using PurrNet.Prediction;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerCar))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SafePredictedTransform))]
[RequireComponent(typeof(PredictedRigidbody))]
public sealed class PurrVehiclePredictedController : PredictedIdentity<PurrVehiclePredictedInput, PurrVehiclePredictedControllerState>
{
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private SafePredictedTransform predictedTransform;
    [SerializeField] private CarControllerBase controller;
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
        Transform graphicsTarget = RefreshPredictionView();
        if (graphicsTarget != null)
            return graphicsTarget;

        if (predictedTransform != null && predictedTransform.graphics != null)
            return predictedTransform.graphics;

        return playerCar != null ? playerCar.transform : transform;
    }

    private Transform RefreshPredictionView()
    {
        if (predictedTransform == null)
            return null;

        return PurrVehicleGraphicsBindingUtility.RefreshGraphicsBinding(this, predictedTransform);
    }
}

[DefaultExecutionOrder(1100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SafePredictedTransform))]
public sealed class PurrVehicleWheelPresentation : MonoBehaviour
{
    private struct WheelBinding
    {
        public WheelCollider collider;
        public Transform visual;
    }

    [SerializeField] private SafePredictedTransform predictedTransform;
    [SerializeField] private Transform vehicleRoot;

    private readonly System.Collections.Generic.List<WheelBinding> wheelBindings =
        new System.Collections.Generic.List<WheelBinding>(4);
    private Transform graphicsRoot;
    private Vector3 graphicsLocalPosition;
    private Quaternion graphicsLocalRotation = Quaternion.identity;

    private void Awake()
    {
        ResolveReferences();
        RefreshBindings(force: true);
    }

    private void OnEnable()
    {
        RefreshBindings(force: true);
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        RefreshBindings(force: false);

        if (vehicleRoot == null || graphicsRoot == null || wheelBindings.Count == 0)
            return;

        Quaternion authoritativeRootRotation = vehicleRoot.rotation;
        Vector3 authoritativeRootPosition = vehicleRoot.position;
        Quaternion smoothedRootRotation = graphicsRoot.rotation * Quaternion.Inverse(graphicsLocalRotation);
        Vector3 smoothedRootPosition = graphicsRoot.position - (smoothedRootRotation * graphicsLocalPosition);
        Quaternion wheelVisualRotationOffset = Quaternion.Euler(0.0f, 0.0f, 90.0f);

        for (int i = 0; i < wheelBindings.Count; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding.collider == null || binding.visual == null)
                continue;

            binding.collider.GetWorldPose(out Vector3 colliderPosition, out Quaternion colliderRotation);
            Vector3 localWheelPosition = Quaternion.Inverse(authoritativeRootRotation) * (colliderPosition - authoritativeRootPosition);
            Quaternion localWheelRotation = Quaternion.Inverse(authoritativeRootRotation) * colliderRotation;
            Vector3 smoothedWheelPosition = smoothedRootPosition + (smoothedRootRotation * localWheelPosition);
            Quaternion smoothedWheelRotation = smoothedRootRotation * localWheelRotation * wheelVisualRotationOffset;
            binding.visual.SetPositionAndRotation(smoothedWheelPosition, smoothedWheelRotation);
        }
    }

    private void ResolveReferences()
    {
        if (predictedTransform == null)
            predictedTransform = GetComponent<SafePredictedTransform>();
        if (vehicleRoot == null)
            vehicleRoot = transform;
    }

    private void RefreshBindings(bool force)
    {
        Transform targetGraphicsRoot = predictedTransform != null ? predictedTransform.graphics : null;
        bool graphicsChanged = targetGraphicsRoot != graphicsRoot;
        if (!force && !graphicsChanged && wheelBindings.Count > 0)
            return;

        graphicsRoot = targetGraphicsRoot;
        if (graphicsRoot != null && vehicleRoot != null)
        {
            graphicsLocalPosition = vehicleRoot.InverseTransformPoint(graphicsRoot.position);
            graphicsLocalRotation = Quaternion.Inverse(vehicleRoot.rotation) * graphicsRoot.rotation;
        }

        RebuildWheelBindings();
    }

    private void RebuildWheelBindings()
    {
        wheelBindings.Clear();

        WheelCollider[] colliders = GetComponentsInChildren<WheelCollider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            WheelCollider wheelCollider = colliders[i];
            if (wheelCollider == null)
                continue;

            Transform visual = wheelCollider.transform.Find("VisualRoot");
            if (visual == null)
                visual = wheelCollider.transform.Find("Visual");
            if (visual == null)
                continue;

            wheelBindings.Add(new WheelBinding
            {
                collider = wheelCollider,
                visual = visual
            });
        }
    }
}
