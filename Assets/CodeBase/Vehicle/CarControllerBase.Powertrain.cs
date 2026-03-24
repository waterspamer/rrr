using System.Collections.Generic;
using UnityEngine;

public abstract partial class CarControllerBase
{
    public void SetGear(int gear)
    {
        RequestShift(gear);
    }

    private void UpdatePowertrain(VehicleDynamics.Inputs inputs, float deltaTime)
    {
        EnsureDefaultGears();
        UpdateShiftState(deltaTime);

        if (gearbox.automatic)
            HandleAutoShift(inputs);

        UpdateRpm(inputs, deltaTime);
    }

    private void HandleAutoShift(VehicleDynamics.Inputs inputs)
    {
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
        float forwardSpeed = localVel.z;
        float speed = Mathf.Abs(forwardSpeed);
        int maxGear = Mathf.Max(1, gearbox.forwardGears.Count);

        if (gearbox.allowAutoReverse)
        {
            if (forwardSpeed < -0.5f && inputs.Motor < -0.2f && currentGear >= 0)
            {
                RequestShift(-1);
                return;
            }

            if (forwardSpeed > 0.5f && inputs.Motor > 0.2f && currentGear <= 0)
            {
                RequestShift(1);
                return;
            }

            if (speed < 1.0f)
            {
                if (inputs.Motor < -0.2f && currentGear >= 0)
                {
                    RequestShift(-1);
                    return;
                }

                if (inputs.Motor > 0.2f && currentGear <= 0)
                {
                    RequestShift(1);
                    return;
                }
            }
        }

        if (currentGear <= 0)
            return;

        float minUpshiftRpm = Mathf.Max(engine.maxRpm * 0.9f, gearbox.upshiftRpm);
        if (inputs.Motor > 0.1f && currentRpm >= minUpshiftRpm && currentGear < maxGear)
        {
            float nextRpm = ComputeCoupledRpm(currentGear + 1, currentRpm);
            if (nextRpm >= engine.idleRpm)
                RequestShift(currentGear + 1);
        }
        else if (currentRpm <= gearbox.downshiftRpm && currentGear > 1)
        {
            float nextRpm = ComputeCoupledRpm(currentGear - 1, currentRpm);
            if (nextRpm <= engine.maxRpm * 0.98f)
                RequestShift(currentGear - 1);
        }
    }

    private void UpdateSteering(VehicleDynamics.Inputs inputs, float deltaTime)
    {
        float speedKph = rb.linearVelocity.magnitude * 3.6f;
        float steerScale = 1.0f;
        if (steerBySpeed != null && steerBySpeed.length > 0)
            steerScale = Mathf.Clamp01(steerBySpeed.Evaluate(speedKph));

        float steerLimit = maxSteerAngle * steerScale;
        float targetAngle = inputs.Steer * steerLimit;
        float maxStep = steerResponse * deltaTime * maxSteerAngle;
        currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, targetAngle, maxStep);
    }

    private void UpdateDriftKick(VehicleDynamics.Inputs inputs, float deltaTime)
    {
        float targetForce = 0.0f;
        if (inputs.Handbrake && Mathf.Abs(inputs.Steer) >= driftKickSteerThreshold)
        {
            float speedKph = rb.linearVelocity.magnitude * 3.6f;
            float curveScale = 1.0f;
            if (driftKickBySpeed != null && driftKickBySpeed.length > 0)
                curveScale = Mathf.Clamp01(driftKickBySpeed.Evaluate(speedKph));
            targetForce = curveScale * driftKickMaxForce;
        }

        float t = 1.0f - Mathf.Exp(-driftKickResponse * deltaTime);
        currentDriftKickForce = Mathf.Lerp(currentDriftKickForce, targetForce, t);
    }

    private void ApplyAutoBrakeFromOppositeInput(ref VehicleDynamics.Inputs inputs, Rigidbody body)
    {
        if (inputs.Brake)
            return;

        if (body == null)
            return;

        Vector3 localVel = transform.InverseTransformDirection(body.linearVelocity);
        float forwardSpeed = localVel.z;
        if (gearbox != null && gearbox.allowAutoReverse && Mathf.Abs(forwardSpeed) < 0.5f)
            return;

        if (forwardSpeed > 0.5f && inputs.Motor < -0.2f)
        {
            inputs.Brake = true;
            inputs.Motor = 0.0f;
        }
        else if (forwardSpeed < -0.5f && inputs.Motor > 0.2f)
        {
            inputs.Brake = true;
            inputs.Motor = 0.0f;
        }
    }

    private void RequestShift(int targetGear)
    {
        if (shiftState == GearShiftState.Shifting)
            return;

        int maxGear = Mathf.Max(1, gearbox.forwardGears.Count);
        int clamped = Mathf.Clamp(targetGear, -1, maxGear);
        if (clamped == currentGear)
            return;

        requestedGear = clamped;
        shiftTimer = gearbox.shiftDuration;
        shiftState = GearShiftState.Shifting;
        shiftTargetRpm = Mathf.Clamp(ComputeCoupledRpm(requestedGear, currentRpm), engine.idleRpm, engine.maxRpm);
    }

    private void UpdateShiftState(float deltaTime)
    {
        if (shiftState != GearShiftState.Shifting)
            return;

        if (shiftTimer > 0.0f)
            shiftTimer = Mathf.Max(0.0f, shiftTimer - deltaTime);

        if (shiftTimer > 0.0f)
            return;

        currentGear = requestedGear;
        currentRpm = Mathf.Clamp(ComputeCoupledRpm(currentGear, currentRpm), engine.idleRpm, engine.maxRpm);
        shiftState = GearShiftState.Ready;
    }

    private void UpdateRpm(VehicleDynamics.Inputs inputs, float deltaTime)
    {
        float targetRpm;

        if (shiftState == GearShiftState.Shifting)
            targetRpm = shiftTargetRpm;
        else if (Mathf.Abs(GetEffectiveGearRatio()) > 0.01f)
            targetRpm = ComputeCoupledRpm(currentGear, currentRpm);
        else
            targetRpm = ComputeFreeRpm(inputs.Motor);

        if (currentGear < 0 && inputs.Motor < -0.1f)
            targetRpm = Mathf.Max(targetRpm, ComputeFreeRpm(inputs.Motor));

        targetRpm = Mathf.Clamp(targetRpm, engine.idleRpm, engine.maxRpm);
        currentRpm = MoveRpmToward(targetRpm, Mathf.Abs(inputs.Motor), deltaTime);
    }

    private float ComputeMotorTorque(float motorInput)
    {
        if (shiftState == GearShiftState.Shifting)
            return 0.0f;

        float ratio = GetEffectiveGearRatio();
        if (Mathf.Approximately(ratio, 0.0f))
            return 0.0f;

        if (Mathf.Abs(motorInput) > 0.1f && currentRpm >= engine.maxRpm * 0.995f)
        {
            float gearSign = Mathf.Sign(currentGear);
            if (!Mathf.Approximately(gearSign, 0.0f) && Mathf.Sign(motorInput) == gearSign)
                return 0.0f;
        }

        float signedInput = motorInput;
        if (currentGear < 0)
            signedInput = Mathf.Min(0.0f, motorInput);
        else if (currentGear > 0)
            signedInput = Mathf.Max(0.0f, motorInput);

        float engineTorque = ComputeEngineTorque(currentRpm);
        return signedInput * engineTorque * ratio;
    }

    private float GetEffectiveGearRatio()
    {
        if (shiftState == GearShiftState.Shifting)
            return 0.0f;

        return GetGearRatio(currentGear);
    }

    private float ComputeCoupledRpm(int gear, float fallbackRpm)
    {
        float gearRatioAbs = Mathf.Abs(GetGearRatio(gear));
        if (gearRatioAbs <= 0.01f)
            return fallbackRpm;

        float wheelRpm = GetWheelRpmFromSpeed();
        return wheelRpm * gearRatioAbs;
    }

    private float ComputeFreeRpm(float motorInput)
    {
        float throttle = Mathf.Abs(motorInput);
        return Mathf.Lerp(engine.idleRpm, engine.maxRpm, throttle);
    }

    private float MoveRpmToward(float targetRpm, float throttle, float deltaTime)
    {
        float rpmDelta = targetRpm - currentRpm;
        if (Mathf.Approximately(rpmDelta, 0.0f))
            return targetRpm;

        float powerT = Mathf.InverseLerp(60.0f, 3000.0f, horsepower);
        float responseScale = Mathf.Clamp(engine.rpmResponse / 8.0f, 0.6f, 3.0f);
        if (nitroActive)
            responseScale *= Mathf.Max(1.0f, nitroRpmResponseMultiplier);
        float accelRate = Mathf.Lerp(2000.0f, 12000.0f, powerT) * responseScale;
        float decelRate = Mathf.Lerp(3000.0f, 14000.0f, powerT) * responseScale;
        accelRate *= Mathf.Lerp(0.5f, 1.0f, throttle);
        decelRate *= Mathf.Lerp(2.0f, 1.0f, throttle);
        float maxStep = (rpmDelta > 0.0f ? accelRate : decelRate) * deltaTime;
        float step = Mathf.Clamp(rpmDelta, -maxStep, maxStep);
        return currentRpm + step;
    }

    private float GetWheelRpmFromSpeed()
    {
        float speed = rb.linearVelocity.magnitude;
        float circumference = Mathf.Max(0.01f, 2.0f * Mathf.PI * wheelRadius);
        return speed / circumference * 60.0f;
    }

    private float ComputeEngineTorque(float rpm)
    {
        float hp = horsepower;
        if (nitroActive)
            hp *= Mathf.Max(1.0f, nitroPowerMultiplier);
        float baseTorque = HorsepowerToTorque(hp);
        float normalized = Mathf.InverseLerp(engine.idleRpm, engine.maxRpm, rpm);
        float peak = 0.55f;
        float width = 0.55f;
        float torqueShape = Mathf.Clamp01(1.0f - Mathf.Abs(normalized - peak) / width);
        float torqueFactor = Mathf.Lerp(0.7f, 1.1f, torqueShape);
        if (powerCurve != null && powerCurve.length > 0)
            torqueFactor *= Mathf.Clamp(powerCurve.Evaluate(normalized), 0.1f, 2.0f);
        float curveTorque = baseTorque * torqueFactor;
        float omega = Mathf.Max(1.0f, rpm) * Mathf.Deg2Rad * 6.0f;
        float powerWatts = horsepower * 745.7f;
        float powerLimitedTorque = powerWatts / omega;
        return Mathf.Min(curveTorque, powerLimitedTorque);
    }

    private float GetGearRatio(int gear)
    {
        if (gearbox.forwardGears == null || gearbox.forwardGears.Count == 0)
            return 0.0f;

        if (gear > 0)
        {
            int index = Mathf.Clamp(gear - 1, 0, gearbox.forwardGears.Count - 1);
            return gearbox.forwardGears[index] * gearbox.finalDrive;
        }

        if (gear < 0)
            return gearbox.reverseRatio * gearbox.finalDrive;

        return 0.0f;
    }

    private void EnsureDefaultGears()
    {
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
    }

    private void EnsureHud()
    {
        CarHud existingHud = FindObjectOfType<CarHud>();
        if (existingHud != null)
        {
            existingHud.SetTarget(this);
            return;
        }

        GameObject hudRoot = new GameObject("CarHUD");
        CarHud hud = hudRoot.AddComponent<CarHud>();
        hud.SetTarget(this);
    }

    private static float HorsepowerToTorque(float hp)
    {
        const float wattsPerHp = 745.7f;
        const float rpmAtPeak = 5000.0f;
        float watts = hp * wattsPerHp;
        float omega = rpmAtPeak * Mathf.Deg2Rad * 6.0f;
        return watts / Mathf.Max(1.0f, omega);
    }
}
