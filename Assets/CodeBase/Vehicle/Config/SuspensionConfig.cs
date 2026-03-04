using UnityEngine;

[CreateAssetMenu(menuName = "Vehicles/Configs/Suspension", fileName = "SuspensionConfig")]
public class SuspensionConfig : ScriptableObject
{
    [Header("UI")]
    public Sprite icon;

    [Header("Visual")]
    public bool applyVisualRideHeight = true;
    public float visualWheelHeight = 0.35f;

    [Header("Suspension")]
    [Range(0.05f, 0.5f)] public float suspensionDistance = 0.2f;
    [Range(1.0f, 6.0f)] public float suspensionFrequency = 3.5f;
    [Range(0.1f, 1.0f)] public float suspensionDamping = 0.8f;
    [Range(0.0f, 1.0f)] public float suspensionTargetPosition = 0.5f;
    [Range(0.3f, 0.7f)] public float frontWeightBias = 0.55f;
    [Range(0.0f, 15000.0f)] public float antiRollFront = 5000.0f;
    [Range(0.0f, 15000.0f)] public float antiRollRear = 4500.0f;

    [Header("Chassis")]
    [Range(0.0f, 1.0f)] public float centerOfMassHeight = 0.3f;

    private void OnValidate() => Validate();

    public void Validate()
    {
        visualWheelHeight = Mathf.Clamp(visualWheelHeight, -0.2f, 1.0f);
        suspensionDistance = Mathf.Clamp(suspensionDistance, 0.05f, 0.5f);
        suspensionFrequency = Mathf.Clamp(suspensionFrequency, 1.0f, 6.0f);
        suspensionDamping = Mathf.Clamp(suspensionDamping, 0.1f, 1.0f);
        suspensionTargetPosition = Mathf.Clamp01(suspensionTargetPosition);
        frontWeightBias = Mathf.Clamp(frontWeightBias, 0.3f, 0.7f);
        antiRollFront = Mathf.Max(0.0f, antiRollFront);
        antiRollRear = Mathf.Max(0.0f, antiRollRear);
        centerOfMassHeight = Mathf.Clamp01(centerOfMassHeight);
    }
}
