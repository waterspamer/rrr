using System;
using System.Diagnostics;
using PurrNet;
using PurrNet.Prediction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PurrNetEditorSmokeRunner
{
    private const string DefaultScenePath = "Assets/Scenes/Game.unity";
    private const string HostArg = "-rrrSmokeHost";
    private const string PortArg = "-rrrSmokePort";
    private const string TickRateArg = "-rrrSmokeTickRate";
    private const string DurationArg = "-rrrSmokeDuration";
    private const string SceneArg = "-rrrSmokeScene";
    private const string ExpectedCarsArg = "-rrrSmokeExpectedCars";

    private static Stopwatch stopwatch;
    private static double timeoutSeconds;
    private static double nextLogAt;
    private static int expectedCars;
    private static bool exitRequested;
    private static bool smokeSucceeded;

    public static void RunClientSmoke()
    {
        Cleanup();

        string host = GetArgValue(HostArg, "93.183.80.30");
        ushort port = GetArgUShort(PortArg, 5000);
        int tickRate = Mathf.Clamp(GetArgInt(TickRateArg, 30), 10, 120);
        timeoutSeconds = Mathf.Max(5.0f, GetArgFloat(DurationArg, 20.0f));
        expectedCars = Mathf.Max(1, GetArgInt(ExpectedCarsArg, 2));
        string scenePath = GetArgValue(SceneArg, DefaultScenePath);

        UnityEngine.Debug.Log(
            $"PurrNetEditorSmokeRunner: starting client smoke host={host}:{port} tick={tickRate} timeout={timeoutSeconds}s expectedCars={expectedCars} scene='{scenePath}'.");

        PurrNetSessionRuntime.ConfigureClient(host, port, tickRate);
        EditorSceneManager.OpenScene(scenePath);

        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorApplication.update += HandleEditorUpdate;
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.EnteredPlayMode:
                stopwatch = Stopwatch.StartNew();
                nextLogAt = 0.0;
                exitRequested = false;
                smokeSucceeded = false;
                UnityEngine.Debug.Log("PurrNetEditorSmokeRunner: entered Play Mode.");
                break;
            case PlayModeStateChange.EnteredEditMode:
                if (!exitRequested)
                    return;

                Cleanup();
                EditorApplication.Exit(smokeSucceeded ? 0 : 1);
                break;
        }
    }

    private static void HandleEditorUpdate()
    {
        if (!EditorApplication.isPlaying || stopwatch == null)
            return;

        double elapsed = stopwatch.Elapsed.TotalSeconds;
        if (elapsed >= nextLogAt)
        {
            nextLogAt = elapsed + 1.0;
            LogSceneState(elapsed);
        }

        if (CheckSuccess())
        {
            smokeSucceeded = true;
            RequestExit($"PurrNetEditorSmokeRunner: success after {elapsed:F1}s.");
            return;
        }

        if (elapsed >= timeoutSeconds)
        {
            smokeSucceeded = false;
            RequestExit($"PurrNetEditorSmokeRunner: timeout after {elapsed:F1}s.");
        }
    }

    private static bool CheckSuccess()
    {
        PredictionManager predictionManager = UnityEngine.Object.FindFirstObjectByType<PredictionManager>(FindObjectsInactive.Include);
        if (predictionManager == null || !predictionManager.isSpawned || predictionManager.hierarchy == null)
            return false;

        PlayerCar[] cars = UnityEngine.Object.FindObjectsByType<PlayerCar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int activePredictedCars = 0;
        for (int i = 0; i < cars.Length; i++)
        {
            PlayerCar car = cars[i];
            if (car == null || !car.gameObject.activeInHierarchy)
                continue;
            if (car.GetComponent<PurrVehiclePredictedController>() == null)
                continue;
            activePredictedCars++;
        }

        return activePredictedCars >= expectedCars;
    }

    private static void LogSceneState(double elapsed)
    {
        PurrNetGameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<PurrNetGameBootstrap>(FindObjectsInactive.Include);
        NetworkManager[] managers = UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        PredictionManager predictionManager = UnityEngine.Object.FindFirstObjectByType<PredictionManager>(FindObjectsInactive.Include);
        PlayerCar[] cars = UnityEngine.Object.FindObjectsByType<PlayerCar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        PurrVehiclePredictedController[] controllers = UnityEngine.Object.FindObjectsByType<PurrVehiclePredictedController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        UnityEngine.Debug.Log(
            $"PurrNetEditorSmokeRunner: t={elapsed:F1}s scene='{EditorSceneManager.GetActiveScene().name}' bootstrap={(bootstrap != null)} networkManagers={managers.Length} cars={cars.Length} controllers={controllers.Length} predictionManager={(predictionManager != null)} predictionSpawned={(predictionManager != null && predictionManager.isSpawned)} hierarchy={(predictionManager != null && predictionManager.hierarchy != null)} localPlayer={(predictionManager != null ? predictionManager.localPlayer?.ToString() ?? "null" : "null")}");

        if (bootstrap != null)
        {
            UnityEngine.Debug.Log(
                $"PurrNetEditorSmokeRunner: bootstrap root='{bootstrap.gameObject.name}' activeSelf={bootstrap.gameObject.activeSelf} activeInHierarchy={bootstrap.gameObject.activeInHierarchy} scene='{bootstrap.gameObject.scene.name}'.",
                bootstrap);
        }

        for (int i = 0; i < managers.Length; i++)
        {
            NetworkManager manager = managers[i];
            if (manager == null)
                continue;

            UnityEngine.Debug.Log(
                $"PurrNetEditorSmokeRunner: manager[{i}] name='{manager.gameObject.name}' activeSelf={manager.gameObject.activeSelf} activeInHierarchy={manager.gameObject.activeInHierarchy} scene='{manager.gameObject.scene.name}' isServer={manager.isServer} isClient={manager.isClient} clientState={manager.clientState} serverState={manager.serverState}",
                manager);
        }

        for (int i = 0; i < cars.Length; i++)
        {
            PlayerCar car = cars[i];
            if (car == null)
                continue;

            PurrVehiclePredictedController controller = car.GetComponent<PurrVehiclePredictedController>();
            UnityEngine.Debug.Log(
                $"PurrNetEditorSmokeRunner: car[{i}] name='{car.name}' activeSelf={car.gameObject.activeSelf} activeInHierarchy={car.gameObject.activeInHierarchy} scene='{car.gameObject.scene.name}' pos={car.transform.position} predicted={(controller != null)} owner={(controller != null ? controller.owner?.ToString() ?? "null" : "none")} isOwner={(controller != null && controller.isOwner)}",
                car);
        }
    }

    private static void RequestExit(string message)
    {
        if (exitRequested)
            return;

        exitRequested = true;
        UnityEngine.Debug.Log(message);
        EditorApplication.ExitPlaymode();
    }

    private static void Cleanup()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.update -= HandleEditorUpdate;
        stopwatch = null;
        nextLogAt = 0.0;
        timeoutSeconds = 0.0;
        expectedCars = 0;
        exitRequested = false;
        smokeSucceeded = false;
        PurrNetSessionRuntime.Reset();
    }

    private static string GetArgValue(string key, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        if (args != null)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(args[i + 1]) ? fallback : args[i + 1].Trim();
            }
        }

        return fallback;
    }

    private static int GetArgInt(string key, int fallback)
    {
        return int.TryParse(GetArgValue(key, string.Empty), out int parsed) ? parsed : fallback;
    }

    private static ushort GetArgUShort(string key, ushort fallback)
    {
        return ushort.TryParse(GetArgValue(key, string.Empty), out ushort parsed) ? parsed : fallback;
    }

    private static float GetArgFloat(string key, float fallback)
    {
        return float.TryParse(GetArgValue(key, string.Empty), out float parsed) ? parsed : fallback;
    }
}
