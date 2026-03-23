using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class LocalKeyboardCarInputSource : MonoBehaviour, ICarInputSource
{
    public bool TryGetControlFrame(out CarControlFrame frame)
    {
        frame = default;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            frame = new CarControlFrame
            {
                Motor = (Keyboard.current.wKey.isPressed ? 1.0f : 0.0f) +
                        (Keyboard.current.sKey.isPressed ? -1.0f : 0.0f),
                Steer = (Keyboard.current.dKey.isPressed ? 1.0f : 0.0f) +
                        (Keyboard.current.aKey.isPressed ? -1.0f : 0.0f),
                Brake = false,
                Handbrake = Keyboard.current.spaceKey.isPressed,
                Nitro = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed
            };
            frame.Clamp();
            return true;
        }
#else
        frame = new CarControlFrame
        {
            Motor = Input.GetAxis("Vertical"),
            Steer = Input.GetAxis("Horizontal"),
            Brake = false,
            Handbrake = Input.GetKey(KeyCode.Space),
            Nitro = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
        };
        frame.Clamp();
        return true;
#endif

        return false;
    }
}
