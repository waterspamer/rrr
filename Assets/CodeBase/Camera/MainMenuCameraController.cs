using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MainMenuCameraController : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Volume postProcessVolume;
    [SerializeField] private Vector3 pivotOffset = new Vector3(0.0f, 1.3f, 0.0f);
    [SerializeField, Min(0.1f)] private float distance = 7.0f;
    [SerializeField] private float yaw = 0.0f;
    [SerializeField] private float pitch = 15.0f;
    [SerializeField, Min(0.01f)] private float yawSensitivity = 0.2f;
    [SerializeField, Min(0.01f)] private float pitchSensitivity = 0.15f;
    [SerializeField] private float minPitch = -20.0f;
    [SerializeField] private float maxPitch = 55.0f;
    [SerializeField, Min(0.1f)] private float smooth = 12.0f;

    private Vector3 defaultPivotOffset;
    private float defaultDistance;
    private float defaultYaw;
    private float defaultPitch;
    private Vector3 desiredPivotOffset;
    private float desiredDistance;
    private float desiredYaw;
    private float desiredPitch;
    private Vector3 currentPos;
    private Quaternion currentRot;
    private VolumeProfile runtimeVolumeProfile;
    private DepthOfField depthOfField;
    private float defaultFocusDistance;

    private void Awake()
    {
        if (target == null)
        {
            PlayerCar car = FindFirstObjectByType<PlayerCar>();
            if (car != null)
                target = car.transform;
        }

        if (postProcessVolume == null)
            postProcessVolume = FindFirstObjectByType<Volume>();

        defaultPivotOffset = pivotOffset;
        defaultDistance = distance;
        defaultYaw = yaw;
        defaultPitch = pitch;
        desiredPivotOffset = pivotOffset;
        desiredDistance = distance;
        desiredYaw = yaw;
        desiredPitch = pitch;
        currentPos = transform.position;
        currentRot = transform.rotation;

        SetupDepthOfField();
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (IsRotateHeld())
        {
            Vector2 delta = GetMouseDelta();
            desiredYaw += delta.x * yawSensitivity;
            desiredPitch = Mathf.Clamp(desiredPitch - delta.y * pitchSensitivity, minPitch, maxPitch);
        }

        float t = 1.0f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);
        pivotOffset = Vector3.Lerp(pivotOffset, desiredPivotOffset, t);
        distance = Mathf.Lerp(distance, desiredDistance, t);
        yaw = Mathf.LerpAngle(yaw, desiredYaw, t);
        pitch = Mathf.Lerp(pitch, desiredPitch, t);

        Vector3 pivot = target.TransformPoint(pivotOffset);
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0.0f);
        Vector3 desiredPos = pivot + orbit * (Vector3.back * distance);
        Quaternion desiredRot = Quaternion.LookRotation(pivot - desiredPos, Vector3.up);

        currentPos = Vector3.Lerp(currentPos, desiredPos, t);
        currentRot = Quaternion.Slerp(currentRot, desiredRot, t);

        transform.position = currentPos;
        transform.rotation = currentRot;

        UpdateDepthOfField(pivot);
    }

    public void ResetFocus()
    {
        desiredPivotOffset = defaultPivotOffset;
        desiredDistance = defaultDistance;
    }

    public void FocusCategory(string familyKey)
    {
        Vector3 localMin;
        Vector3 localMax;
        Vector3 localFocusPoint;
        if (!TryGetCategoryFocusData(familyKey, out localMin, out localMax, out localFocusPoint))
        {
            ResetFocus();
            return;
        }

        ApplyFocusFrame(familyKey, localMin, localMax, localFocusPoint);
    }

    public void FocusSelection(string familyKey, string variantName, string[] selectorPaths)
    {
        Vector3 localMin;
        Vector3 localMax;
        Vector3 localFocusPoint;
        if (TryGetSelectionFocusData(familyKey, variantName, selectorPaths, out localMin, out localMax, out localFocusPoint))
        {
            ApplyFocusFrame(familyKey, localMin, localMax, localFocusPoint);
            return;
        }

        FocusCategory(familyKey);
    }

    private bool TryGetCategoryFocusData(string familyKey, out Vector3 localMin, out Vector3 localMax, out Vector3 localFocusPoint)
    {
        localFocusPoint = Vector3.zero;
        string[] tokens = GetFocusTokens(familyKey);
        if (tokens != null && tokens.Length > 0)
        {
            if (TryGetCategoryBoundsWithSpatialBias(familyKey, tokens, true, out localMin, out localMax, out localFocusPoint))
                return true;

            if (TryGetCategoryBoundsWithSpatialBias(familyKey, tokens, false, out localMin, out localMax, out localFocusPoint))
                return true;
        }

        tokens = GetCategoryTokens(familyKey);
        if (tokens == null || tokens.Length == 0)
        {
            if (!TryGetTargetLocalBounds(out localMin, out localMax))
                return false;

            localFocusPoint = (localMin + localMax) * 0.5f;
            return true;
        }

        if (TryGetCategoryBoundsWithSpatialBias(familyKey, tokens, true, out localMin, out localMax, out localFocusPoint))
            return true;

        return TryGetCategoryBoundsWithSpatialBias(familyKey, tokens, false, out localMin, out localMax, out localFocusPoint);
    }

    private bool TryGetSelectionFocusData(string familyKey, string variantName, string[] selectorPaths, out Vector3 localMin, out Vector3 localMax, out Vector3 localFocusPoint)
    {
        localMin = Vector3.zero;
        localMax = Vector3.zero;
        localFocusPoint = Vector3.zero;

        if (target == null || selectorPaths == null || selectorPaths.Length == 0)
            return false;

        Transform customsRoot = FindCustomsRoot();
        if (customsRoot == null)
            return false;

        var variantInfos = new System.Collections.Generic.List<RendererBoundsInfo>();
        for (int i = 0; i < selectorPaths.Length; i++)
        {
            Transform selectorRoot = FindSelectorRoot(customsRoot, selectorPaths[i]);
            if (selectorRoot == null)
                continue;

            Transform variantRoot = ResolveVariantRoot(selectorRoot, variantName);
            if (variantRoot == null)
                continue;

            if (TryGetNodeBoundsInfo(variantRoot, true, out RendererBoundsInfo info) ||
                TryGetNodeBoundsInfo(variantRoot, false, out info))
            {
                variantInfos.Add(info);
            }
        }

        if (variantInfos.Count == 0)
            return false;

        if (ShouldPreferLeftSide(familyKey) && variantInfos.Count > 1)
        {
            int bestIndex = 0;
            float bestX = variantInfos[0].LocalCenter.x;
            for (int i = 1; i < variantInfos.Count; i++)
            {
                if (variantInfos[i].LocalCenter.x < bestX)
                {
                    bestX = variantInfos[i].LocalCenter.x;
                    bestIndex = i;
                }
            }

            RendererBoundsInfo best = variantInfos[bestIndex];
            localMin = best.LocalMin;
            localMax = best.LocalMax;
            localFocusPoint = (best.LocalMin + best.LocalMax) * 0.5f;
            return true;
        }

        localMin = variantInfos[0].LocalMin;
        localMax = variantInfos[0].LocalMax;
        for (int i = 1; i < variantInfos.Count; i++)
        {
            localMin = Vector3.Min(localMin, variantInfos[i].LocalMin);
            localMax = Vector3.Max(localMax, variantInfos[i].LocalMax);
        }

        localFocusPoint = (localMin + localMax) * 0.5f;
        return true;
    }

    private bool TryGetTargetLocalBounds(out Vector3 localMin, out Vector3 localMax)
    {
        return TryGetLocalBounds(null, out localMin, out localMax);
    }

    private bool TryGetCategoryBoundsWithSpatialBias(string familyKey, string[] tokens, bool useConvexColliders, out Vector3 localMin, out Vector3 localMax, out Vector3 localFocusPoint)
    {
        localMin = Vector3.zero;
        localMax = Vector3.zero;
        localFocusPoint = Vector3.zero;

        if (target == null)
            return false;

        var candidates = new System.Collections.Generic.List<RendererBoundsInfo>();
        if (useConvexColliders)
        {
            MeshCollider[] colliders = target.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                MeshCollider collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.convex || !collider.gameObject.activeInHierarchy)
                    continue;
                if (!MatchesAnyToken(collider.transform, tokens))
                    continue;
                if (!TryBuildColliderBoundsInfo(collider, out RendererBoundsInfo info))
                    continue;

                candidates.Add(info);
            }
        }
        else
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!MatchesAnyToken(renderer.transform, tokens))
                    continue;
                if (!TryBuildRendererBoundsInfo(renderer, out RendererBoundsInfo info))
                    continue;

                candidates.Add(info);
            }
        }

        if (candidates.Count == 0)
            return false;

        Vector3 aggregateMin = candidates[0].LocalMin;
        Vector3 aggregateMax = candidates[0].LocalMax;
        for (int i = 1; i < candidates.Count; i++)
        {
            aggregateMin = Vector3.Min(aggregateMin, candidates[i].LocalMin);
            aggregateMax = Vector3.Max(aggregateMax, candidates[i].LocalMax);
        }

        Vector3 aggregateCenter = (aggregateMin + aggregateMax) * 0.5f;
        var filtered = new System.Collections.Generic.List<RendererBoundsInfo>();
        for (int i = 0; i < candidates.Count; i++)
        {
            RendererBoundsInfo candidate = candidates[i];
            if (ShouldKeepCandidateForCategory(familyKey, candidate, aggregateCenter))
                filtered.Add(candidate);
        }

        if (filtered.Count == 0)
            filtered.AddRange(candidates);

        localMin = filtered[0].LocalMin;
        localMax = filtered[0].LocalMax;
        for (int i = 1; i < filtered.Count; i++)
        {
            localMin = Vector3.Min(localMin, filtered[i].LocalMin);
            localMax = Vector3.Max(localMax, filtered[i].LocalMax);
        }

        Vector3 centerSum = Vector3.zero;
        for (int i = 0; i < filtered.Count; i++)
            centerSum += filtered[i].LocalCenter;
        localFocusPoint = centerSum / filtered.Count;

        return true;
    }

    private bool TryGetLocalBounds(System.Predicate<Transform> predicate, out Vector3 localMin, out Vector3 localMax)
    {
        localMin = Vector3.zero;
        localMax = Vector3.zero;

        if (target == null)
            return false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (predicate != null && !predicate(renderer.transform))
                continue;

            if (!TryBuildRendererBoundsInfo(renderer, out RendererBoundsInfo info))
                continue;

            if (!found)
            {
                localMin = info.LocalMin;
                localMax = info.LocalMax;
                found = true;
            }
            else
            {
                localMin = Vector3.Min(localMin, info.LocalMin);
                localMax = Vector3.Max(localMax, info.LocalMax);
            }
        }

        return found;
    }

    private bool TryBuildRendererBoundsInfo(Renderer renderer, out RendererBoundsInfo info)
    {
        info = default;
        if (renderer == null || target == null)
            return false;

        Bounds bounds = renderer.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        bool found = false;
        Vector3 localMin = Vector3.zero;
        Vector3 localMax = Vector3.zero;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 localCorner = target.InverseTransformPoint(corner);
                    if (!found)
                    {
                        localMin = localCorner;
                        localMax = localCorner;
                        found = true;
                    }
                    else
                    {
                        localMin = Vector3.Min(localMin, localCorner);
                        localMax = Vector3.Max(localMax, localCorner);
                    }
                }
            }
        }

        if (!found)
            return false;

        info = new RendererBoundsInfo
        {
            LocalMin = localMin,
            LocalMax = localMax,
            LocalCenter = (localMin + localMax) * 0.5f
        };
        return true;
    }

    private bool TryGetNodeBoundsInfo(Transform root, bool useConvexColliders, out RendererBoundsInfo info)
    {
        info = default;
        if (root == null)
            return false;

        bool found = false;
        Vector3 localMin = Vector3.zero;
        Vector3 localMax = Vector3.zero;

        if (useConvexColliders)
        {
            MeshCollider[] colliders = root.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                MeshCollider collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.convex || !collider.gameObject.activeInHierarchy)
                    continue;
                if (!TryBuildColliderBoundsInfo(collider, out RendererBoundsInfo candidate))
                    continue;

                if (!found)
                {
                    localMin = candidate.LocalMin;
                    localMax = candidate.LocalMax;
                    found = true;
                }
                else
                {
                    localMin = Vector3.Min(localMin, candidate.LocalMin);
                    localMax = Vector3.Max(localMax, candidate.LocalMax);
                }
            }
        }
        else
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!TryBuildRendererBoundsInfo(renderer, out RendererBoundsInfo candidate))
                    continue;

                if (!found)
                {
                    localMin = candidate.LocalMin;
                    localMax = candidate.LocalMax;
                    found = true;
                }
                else
                {
                    localMin = Vector3.Min(localMin, candidate.LocalMin);
                    localMax = Vector3.Max(localMax, candidate.LocalMax);
                }
            }
        }

        if (!found)
            return false;

        info = new RendererBoundsInfo
        {
            LocalMin = localMin,
            LocalMax = localMax,
            LocalCenter = (localMin + localMax) * 0.5f
        };
        return true;
    }

    private bool TryBuildColliderBoundsInfo(Collider collider, out RendererBoundsInfo info)
    {
        info = default;
        if (collider == null || target == null)
            return false;

        Bounds bounds = collider.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        bool found = false;
        Vector3 localMin = Vector3.zero;
        Vector3 localMax = Vector3.zero;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 localCorner = target.InverseTransformPoint(corner);
                    if (!found)
                    {
                        localMin = localCorner;
                        localMax = localCorner;
                        found = true;
                    }
                    else
                    {
                        localMin = Vector3.Min(localMin, localCorner);
                        localMax = Vector3.Max(localMax, localCorner);
                    }
                }
            }
        }

        if (!found)
            return false;

        info = new RendererBoundsInfo
        {
            LocalMin = localMin,
            LocalMax = localMax,
            LocalCenter = target.InverseTransformPoint(bounds.center)
        };
        return true;
    }

    private static bool ShouldKeepCandidateForCategory(string familyKey, RendererBoundsInfo candidate, Vector3 aggregateCenter)
    {
        bool keep;
        switch (familyKey)
        {
            case "front-bumper":
            case "headlights":
            case "grille":
            case "splitter":
            case "front-fenders":
                keep = candidate.LocalCenter.z >= aggregateCenter.z;
                break;
            case "rear-bumper":
            case "taillights":
            case "diffuser":
            case "exhaust":
            case "rear-fenders":
            case "spoiler":
                keep = candidate.LocalCenter.z <= aggregateCenter.z;
                break;
            case "hood":
                keep = candidate.LocalCenter.y >= aggregateCenter.y;
                break;
            case "skirts":
            case "wheels":
            case "suspension":
                keep = candidate.LocalCenter.y <= aggregateCenter.y;
                break;
            default:
                keep = true;
                break;
        }

        if (!keep)
            return false;

        if (ShouldPreferLeftSide(familyKey))
            return candidate.LocalCenter.x <= aggregateCenter.x;

        return true;
    }

    private static bool ShouldPreferLeftSide(string familyKey)
    {
        switch (familyKey)
        {
            case "mirrors":
            case "skirts":
            case "headlights":
            case "taillights":
            case "front-fenders":
            case "rear-fenders":
            case "wheels":
            case "suspension":
                return true;
            default:
                return false;
        }
    }

    private bool MatchesAnyToken(Transform node, string[] tokens)
    {
        Transform current = node;
        while (current != null && current != target)
        {
            string normalized = NormalizeName(current.name);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (normalized.Contains(tokens[i]))
                    return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static string NormalizeName(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
    }

    private static string[] GetCategoryTokens(string familyKey)
    {
        switch (familyKey)
        {
            case "front-bumper":
                return new[] { "bumperf", "bumperchassisf" };
            case "rear-bumper":
                return new[] { "bumperr", "bumperchassisr" };
            case "splitter":
                return new[] { "splitter" };
            case "skirts":
                return new[] { "skirts" };
            case "spoiler":
                return new[] { "spoiler" };
            case "hood":
                return new[] { "hood" };
            case "mirrors":
                return new[] { "mirror", "mirrorbase", "fendermirror" };
            case "grille":
                return new[] { "grille" };
            case "headlights":
                return new[] { "headlight", "headlights", "lightglass", "lightset" };
            case "taillights":
                return new[] { "taillight", "taillights" };
            case "diffuser":
                return new[] { "diffuser" };
            case "exhaust":
                return new[] { "exhaust" };
            case "front-fenders":
                return new[] { "fenderfl", "fenderfr", "fenderschassisf" };
            case "rear-fenders":
                return new[] { "fendersr", "fenderschassisr" };
            case "wheels":
            case "suspension":
                return new[] { "wheel", "frontleft", "frontright", "rearleft", "rearright" };
            case "body-kit":
                return new[] { "body", "customs" };
            default:
                return null;
        }
    }

    private static string[] GetFocusTokens(string familyKey)
    {
        switch (familyKey)
        {
            case "front-bumper":
                return new[] { "bumperf" };
            case "rear-bumper":
                return new[] { "bumperr" };
            default:
                return null;
        }
    }

    private Transform FindCustomsRoot()
    {
        if (target == null)
            return null;

        Transform body = target.Find("Body");
        if (body != null)
        {
            Transform customs = body.Find("Customs");
            if (customs != null)
                return customs;
        }

        return target.Find("Customs");
    }

    private static Transform FindSelectorRoot(Transform customsRoot, string selectorPath)
    {
        if (customsRoot == null || string.IsNullOrWhiteSpace(selectorPath))
            return null;

        return customsRoot.Find(selectorPath);
    }

    private static Transform ResolveVariantRoot(Transform selectorRoot, string variantName)
    {
        if (selectorRoot == null || selectorRoot.childCount == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(variantName))
        {
            for (int i = 0; i < selectorRoot.childCount; i++)
            {
                Transform child = selectorRoot.GetChild(i);
                if (string.Equals(child.name, variantName, System.StringComparison.OrdinalIgnoreCase))
                    return child;
            }
        }

        return selectorRoot.GetChild(0);
    }

    private void ApplyFocusFrame(string familyKey, Vector3 localMin, Vector3 localMax, Vector3 localFocusPoint)
    {
        Vector3 localExtents = (localMax - localMin) * 0.5f;
        float width = Mathf.Max(0.2f, localExtents.x * 2.0f);
        float height = Mathf.Max(0.2f, localExtents.y * 2.0f);
        float length = Mathf.Max(0.2f, localExtents.z * 2.0f);
        float focusDistance = Mathf.Clamp(Mathf.Max(width, height, length) * 0.75f, 1.6f, 3.4f);
        Vector3 focusPoint = localFocusPoint;

        switch (familyKey)
        {
            case "front-bumper":
            case "headlights":
            case "grille":
            case "splitter":
            case "front-fenders":
                focusDistance = Mathf.Clamp(length * 0.55f, 1.8f, 3.0f);
                break;
            case "hood":
                focusDistance = Mathf.Clamp(length * 0.60f, 1.8f, 3.0f);
                break;
            case "mirrors":
                focusDistance = Mathf.Clamp(width * 0.95f, 1.7f, 2.8f);
                break;
            case "skirts":
                focusDistance = Mathf.Clamp(length * 0.55f, 1.8f, 3.0f);
                break;
            case "rear-bumper":
            case "taillights":
            case "diffuser":
            case "exhaust":
            case "rear-fenders":
                focusDistance = Mathf.Clamp(length * 0.55f, 1.8f, 3.0f);
                break;
            case "spoiler":
                focusDistance = Mathf.Clamp(width * 0.95f, 1.8f, 2.8f);
                break;
            case "wheels":
            case "suspension":
                focusDistance = Mathf.Clamp(Mathf.Max(width, height, length) * 1.15f, 1.7f, 2.8f);
                break;
            case "body-kit":
                if (TryGetTargetLocalBounds(out localMin, out localMax))
                {
                    localExtents = (localMax - localMin) * 0.5f;
                    focusPoint = (localMin + localMax) * 0.5f;
                    focusDistance = Mathf.Clamp(Mathf.Max(localExtents.x, localExtents.y, localExtents.z) * 3.2f, 4.8f, defaultDistance);
                }
                break;
        }

        desiredPivotOffset = focusPoint;
        desiredDistance = focusDistance;
    }

    private struct RendererBoundsInfo
    {
        public Vector3 LocalMin;
        public Vector3 LocalMax;
        public Vector3 LocalCenter;
    }

    private void SetupDepthOfField()
    {
        if (postProcessVolume == null)
            return;

        runtimeVolumeProfile = postProcessVolume.profile;
        if (runtimeVolumeProfile == null)
        {
            if (postProcessVolume.sharedProfile == null)
                return;

            runtimeVolumeProfile = Instantiate(postProcessVolume.sharedProfile);
            runtimeVolumeProfile.name = postProcessVolume.sharedProfile.name + " (Runtime)";
            postProcessVolume.profile = runtimeVolumeProfile;
        }

        if (!runtimeVolumeProfile.TryGet(out depthOfField) || depthOfField == null)
            return;

        defaultFocusDistance = depthOfField.focusDistance.value;
        depthOfField.focusDistance.overrideState = true;
    }

    private void UpdateDepthOfField(Vector3 pivot)
    {
        if (depthOfField == null)
            return;

        float focusDistanceValue = Vector3.Distance(currentPos, pivot);
        if (focusDistanceValue <= 0.01f)
            focusDistanceValue = defaultFocusDistance;

        depthOfField.focusDistance.value = focusDistanceValue;
    }

    private static bool IsRotateHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private static Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
    }
}
