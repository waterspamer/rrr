using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BackendRuntime : MonoBehaviour
{
    private static BackendRuntime instance;
    private readonly Queue<Action> mainThreadQueue = new Queue<Action>();
    private BackendClient client;
    private BackendConfig config;

    public static BackendRuntime Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject root = new GameObject("BackendRuntime");
                DontDestroyOnLoad(root);
                instance = root.AddComponent<BackendRuntime>();
            }

            return instance;
        }
    }

    public static BackendClient Client => Instance.GetClient();
    public BackendConfig Config => config != null ? config : (config = BackendConfig.LoadOrDefault());

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
    }

    private void Update()
    {
        while (true)
        {
            Action action = null;
            lock (mainThreadQueue)
            {
                if (mainThreadQueue.Count > 0)
                    action = mainThreadQueue.Dequeue();
            }

            if (action == null)
                break;

            action.Invoke();
        }
    }

    public void PostToMainThread(Action action)
    {
        if (action == null)
            return;

        lock (mainThreadQueue)
            mainThreadQueue.Enqueue(action);
    }

    private BackendClient GetClient()
    {
        if (client == null)
            client = new BackendClient(Config, PostToMainThread);
        return client;
    }
}

public static class Backend
{
    public static BackendClient Client => BackendRuntime.Client;
    public static BackendConfig Config => BackendRuntime.Instance.Config;
}
