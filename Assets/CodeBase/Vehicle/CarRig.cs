using UnityEngine;

public class CarRig : MonoBehaviour
{
    [Header("Body")]
    [SerializeField] private Transform bodyRoot;

    [Header("Wheels")]
    [SerializeField] private Transform frontLeft;
    [SerializeField] private Transform frontRight;
    [SerializeField] private Transform rearLeft;
    [SerializeField] private Transform rearRight;
    [SerializeField] private bool reparentWheelVisuals = false;
    [SerializeField] private Vector3 wheelColliderOffset = Vector3.zero;

    public Transform BodyRoot => bodyRoot;
    public Transform FrontLeft => frontLeft;
    public Transform FrontRight => frontRight;
    public Transform RearLeft => rearLeft;
    public Transform RearRight => rearRight;
    public bool ReparentWheelVisuals => reparentWheelVisuals;
    public Vector3 WheelColliderOffset => wheelColliderOffset;
}
