using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Rigidbody))]
public partial class CarDamageController : MonoBehaviour
{
    private static bool webGlComputeWarningShown;
    [Header("Texture")]
    [HideInInspector, SerializeField] private RenderTexture damageTexture;
    [HideInInspector, SerializeField] private Renderer targetRenderer;
    [HideInInspector, SerializeField] private Material[] targetMaterials;
    [HideInInspector, SerializeField] private string textureProperty = "_MainTex";
    [HideInInspector, SerializeField, Min(1)] private int textureWidth = 16;
    [HideInInspector, SerializeField, Min(1)] private int textureHeight = 8;

    [Header("Collision")]
    [HideInInspector, SerializeField] private string obstacleTag = "Obstacle";
    [HideInInspector, SerializeField] private DamageManager damageManager;
    [HideInInspector, SerializeField] private FollowCarCamera followCarCamera;
    [HideInInspector, SerializeField, Min(0.0001f)] private float impulseToColor = 0.0025f;
    [HideInInspector, SerializeField, Range(0.0f, 1.0f)] private float maxColorStep = 0.35f;
    [HideInInspector, SerializeField, Min(0.0f)] private float impulseToRadius = 0.02f;
    [HideInInspector, SerializeField, Min(0.0f)] private float impulseFromSpeedFactor = 0.25f;
    [HideInInspector, SerializeField, Min(0)] private int maxRadiusCells = 3;
    [HideInInspector, SerializeField, Min(0.1f)] private float minSpeedForDamageKmh = 5.0f;
    [HideInInspector, SerializeField, Min(1.0f)] private float maxSpeedForDamageKmh = 80.0f;
    [HideInInspector, SerializeField, Range(0.0f, 1.0f)] private float minDamageScale = 0.01f;
    [HideInInspector, SerializeField, Range(0.0f, 1.0f)] private float glancingDamageScale = 0.2f;
    [HideInInspector, SerializeField, Min(0.1f)] private float impactAlignmentPower = 1.5f;
    [HideInInspector, SerializeField] private AnimationCurve speedDamageCurve = new AnimationCurve(
        new Keyframe(0.0f, 0.0f),
        new Keyframe(1.0f, 1.0f));
    [HideInInspector, SerializeField, Range(0.0f, 2.0f)] private float speedRadiusBoost = 0.4f;
    [HideInInspector, SerializeField] private AnimationCurve damageFalloff = new AnimationCurve(
        new Keyframe(0.0f, 1.0f),
        new Keyframe(1.0f, 0.0f));
    [HideInInspector, SerializeField] private AnimationCurve verticalDamageFalloff = new AnimationCurve(
        new Keyframe(0.0f, 1.0f),
        new Keyframe(0.5f, 0.5f),
        new Keyframe(1.0f, 0.0f));

    [Header("Compute Deformation")]
    [HideInInspector, SerializeField] private bool deformMeshWithCompute = false;
    [HideInInspector, SerializeField] private ComputeShader damageDeformCompute;
    [HideInInspector, SerializeField] private string[] deformIgnoreNameFragments;
    [HideInInspector, SerializeField] private bool computeUseNormals = true;
    [HideInInspector, SerializeField] private bool computeRecalculateNormals = true;
    [HideInInspector, SerializeField, Range(0.0f, 0.5f)] private float computeDeformAmplitude = 0.08f;
    [HideInInspector, SerializeField, Range(-1.0f, 1.0f)] private float computeDeformDirection = -1.0f;
    [HideInInspector, SerializeField, Range(0.0f, 40.0f)] private float computeDeformSinFrequency = 10.0f;
    [HideInInspector, SerializeField, Range(0.0f, 1.0f)] private float computeDeformSinStrength = 0.25f;
    [HideInInspector, SerializeField, Range(0.0f, 1.0f)] private float computeYieldThreshold = 0.15f;
    [HideInInspector, SerializeField, Range(0.1f, 8.0f)] private float computeHardening = 2.0f;
    [HideInInspector, SerializeField, Range(0.0f, 0.5f)] private float computeMaxDeform = 0.25f;
    [HideInInspector, SerializeField] private bool computeTwoLevelDamage = false;
    [HideInInspector, SerializeField, Range(1, 6)] private int computeCoarseRadius = 2;
    [HideInInspector, SerializeField, Range(0.0f, 1.0f)] private float computeCoarseWeight = 0.5f;
    [HideInInspector, SerializeField, Range(1.0f, 4.0f)] private float computeCoarseBoost = 1.5f;
    [HideInInspector, SerializeField, Range(0.0f, 0.6f)] private float computeCoarseDeformMeters = 0.3f;

    [Header("Map")]
    [HideInInspector, SerializeField] private bool includeTriggers = false;
    [HideInInspector, SerializeField] private bool includeWheelColliders = false;

    [Header("Debug")]
    [HideInInspector, SerializeField] private RenderTexture runtimeTexture;
    [HideInInspector, SerializeField] private bool showDebugVoxels = false;
    [HideInInspector, SerializeField, Min(0.1f)] private float debugVoxelHeightScale = 1.0f;
    [HideInInspector, SerializeField, Range(0.05f, 1.0f)] private float debugVoxelOpacity = 0.5f;
    [HideInInspector, SerializeField] private float debugVoxelYOffset = 0.02f;
    [HideInInspector, SerializeField, Range(0.1f, 2.0f)] private float debugVoxelScale = 0.98f;

    [Header("Computed")]
    [HideInInspector, SerializeField] private Vector3 computedVehicleSize;
    [HideInInspector, SerializeField] private Vector3 computedVoxelSize;
    [HideInInspector, SerializeField] private Vector3 computedBoundsMin;
    [HideInInspector, SerializeField] private Vector3 computedBoundsMax;

    private Texture2D cpuTexture;
    private Bounds damageBounds;
    private bool hasBounds;
    private bool isInitialized;
    private float[,] heightMap;
    private static Material voxelMaterial;
    private static Mesh voxelMesh;
    private MeshDeformTarget[] deformTargets;
    private bool applyComputeOnInit;
    private bool computeRefreshQueued;
    private const int VelocitySampleCount = 8;
    private readonly Vector3[] velocitySamples = new Vector3[VelocitySampleCount];
    private int velocitySampleIndex;
    private int velocitySampleFilled;
    private bool obstacleTagWarningShown;
    private bool obstacleTagIsValid = true;

    private struct MeshDeformTarget
    {
        public MeshFilter Filter;
        public SkinnedMeshRenderer Skinned;
        public Mesh Mesh;
        public Vector3[] OriginalVertices;
        public Vector3[] OriginalNormals;
        public bool HasNormals;
        public Color[] OriginalColors;
        public Color[] WorkingColors;
        public ComputeBuffer VertexBuffer;
        public ComputeBuffer NormalBuffer;
        public ComputeBuffer OutputBuffer;
        public bool ReadbackPending;
    }

    public void InitializeFromBody(GameObject bodyRoot)
    {
        if (isInitialized)
            return;

        if (bodyRoot == null)
        {
            InitializeFromColliders(GetComponentsInChildren<Collider>(true));
            return;
        }

        InitializeFromSources(
            bodyRoot.GetComponentsInChildren<Collider>(true),
            bodyRoot.GetComponentsInChildren<Renderer>(true));
    }

    public void ReinitializeFromBody(GameObject bodyRoot)
    {
        ResetInitializationState();

        if (bodyRoot == null)
        {
            InitializeFromColliders(GetComponentsInChildren<Collider>(true));
            return;
        }

        InitializeFromSources(
            bodyRoot.GetComponentsInChildren<Collider>(true),
            bodyRoot.GetComponentsInChildren<Renderer>(true));
    }

    public void InitializeFromColliders(Collider[] colliders)
    {
        if (isInitialized)
            return;

        InitializeFromSources(colliders, null);
    }

    private void InitializeFromSources(Collider[] colliders, Renderer[] renderers)
    {
        CreateTexture();
        GenerateMapFromColliders(colliders, renderers);
        CacheDeformTargets();
        isInitialized = hasBounds;
        if (applyComputeOnInit && deformMeshWithCompute)
            ApplyComputeDeformation();
        applyComputeOnInit = false;
    }

    private void ResetInitializationState()
    {
        ReleaseDeformTargets();
        hasBounds = false;
        isInitialized = false;
        heightMap = null;
        computeRefreshQueued = false;

        if (cpuTexture != null)
        {
            DestroyRuntimeObject(cpuTexture);
            cpuTexture = null;
        }

        if (runtimeTexture != null && damageTexture == null)
        {
            runtimeTexture.Release();
            DestroyRuntimeObject(runtimeTexture);
            runtimeTexture = null;
        }
    }

    private static void DestroyRuntimeObject(Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    public void RepairDamage()
    {
        if (cpuTexture == null || runtimeTexture == null)
            return;

        ClearDamageTexture();
        if (deformMeshWithCompute)
        {
            if (deformTargets == null)
                CacheDeformTargets();
            ApplyComputeDeformation();
        }
        else
        {
            ResetVertexColorsAlphaOne();
        }

        NotifyDamageMapChanged();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!isInitialized)
            return;

        if (!IsObstacle(collision))
            return;

        ApplyCollisionDamage(collision);
    }

    private void Start()
    {
        // Ensure damage system is initialized even when external setup did not call InitializeFrom*.
        if (!isInitialized)
            InitializeFromColliders(GetComponentsInChildren<Collider>(true));
    }

    private void Awake()
    {
        ApplyPlatformCompatibility();
        applyComputeOnInit = true;
        velocitySampleIndex = 0;
        velocitySampleFilled = 0;
        obstacleTagIsValid = string.IsNullOrWhiteSpace(obstacleTag) || IsValidTag(obstacleTag);
        if (damageManager == null)
            damageManager = GetComponent<DamageManager>();
        if (followCarCamera == null)
            followCarCamera = FindFirstObjectByType<FollowCarCamera>();
    }

    private void ApplyPlatformCompatibility()
    {
        if (deformMeshWithCompute && (Application.platform == RuntimePlatform.WebGLPlayer || !SystemInfo.supportsComputeShaders))
        {
            deformMeshWithCompute = false;
            damageDeformCompute = null;

            if (!webGlComputeWarningShown)
            {
                Debug.LogWarning(
                    "CarDamageController: compute mesh deformation is disabled on WebGL / unsupported GPU because it is not reliable in this runtime.",
                    this);
                webGlComputeWarningShown = true;
            }
        }
    }

    private void FixedUpdate()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
            return;

        velocitySamples[velocitySampleIndex] = rb.linearVelocity;
        velocitySampleIndex = (velocitySampleIndex + 1) % VelocitySampleCount;
        if (velocitySampleFilled < VelocitySampleCount)
            velocitySampleFilled++;
    }

    private void OnRenderObject()
    {
        if (!showDebugVoxels || !isInitialized || !hasBounds || cpuTexture == null || heightMap == null)
            return;

        Camera current = Camera.current;
        if (current == null || current.cameraType != CameraType.Game)
            return;

        EnsureVoxelResources();
        if (voxelMaterial == null || voxelMesh == null)
            return;

        float cellWidth = damageBounds.size.x / Mathf.Max(1, textureWidth);
        float cellDepth = damageBounds.size.z / Mathf.Max(1, textureHeight);
        float layerHeight = (computedVehicleSize.y / 3.0f) * debugVoxelHeightScale;
        Vector3 cellScale = new Vector3(cellWidth * debugVoxelScale, layerHeight, cellDepth * debugVoxelScale);

        voxelMaterial.SetPass(0);
        for (int x = 0; x < textureWidth; x++)
        {
            for (int y = 0; y < textureHeight; y++)
            {
                float localX = damageBounds.min.x + (x + 0.5f) * cellWidth;
                float localZ = damageBounds.min.z + (y + 0.5f) * cellDepth;
                Color color = cpuTexture.GetPixel(x, y);

                for (int layer = 0; layer < 3; layer++)
                {
                    float channel = layer == 0 ? color.b : (layer == 1 ? color.g : color.r);
                    Color layerColor = layer == 0 ? new Color(0.0f, 0.0f, channel, debugVoxelOpacity)
                        : (layer == 1 ? new Color(0.0f, channel, 0.0f, debugVoxelOpacity)
                        : new Color(channel, 0.0f, 0.0f, debugVoxelOpacity));
                    voxelMaterial.SetColor("_Color", layerColor);

                    float localY = damageBounds.min.y + debugVoxelYOffset + (layer + 0.5f) * layerHeight;
                    Vector3 localPos = new Vector3(localX, localY, localZ);
                    Vector3 worldPos = transform.TransformPoint(localPos);
                    Matrix4x4 matrix = Matrix4x4.TRS(worldPos, transform.rotation, cellScale);
                    Graphics.DrawMeshNow(voxelMesh, matrix);
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!showDebugVoxels || !isInitialized || !hasBounds || cpuTexture == null || heightMap == null)
            return;

        float cellWidth = damageBounds.size.x / Mathf.Max(1, textureWidth);
        float cellDepth = damageBounds.size.z / Mathf.Max(1, textureHeight);
        float layerHeight = (computedVehicleSize.y / 3.0f) * debugVoxelHeightScale;
        Vector3 cellScale = new Vector3(cellWidth * debugVoxelScale, layerHeight, cellDepth * debugVoxelScale);

        Matrix4x4 prevMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

        for (int x = 0; x < textureWidth; x++)
        {
            for (int y = 0; y < textureHeight; y++)
            {
                float localX = damageBounds.min.x + (x + 0.5f) * cellWidth;
                float localZ = damageBounds.min.z + (y + 0.5f) * cellDepth;
                Color color = cpuTexture.GetPixel(x, y);

                for (int layer = 0; layer < 3; layer++)
                {
                    float channel = layer == 0 ? color.b : (layer == 1 ? color.g : color.r);
                    Color layerColor = layer == 0 ? new Color(0.0f, 0.0f, channel, debugVoxelOpacity)
                        : (layer == 1 ? new Color(0.0f, channel, 0.0f, debugVoxelOpacity)
                        : new Color(channel, 0.0f, 0.0f, debugVoxelOpacity));
                    Gizmos.color = layerColor;

                    float localY = damageBounds.min.y + debugVoxelYOffset + (layer + 0.5f) * layerHeight;
                    Vector3 localPos = new Vector3(localX, localY, localZ);
                    Gizmos.DrawCube(localPos, cellScale);
                }
            }
        }

        Gizmos.matrix = prevMatrix;
    }

    private bool IsObstacle(Collision collision)
    {
        Collider col = collision.collider;
        if (col == null)
            return false;

        if (string.IsNullOrWhiteSpace(obstacleTag))
            return true;

        if (!obstacleTagIsValid)
        {
            if (!obstacleTagWarningShown)
            {
                Debug.LogWarning($"CarDamageController: obstacleTag '{obstacleTag}' is not defined in Tags. Damage collisions will not be filtered by tag.", this);
                obstacleTagWarningShown = true;
            }
            return true;
        }

        if (col.CompareTag(obstacleTag))
            return true;

        Rigidbody otherBody = col.attachedRigidbody;
        if (otherBody != null && otherBody.CompareTag(obstacleTag))
            return true;

        Transform t = col.transform;
        while (t != null)
        {
            if (t.CompareTag(obstacleTag))
                return true;
            t = t.parent;
        }

        return false;
    }

    private static bool IsValidTag(string tagName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tagName))
                return false;
            _ = GameObject.FindWithTag(tagName);
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    private void CreateTexture()
    {
        if (damageTexture != null)
        {
            runtimeTexture = damageTexture;
            textureWidth = runtimeTexture.width;
            textureHeight = runtimeTexture.height;
        }
        else
        {
            runtimeTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "DamageTexture_Runtime",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
        }

        if (!runtimeTexture.IsCreated())
            runtimeTexture.Create();

        cpuTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false, true)
        {
            name = "DamageTexture_CPU",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };

        Color[] pixels = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.black;

        cpuTexture.SetPixels(pixels);
        cpuTexture.Apply();
        Graphics.Blit(cpuTexture, runtimeTexture);

        if (targetRenderer != null)
            targetRenderer.material.SetTexture(textureProperty, runtimeTexture);
    }

    private void ClearDamageTexture()
    {
        Color[] pixels = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.black;

        cpuTexture.SetPixels(pixels);
        cpuTexture.Apply();
        Graphics.Blit(cpuTexture, runtimeTexture);
    }

    private void GenerateMapFromColliders(Collider[] colliders, Renderer[] renderers)
    {
        if (!TryBuildBounds(colliders, out damageBounds))
        {
            if (renderers == null || renderers.Length == 0 || !TryBuildBounds(renderers, out damageBounds))
            {
                hasBounds = false;
                heightMap = null;
                return;
            }
        }

        hasBounds = true;
        UpdateComputedSizes();
        heightMap = new float[textureWidth, textureHeight];
        if (colliders != null && colliders.Length > 0)
            PopulateHeightMap(colliders);
        else
            PopulateHeightMap(renderers);
    }

    private void CacheDeformTargets()
    {
        ReleaseDeformTargets();
        if (!deformMeshWithCompute)
            return;

        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var targets = new System.Collections.Generic.List<MeshDeformTarget>(filters.Length + skinned.Length);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            Mesh mesh = filter != null ? filter.mesh : null;
            if (filter == null || mesh == null)
                continue;
            if (ShouldIgnoreDeform(filter.name))
                continue;
            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"CarDamageController: mesh '{mesh.name}' on '{filter.name}' is not readable. Enable Read/Write in import settings or it will be skipped.",
                    this);
                continue;
            }

            targets.Add(new MeshDeformTarget
            {
                Filter = filter,
                Skinned = null,
                Mesh = mesh,
                OriginalVertices = mesh != null ? mesh.vertices : null,
                OriginalNormals = mesh != null ? mesh.normals : null,
                HasNormals = mesh != null && mesh.normals != null && mesh.normals.Length == mesh.vertexCount,
                OriginalColors = mesh != null ? mesh.colors : null
            });
            mesh.MarkDynamic();
        }

        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer renderer = skinned[i];
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            if (renderer == null || mesh == null)
                continue;
            if (ShouldIgnoreDeform(renderer.name))
                continue;
            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"CarDamageController: skinned mesh '{mesh.name}' on '{renderer.name}' is not readable. Enable Read/Write in import settings or it will be skipped.",
                    this);
                continue;
            }

            Mesh meshInstance = Object.Instantiate(mesh);
            meshInstance.name = $"{mesh.name}_DamageInstance";
            renderer.sharedMesh = meshInstance;

            targets.Add(new MeshDeformTarget
            {
                Filter = null,
                Skinned = renderer,
                Mesh = meshInstance,
                OriginalVertices = meshInstance.vertices,
                OriginalNormals = meshInstance.normals,
                HasNormals = meshInstance.normals != null && meshInstance.normals.Length == meshInstance.vertexCount,
                OriginalColors = meshInstance.colors
            });
        }

        deformTargets = targets.ToArray();
    }

    

}

