using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CarGunShooter : MonoBehaviour
{
    private const string BulletPointName = "BULLET_POINT";

    [Header("References")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private string shootPointName = "SHOOT_POINT";
    [SerializeField] private GameObject weaponPrefab;

    [Header("Fire")]
    [SerializeField, Min(0.1f)] private float fireRate = 10.0f;
    [SerializeField, Min(1.0f)] private float maxDistance = 400.0f;
    [SerializeField] private bool requireAimButton = true;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private LayerMask ignoreMask = 0;

    [Header("Tracer")]
    [SerializeField] private Material tracerMaterial;
    [SerializeField] private Color tracerColor = new Color(1.0f, 0.9f, 0.6f, 1.0f);
    [SerializeField, Min(0.001f)] private float tracerWidth = 0.04f;
    [SerializeField, Min(0.005f)] private float tracerFlightTime = 0.03f;
    [SerializeField, Min(0.05f)] private float tracerLifetime = 0.15f;

    [Header("Weapon Visual")]
    [SerializeField, Min(0.0f)] private float rightSectorHalfAngle = 70.0f;
    [SerializeField, Min(0.1f)] private float raiseTransitionAngle = 15.0f;
    [SerializeField, Min(0.0f)] private float raisedHeight = 0.4f;

    [Header("Impact VFX")]
    [SerializeField] private ParticleSystem hitParticlePrefab;
    [SerializeField] private FollowCarCamera followCarCamera;

    private Transform shootPoint;
    private float nextFireTime;
    private bool missingShootPointWarningShown;
    private bool missingBulletPointWarningShown;
    private static Material defaultTracerMaterial;
    private Transform weaponVisual;
    private Transform bulletPoint;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public float FireRate
    {
        get => fireRate;
        set => fireRate = Mathf.Max(0.1f, value);
    }

    private void Awake()
    {
        FindShootPoint();
        ResolveAimCamera();
        ResolveCameraFx();
        EnsureWeaponVisual();
    }

    private void Update()
    {
        if (shootPoint == null)
            FindShootPoint();
        if (aimCamera == null)
            ResolveAimCamera();
        if (followCarCamera == null)
            ResolveCameraFx();
        if (shootPoint == null)
            return;

        EnsureWeaponVisual();
        EnsureBulletPoint();
        UpdateWeaponVisualPose();

        if (requireAimButton && !IsAimHeld())
            return;
        if (!IsFireHeld())
            return;
        if (Time.time < nextFireTime)
            return;

        float shotInterval = 1.0f / Mathf.Max(0.1f, fireRate);
        nextFireTime = Time.time + shotInterval;
        FireShot();
    }

    private void FindShootPoint()
    {
        shootPoint = FindChildByName(transform, shootPointName);
        if (shootPoint == null)
        {
            if (!missingShootPointWarningShown)
            {
                Debug.LogWarning($"CarGunShooter: shoot point '{shootPointName}' not found on {name}", this);
                missingShootPointWarningShown = true;
            }
        }
        else
        {
            missingShootPointWarningShown = false;
        }
    }

    private void ResolveAimCamera()
    {
        if (aimCamera == null)
            aimCamera = Camera.main;
    }

    private void ResolveCameraFx()
    {
        if (followCarCamera == null)
            followCarCamera = FindFirstObjectByType<FollowCarCamera>();
    }

    private void EnsureWeaponVisual()
    {
        if (weaponVisual != null || weaponPrefab == null || shootPoint == null)
            return;

        GameObject instance = Instantiate(weaponPrefab, shootPoint.position, shootPoint.rotation, transform);
        weaponVisual = instance != null ? instance.transform : null;
        bulletPoint = null;
        missingBulletPointWarningShown = false;
    }

    private void EnsureBulletPoint()
    {
        if (weaponPrefab == null)
            return;
        if (weaponVisual == null)
            return;
        if (bulletPoint != null)
            return;

        bulletPoint = FindChildByName(weaponVisual, BulletPointName);
        if (bulletPoint == null && !missingBulletPointWarningShown)
        {
            Debug.LogWarning($"CarGunShooter: weapon prefab '{weaponPrefab.name}' must contain '{BulletPointName}'", this);
            missingBulletPointWarningShown = true;
        }
        else if (bulletPoint != null)
        {
            missingBulletPointWarningShown = false;
        }
    }

    private void UpdateWeaponVisualPose()
    {
        if (weaponVisual == null || shootPoint == null)
            return;

        Vector3 basePos = shootPoint.position;
        Vector3 aimTarget = GetAimTarget(basePos);
        Vector3 aimDir = aimTarget - basePos;
        if (aimDir.sqrMagnitude <= 0.0001f)
            aimDir = shootPoint.forward;
        aimDir.Normalize();

        float raise = ComputeRaiseOffset(aimDir);
        weaponVisual.position = basePos + transform.up * raise;
        weaponVisual.rotation = Quaternion.LookRotation(aimDir, transform.up);
    }

    private float ComputeRaiseOffset(Vector3 worldAimDir)
    {
        Vector3 localDir = transform.InverseTransformDirection(worldAimDir);
        Vector3 flat = new Vector3(localDir.x, 0.0f, localDir.z);
        if (flat.sqrMagnitude <= 0.0001f)
            return 0.0f;

        float angleFromRight = Mathf.Abs(Vector3.SignedAngle(Vector3.right, flat.normalized, Vector3.up));
        float transitionStart = Mathf.Max(0.0f, rightSectorHalfAngle);
        float transitionWidth = Mathf.Max(0.1f, raiseTransitionAngle);
        float t = Mathf.Clamp01((angleFromRight - transitionStart) / transitionWidth);
        t = t * t * (3.0f - 2.0f * t);
        return raisedHeight * t;
    }

    private void FireShot()
    {
        Transform muzzle = bulletPoint != null ? bulletPoint : shootPoint;
        if (weaponPrefab != null && bulletPoint == null)
            return;

        Vector3 start = muzzle.position;
        Vector3 aimTarget = GetAimTarget(start);
        Vector3 dir = (aimTarget - start);
        if (dir.sqrMagnitude <= 0.0001f)
            dir = muzzle.forward;
        dir.Normalize();

        Vector3 end = start + dir * maxDistance;
        int mask = GetEffectiveHitMask();
        if (Physics.Raycast(start, dir, out RaycastHit hit, maxDistance, mask, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            SpawnHitFx(hit.point, hit.normal);
        }

        StartCoroutine(PlayTracer(start, end));
        followCarCamera?.PlayShotShake();
    }

    private Vector3 GetAimTarget(Vector3 origin)
    {
        if (aimCamera == null)
            return origin + shootPoint.forward * maxDistance;

        Vector2 screenPos = GetAimScreenPosition();
        Ray ray = aimCamera.ScreenPointToRay(screenPos);
        int mask = GetEffectiveHitMask();
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, mask, QueryTriggerInteraction.Ignore))
            return hit.point;

        return ray.origin + ray.direction * maxDistance;
    }

    private static Vector2 GetAimScreenPosition()
    {
        if (Application.isBatchMode)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        if (Cursor.lockState == CursorLockMode.Locked)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
        return Input.mousePosition;
    }

    private IEnumerator PlayTracer(Vector3 start, Vector3 end)
    {
        GameObject tracerObj = new GameObject("BulletTracer");
        LineRenderer line = tracerObj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = tracerWidth;
        line.endWidth = tracerWidth;
        line.alignment = LineAlignment.View;
        line.generateLightingData = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        Material lineMaterial = tracerMaterial != null ? tracerMaterial : CreateDefaultTracerMaterial();
        line.material = lineMaterial;
        ApplyTracerColor(line, tracerColor);

        float holdTime = Mathf.Max(0.0f, tracerFlightTime);
        if (holdTime > 0.0f)
            yield return new WaitForSeconds(holdTime);

        float duration = Mathf.Max(0.05f, tracerLifetime);
        float elapsed = 0.0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1.0f - Mathf.Clamp01(elapsed / duration);
            Color c = tracerColor;
            c.a *= alpha;
            ApplyTracerColor(line, c);
            yield return null;
        }

        Destroy(tracerObj);
    }

    private static Material CreateDefaultTracerMaterial()
    {
        if (defaultTracerMaterial != null)
            return defaultTracerMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;
        defaultTracerMaterial = new Material(shader);
        return defaultTracerMaterial;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool IsFireHeld()
    {
        if (Application.isBatchMode)
            return false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.leftButton.isPressed;
        return false;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private static bool IsAimHeld()
    {
        if (Application.isBatchMode)
            return false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.rightButton.isPressed;
        return false;
#else
        return Input.GetMouseButton(1);
#endif
    }

    private int GetEffectiveHitMask()
    {
        return hitMask & ~ignoreMask;
    }

    private void SpawnHitFx(Vector3 position, Vector3 normal)
    {
        if (hitParticlePrefab == null)
            return;

        Quaternion rot = normal.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(normal, transform.up)
            : Quaternion.identity;
        ParticleSystem instance = Instantiate(hitParticlePrefab, position, rot);
        instance.Play(true);
        Destroy(instance.gameObject, 5.0f);
    }

    private static void ApplyTracerColor(LineRenderer line, Color color)
    {
        if (line == null)
            return;

        line.startColor = color;
        line.endColor = color;

        var block = new MaterialPropertyBlock();
        line.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        line.SetPropertyBlock(block);
    }
}
