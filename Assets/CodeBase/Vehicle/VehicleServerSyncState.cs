using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct ServerSyncedWheelPose
{
    public bool hasPosition;
    public Vector3 position;
    public bool hasRotation;
    public Quaternion rotation;
}

[DisallowMultipleComponent]
[ServerSyncedComponent("vehicle_state")]
public sealed class VehicleServerSyncState : MonoBehaviour
{
    public const string PositionPropertyName = "position";
    public const string RotationPropertyName = "rotation";
    public const string LinearVelocityPropertyName = "linear_velocity";
    public const string AngularVelocityPropertyName = "angular_velocity";
    public const string WheelStatesPropertyName = "wheel_states";
    public const string ControllerStatePropertyName = "controller_state";

    [SerializeField] private Rigidbody body;
    [SerializeField] private CarControllerBase controller;

    private readonly List<WheelBinding> wheelBindings = new List<WheelBinding>(4);

    private sealed class WheelBinding
    {
        public WheelCollider Collider;
        public Transform VisualRoot;
    }

    [ServerSyncedProperty(PositionPropertyName)]
    private Vector3 Position
    {
        get => body != null ? body.position : transform.position;
        set
        {
            if (body != null)
                body.position = value;
            else
                transform.position = value;
        }
    }

    [ServerSyncedProperty(RotationPropertyName)]
    private Quaternion Rotation
    {
        get => body != null ? body.rotation : transform.rotation;
        set
        {
            if (body != null)
                body.rotation = value;
            else
                transform.rotation = value;
        }
    }

    [ServerSyncedProperty(LinearVelocityPropertyName)]
    private Vector3 LinearVelocity
    {
        get => body != null ? body.linearVelocity : Vector3.zero;
        set
        {
            if (body != null)
                body.linearVelocity = value;
        }
    }

    [ServerSyncedProperty(AngularVelocityPropertyName)]
    private Vector3 AngularVelocity
    {
        get => body != null ? body.angularVelocity : Vector3.zero;
        set
        {
            if (body != null)
                body.angularVelocity = value;
        }
    }

    [ServerSyncedProperty(WheelStatesPropertyName)]
    private ServerSyncedWheelPose[] WheelStates
    {
        get => CaptureWheelStates();
        set => ApplyWheelStates(value);
    }

    [ServerSyncedProperty(ControllerStatePropertyName)]
    private CarControllerSimulationState ControllerState
    {
        get => controller != null ? controller.CaptureSimulationState() : default;
        set
        {
            if (controller != null)
                controller.ApplySimulationState(value);
        }
    }

    public Vector3 WorldPosition => Position;
    public Quaternion WorldRotation => Rotation;
    public Vector3 WorldLinearVelocity => LinearVelocity;
    public Vector3 WorldAngularVelocity => AngularVelocity;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    public ServerSyncedComponentState CaptureState()
    {
        ResolveReferences();
        return ServerSyncedStateUtility.CaptureComponent(this);
    }

    public void ApplyState(ServerSyncedComponentState state)
    {
        ResolveReferences();
        ServerSyncedStateUtility.ApplyComponent(this, state);
    }

    public ServerSyncedComponentState CreateState(BackendMatchPlayerState playerState)
    {
        ResolveReferences();
        ServerSyncedComponentState state = new ServerSyncedComponentState(ServerSyncedStateUtility.ResolveComponentId(this));
        state.Set(PositionPropertyName, playerState != null ? playerState.PositionVector : Vector3.zero);
        state.Set(RotationPropertyName, playerState != null ? Quaternion.Euler(playerState.RotationVector) : Quaternion.identity);
        state.Set(LinearVelocityPropertyName, playerState != null ? playerState.VelocityVector : Vector3.zero);
        state.Set(AngularVelocityPropertyName, playerState != null ? playerState.AngularVelocityVector : Vector3.zero);
        state.Set(WheelStatesPropertyName, ConvertWheelStates(playerState != null ? playerState.wheel_states : null));
        if (playerState != null && playerState.debug != null)
            state.Set(ControllerStatePropertyName, playerState.debug.ToSimulationState());
        return state;
    }

    public static float ComputePositionError(ServerSyncedComponentState authoritative, ServerSyncedComponentState predicted)
    {
        if (authoritative == null || predicted == null)
            return 0.0f;

        if (!authoritative.TryGetValue(PositionPropertyName, out Vector3 authoritativePosition) ||
            !predicted.TryGetValue(PositionPropertyName, out Vector3 predictedPosition))
        {
            return 0.0f;
        }

        return Vector3.Distance(authoritativePosition, predictedPosition);
    }

    public static float ComputeRotationErrorDegrees(ServerSyncedComponentState authoritative, ServerSyncedComponentState predicted)
    {
        if (authoritative == null || predicted == null)
            return 0.0f;

        if (!authoritative.TryGetValue(RotationPropertyName, out Quaternion authoritativeRotation) ||
            !predicted.TryGetValue(RotationPropertyName, out Quaternion predictedRotation))
        {
            return 0.0f;
        }

        return Quaternion.Angle(predictedRotation, authoritativeRotation);
    }

    private void ResolveReferences()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (controller == null)
            controller = GetComponent<CarControllerBase>();
    }

    private ServerSyncedWheelPose[] CaptureWheelStates()
    {
        RefreshWheelBindings();
        if (wheelBindings.Count == 0)
            return Array.Empty<ServerSyncedWheelPose>();

        ServerSyncedWheelPose[] result = new ServerSyncedWheelPose[wheelBindings.Count];
        for (int i = 0; i < wheelBindings.Count; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding == null || binding.VisualRoot == null)
                continue;

            result[i] = new ServerSyncedWheelPose
            {
                hasPosition = true,
                position = binding.VisualRoot.localPosition,
                hasRotation = true,
                rotation = binding.VisualRoot.localRotation
            };
        }

        return result;
    }

    private void ApplyWheelStates(ServerSyncedWheelPose[] wheelStates)
    {
        RefreshWheelBindings();
        if (wheelStates == null || wheelStates.Length == 0 || wheelBindings.Count == 0)
            return;

        int count = Mathf.Min(wheelBindings.Count, wheelStates.Length);
        for (int i = 0; i < count; i++)
        {
            WheelBinding binding = wheelBindings[i];
            if (binding == null || binding.VisualRoot == null)
                continue;

            ServerSyncedWheelPose pose = wheelStates[i];
            if (pose.hasPosition)
                binding.VisualRoot.localPosition = pose.position;
            if (pose.hasRotation)
                binding.VisualRoot.localRotation = pose.rotation;
        }
    }

    private void RefreshWheelBindings()
    {
        WheelCollider[] colliders = GetComponentsInChildren<WheelCollider>(true);
        if (colliders == null || colliders.Length == 0)
        {
            wheelBindings.Clear();
            return;
        }

        if (wheelBindings.Count == colliders.Length)
            return;

        Array.Sort(colliders, CompareWheelColliders);
        wheelBindings.Clear();
        for (int i = 0; i < colliders.Length; i++)
        {
            WheelCollider collider = colliders[i];
            if (collider == null)
                continue;

            Transform visualRoot = collider.transform.Find("VisualRoot");
            if (visualRoot == null)
                visualRoot = collider.transform.Find("Visual");
            if (visualRoot == null)
                continue;

            wheelBindings.Add(new WheelBinding
            {
                Collider = collider,
                VisualRoot = visualRoot
            });
        }
    }

    private static int CompareWheelColliders(WheelCollider left, WheelCollider right)
    {
        if (left == null || right == null)
            return 0;

        Vector3 leftLocal = left.transform.localPosition;
        Vector3 rightLocal = right.transform.localPosition;
        int zCompare = rightLocal.z.CompareTo(leftLocal.z);
        return zCompare != 0 ? zCompare : leftLocal.x.CompareTo(rightLocal.x);
    }

    private static ServerSyncedWheelPose[] ConvertWheelStates(List<BackendWheelPose> wheelStates)
    {
        if (wheelStates == null || wheelStates.Count == 0)
            return Array.Empty<ServerSyncedWheelPose>();

        ServerSyncedWheelPose[] result = new ServerSyncedWheelPose[wheelStates.Count];
        for (int i = 0; i < wheelStates.Count; i++)
        {
            BackendWheelPose pose = wheelStates[i];
            if (pose == null)
                continue;

            result[i] = new ServerSyncedWheelPose
            {
                hasPosition = pose.position != null,
                position = pose.position != null ? pose.position.ToVector3() : Vector3.zero,
                hasRotation = pose.rotation != null,
                rotation = pose.rotation != null ? Quaternion.Euler(pose.rotation.ToVector3()) : Quaternion.identity
            };
        }

        return result;
    }
}
