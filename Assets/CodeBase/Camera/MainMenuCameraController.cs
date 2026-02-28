using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MainMenuCameraController : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 pivotOffset = new Vector3(0.0f, 1.3f, 0.0f);
    [SerializeField, Min(0.1f)] private float distance = 7.0f;
    [SerializeField] private float yaw = 0.0f;
    [SerializeField] private float pitch = 15.0f;
    [SerializeField, Min(0.01f)] private float yawSensitivity = 0.2f;
    [SerializeField, Min(0.01f)] private float pitchSensitivity = 0.15f;
    [SerializeField] private float minPitch = -20.0f;
    [SerializeField] private float maxPitch = 55.0f;
    [SerializeField, Min(0.1f)] private float smooth = 12.0f;

    private Vector3 currentPos;
    private Quaternion currentRot;

    private void Awake()
    {
        if (target == null)
        {
            PlayerCar car = FindFirstObjectByType<PlayerCar>();
            if (car != null)
                target = car.transform;
        }

        currentPos = transform.position;
        currentRot = transform.rotation;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (IsRotateHeld())
        {
            Vector2 delta = GetMouseDelta();
            yaw += delta.x * yawSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * pitchSensitivity, minPitch, maxPitch);
        }

        Vector3 pivot = target.TransformPoint(pivotOffset);
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0.0f);
        Vector3 desiredPos = pivot + orbit * (Vector3.back * distance);
        Quaternion desiredRot = Quaternion.LookRotation(pivot - desiredPos, Vector3.up);

        float t = 1.0f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);
        currentPos = Vector3.Lerp(currentPos, desiredPos, t);
        currentRot = Quaternion.Slerp(currentRot, desiredRot, t);

        transform.position = currentPos;
        transform.rotation = currentRot;
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
