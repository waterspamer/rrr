using System;
using System.Collections;
using PurrNet.Prediction;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PurrNetAutoLaunchRuntime
{
    private const string AutoClientArg = "-rrrAutoPurrClient";
    private const string AutoSceneArg = "-rrrAutoScene";
    private const string AutoDumpDelayArg = "-rrrAutoDumpAfterSeconds";
    private const string AutoQuitDelayArg = "-rrrAutoQuitAfterSeconds";

    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        if (!HasArg(AutoClientArg))
            return;

        string targetScene = GetArgValue(AutoSceneArg, "Game");
        float dumpDelay = GetArgFloat(AutoDumpDelayArg, 6.0f);
        float quitDelay = GetArgFloat(AutoQuitDelayArg, 12.0f);

        GameObject root = new GameObject("PurrNetAutoLaunchRuntime");
        UnityEngine.Object.DontDestroyOnLoad(root);
        PurrNetAutoLaunchBehaviour behaviour = root.AddComponent<PurrNetAutoLaunchBehaviour>();
        behaviour.Configure(targetScene, dumpDelay, quitDelay);
    }

    private static bool HasArg(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        if (args == null)
            return false;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

    private static float GetArgFloat(string key, float fallback)
    {
        string raw = GetArgValue(key, string.Empty);
        return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
            ? Mathf.Max(0.1f, parsed)
            : fallback;
    }
}

[DisallowMultipleComponent]
public sealed class PurrNetAutoLaunchBehaviour : MonoBehaviour
{
    private string targetSceneName = "Game";
    private float dumpDelaySeconds = 6.0f;
    private float quitDelaySeconds = 12.0f;
    private bool loadRequested;
    private Coroutine diagnosticsCoroutine;

    public void Configure(string sceneName, float dumpDelay, float quitDelay)
    {
        targetSceneName = string.IsNullOrWhiteSpace(sceneName) ? "Game" : sceneName.Trim();
        dumpDelaySeconds = Mathf.Max(0.1f, dumpDelay);
        quitDelaySeconds = Mathf.Max(dumpDelaySeconds + 1.0f, quitDelay);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryEnterTargetScene(SceneManager.GetActiveScene());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryEnterTargetScene(scene);
    }

    private void TryEnterTargetScene(Scene scene)
    {
        if (!scene.IsValid())
            return;

        if (!PurrNetSessionRuntime.IsEnabled)
        {
            Debug.LogWarning("PurrNetAutoLaunch: session runtime is not enabled. Use -rrrNetMode client/server/host.", this);
            return;
        }

        if (!string.Equals(scene.name, targetSceneName, StringComparison.OrdinalIgnoreCase))
        {
            if (loadRequested)
                return;

            loadRequested = true;
            Debug.Log($"PurrNetAutoLaunch: loading target scene '{targetSceneName}' from '{scene.name}'.", this);
            SceneManager.LoadScene(targetSceneName);
            return;
        }

        loadRequested = false;
        if (diagnosticsCoroutine != null)
            StopCoroutine(diagnosticsCoroutine);
        diagnosticsCoroutine = StartCoroutine(RunDiagnostics());
    }

    private IEnumerator RunDiagnostics()
    {
        yield return new WaitForSecondsRealtime(dumpDelaySeconds);
        DumpSceneState();

        float remaining = Mathf.Max(0.1f, quitDelaySeconds - dumpDelaySeconds);
        yield return new WaitForSecondsRealtime(remaining);
        Debug.Log("PurrNetAutoLaunch: quitting after automated smoke run.", this);
        Application.Quit(0);
    }

    private void DumpSceneState()
    {
        PredictionManager predictionManager = FindFirstObjectByType<PredictionManager>(FindObjectsInactive.Include);
        PlayerCar[] cars = FindObjectsByType<PlayerCar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        PurrVehiclePredictedController[] controllers = FindObjectsByType<PurrVehiclePredictedController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        Debug.Log(
            $"PurrNetAutoLaunch: scene='{SceneManager.GetActiveScene().name}' cars={cars.Length} predictedControllers={controllers.Length} predictionManager={(predictionManager != null)} predictionSpawned={(predictionManager != null && predictionManager.isSpawned)} hierarchy={(predictionManager != null && predictionManager.hierarchy != null)}",
            this);

        for (int i = 0; i < cars.Length; i++)
        {
            PlayerCar car = cars[i];
            if (car == null)
                continue;

            Transform trs = car.transform;
            Debug.Log(
                $"PurrNetAutoLaunch: car[{i}] name='{car.name}' activeSelf={car.gameObject.activeSelf} activeInHierarchy={car.gameObject.activeInHierarchy} pos={trs.position} predicted={car.GetComponent<PurrVehiclePredictedController>() != null} entity={car.GetComponent<NetworkVehicleEntity>() != null}",
                car);
        }
    }
}
