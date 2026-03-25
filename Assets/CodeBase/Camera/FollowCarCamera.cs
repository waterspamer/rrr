using UnityEngine;
using UnityEngine.UI;
using PrimeTween;
using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(1200)]
public class FollowCarCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 pivotOffset = new Vector3(0.0f, 1.3f, 0.0f);

    [Header("Orbit")]
    [SerializeField] private float yaw = 0.0f;
    [SerializeField] private float pitch = 12.0f;
    [SerializeField, Min(0.01f)] private float yawSensitivity = 2.5f;
    [SerializeField, Min(0.01f)] private float pitchSensitivity = 2.0f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float minPitch = -25.0f;
    [SerializeField] private float maxPitch = 55.0f;
    [SerializeField] private bool orbitOnlyWhileAiming = true;

    [Header("Aim Mode (RMB)")]
    [SerializeField] private bool holdRightMouseToAim = true;
    [SerializeField] private Vector3 aimPivotOffset = new Vector3(0.0f, 1.45f, 0.0f);
    [SerializeField, Min(0.0f)] private float aimHeightOffset = 0.6f;
    [SerializeField, Min(0.5f)] private float aimDistance = 4.4f;
    [SerializeField, Min(0.1f)] private float aimTransitionSpeed = 10.0f;
    [SerializeField, Range(0.1f, 3.0f)] private float aimSensitivityMultiplier = 1.0f;
    [SerializeField] private bool adjustDofFocusWhileAiming = true;
    [SerializeField] private bool autoFindAimVolume = true;
    [SerializeField] private Volume aimVolume;
    [SerializeField] private LayerMask aimFocusMask = ~0;
    [SerializeField, Min(1.0f)] private float aimFocusMaxDistance = 120.0f;
    [SerializeField, Min(0.1f)] private float aimFocusSmooth = 10.0f;

    [Header("Free Mode Return")]
    [SerializeField, Min(0.0f)] private float freeReturnDelay = 1.0f;
    [SerializeField, Min(0.1f)] private float freeReturnSpeed = 4.5f;

    [Header("Distance")]
    [SerializeField, Min(1.0f)] private float defaultDistance = 6.5f;
    [SerializeField, Min(0.5f)] private float minDistance = 1.8f;
    [SerializeField, Min(1.0f)] private float maxDistance = 9.0f;
    [SerializeField, Min(0.01f)] private float zoomSpeed = 5.0f;
    [SerializeField] private bool adjustDistanceByPitch = true;
    [SerializeField] private float distanceAtMinPitch = 6.0f;
    [SerializeField] private float distanceAtMaxPitch = 7.0f;

    [Header("Free Mode")]
    [SerializeField] private bool autoFollowYaw = true;
    [SerializeField] private bool lockDistanceInFreeMode = true;
    [SerializeField] private bool useAimPivotOffset = false;

    [Header("Smoothing")]
    [SerializeField, Min(0.0f)] private float positionSmoothTime = 0.06f;
    [SerializeField, Min(0.0f)] private float rotationSmooth = 12.0f;
    [SerializeField, Min(0.01f)] private float collisionInSmoothTime = 0.03f;
    [SerializeField, Min(0.01f)] private float collisionOutSmoothTime = 0.15f;

    [Header("FOV")]
    [SerializeField] private bool fovBySpeed = true;
    [SerializeField, Min(1.0f)] private float baseFov = 60.0f;
    [SerializeField, Min(1.0f)] private float maxFov = 75.0f;
    [SerializeField, Min(1.0f)] private float speedForMaxFov = 160.0f;
    [SerializeField, Min(0.01f)] private float fovSmooth = 8.0f;

    [Header("Cursor")]
    [SerializeField] private bool hideCursorWhenLocked = true;

    [Header("Reticle")]
    [SerializeField] private bool showReticle = true;
    [SerializeField] private Color reticleColor = new Color(1.0f, 1.0f, 1.0f, 0.95f);
    [SerializeField, Min(2.0f)] private float reticleLineLength = 12.0f;
    [SerializeField, Min(1.0f)] private float reticleLineThickness = 2.0f;
    [SerializeField, Min(0.0f)] private float reticleGap = 10.0f;
    [SerializeField, Min(1.0f)] private float reticleCenterDotSize = 3.0f;

    [Header("Camera FX (PrimeTween)")]
    [SerializeField] private bool enableCameraShake = true;
    [SerializeField, Min(0.0f)] private float shotShakeStrength = 0.18f;
    [SerializeField, Min(0.0f)] private float shotShakeDuration = 0.1f;
    [SerializeField, Min(1)] private int shotShakeFrequency = 28;
    [SerializeField] private Vector3 shotPositionShake = new Vector3(0.03f, 0.02f, 0.06f);
    [SerializeField] private Vector3 shotRotationShake = new Vector3(0.4f, 0.8f, 1.3f);
    [SerializeField, Min(0.0f)] private float collisionMinImpulse = 2.0f;
    [SerializeField, Min(0.0f)] private float collisionMaxImpulse = 120.0f;
    [SerializeField, Min(0.0f)] private float collisionMinShakeStrength = 0.2f;
    [SerializeField, Min(0.0f)] private float collisionMaxShakeStrength = 1.0f;
    [SerializeField, Min(0.0f)] private float collisionShakeDuration = 0.22f;
    [SerializeField, Min(1)] private int collisionShakeFrequency = 18;
    [SerializeField] private Vector3 collisionPositionShake = new Vector3(0.08f, 0.06f, 0.14f);
    [SerializeField] private Vector3 collisionRotationShake = new Vector3(1.2f, 2.4f, 3.6f);

    private float targetDistance;
    private float currentDistance;
    private float distanceVelocity;
    private float yawFollowVelocity;
    private Vector3 positionVelocity;
    private float aimBlend;
    private float lastManualLookTime;
    private Camera cachedCamera;

    private Canvas reticleCanvas;
    private RectTransform reticleCanvasRect;
    private RectTransform reticleRoot;
    private Vector3 smoothedOrbitPosition;
    private Quaternion smoothedLookRotation;
    private Vector3 shakeLocalPosition;
    private Vector3 shakeLocalRotation;
    private Tween positionShakeTween;
    private Tween rotationShakeTween;
    private static readonly Action<FollowCarCamera, Vector3> SetShakeLocalPosition = static (self, value) => self.shakeLocalPosition = value;
    private static readonly Action<FollowCarCamera, Vector3> SetShakeLocalRotation = static (self, value) => self.shakeLocalRotation = value;
    private float currentFocusDistance;
    private float defaultFocusDistance;
    private DepthOfField cachedDof;
    private VolumeProfile runtimeAimVolumeProfile;

    private void Awake()
    {
        TryFindTarget();
        BuildReticleIfNeeded();
        cachedCamera = GetComponent<Camera>();
        ResolveAimVolume();
        SetupAimDof();
        if (cachedCamera != null)
            cachedCamera.fieldOfView = baseFov;

        targetDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
        currentDistance = targetDistance;

        if (target != null)
        {
            Vector3 flatForward = new Vector3(target.forward.x, 0.0f, target.forward.z);
            if (flatForward.sqrMagnitude > 0.0001f)
                yaw = Quaternion.LookRotation(flatForward.normalized, Vector3.up).eulerAngles.y;
        }

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        lastManualLookTime = Time.time;
        smoothedOrbitPosition = transform.position;
        smoothedLookRotation = transform.rotation;
        UpdateCursorState(force: true);
    }

    private void Update()
    {
        UpdateCursorState(force: false);
        UpdateOrbitInput();
        UpdateZoomInput();
        UpdateReticle();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            UpdateCursorState(force: true);
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            TryFindTarget();
            if (target == null)
                return;
        }

        Vector3 pivot = target.TransformPoint(pivotOffset);
        bool aiming = IsAiming();
        float aimTarget = aiming ? 1.0f : 0.0f;
        aimBlend = Mathf.MoveTowards(aimBlend, aimTarget, Time.deltaTime * Mathf.Max(0.1f, aimTransitionSpeed));
        if (useAimPivotOffset)
        {
            Vector3 aimPivot = target.TransformPoint(aimPivotOffset);
            pivot = Vector3.Lerp(pivot, aimPivot, aimBlend);
        }
        if (aimHeightOffset > 0.0f)
            pivot += target.up * (aimHeightOffset * aimBlend);

        if (!aiming)
        {
            if (Time.time - lastManualLookTime >= Mathf.Max(0.0f, freeReturnDelay))
            {
                if (autoFollowYaw)
                {
                    float followYaw = target.eulerAngles.y;
                    yaw = Mathf.SmoothDampAngle(yaw, followYaw, ref yawFollowVelocity, 1.0f / Mathf.Max(0.01f, freeReturnSpeed));
                }
            }
        }

        float blendedBaseDistance = Mathf.Lerp(targetDistance, Mathf.Clamp(aimDistance, minDistance, maxDistance), aimBlend);
        if (adjustDistanceByPitch && !(lockDistanceInFreeMode && !aiming))
        {
            float pitch01 = Mathf.InverseLerp(minPitch, maxPitch, pitch);
            float byPitch = Mathf.Lerp(distanceAtMinPitch, distanceAtMaxPitch, pitch01);
            blendedBaseDistance = Mathf.Lerp(blendedBaseDistance, byPitch, 1.0f - aimBlend);
        }
        Quaternion orbitRot = Quaternion.Euler(pitch, yaw, 0.0f);

        float desiredDistance = Mathf.Clamp(blendedBaseDistance, minDistance, maxDistance);
        {
            float smoothTime = desiredDistance < currentDistance ? collisionInSmoothTime : collisionOutSmoothTime;
            currentDistance = Mathf.SmoothDamp(currentDistance, desiredDistance, ref distanceVelocity, smoothTime);
        }

        Vector3 desiredPos = pivot + orbitRot * (Vector3.back * currentDistance);
        if (positionSmoothTime > 0.0f)
        {
            smoothedOrbitPosition = Vector3.SmoothDamp(smoothedOrbitPosition, desiredPos, ref positionVelocity, positionSmoothTime);
        }
        else
        {
            smoothedOrbitPosition = desiredPos;
        }

        Quaternion lookRot = Quaternion.LookRotation(pivot - smoothedOrbitPosition, Vector3.up);
        if (!aiming)
        {
            // Keep target centered in free mode: no rotation lag.
            smoothedLookRotation = lookRot;
        }
        else if (rotationSmooth > 0.0f)
        {
            smoothedLookRotation = Quaternion.Slerp(smoothedLookRotation, lookRot, 1.0f - Mathf.Exp(-rotationSmooth * Time.deltaTime));
        }
        else
        {
            smoothedLookRotation = lookRot;
        }

        transform.position = smoothedOrbitPosition + (smoothedLookRotation * shakeLocalPosition);
        transform.rotation = smoothedLookRotation * Quaternion.Euler(shakeLocalRotation);

        UpdateFovBySpeed();
        UpdateAimDof(pivot);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void UpdateOrbitInput()
    {
        bool aiming = IsAiming();
        if (orbitOnlyWhileAiming && !aiming)
            return;

        Vector2 lookDelta = GetMouseDelta();
        if (lookDelta.sqrMagnitude > 0.000001f)
            lastManualLookTime = Time.time;
        float sensitivityMul = aiming ? aimSensitivityMultiplier : 1.0f;
        yaw += lookDelta.x * yawSensitivity * sensitivityMul;
        float pitchSign = invertY ? 1.0f : -1.0f;
        pitch = Mathf.Clamp(pitch + lookDelta.y * pitchSensitivity * sensitivityMul * pitchSign, minPitch, maxPitch);
    }

    private void UpdateZoomInput()
    {
        float scroll = GetMouseScroll();
        if (Mathf.Abs(scroll) <= 0.0001f)
            return;

        targetDistance = Mathf.Clamp(targetDistance - scroll * zoomSpeed, minDistance, maxDistance);
    }

    private void TryFindTarget()
    {
        if (target != null)
            return;

        CarControllerBase car = FindFirstObjectByType<CarControllerBase>();
        if (car != null)
            target = car.transform;
    }

    private void UpdateCursorState(bool force)
    {
        CursorLockMode desiredMode = CursorLockMode.Locked;
        bool desiredVisible = !hideCursorWhenLocked;

        if (force || Cursor.lockState != desiredMode)
            Cursor.lockState = desiredMode;
        if (force || Cursor.visible != desiredVisible)
            Cursor.visible = desiredVisible;
    }

    private static Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.delta.ReadValue();
        return Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
    }

    private static float GetMouseScroll()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.scroll.ReadValue().y * 0.02f;
        return 0.0f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }

    private void BuildReticleIfNeeded()
    {
        if (!showReticle || reticleCanvas != null)
            return;

        GameObject canvasObject = new GameObject("CarOrbitReticleCanvas");
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            canvasObject.layer = uiLayer;

        reticleCanvas = canvasObject.AddComponent<Canvas>();
        reticleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        reticleCanvas.sortingOrder = 70;
        reticleCanvasRect = reticleCanvas.transform as RectTransform;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject root = new GameObject("Reticle");
        root.transform.SetParent(canvasObject.transform, false);
        reticleRoot = root.AddComponent<RectTransform>();
        reticleRoot.anchorMin = new Vector2(0.5f, 0.5f);
        reticleRoot.anchorMax = new Vector2(0.5f, 0.5f);
        reticleRoot.pivot = new Vector2(0.5f, 0.5f);
        float size = (reticleGap + reticleLineLength + reticleLineThickness) * 2.0f;
        reticleRoot.sizeDelta = new Vector2(size, size);

        CreateReticleRect("Top", new Vector2(0.0f, reticleGap + reticleLineLength * 0.5f), new Vector2(reticleLineThickness, reticleLineLength));
        CreateReticleRect("Bottom", new Vector2(0.0f, -(reticleGap + reticleLineLength * 0.5f)), new Vector2(reticleLineThickness, reticleLineLength));
        CreateReticleRect("Left", new Vector2(-(reticleGap + reticleLineLength * 0.5f), 0.0f), new Vector2(reticleLineLength, reticleLineThickness));
        CreateReticleRect("Right", new Vector2(reticleGap + reticleLineLength * 0.5f, 0.0f), new Vector2(reticleLineLength, reticleLineThickness));
        CreateReticleRect("Dot", Vector2.zero, new Vector2(reticleCenterDotSize, reticleCenterDotSize));
    }

    private void CreateReticleRect(string name, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(reticleRoot, false);
        Image image = obj.AddComponent<Image>();
        image.color = reticleColor;
        image.raycastTarget = false;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private void UpdateReticle()
    {
        if (!showReticle || reticleCanvas == null || reticleRoot == null || reticleCanvasRect == null)
            return;

        bool aiming = IsAiming();
        reticleCanvas.enabled = aiming;
        if (!aiming)
            return;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            reticleRoot.anchoredPosition = Vector2.zero;
            return;
        }

        Vector2 mousePos = GetMouseScreenPosition();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(reticleCanvasRect, mousePos, null, out Vector2 local))
            reticleRoot.anchoredPosition = local;
    }

    private static Vector2 GetMouseScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
        return Input.mousePosition;
    }

    private Vector2 GetAimScreenPosition()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        return GetMouseScreenPosition();
    }

    private bool IsAiming()
    {
        if (!holdRightMouseToAim)
            return true;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.rightButton.isPressed;
        return false;
#else
        return Input.GetMouseButton(1);
#endif
    }

    private void UpdateAimDof(Vector3 pivot)
    {
        if (!adjustDofFocusWhileAiming)
            return;

        if (cachedCamera == null)
            return;

        DepthOfField dof = GetAimDof();
        if (dof == null || !dof.active)
            return;

        float targetDistance = Vector3.Distance(transform.position, pivot);
        if (targetDistance <= 0.01f)
            targetDistance = defaultFocusDistance > 0.01f ? defaultFocusDistance : aimFocusMaxDistance;

        float lerp = 1.0f - Mathf.Exp(-aimFocusSmooth * Time.deltaTime);
        currentFocusDistance = Mathf.Lerp(currentFocusDistance, targetDistance, lerp);

        dof.focusDistance.overrideState = true;
        dof.focusDistance.value = currentFocusDistance;
    }

    private void ResolveAimVolume()
    {
        if (aimVolume != null || !autoFindAimVolume)
            return;

        Volume[] volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume v = volumes[i];
            if (v == null || !v.isGlobal)
                continue;

            VolumeProfile profile = v.sharedProfile != null ? v.sharedProfile : v.profile;
            if (profile != null && profile.TryGet(out DepthOfField _))
            {
                aimVolume = v;
                break;
            }
        }
    }

    private DepthOfField GetAimDof()
    {
        if (cachedDof != null)
            return cachedDof;

        if (aimVolume == null)
            ResolveAimVolume();

        if (aimVolume == null)
            return null;

        if (runtimeAimVolumeProfile == null)
            SetupAimDof();

        VolumeProfile profile = aimVolume.profile != null ? aimVolume.profile : runtimeAimVolumeProfile;
        if (profile == null)
            return null;

        if (profile.TryGet(out DepthOfField dof))
            cachedDof = dof;

        return cachedDof;
    }

    private void SetupAimDof()
    {
        if (aimVolume == null)
            return;

        if (aimVolume.profile == null && aimVolume.sharedProfile != null)
        {
            runtimeAimVolumeProfile = Instantiate(aimVolume.sharedProfile);
            runtimeAimVolumeProfile.name = aimVolume.sharedProfile.name + " (Runtime)";
            aimVolume.profile = runtimeAimVolumeProfile;
        }
        else
        {
            runtimeAimVolumeProfile = aimVolume.profile;
        }

        if (runtimeAimVolumeProfile == null || !runtimeAimVolumeProfile.TryGet(out cachedDof) || cachedDof == null)
            return;

        defaultFocusDistance = cachedDof.focusDistance.value;
        currentFocusDistance = defaultFocusDistance;
        cachedDof.focusDistance.overrideState = true;
    }

    public void PlayShotShake(float strengthMultiplier = 1.0f)
    {
        if (!enableCameraShake)
            return;

        float strength = Mathf.Max(0.0f, shotShakeStrength * Mathf.Max(0.0f, strengthMultiplier));
        if (strength <= 0.0001f)
            return;

        PlayShake(
            shotPositionShake * strength,
            shotRotationShake * strength,
            Mathf.Max(0.01f, shotShakeDuration),
            Mathf.Max(1, shotShakeFrequency));
    }

    public void PlayCollisionShake(float impulse)
    {
        if (!enableCameraShake)
            return;

        float minImpulse = Mathf.Max(0.0f, collisionMinImpulse);
        float maxImpulse = Mathf.Max(minImpulse + 0.001f, collisionMaxImpulse);
        float impulse01 = Mathf.InverseLerp(minImpulse, maxImpulse, Mathf.Max(0.0f, impulse));
        float strength = Mathf.Lerp(collisionMinShakeStrength, collisionMaxShakeStrength, impulse01);
        if (strength <= 0.0001f)
            return;

        PlayShake(
            collisionPositionShake * strength,
            collisionRotationShake * strength,
            Mathf.Max(0.01f, collisionShakeDuration),
            Mathf.Max(1, collisionShakeFrequency));
    }

    private void PlayShake(Vector3 positionStrength, Vector3 rotationStrength, float duration, int frequency)
    {
        if (positionShakeTween.isAlive)
            positionShakeTween.Stop();
        if (rotationShakeTween.isAlive)
            rotationShakeTween.Stop();

        var settingsPos = new ShakeSettings(positionStrength, duration, frequency);
        positionShakeTween = Tween.ShakeCustom(this, shakeLocalPosition, settingsPos, SetShakeLocalPosition);

        var settingsRot = new ShakeSettings(rotationStrength, duration, frequency);
        rotationShakeTween = Tween.ShakeCustom(this, shakeLocalRotation, settingsRot, SetShakeLocalRotation);
    }

    private void OnDisable()
    {
        if (positionShakeTween.isAlive)
            positionShakeTween.Stop();
        if (rotationShakeTween.isAlive)
            rotationShakeTween.Stop();
        shakeLocalPosition = Vector3.zero;
        shakeLocalRotation = Vector3.zero;
    }

    private void UpdateFovBySpeed()
    {
        if (!fovBySpeed || cachedCamera == null || target == null)
            return;

        float speedKph = 0.0f;
        CarControllerBase car = target.GetComponent<CarControllerBase>();
        if (car == null)
            car = target.GetComponentInParent<CarControllerBase>();
        if (car != null)
        {
            speedKph = car.SpeedKph;
        }
        else
        {
            Rigidbody targetBody = target.GetComponent<Rigidbody>();
            if (targetBody == null)
                targetBody = target.GetComponentInParent<Rigidbody>();
            speedKph = targetBody != null ? targetBody.linearVelocity.magnitude * 3.6f : 0.0f;
        }

        float t = speedForMaxFov > 1.0f ? Mathf.Clamp01(speedKph / speedForMaxFov) : 0.0f;
        float desired = Mathf.Lerp(baseFov, maxFov, t);
        float lerp = 1.0f - Mathf.Exp(-fovSmooth * Time.deltaTime);
        cachedCamera.fieldOfView = Mathf.Lerp(cachedCamera.fieldOfView, desired, lerp);
    }
}
