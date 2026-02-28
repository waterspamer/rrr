using UnityEngine;

public class ProceduralCarController : CarControllerBase
{
    [Header("Generation")]
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField] private bool autoBodyOffset = true;
    [SerializeField] private Vector3 bodySize = new Vector3(2.0f, 0.6f, 4.0f);
    [SerializeField] private Vector3 bodyOffset = new Vector3(0.0f, 0.6f, 0.0f);
    [SerializeField] private float wheelbase = 2.6f;
    [SerializeField] private float trackWidth = 1.3f;
    protected override void BuildCar()
    {
        if (!generateOnAwake)
            return;

        Transform body = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        body.name = "Body";
        body.SetParent(transform, false);
        body.localScale = bodySize;

        if (autoBodyOffset)
            bodyOffset = new Vector3(0.0f, wheelRadius + bodySize.y * 0.5f, 0.0f);

        body.localPosition = bodyOffset;

        CreateWheel("FrontLeft", new Vector3(-trackWidth * 0.5f, wheelRadius, wheelbase * 0.5f), null, true, true, false, true, false);
        CreateWheel("FrontRight", new Vector3(trackWidth * 0.5f, wheelRadius, wheelbase * 0.5f), null, true, true, false, true, false);
        CreateWheel("RearLeft", new Vector3(-trackWidth * 0.5f, wheelRadius, -wheelbase * 0.5f), null, true, false, true, true, false);
        CreateWheel("RearRight", new Vector3(trackWidth * 0.5f, wheelRadius, -wheelbase * 0.5f), null, true, false, true, true, false);
    }

    protected override void UpdateCenterOfMass()
    {
        if (autoBodyOffset)
            rb.centerOfMass = new Vector3(0.0f, bodyOffset.y * 0.3f, 0.0f);
        else
            base.UpdateCenterOfMass();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        wheelbase = Mathf.Max(0.5f, wheelbase);
        trackWidth = Mathf.Max(0.5f, trackWidth);
    }
}
