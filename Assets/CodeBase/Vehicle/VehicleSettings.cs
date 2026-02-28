using UnityEngine;

[CreateAssetMenu(menuName = "Vehicles/Vehicle Settings", fileName = "VehicleSettings")]
public class VehicleSettings : ScriptableObject
{
    [Header("General")]
    public bool autoCreateHud = true;

    [Header("Handling")]
    [Range(5.0f, 60.0f)] public float maxSteerAngle = 28.0f;
    [Range(1.0f, 20.0f)] public float steerResponse = 8.0f;
    public AnimationCurve steerBySpeed = new AnimationCurve(
        new Keyframe(0.0f, 1.0f),
        new Keyframe(50.0f, 0.7f),
        new Keyframe(120.0f, 0.4f));
    [Range(500.0f, 8000.0f)] public float brakePower = 2200.0f;
    [Range(500.0f, 12000.0f)] public float handbrakePower = 4000.0f;
    [Range(0.5f, 5.0f)] public float forwardFriction = 1.8f;
    [Range(0.5f, 5.0f)] public float sidewaysFriction = 2.0f;
    [Range(0.05f, 1.0f)] public float handbrakeFrictionMultiplier = 0.35f;
    public AnimationCurve handbrakeSidewaysBySpeed = new AnimationCurve(
        new Keyframe(0.0f, 1.0f),
        new Keyframe(40.0f, 0.7f),
        new Keyframe(90.0f, 0.4f));
    [Range(0.0f, 1.0f)] public float driftKickSteerThreshold = 0.2f;
    [Range(0.0f, 20000.0f)] public float driftKickMaxForce = 6000.0f;
    [Range(0.0f, 5.0f)] public float driftKickRearOffset = 1.2f;
    [Range(1.0f, 20.0f)] public float driftKickResponse = 6.0f;
    public AnimationCurve driftKickBySpeed = new AnimationCurve(
        new Keyframe(0.0f, 0.0f),
        new Keyframe(30.0f, 0.6f),
        new Keyframe(80.0f, 1.0f));
    [Range(0.0f, 10.0f)] public float lateralStability = 4.0f;
    [Range(0.0f, 10.0f)] public float yawStability = 2.5f;

    [Header("WheelCollider Curves")]
    [Range(0.1f, 5.0f)] public float forwardExtremumSlip = 1.0f;
    [Range(0.1f, 5.0f)] public float forwardExtremumValue = 1.0f;
    [Range(0.1f, 5.0f)] public float forwardAsymptoteSlip = 2.0f;
    [Range(0.1f, 5.0f)] public float forwardAsymptoteValue = 0.5f;
    [Range(0.1f, 5.0f)] public float sidewaysExtremumSlip = 1.0f;
    [Range(0.1f, 5.0f)] public float sidewaysExtremumValue = 1.0f;
    [Range(0.1f, 5.0f)] public float sidewaysAsymptoteSlip = 2.0f;
    [Range(0.1f, 5.0f)] public float sidewaysAsymptoteValue = 0.5f;

    [Header("Chassis")]
    [Range(600.0f, 2500.0f)] public float mass = 1200.0f;
    [Range(0.0f, 50.0f)] public float downforce = 0.2f;
    [Range(0.0f, 200.0f)] public float rollingResistance = 20.0f;
    [Range(0.0f, 2.0f)] public float aerodynamicDrag = 0.35f;

    [Header("Nitro")]
    public bool nitroEnabled = true;
    [Range(0.0f, 1.0f)] public float nitroStart = 1.0f;
    [Min(0.0f)] public float nitroRegenPerSecond = 0.25f;
    [Min(0.0f)] public float nitroDrainPerSecond = 0.5f;
    [Range(1.0f, 3.0f)] public float nitroPowerMultiplier = 1.25f;
    [Range(1.0f, 3.0f)] public float nitroRpmResponseMultiplier = 1.5f;

    private void OnValidate()
    {
        Validate();
    }

    public void Validate()
    {
        mass = Mathf.Max(50.0f, mass);
        maxSteerAngle = Mathf.Clamp(maxSteerAngle, 1.0f, 60.0f);
        steerResponse = Mathf.Clamp(steerResponse, 1.0f, 20.0f);
        brakePower = Mathf.Max(0.0f, brakePower);
        handbrakePower = Mathf.Max(0.0f, handbrakePower);
        downforce = Mathf.Max(0.0f, downforce);
        rollingResistance = Mathf.Max(0.0f, rollingResistance);
        aerodynamicDrag = Mathf.Max(0.0f, aerodynamicDrag);
        forwardFriction = Mathf.Clamp(forwardFriction, 0.1f, 5.0f);
        sidewaysFriction = Mathf.Clamp(sidewaysFriction, 0.1f, 5.0f);
        handbrakeFrictionMultiplier = Mathf.Clamp(handbrakeFrictionMultiplier, 0.05f, 1.0f);
        if (lateralStability <= 0.0f)
            lateralStability = 4.0f;
        if (yawStability <= 0.0f)
            yawStability = 2.5f;

        if (steerBySpeed == null || steerBySpeed.length == 0)
        {
            steerBySpeed = new AnimationCurve(
                new Keyframe(0.0f, 1.0f),
                new Keyframe(50.0f, 0.7f),
                new Keyframe(120.0f, 0.4f));
        }

        if (handbrakeSidewaysBySpeed == null || handbrakeSidewaysBySpeed.length == 0)
        {
            handbrakeSidewaysBySpeed = new AnimationCurve(
                new Keyframe(0.0f, 1.0f),
                new Keyframe(40.0f, 0.7f),
                new Keyframe(90.0f, 0.4f));
        }

        driftKickSteerThreshold = Mathf.Clamp01(driftKickSteerThreshold);
        driftKickMaxForce = Mathf.Max(0.0f, driftKickMaxForce);
        driftKickRearOffset = Mathf.Max(0.0f, driftKickRearOffset);
        driftKickResponse = Mathf.Clamp(driftKickResponse, 1.0f, 20.0f);
        if (driftKickBySpeed == null || driftKickBySpeed.length == 0)
        {
            driftKickBySpeed = new AnimationCurve(
                new Keyframe(0.0f, 0.0f),
                new Keyframe(30.0f, 0.6f),
                new Keyframe(80.0f, 1.0f));
        }

        nitroStart = Mathf.Clamp01(nitroStart);
        nitroRegenPerSecond = Mathf.Max(0.0f, nitroRegenPerSecond);
        nitroDrainPerSecond = Mathf.Max(0.0f, nitroDrainPerSecond);
        nitroPowerMultiplier = Mathf.Clamp(nitroPowerMultiplier, 1.0f, 3.0f);
        nitroRpmResponseMultiplier = Mathf.Clamp(nitroRpmResponseMultiplier, 1.0f, 3.0f);

    }
}
