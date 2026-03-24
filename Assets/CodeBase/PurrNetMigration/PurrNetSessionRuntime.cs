using System;
using UnityEngine;

public enum PurrNetSessionMode
{
    Disabled = 0,
    Host = 1,
    Client = 2,
    Server = 3
}

public readonly struct PurrNetSessionSettings
{
    public PurrNetSessionSettings(PurrNetSessionMode mode, string address, ushort port, int tickRate, int soloBotCount)
    {
        Mode = mode;
        Address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
        Port = port == 0 ? (ushort)5000 : port;
        TickRate = Mathf.Max(10, tickRate);
        SoloBotCount = Mathf.Max(0, soloBotCount);
    }

    public PurrNetSessionMode Mode { get; }
    public string Address { get; }
    public ushort Port { get; }
    public int TickRate { get; }
    public int SoloBotCount { get; }
    public bool IsEnabled => Mode != PurrNetSessionMode.Disabled;
    public bool IsServerMode => Mode == PurrNetSessionMode.Host || Mode == PurrNetSessionMode.Server;
}

public static class PurrNetSessionRuntime
{
    private static bool initialized;
    private static PurrNetSessionSettings settings;

    public static PurrNetSessionSettings Current
    {
        get
        {
            EnsureInitialized();
            return settings;
        }
    }

    public static bool IsEnabled => Current.IsEnabled;
    public static bool IsServerMode => Current.IsServerMode;

    public static bool TryGetSettings(out PurrNetSessionSettings resolved)
    {
        resolved = Current;
        return resolved.IsEnabled;
    }

    public static void ConfigureHost(string address = "127.0.0.1", ushort port = 5000, int tickRate = 30, int soloBotCount = 1)
    {
        Configure(new PurrNetSessionSettings(PurrNetSessionMode.Host, address, port, tickRate, soloBotCount));
    }

    public static void ConfigureClient(string address = "127.0.0.1", ushort port = 5000, int tickRate = 30)
    {
        Configure(new PurrNetSessionSettings(PurrNetSessionMode.Client, address, port, tickRate, 0));
    }

    public static void ConfigureServer(string address = "0.0.0.0", ushort port = 5000, int tickRate = 30, int soloBotCount = 0)
    {
        Configure(new PurrNetSessionSettings(PurrNetSessionMode.Server, address, port, tickRate, soloBotCount));
    }

    public static void Reset()
    {
        initialized = true;
        settings = new PurrNetSessionSettings(PurrNetSessionMode.Disabled, "127.0.0.1", 5000, 30, 0);
    }

    public static void Configure(PurrNetSessionSettings configured)
    {
        initialized = true;
        settings = configured;
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;
        settings = ParseCommandLine(Environment.GetCommandLineArgs());
    }

    private static PurrNetSessionSettings ParseCommandLine(string[] args)
    {
        PurrNetSessionMode mode = PurrNetSessionMode.Disabled;
        string address = "127.0.0.1";
        ushort port = 5000;
        int tickRate = 30;
        int soloBotCount = 0;

        if (TryGetArgValue(args, "-rrrNetMode", out string modeValue))
            mode = ParseMode(modeValue);
        if (TryGetArgValue(args, "-rrrNetAddress", out string addressValue) && !string.IsNullOrWhiteSpace(addressValue))
            address = addressValue.Trim();
        if (TryGetArgValue(args, "-rrrNetPort", out string portValue) && ushort.TryParse(portValue, out ushort parsedPort))
            port = parsedPort;
        if (TryGetArgValue(args, "-rrrNetTickRate", out string tickRateValue) && int.TryParse(tickRateValue, out int parsedTickRate))
            tickRate = parsedTickRate;
        if (TryGetArgValue(args, "-rrrNetSoloBots", out string botCountValue) && int.TryParse(botCountValue, out int parsedBotCount))
            soloBotCount = parsedBotCount;

        if (mode == PurrNetSessionMode.Server && string.Equals(address, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            address = "0.0.0.0";

        return new PurrNetSessionSettings(mode, address, port, tickRate, soloBotCount);
    }

    private static bool TryGetArgValue(string[] args, string key, out string value)
    {
        value = null;
        if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(key))
            return false;

        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                continue;

            int next = i + 1;
            if (next >= args.Length)
                return false;

            value = args[next];
            return true;
        }

        return false;
    }

    private static PurrNetSessionMode ParseMode(string rawMode)
    {
        if (string.IsNullOrWhiteSpace(rawMode))
            return PurrNetSessionMode.Disabled;

        switch (rawMode.Trim().ToLowerInvariant())
        {
            case "host":
                return PurrNetSessionMode.Host;
            case "client":
                return PurrNetSessionMode.Client;
            case "server":
                return PurrNetSessionMode.Server;
            default:
                return PurrNetSessionMode.Disabled;
        }
    }
}
