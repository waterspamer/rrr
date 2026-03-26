public enum GameLaunchVehicleMode
{
    Stock = 0,
    ArcadePrototype = 1
}

public static class GameLaunchRuntime
{
    private static GameLaunchVehicleMode vehicleMode = GameLaunchVehicleMode.Stock;

    public static GameLaunchVehicleMode VehicleMode => vehicleMode;

    public static void SetVehicleMode(GameLaunchVehicleMode mode)
    {
        vehicleMode = mode;
    }

    public static void Reset()
    {
        vehicleMode = GameLaunchVehicleMode.Stock;
    }
}
