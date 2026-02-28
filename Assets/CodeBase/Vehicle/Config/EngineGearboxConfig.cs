using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Vehicles/Configs/Engine Gearbox", fileName = "EngineGearboxConfig")]
public class EngineGearboxConfig : ScriptableObject
{
    [Header("UI")]
    public Sprite icon;

    [Header("Drive")]
    public CarControllerBase.DriveType driveType = CarControllerBase.DriveType.Rwd;
    [Range(60.0f, 3000.0f)] public float horsepower = 320.0f;

    [Header("Gearbox")]
    public CarControllerBase.GearboxSettings gearbox = new CarControllerBase.GearboxSettings();

    [Header("Engine")]
    public CarControllerBase.EngineSettings engine = new CarControllerBase.EngineSettings();
    public AnimationCurve powerCurve = new AnimationCurve(
        new Keyframe(0.0f, 0.7f),
        new Keyframe(0.45f, 1.0f),
        new Keyframe(0.75f, 0.9f),
        new Keyframe(1.0f, 0.75f));

    private void OnValidate() => Validate();

    public void Validate()
    {
        horsepower = Mathf.Max(10.0f, horsepower);

        if (gearbox != null)
        {
            gearbox.finalDrive = Mathf.Max(0.1f, gearbox.finalDrive);
            gearbox.reverseRatio = Mathf.Max(0.1f, gearbox.reverseRatio);
            gearbox.shiftDuration = Mathf.Max(0.01f, gearbox.shiftDuration);
            gearbox.upshiftRpm = Mathf.Max(500.0f, gearbox.upshiftRpm);
            gearbox.downshiftRpm = Mathf.Max(500.0f, gearbox.downshiftRpm);
            if (gearbox.forwardGears == null)
                gearbox.forwardGears = new List<float>();

            if (gearbox.forwardGears.Count == 0)
            {
                gearbox.forwardGears.Add(3.1f);
                gearbox.forwardGears.Add(2.2f);
                gearbox.forwardGears.Add(1.6f);
                gearbox.forwardGears.Add(1.2f);
                gearbox.forwardGears.Add(1.0f);
            }

            for (int i = 0; i < gearbox.forwardGears.Count; i++)
                gearbox.forwardGears[i] = Mathf.Max(0.1f, gearbox.forwardGears[i]);
        }

        if (engine != null)
        {
            engine.idleRpm = Mathf.Max(400.0f, engine.idleRpm);
            engine.maxRpm = Mathf.Max(engine.idleRpm + 500.0f, engine.maxRpm);
            engine.rpmResponse = Mathf.Clamp(engine.rpmResponse, 1.0f, 25.0f);
        }

        if (powerCurve == null || powerCurve.length == 0)
        {
            powerCurve = new AnimationCurve(
                new Keyframe(0.0f, 0.7f),
                new Keyframe(0.45f, 1.0f),
                new Keyframe(0.75f, 0.9f),
                new Keyframe(1.0f, 0.75f));
        }
    }
}
