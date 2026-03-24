using System;

[Serializable]
public struct CarControllerSimulationState
{
    public int currentGear;
    public int requestedGear;
    public float currentRpm;
    public float shiftTimer;
    public float shiftTargetRpm;
    public int shiftState;
    public float currentSteerAngle;
    public float currentDriftKickForce;
    public float currentSteeringWheelAngle;
    public float nitroAmount;
    public bool nitroActive;
    public bool nitroInitialized;
}
