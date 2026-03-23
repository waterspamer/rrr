using UnityEngine;

public struct CarControlFrame
{
    public float Motor;
    public float Steer;
    public bool Brake;
    public bool Handbrake;
    public bool Nitro;

    public static CarControlFrame CreateBrakingFrame()
    {
        return new CarControlFrame
        {
            Motor = 0.0f,
            Steer = 0.0f,
            Brake = true,
            Handbrake = true,
            Nitro = false
        };
    }

    public void Clamp()
    {
        Motor = Mathf.Clamp(Motor, -1.0f, 1.0f);
        Steer = Mathf.Clamp(Steer, -1.0f, 1.0f);
    }
}
