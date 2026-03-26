using System;
using PurrNet.Prediction;

[Serializable]
public enum PurrVehicleSimulationBackend : byte
{
    None = 0,
    LegacyController = 1,
    ArcadePrototype = 2
}

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
    public byte simulationBackend;
    public CarControllerSimulationState legacySimulation;
    public ArcadePrototypeCarController.VehicleState arcadeSimulation;
    public PurrVehiclePredictedDamageState damage;

    public static PurrVehiclePredictedControllerState Capture(PurrVehicleSimulationBridge bridge, CarDamageController damageController)
    {
        PurrVehicleSimulationBackend backend = PurrVehicleSimulationBackend.None;
        CarControllerSimulationState legacy = default;
        ArcadePrototypeCarController.VehicleState arcade = default;

        if (bridge != null)
        {
            if (bridge.UsesArcadeController)
            {
                backend = PurrVehicleSimulationBackend.ArcadePrototype;
                ArcadePrototypeCarController controller = bridge.ArcadeController;
                if (controller != null)
                    arcade = controller.CaptureState();
            }
            else if (bridge.UsesLegacyController)
            {
                backend = PurrVehicleSimulationBackend.LegacyController;
                CarControllerBase controller = bridge.LegacyController;
                legacy = controller != null ? controller.CaptureSimulationState() : default;
            }
        }

        return new PurrVehiclePredictedControllerState
        {
            simulationBackend = (byte)backend,
            legacySimulation = legacy,
            arcadeSimulation = arcade,
            damage = PurrVehiclePredictedDamageState.Capture(damageController)
        };
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
