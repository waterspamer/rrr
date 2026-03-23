using UnityEngine;

public static class VehicleSpawnUtility
{
    public static Vector3 ResolveMatchSpawnPosition(
        Vector3 requestedSpawnPosition,
        Vector3 anchorPosition,
        Quaternion anchorRotation)
    {
        Vector3 rotatedOffset = anchorRotation * requestedSpawnPosition;
        return anchorPosition + rotatedOffset;
    }

    public static Quaternion ResolveMatchSpawnRotation(Vector3 requestedSpawnEuler, Quaternion anchorRotation)
    {
        return anchorRotation * Quaternion.Euler(requestedSpawnEuler);
    }

    public static Vector3 ResolveGroundedSpawnPosition(
        PlayerCar playerCar,
        Vector3 requestedSpawnPosition,
        float fallbackSpawnLift,
        float probeHeight,
        float probeDistance,
        Transform ignoreRoot = null)
    {
        float rideHeight = MeasureRideHeight(playerCar, fallbackSpawnLift);
        if (TryGetGroundHeight(requestedSpawnPosition, probeHeight, probeDistance, out float groundY, ignoreRoot))
            requestedSpawnPosition.y = groundY + rideHeight;
        else
            requestedSpawnPosition.y += rideHeight;

        return requestedSpawnPosition;
    }

    public static float MeasureRideHeight(PlayerCar playerCar, float fallbackSpawnLift)
    {
        float fallback = Mathf.Max(0.05f, fallbackSpawnLift);
        if (playerCar == null)
            return fallback;

        Transform root = playerCar.transform;
        float rideHeight = fallback;

        Collider[] colliders = playerCar.GetComponentsInChildren<Collider>(true);
        float minY = float.PositiveInfinity;
        bool foundCollider = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider.isTrigger || collider is WheelCollider)
                continue;

            Bounds bounds = collider.bounds;
            if (bounds.size.sqrMagnitude <= 0.0f)
                continue;

            minY = Mathf.Min(minY, bounds.min.y);
            foundCollider = true;
        }

        if (foundCollider)
            rideHeight = Mathf.Max(rideHeight, root.position.y - minY);

        WheelCollider[] wheelColliders = playerCar.GetComponentsInChildren<WheelCollider>(true);
        float minWheelRestBottomY = float.PositiveInfinity;
        bool foundWheelCollider = false;

        for (int i = 0; i < wheelColliders.Length; i++)
        {
            WheelCollider collider = wheelColliders[i];
            if (collider == null)
                continue;

            float targetPosition = 0.5f;
            JointSpring suspension = collider.suspensionSpring;
            if (suspension.spring > 0.0f || suspension.damper > 0.0f || suspension.targetPosition > 0.0f)
                targetPosition = Mathf.Clamp01(suspension.targetPosition);

            float suspensionExtension = collider.suspensionDistance * (1.0f - targetPosition);
            float wheelBottomY = collider.transform.position.y - collider.radius - suspensionExtension;
            minWheelRestBottomY = Mathf.Min(minWheelRestBottomY, wheelBottomY);
            foundWheelCollider = true;
        }

        if (foundWheelCollider)
            rideHeight = Mathf.Max(rideHeight, root.position.y - minWheelRestBottomY);

        return rideHeight;
    }

    public static bool TryGetGroundHeight(
        Vector3 aroundPosition,
        float probeHeight,
        float probeDistance,
        out float groundY,
        Transform ignoreRoot = null)
    {
        float sampleHeight = Mathf.Max(0.25f, probeHeight);
        float sampleDistance = Mathf.Max(0.5f, probeDistance);
        Vector3 sampleOrigin = aroundPosition + Vector3.up * sampleHeight;
        RaycastHit[] hits = Physics.RaycastAll(
            sampleOrigin,
            Vector3.down,
            sampleDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;
            if (ignoreRoot != null && hit.collider.transform.IsChildOf(ignoreRoot))
                continue;
            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                groundY = hit.point.y;
                return true;
            }
        }

        groundY = 0.0f;
        return false;
    }
}
