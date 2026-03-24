using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class PurrNetDedicatedSmokeTests
{
    private const string DefaultHost = "93.183.80.30";
    private const ushort DefaultPort = 5000;
    private const int DefaultTickRate = 30;
    private const int DefaultExpectedCars = 2;
    private const float DefaultTimeoutSeconds = 25.0f;
    private const string DefaultSceneName = "Game";

    private static readonly string[] CommandLineArgs = Environment.GetCommandLineArgs() ?? Array.Empty<string>();

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        LogAssert.ignoreFailingMessages = true;
        ResetSessionRuntime();
        ConfigureClient();

        SceneManager.LoadScene(DefaultSceneName, LoadSceneMode.Single);
        yield return null;
        yield return null;
        DisableSceneRendering();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        ResetSessionRuntime();
        LogAssert.ignoreFailingMessages = false;
        yield return null;
    }

    [UnityTest]
    public IEnumerator DedicatedClientSpawnsPredictedVehicles()
    {
        float timeoutSeconds = GetFloatArg("-rrrSmokeDuration", DefaultTimeoutSeconds);
        int expectedCars = Mathf.Max(1, GetIntArg("-rrrSmokeExpectedCars", DefaultExpectedCars));
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        string lastSummary = "no samples collected";

        while (Time.realtimeSinceStartup < deadline)
        {
            lastSummary = BuildRuntimeSummary(
                out int activePredictedCars,
                out int localOwnedPredictedCars,
                out int remotePredictedCars,
                out bool clientConnected,
                out bool predictionReady);
            Debug.Log($"PurrNetDedicatedSmokeTests: {lastSummary}");

            if (clientConnected &&
                predictionReady &&
                activePredictedCars >= expectedCars &&
                localOwnedPredictedCars >= 1 &&
                remotePredictedCars >= Mathf.Max(0, expectedCars - 1))
            {
                yield break;
            }

            yield return new WaitForSecondsRealtime(1.0f);
        }

        Assert.Fail($"Smoke timeout after {timeoutSeconds:F1}s. {lastSummary}");
    }

    private static void ConfigureClient()
    {
        Type sessionRuntimeType = FindType("PurrNetSessionRuntime");
        Assert.NotNull(sessionRuntimeType, "PurrNetSessionRuntime type not found.");

        MethodInfo configureClient = sessionRuntimeType.GetMethod(
            "ConfigureClient",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), typeof(ushort), typeof(int) },
            null);
        Assert.NotNull(configureClient, "PurrNetSessionRuntime.ConfigureClient not found.");

        string host = GetStringArg("-rrrSmokeHost", DefaultHost);
        ushort port = (ushort)Mathf.Clamp(GetIntArg("-rrrSmokePort", DefaultPort), 1, 65535);
        int tickRate = Mathf.Clamp(GetIntArg("-rrrSmokeTickRate", DefaultTickRate), 10, 120);
        configureClient.Invoke(null, new object[] { host, port, tickRate });
    }

    private static void DisableSceneRendering()
    {
        Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null)
                camera.enabled = false;
        }

        ReflectionProbe[] probes = Resources.FindObjectsOfTypeAll<ReflectionProbe>();
        for (int i = 0; i < probes.Length; i++)
        {
            ReflectionProbe probe = probes[i];
            if (probe != null)
                probe.enabled = false;
        }
    }

    private static void ResetSessionRuntime()
    {
        Type sessionRuntimeType = FindType("PurrNetSessionRuntime");
        MethodInfo reset = sessionRuntimeType?.GetMethod("Reset", BindingFlags.Public | BindingFlags.Static);
        reset?.Invoke(null, null);
    }

    private static string BuildRuntimeSummary(
        out int activePredictedCars,
        out int localOwnedPredictedCars,
        out int remotePredictedCars,
        out bool clientConnected,
        out bool predictionReady)
    {
        activePredictedCars = 0;
        localOwnedPredictedCars = 0;
        remotePredictedCars = 0;
        clientConnected = false;
        predictionReady = false;

        Type bootstrapType = FindType("PurrNetGameBootstrap");
        Type networkManagerType = FindType("PurrNet.NetworkManager");
        Type predictionManagerType = FindType("PurrNet.Prediction.PredictionManager");
        Type playerCarType = FindType("PlayerCar");
        Type predictedControllerType = FindType("PurrVehiclePredictedController");

        UnityEngine.Object bootstrap = FindFirstObject(bootstrapType);
        UnityEngine.Object predictionManager = FindFirstObject(predictionManagerType);
        UnityEngine.Object[] managers = FindObjects(networkManagerType);
        UnityEngine.Object[] cars = FindObjects(playerCarType);

        string clientStates = string.Join(
            ",",
            managers.Select(m => $"{m.name}:{GetPropertyValue(m, "clientState")}/{GetPropertyValue(m, "serverState")}"));

        bool hasHierarchy = GetPropertyValue(predictionManager, "hierarchy") != null;
        bool isPredictionSpawned = GetBooleanProperty(predictionManager, "isSpawned");
        predictionReady = predictionManager != null && isPredictionSpawned && hasHierarchy;

        for (int i = 0; i < managers.Length; i++)
        {
            object state = GetPropertyValue(managers[i], "clientState");
            if (state != null && string.Equals(state.ToString(), "Connected", StringComparison.OrdinalIgnoreCase))
            {
                clientConnected = true;
                break;
            }
        }

        for (int i = 0; i < cars.Length; i++)
        {
            if (!(cars[i] is Component component))
                continue;

            if (!component.gameObject.activeInHierarchy)
                continue;

            if (component.GetComponent(predictedControllerType) == null)
                continue;

            activePredictedCars++;

            bool isOwner = GetBooleanProperty(component.GetComponent(predictedControllerType), "isOwner");
            if (isOwner)
                localOwnedPredictedCars++;
            else
                remotePredictedCars++;
        }

        return
            $"scene={SceneManager.GetActiveScene().name} bootstrap={(bootstrap != null)} managers={managers.Length} clientStates=[{clientStates}] predictionManager={(predictionManager != null)} predictionSpawned={isPredictionSpawned} hasHierarchy={hasHierarchy} activePredictedCars={activePredictedCars} localOwnedPredictedCars={localOwnedPredictedCars} remotePredictedCars={remotePredictedCars} totalCars={cars.Length}";
    }

    private static Type FindType(string fullNameOrName)
    {
        if (string.IsNullOrWhiteSpace(fullNameOrName))
            return null;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Assembly assembly = assemblies[i];
            Type exact = assembly.GetType(fullNameOrName, false);
            if (exact != null)
                return exact;
        }

        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
                continue;

            for (int j = 0; j < types.Length; j++)
            {
                Type type = types[j];
                if (type == null)
                    continue;

                if (string.Equals(type.Name, fullNameOrName, StringComparison.Ordinal))
                    return type;
            }
        }

        return null;
    }

    private static UnityEngine.Object FindFirstObject(Type type)
    {
        UnityEngine.Object[] objects = FindObjects(type);
        return objects.Length > 0 ? objects[0] : null;
    }

    private static UnityEngine.Object[] FindObjects(Type type)
    {
        if (type == null)
            return Array.Empty<UnityEngine.Object>();

        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
        return objects
            .Where(obj => obj != null)
            .Where(IsSceneObject)
            .ToArray();
    }

    private static bool IsSceneObject(UnityEngine.Object obj)
    {
        if (obj is GameObject gameObject)
            return gameObject.scene.IsValid() && !string.IsNullOrWhiteSpace(gameObject.scene.name);

        if (obj is Component component)
            return component.gameObject.scene.IsValid() && !string.IsNullOrWhiteSpace(component.gameObject.scene.name);

        return false;
    }

    private static object GetPropertyValue(UnityEngine.Object target, string propertyName)
    {
        if (target == null || string.IsNullOrWhiteSpace(propertyName))
            return null;

        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(target);
    }

    private static bool GetBooleanProperty(UnityEngine.Object target, string propertyName)
    {
        object value = GetPropertyValue(target, propertyName);
        return value is bool boolValue && boolValue;
    }

    private static string GetStringArg(string key, string fallback)
    {
        for (int i = 0; i < CommandLineArgs.Length - 1; i++)
        {
            if (string.Equals(CommandLineArgs[i], key, StringComparison.OrdinalIgnoreCase))
            {
                string value = CommandLineArgs[i + 1];
                return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            }
        }

        return fallback;
    }

    private static int GetIntArg(string key, int fallback)
    {
        return int.TryParse(GetStringArg(key, string.Empty), out int parsed) ? parsed : fallback;
    }

    private static float GetFloatArg(string key, float fallback)
    {
        return float.TryParse(GetStringArg(key, string.Empty), out float parsed) ? parsed : fallback;
    }
}
