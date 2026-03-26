using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ArcadePrototypeRigBuilder : MonoBehaviour
{
    private const string BodyName = "Body";
    private static readonly System.Type[] VisualGameplayComponentTypes =
    {
        typeof(Collider),
        typeof(Rigidbody),
        typeof(CarControllerBase),
        typeof(WheelCollider),
        typeof(Joint),
        typeof(CarDamageController)
    };
    private static readonly string[] WheelNames = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };

    public void Build(
        PlayerCarConfig carConfig,
        VehicleSettings handling,
        SuspensionConfig suspension,
        ArcadePrototypeControllerRuntimeTuning controllerTuning,
        BodySetConfig bodySet,
        IReadOnlyList<CarCustomizationSelection> customizations,
        Color paint,
        bool applyPaint)
    {
        ClearGeneratedRig();

        PlayerCarVisualSettings visual = carConfig != null ? carConfig.Visual : null;
        if (visual == null)
            visual = new PlayerCarVisualSettings();

        visual.Validate();
        Transform bodyRoot = BuildBody(visual, handling, suspension, bodySet, customizations);
        BuildWheels(visual, handling, suspension, controllerTuning);
        RebuildBodyCollider(visual, handling, suspension);

        if (applyPaint)
            ApplyPaint(bodyRoot, visual, paint);
    }

    public void Build(
        PlayerCarConfig carConfig,
        SuspensionConfig suspension,
        BodySetConfig bodySet,
        IReadOnlyList<CarCustomizationSelection> customizations,
        Color paint,
        bool applyPaint)
    {
        Build(carConfig, null, suspension, null, bodySet, customizations, paint, applyPaint);
    }

    private void ClearGeneratedRig()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
                continue;

            DestroyRuntimeObject(child.gameObject);
        }

        Collider[] colliders = GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            DestroyRuntimeObject(colliders[i]);
    }

    private Transform BuildBody(
        PlayerCarVisualSettings visual,
        VehicleSettings handling,
        SuspensionConfig suspension,
        BodySetConfig bodySet,
        IReadOnlyList<CarCustomizationSelection> customizations)
    {
        if (visual != null && visual.bodyPrefab != null)
            return BuildLoadoutBody(visual, bodySet, customizations);

        float wheelHeight = ResolveWheelCenterHeight(visual, handling, suspension);
        float bodyWidth = Mathf.Max(1.6f, visual.axleWidth + 0.45f);
        float bodyLength = Mathf.Max(3.2f, visual.wheelBase + 0.85f);
        float bodyHeight = 0.75f;
        float cabinHeight = 0.45f;
        float bodyCenterY = wheelHeight + 0.55f;

        GameObject bodyRoot = new GameObject(BodyName);
        bodyRoot.transform.SetParent(transform, false);

        GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chassis.name = "Chassis";
        chassis.transform.SetParent(bodyRoot.transform, false);
        chassis.transform.localPosition = new Vector3(0.0f, bodyCenterY, 0.0f);
        chassis.transform.localRotation = Quaternion.identity;
        chassis.transform.localScale = new Vector3(bodyWidth, bodyHeight, bodyLength);
        DestroyRuntimeObject(chassis.GetComponent<Collider>());

        GameObject cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cabin.name = "Cabin";
        cabin.transform.SetParent(bodyRoot.transform, false);
        cabin.transform.localPosition = new Vector3(0.0f, bodyCenterY + 0.45f, -0.1f);
        cabin.transform.localRotation = Quaternion.identity;
        cabin.transform.localScale = new Vector3(bodyWidth * 0.72f, cabinHeight, bodyLength * 0.42f);
        DestroyRuntimeObject(cabin.GetComponent<Collider>());

        return bodyRoot.transform;
    }

    private Transform BuildLoadoutBody(
        PlayerCarVisualSettings visual,
        BodySetConfig bodySet,
        IReadOnlyList<CarCustomizationSelection> customizations)
    {
        GameObject bodyInstance = Instantiate(visual.bodyPrefab, transform);
        bodyInstance.name = BodyName;
        bodyInstance.transform.localPosition = Vector3.zero;
        bodyInstance.transform.localRotation = Quaternion.identity;
        bodyInstance.transform.localScale = Vector3.one;

        if (bodySet != null && bodySet.BodySetPrefab != null)
        {
            GameObject bodySetInstance = Instantiate(bodySet.BodySetPrefab, bodyInstance.transform);
            bodySetInstance.name = bodySet.BodySetPrefab.name;
            bodySetInstance.transform.localPosition = Vector3.zero;
            bodySetInstance.transform.localRotation = Quaternion.identity;
            bodySetInstance.transform.localScale = Vector3.one;
        }

        if (customizations != null && customizations.Count > 0)
            CarCustomizationUtility.ApplySelections(bodyInstance.transform, customizations);

        StripVisualGameplayComponents(bodyInstance);
        return bodyInstance.transform;
    }

    private void BuildWheels(PlayerCarVisualSettings visual, VehicleSettings handling, SuspensionConfig suspension, ArcadePrototypeControllerRuntimeTuning controllerTuning)
    {
        float wheelHeight = ResolveWheelCenterHeight(visual, handling, suspension);
        float restLength = ResolveRestLength(suspension, controllerTuning);
        float wheelRadius = ResolveWheelRadius(visual, handling);
        float wheelWidth = ResolveWheelWidth(handling);

        float halfWheelBase = visual.wheelBase * 0.5f;
        float halfAxle = visual.axleWidth * 0.5f;
        float frontZ = visual.zOffset + halfWheelBase;
        float rearZ = visual.zOffset - halfWheelBase;

        CreateWheel(visual, WheelNames[0], new Vector3(-halfAxle, wheelHeight + restLength, frontZ), restLength, wheelRadius, wheelWidth, false);
        CreateWheel(visual, WheelNames[1], new Vector3(halfAxle, wheelHeight + restLength, frontZ), restLength, wheelRadius, wheelWidth, true);
        CreateWheel(visual, WheelNames[2], new Vector3(-halfAxle, wheelHeight + restLength, rearZ), restLength, wheelRadius, wheelWidth, false);
        CreateWheel(visual, WheelNames[3], new Vector3(halfAxle, wheelHeight + restLength, rearZ), restLength, wheelRadius, wheelWidth, true);
    }

    private void CreateWheel(PlayerCarVisualSettings visual, string wheelName, Vector3 localPosition, float restLength, float wheelRadius, float wheelWidth, bool rightSide)
    {
        GameObject hardpoint = new GameObject(wheelName);
        hardpoint.transform.SetParent(transform, false);
        hardpoint.transform.localPosition = localPosition;
        hardpoint.transform.localRotation = Quaternion.identity;
        hardpoint.transform.localScale = Vector3.one;

        GameObject visualRootObject = new GameObject("VisualRoot");
        Transform visualRoot = visualRootObject.transform;
        visualRoot.SetParent(hardpoint.transform, false);
        visualRoot.localPosition = Vector3.down * restLength;
        visualRoot.localRotation = Quaternion.identity;
        visualRoot.localScale = Vector3.one;

        if (visual != null && visual.wheelPrefab != null)
        {
            GameObject wheelInstance = Instantiate(visual.wheelPrefab, visualRoot);
            wheelInstance.name = "WheelMesh";
            wheelInstance.transform.localPosition = Vector3.zero;
            wheelInstance.transform.localRotation = rightSide
                ? Quaternion.Euler(0.0f, 0.0f, 180.0f)
                : Quaternion.identity;
            wheelInstance.transform.localScale = Vector3.one;
            StripVisualGameplayComponents(wheelInstance);
            return;
        }

        Transform primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
        primitive.name = "WheelMesh";
        primitive.SetParent(visualRoot, false);
        primitive.localPosition = Vector3.zero;
        primitive.localRotation = Quaternion.Euler(0.0f, 0.0f, rightSide ? 270.0f : 90.0f);
        primitive.localScale = new Vector3(wheelRadius * 2.0f, wheelWidth * 0.5f, wheelRadius * 2.0f);
        Collider primitiveCollider = primitive.GetComponent<Collider>();
        if (primitiveCollider != null)
            DestroyRuntimeObject(primitiveCollider);
    }

    private static void StripVisualGameplayComponents(GameObject root)
    {
        if (root == null)
            return;

        for (int typeIndex = 0; typeIndex < VisualGameplayComponentTypes.Length; typeIndex++)
        {
            System.Type componentType = VisualGameplayComponentTypes[typeIndex];
            Component[] components = root.GetComponentsInChildren(componentType, true);
            for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                DestroyRuntimeObject(components[componentIndex]);
        }
    }

    private void RebuildBodyCollider(PlayerCarVisualSettings visual, VehicleSettings handling, SuspensionConfig suspension)
    {
        BoxCollider collider = gameObject.AddComponent<BoxCollider>();
        float wheelHeight = ResolveWheelCenterHeight(visual, handling, suspension);
        float bodyWidth = Mathf.Max(1.6f, visual.axleWidth + 0.35f);
        float bodyLength = Mathf.Max(3.2f, visual.wheelBase + 0.65f);
        collider.center = new Vector3(0.0f, wheelHeight + 0.68f, 0.0f);
        collider.size = new Vector3(bodyWidth, 0.7f, bodyLength);
    }

    private static void ApplyPaint(Transform bodyRoot, PlayerCarVisualSettings visual, Color paint)
    {
        if (bodyRoot == null || visual == null)
            return;

        int propertyId = !string.IsNullOrWhiteSpace(visual.paintProperty)
            ? Shader.PropertyToID(visual.paintProperty)
            : Shader.PropertyToID("_MainColor");

        Renderer[] renderers = bodyRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            bool hasProperty = false;
            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material != null && material.HasProperty(propertyId))
                {
                    hasProperty = true;
                    break;
                }
            }

            if (!hasProperty)
                continue;

            renderer.GetPropertyBlock(block);
            block.SetColor(propertyId, paint);
            renderer.SetPropertyBlock(block);
        }
    }

    private static float ResolveWheelCenterHeight(PlayerCarVisualSettings visual, VehicleSettings handling, SuspensionConfig suspension)
    {
        float wheelRadius = ResolveWheelRadius(visual, handling);
        float wheelHeight = visual != null ? Mathf.Max(wheelRadius, visual.wheelHeight) : wheelRadius;
        if (suspension != null)
        {
            suspension.Validate();
            if (suspension.applyVisualRideHeight)
                wheelHeight = Mathf.Max(wheelRadius, suspension.visualWheelHeight);
        }

        return wheelHeight;
    }

    private static float ResolveWheelRadius(PlayerCarVisualSettings visual, VehicleSettings handling)
    {
        if (handling != null)
            return Mathf.Clamp(handling.wheelRadius, 0.05f, 2.0f);

        if (visual != null)
            return Mathf.Clamp(visual.wheelHeight, 0.05f, 2.0f);

        return 0.35f;
    }

    private static float ResolveWheelWidth(VehicleSettings handling)
    {
        return handling != null ? Mathf.Clamp(handling.wheelWidth, 0.05f, 1.0f) : 0.22f;
    }

    private static float ResolveRestLength(SuspensionConfig suspension, ArcadePrototypeControllerRuntimeTuning controllerTuning)
    {
        if (controllerTuning != null && controllerTuning.springStartToWheelCenterDistanceOverride > 0.0f)
            return Mathf.Clamp(controllerTuning.springStartToWheelCenterDistanceOverride, 0.02f, 1.0f);

        if (suspension == null)
            return 0.1f;

        suspension.Validate();
        float distance = Mathf.Clamp(suspension.suspensionDistance, 0.05f, 0.5f);
        float target = Mathf.Clamp01(suspension.suspensionTargetPosition);
        return Mathf.Clamp(distance * (1.0f - target), 0.02f, distance);
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(target);
        else
            Object.DestroyImmediate(target);
    }
}

public sealed class ArcadePrototypeSceneBootstrap : MonoBehaviour
{
    private const string SceneName = "ArcadePrototype";
    private const string PrefabResourcePath = "Vehicles/ArcadePrototypeVehicle";
    private const string BootstrapRootName = "ArcadePrototypeSceneBootstrap";

    private bool initialized;
    private GameObject spawnedVehicle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHooks()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, SceneName, System.StringComparison.Ordinal))
            return;

        ArcadePrototypeSceneBootstrap bootstrap = Object.FindFirstObjectByType<ArcadePrototypeSceneBootstrap>();
        if (bootstrap == null)
        {
            GameObject root = new GameObject(BootstrapRootName);
            bootstrap = root.AddComponent<ArcadePrototypeSceneBootstrap>();
        }

        bootstrap.TryInitialize();
    }

    private void Start()
    {
        TryInitialize();
    }

    private void TryInitialize()
    {
        if (initialized)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.name, SceneName, System.StringComparison.Ordinal))
            return;

        initialized = true;
        SpawnPrototypeVehicle();
        GameLaunchRuntime.Reset();
    }

    private void SpawnPrototypeVehicle()
    {
        if (spawnedVehicle != null)
            return;

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError($"ArcadePrototypeSceneBootstrap: missing prefab at Resources/{PrefabResourcePath}.", this);
            return;
        }

        Vector3 spawnPosition = ResolveSpawnPosition();
        Quaternion spawnRotation = Quaternion.identity;

        GameObject instance = Instantiate(prefab, spawnPosition, spawnRotation);
        instance.name = prefab.name;

        ArcadePrototypeRigBuilder rigBuilder = instance.GetComponent<ArcadePrototypeRigBuilder>();
        if (rigBuilder == null)
            rigBuilder = instance.AddComponent<ArcadePrototypeRigBuilder>();

        ArcadePrototypeSceneTuning tuning = ResolveSceneTuning();
        ArcadePrototypeSceneTuning.ResolvedSetup resolved = tuning.Resolve();

        rigBuilder.Build(
            resolved.carConfig,
            resolved.handling,
            resolved.suspension,
            resolved.controllerTuning,
            resolved.bodySet,
            resolved.customizations,
            resolved.paint,
            resolved.hasPaint);

        ArcadePrototypeCarController controller = instance.GetComponent<ArcadePrototypeCarController>();
        if (controller == null)
            controller = instance.AddComponent<ArcadePrototypeCarController>();
        controller.ApplyRuntimeTuning(resolved.controllerTuning);
        controller.ConfigureResolved(resolved.handling, resolved.engine, resolved.suspension);
        controller.PrimeSpawnPose();

        FollowCarCamera followCamera = Object.FindFirstObjectByType<FollowCarCamera>();
        if (followCamera != null)
            followCamera.SetTarget(instance.transform);

        spawnedVehicle = instance;
    }

    private ArcadePrototypeSceneTuning ResolveSceneTuning()
    {
        ArcadePrototypeSceneTuning tuning = Object.FindFirstObjectByType<ArcadePrototypeSceneTuning>();
        if (tuning != null)
            return tuning;

        GameObject tuningRoot = new GameObject("ArcadePrototypeSetup");
        tuningRoot.hideFlags = HideFlags.DontSave;
        return tuningRoot.AddComponent<ArcadePrototypeSceneTuning>();
    }

    private static Vector3 ResolveSpawnPosition()
    {
        if (VehicleSpawnUtility.TryGetGroundHeight(Vector3.zero, 3.0f, 20.0f, out float groundY))
            return new Vector3(0.0f, groundY + 1.25f, 0.0f);

        return new Vector3(0.0f, 1.25f, 0.0f);
    }

}
