using System;
using PurrNet.Prediction;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public interface IPurrVehicleInputProvider
{
    void GetFinalInput(ref PurrVehiclePredictedInput input);
    void UpdateInput(ref PurrVehiclePredictedInput input);
}

[Serializable]
public struct PurrVehiclePredictedInput : IPredictedData, IEquatable<PurrVehiclePredictedInput>
{
    public float motor;
    public float steer;
    public bool brake;
    public bool handbrake;
    public bool nitro;

    public void Clamp()
    {
        motor = Mathf.Clamp(motor, -1.0f, 1.0f);
        steer = Mathf.Clamp(steer, -1.0f, 1.0f);
    }

    public CarControlFrame ToControlFrame()
    {
        CarControlFrame frame = new CarControlFrame
        {
            Motor = motor,
            Steer = steer,
            Brake = brake,
            Handbrake = handbrake,
            Nitro = nitro
        };
        frame.Clamp();
        return frame;
    }

    public void Dispose()
    {
    }

    public bool Equals(PurrVehiclePredictedInput other)
    {
        return Mathf.Approximately(motor, other.motor) &&
               Mathf.Approximately(steer, other.steer) &&
               brake == other.brake &&
               handbrake == other.handbrake &&
               nitro == other.nitro;
    }

    public override bool Equals(object obj)
    {
        return obj is PurrVehiclePredictedInput other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(motor, steer, brake, handbrake, nitro);
    }
}

[DisallowMultipleComponent]
public sealed class PurrVehicleLocalInputProvider : MonoBehaviour, IPurrVehicleInputProvider
{
    [SerializeField] private PlayerCar playerCar;
    [SerializeField] private CarControllerBase controller;
    [SerializeField] private MonoBehaviour inputSourceBehaviour;

    private ICarInputSource inputSource;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    public void GetFinalInput(ref PurrVehiclePredictedInput input)
    {
        input = ReadCurrentInput();
    }

    public void UpdateInput(ref PurrVehiclePredictedInput input)
    {
        input = ReadCurrentInput();
    }

    private PurrVehiclePredictedInput ReadCurrentInput()
    {
        ResolveReferences();
        CarControlFrame frame = ResolveControlFrame();
        PurrVehiclePredictedInput input = new PurrVehiclePredictedInput
        {
            motor = frame.Motor,
            steer = frame.Steer,
            brake = frame.Brake,
            handbrake = frame.Handbrake,
            nitro = frame.Nitro
        };
        input.Clamp();
        return input;
    }

    private void ResolveReferences()
    {
        if (playerCar == null)
            playerCar = GetComponent<PlayerCar>();
        if (controller == null && playerCar != null)
            controller = playerCar.Controller;
        if (controller == null)
            controller = GetComponent<CarControllerBase>();
        if (inputSourceBehaviour == null)
            inputSourceBehaviour = GetComponent<ICarInputSource>() as MonoBehaviour;
        inputSource = inputSourceBehaviour as ICarInputSource;
    }

    private CarControlFrame ResolveControlFrame()
    {
        if (inputSource != null && inputSource.TryGetControlFrame(out CarControlFrame frame))
        {
            frame.Clamp();
            return frame;
        }

        return ReadDefaultLocalControlFrame();
    }

    private static CarControlFrame ReadDefaultLocalControlFrame()
    {
        CarControlFrame frame = default;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            frame.Motor = (Keyboard.current.wKey.isPressed ? 1.0f : 0.0f) +
                          (Keyboard.current.sKey.isPressed ? -1.0f : 0.0f);
            frame.Steer = (Keyboard.current.dKey.isPressed ? 1.0f : 0.0f) +
                          (Keyboard.current.aKey.isPressed ? -1.0f : 0.0f);
            frame.Brake = false;
            frame.Handbrake = Keyboard.current.spaceKey.isPressed;
            frame.Nitro = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            frame.Clamp();
            return frame;
        }
#else
        frame.Motor = Input.GetAxis("Vertical");
        frame.Steer = Input.GetAxis("Horizontal");
        frame.Brake = false;
        frame.Handbrake = Input.GetKey(KeyCode.Space);
        frame.Nitro = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        frame.Clamp();
        return frame;
#endif

        return frame;
    }
}

[DisallowMultipleComponent]
public sealed class PurrVehicleBotInputProvider : MonoBehaviour, IPurrVehicleInputProvider
{
    [SerializeField] private bool stayStationary = true;
    [SerializeField, Range(0.1f, 1.0f)] private float cruiseThrottle = 0.72f;
    [SerializeField, Range(0.0f, 0.75f)] private float steerAmplitude = 0.18f;
    [SerializeField, Range(0.05f, 1.5f)] private float steerFrequency = 0.22f;
    [SerializeField] private bool allowNitro;

    public void GetFinalInput(ref PurrVehiclePredictedInput input)
    {
        input = BuildInput();
    }

    public void UpdateInput(ref PurrVehiclePredictedInput input)
    {
        input = BuildInput();
    }

    private PurrVehiclePredictedInput BuildInput()
    {
        if (stayStationary)
        {
            return new PurrVehiclePredictedInput
            {
                motor = 0.0f,
                steer = 0.0f,
                brake = true,
                handbrake = true,
                nitro = false
            };
        }

        float phaseOffset = (gameObject.GetInstanceID() & 255) * 0.013f;
        PurrVehiclePredictedInput input = new PurrVehiclePredictedInput
        {
            motor = cruiseThrottle,
            steer = Mathf.Sin((Time.unscaledTime + phaseOffset) * steerFrequency * Mathf.PI * 2.0f) * steerAmplitude,
            brake = false,
            handbrake = false,
            nitro = allowNitro
        };
        input.Clamp();
        return input;
    }
}
