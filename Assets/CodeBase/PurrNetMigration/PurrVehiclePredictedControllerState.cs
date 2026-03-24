using System;
using PurrNet.Prediction;

[Serializable]
public struct PurrVehiclePredictedDamageState
{
    public int revision;
    public int width;
    public int height;
    public byte[] rawBytes;

    public bool HasSnapshot => width > 0 && height > 0 && rawBytes != null && rawBytes.Length > 0;

    public static PurrVehiclePredictedDamageState Capture(CarDamageController damageController)
    {
        try
        {
            if (damageController == null || !damageController.TryCaptureDamageSnapshot(out CarDamageNetworkSnapshot snapshot) || snapshot == null)
                return default;

            return new PurrVehiclePredictedDamageState
            {
                revision = snapshot.revision,
                width = snapshot.width,
                height = snapshot.height,
                rawBytes = snapshot.rawBytes
            };
        }
        catch
        {
            return default;
        }
    }

    public CarDamageNetworkSnapshot ToSnapshot()
    {
        if (!HasSnapshot)
            return null;

        return new CarDamageNetworkSnapshot
        {
            revision = revision,
            width = width,
            height = height,
            rawBytes = rawBytes
        };
    }
}

public struct PurrVehiclePredictedControllerState : IPredictedData<PurrVehiclePredictedControllerState>
{
    public CarControllerSimulationState simulation;
    public PurrVehiclePredictedDamageState damage;

    public static PurrVehiclePredictedControllerState Capture(CarControllerBase controller, CarDamageController damageController)
    {
        return new PurrVehiclePredictedControllerState
        {
            simulation = controller != null ? controller.CaptureSimulationState() : default,
            damage = PurrVehiclePredictedDamageState.Capture(damageController)
        };
    }

    public CarControllerSimulationState ToSimulationState()
    {
        return simulation;
    }

    public bool HasDamageSnapshot => damage.HasSnapshot;

    public CarDamageNetworkSnapshot CreateDamageSnapshot()
    {
        return damage.ToSnapshot();
    }

    public void Dispose()
    {
    }
}
