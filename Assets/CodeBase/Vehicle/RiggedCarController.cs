using UnityEngine;
using System.Collections.Generic;

public class RiggedCarController : CarControllerBase
{
    private const string WeaponTag = "Weapon";
    private static bool webGlColliderWarningShown;

    [Header("Prefabs")]
    [HideInInspector, SerializeField] private GameObject bodyPrefab;
    [HideInInspector, SerializeField] private GameObject wheelPrefab;
    [HideInInspector, SerializeField] private bool addBodyCollider = false;
    [HideInInspector, SerializeField] private bool generateConvexBodyColliders = false;

    [Header("Wheel Layout (Local)")]
    [HideInInspector, SerializeField, Min(0.2f)] private float wheelBase = 2.4f;
    [HideInInspector, SerializeField, Min(0.2f)] private float axleWidth = 1.5f;
    [HideInInspector, SerializeField] private float zOffset = 0.0f;
    [HideInInspector, SerializeField] private float wheelHeight = 0.35f;

    [Header("Rig Options")]
    [HideInInspector, SerializeField, Range(0.0f, 1.0f)] private float bodyRootHeightFactor = 0.3f;
    [HideInInspector, SerializeField] private bool liveWheelPositions = true;
    [HideInInspector, SerializeField] private BodySetConfig bodySetConfig;
    [HideInInspector, SerializeField] private List<CarCustomizationSelection> customizationSelections = new List<CarCustomizationSelection>();
    private Transform activeBodySetInstance;

    public void ApplyVisualSettings(PlayerCarVisualSettings settings)
    {
        if (settings == null)
            return;

        bool bodyChanged = bodyPrefab != settings.bodyPrefab;
        bool wheelChanged = wheelPrefab != settings.wheelPrefab;
        settings.Validate();
        bodyPrefab = settings.bodyPrefab;
        wheelPrefab = settings.wheelPrefab;
        addBodyCollider = settings.addBodyCollider;
        generateConvexBodyColliders = ShouldUseConvexBodyColliders(settings.generateConvexBodyColliders);
        wheelBase = settings.wheelBase;
        axleWidth = settings.axleWidth;
        zOffset = settings.zOffset;
        wheelHeight = settings.wheelHeight;
        bodyRootHeightFactor = settings.bodyRootHeightFactor;
        liveWheelPositions = settings.liveWheelPositions;

        if (Application.isPlaying)
            RefreshVisuals(bodyChanged, wheelChanged);

        if (Application.isPlaying && liveWheelPositions && GetComponentsInChildren<WheelCollider>(true).Length > 0)
            ApplyWheelPositions();
    }

    public void ApplySuspensionVisualSettings(SuspensionConfig suspensionConfig)
    {
        if (suspensionConfig == null)
            return;

        suspensionConfig.Validate();
        if (!suspensionConfig.applyVisualRideHeight)
            return;

        wheelHeight = suspensionConfig.visualWheelHeight;
        if (liveWheelPositions)
            ApplyWheelPositions();
    }

    public void ApplyBodySetConfig(BodySetConfig bodySet)
    {
        bodySetConfig = bodySet;
        if (!Application.isPlaying)
            return;

        ApplyBodySetToCurrentBody();
    }

    public void ApplyCustomizationSelections(IReadOnlyList<CarCustomizationSelection> selections)
    {
        customizationSelections = selections != null
            ? new List<CarCustomizationSelection>(selections)
            : new List<CarCustomizationSelection>();

        if (!Application.isPlaying)
            return;

        ApplyCustomizationsToCurrentBody();
    }

    protected override void BuildCar()
    {
        if (bodyPrefab != null)
        {
            GameObject bodyInstance = Instantiate(bodyPrefab, transform);
            bodyInstance.name = "Body";
            DisableStaticFlags(bodyInstance);
            if (addBodyCollider)
                EnsureBodyCollider(bodyInstance);
            if (generateConvexBodyColliders)
                GenerateConvexBodyColliders(bodyInstance);
        }

        ApplyBodySetToCurrentBody();

        Vector3 flPos;
        Vector3 frPos;
        Vector3 rlPos;
        Vector3 rrPos;
        GetWheelPositions(out flPos, out frPos, out rlPos, out rrPos);

        Transform fl = CreateWheelWithDefaultVisual("FrontLeft", flPos, true, true, false);
        Transform fr = CreateWheelWithDefaultVisual("FrontRight", frPos, true, true, false);
        Transform rl = CreateWheelWithDefaultVisual("RearLeft", rlPos, true, false, true);
        Transform rr = CreateWheelWithDefaultVisual("RearRight", rrPos, true, false, true);

        AttachWheelPrefab(fl);
        AttachWheelPrefab(fr);
        AttachWheelPrefab(rl);
        AttachWheelPrefab(rr);
    }

    protected override void UpdateCenterOfMass()
    {
        Transform body = transform.Find("Body");
        if (body != null)
        {
            Vector3 localPos = transform.InverseTransformPoint(body.position);
            rb.centerOfMass = new Vector3(0.0f, localPos.y * bodyRootHeightFactor, 0.0f);
        }
        else
        {
            base.UpdateCenterOfMass();
        }
    }

    private void AttachWheelPrefab(Transform visualRoot)
    {
        if (wheelPrefab == null || visualRoot == null)
            return;

        Transform cylinder = visualRoot.Find("Visual");
        if (cylinder != null)
        {
            MeshRenderer renderer = cylinder.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;
        }

        GameObject instance = Instantiate(wheelPrefab, visualRoot);
        instance.name = "WheelMesh";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        Transform wheelRoot = visualRoot.parent;
        if (wheelRoot != null && (wheelRoot.name == "FrontRight" || wheelRoot.name == "RearRight"))
            instance.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 180.0f);
        StripPhysicsComponents(instance);
    }

    private void RefreshVisuals(bool bodyChanged, bool wheelChanged)
    {
        if (!bodyChanged && !wheelChanged)
            return;

        if (GetComponentsInChildren<WheelCollider>(true).Length == 0)
            return;

        if (bodyChanged)
            RebuildBody();

        if (wheelChanged)
            RebuildWheelVisuals();
    }

    private void RebuildBody()
    {
        Transform existingBody = transform.Find("Body");
        if (existingBody != null)
            Destroy(existingBody.gameObject);

        if (bodyPrefab == null)
            return;

        GameObject bodyInstance = Instantiate(bodyPrefab, transform);
        bodyInstance.name = "Body";
        if (addBodyCollider)
            EnsureBodyCollider(bodyInstance);
        if (generateConvexBodyColliders)
            GenerateConvexBodyColliders(bodyInstance);

        UpdateCenterOfMass();

        if (!generateConvexBodyColliders)
        {
            CarDamageController damageController = GetComponent<CarDamageController>();
            if (damageController != null)
                damageController.ReinitializeFromBody(bodyInstance);
        }

        ApplyBodySetToCurrentBody();
    }

    private void RebuildWheelVisuals()
    {
        WheelCollider[] colliders = GetComponentsInChildren<WheelCollider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Transform visualRoot = colliders[i].transform.Find("VisualRoot");
            if (visualRoot == null)
                visualRoot = colliders[i].transform.Find("Visual");
            if (visualRoot == null)
                continue;

            for (int childIndex = visualRoot.childCount - 1; childIndex >= 0; childIndex--)
            {
                Transform child = visualRoot.GetChild(childIndex);
                if (child != null && child.name == "WheelMesh")
                    Destroy(child.gameObject);
            }

            if (wheelPrefab == null)
            {
                Transform cylinder = visualRoot.Find("Visual");
                if (cylinder != null)
                {
                    MeshRenderer renderer = cylinder.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        renderer.enabled = true;
                }
                continue;
            }

            AttachWheelPrefab(visualRoot);
        }
    }

    private void EnsureBodyCollider(GameObject bodyInstance)
    {
        if (bodyInstance.GetComponentInChildren<Collider>() != null)
            return;

        Renderer[] renderers = bodyInstance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        BoxCollider collider = bodyInstance.AddComponent<BoxCollider>();
        Vector3 lossyScale = bodyInstance.transform.lossyScale;
        collider.center = bodyInstance.transform.InverseTransformPoint(bounds.center);
        collider.size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, lossyScale.x),
            bounds.size.y / Mathf.Max(0.0001f, lossyScale.y),
            bounds.size.z / Mathf.Max(0.0001f, lossyScale.z));
    }

    private static void DisableStaticFlags(GameObject root)
    {
        if (root == null)
            return;

        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            GameObject obj = nodes[i].gameObject;
            if (obj.isStatic)
                obj.isStatic = false;
        }
    }

    private void GenerateConvexBodyColliders(GameObject bodyInstance)
    {
        if (bodyInstance == null)
            return;

        foreach (Collider existing in bodyInstance.GetComponentsInChildren<Collider>(true))
        {
            if (IsUnderTaggedTransform(existing.transform, WeaponTag))
                continue;
            Destroy(existing);
        }

        MeshFilter[] meshFilters = bodyInstance.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            if (IsUnderTaggedTransform(meshFilters[i].transform, WeaponTag))
                continue;

            Mesh mesh = meshFilters[i].sharedMesh;
            if (mesh == null)
                continue;

            MeshCollider collider = meshFilters[i].gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
        }

        SkinnedMeshRenderer[] skinned = bodyInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            if (IsUnderTaggedTransform(skinned[i].transform, WeaponTag))
                continue;

            if (skinned[i].sharedMesh == null)
                continue;

            Mesh baked = new Mesh();
            skinned[i].BakeMesh(baked);
            MeshCollider collider = skinned[i].gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = baked;
            collider.convex = true;
        }

        if (bodyInstance.GetComponentsInChildren<Collider>(true).Length == 0)
            EnsureBodyCollider(bodyInstance);

        CarDamageController damageController = GetComponent<CarDamageController>();
        if (damageController != null)
            damageController.ReinitializeFromBody(bodyInstance);
    }

    private static bool ShouldUseConvexBodyColliders(bool requested)
    {
        if (!requested)
            return false;

        if (Application.platform != RuntimePlatform.WebGLPlayer)
            return true;

        if (!webGlColliderWarningShown)
        {
            Debug.LogWarning(
                "RiggedCarController: convex body collider generation is disabled on WebGL to avoid slow mesh simplification and runtime rendering issues.");
            webGlColliderWarningShown = true;
        }

        return false;
    }

    private static bool IsUnderTaggedTransform(Transform node, string tag)
    {
        Transform current = node;
        while (current != null)
        {
            if (current.CompareTag(tag))
                return true;
            current = current.parent;
        }

        return false;
    }

    private void ApplyBodySetToCurrentBody()
    {
        Transform body = transform.Find("Body");
        if (body == null)
            return;
        if (activeBodySetInstance != null)
        {
            Destroy(activeBodySetInstance.gameObject);
            activeBodySetInstance = null;
        }

        if (bodySetConfig == null || bodySetConfig.BodySetPrefab == null)
        {
            ApplyCustomizationsToCurrentBody();
            return;
        }

        GameObject instance = Instantiate(bodySetConfig.BodySetPrefab, body);
        instance.name = bodySetConfig.BodySetPrefab.name;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Transform root = body.Find("Root");
        if (root != null)
            instance.transform.SetSiblingIndex(root.GetSiblingIndex() + 1);

        activeBodySetInstance = instance.transform;
        ApplyCustomizationsToCurrentBody();

        if (generateConvexBodyColliders)
        {
            GenerateConvexBodyColliders(body.gameObject);
            return;
        }

        CarDamageController damageController = GetComponent<CarDamageController>();
        if (damageController != null)
            damageController.ReinitializeFromBody(body.gameObject);
    }

    private void ApplyCustomizationsToCurrentBody()
    {
        Transform body = transform.Find("Body");
        if (body == null)
            return;

        CarCustomizationUtility.ApplySelections(body, customizationSelections);
    }

    private void StripPhysicsComponents(GameObject root)
    {
        foreach (WheelCollider wc in root.GetComponentsInChildren<WheelCollider>(true))
            Destroy(wc);

        foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
            Destroy(col);

        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            Destroy(body);

        foreach (Joint joint in root.GetComponentsInChildren<Joint>(true))
            Destroy(joint);
    }

    private void ApplyWheelPositions()
    {
        Vector3 flPos;
        Vector3 frPos;
        Vector3 rlPos;
        Vector3 rrPos;
        GetWheelPositions(out flPos, out frPos, out rlPos, out rrPos);

        Transform fl = transform.Find("FrontLeft");
        if (fl != null) fl.localPosition = flPos;

        Transform fr = transform.Find("FrontRight");
        if (fr != null) fr.localPosition = frPos;

        Transform rl = transform.Find("RearLeft");
        if (rl != null) rl.localPosition = rlPos;

        Transform rr = transform.Find("RearRight");
        if (rr != null) rr.localPosition = rrPos;
    }

    private void GetWheelPositions(out Vector3 fl, out Vector3 fr, out Vector3 rl, out Vector3 rr)
    {
        float halfWheelBase = wheelBase * 0.5f;
        float halfAxle = axleWidth * 0.5f;
        float frontZ = zOffset + halfWheelBase;
        float rearZ = zOffset - halfWheelBase;

        fl = new Vector3(-halfAxle, wheelHeight, frontZ);
        fr = new Vector3(halfAxle, wheelHeight, frontZ);
        rl = new Vector3(-halfAxle, wheelHeight, rearZ);
        rr = new Vector3(halfAxle, wheelHeight, rearZ);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        if (liveWheelPositions)
            ApplyWheelPositions();
    }
}

