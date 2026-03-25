using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkVehicleEntity : MonoBehaviour
{
    [SerializeField] private string playerId;
    [SerializeField] private string playerName;
    [SerializeField] private string accountPlayerId;
    [SerializeField] private string authProvider;
    [SerializeField] private string authState;
    [SerializeField] private string sessionId;
    [SerializeField] private bool isBot;
    [SerializeField] private bool isLocalPlayer;
    [SerializeField] private PurrPlayerStatsData replicatedStats = new PurrPlayerStatsData();

    public string PlayerId => playerId;
    public string PlayerName => string.IsNullOrWhiteSpace(playerName) ? playerId : playerName;
    public string AccountPlayerId => accountPlayerId;
    public string AuthProvider => authProvider;
    public string AuthState => authState;
    public string SessionId => sessionId;
    public bool IsBot => isBot;
    public bool IsLocalPlayer => isLocalPlayer;
    public event System.Action<NetworkVehicleEntity> IdentityChanged;
    public event System.Action<NetworkVehicleEntity> StatsChanged;

    public void Configure(string id, bool localPlayer)
    {
        bool changed =
            !string.Equals(playerId, id, System.StringComparison.Ordinal) ||
            isLocalPlayer != localPlayer;
        playerId = id;
        isLocalPlayer = localPlayer;
        if (changed)
            IdentityChanged?.Invoke(this);
    }

    public void ApplyProfile(PurrPlayerProfileData profile)
    {
        if (profile == null)
            return;

        bool changed =
            !string.Equals(playerId, profile.networkPlayerId, System.StringComparison.Ordinal) ||
            !string.Equals(playerName, profile.playerName, System.StringComparison.Ordinal) ||
            !string.Equals(accountPlayerId, profile.accountPlayerId, System.StringComparison.Ordinal) ||
            !string.Equals(authProvider, profile.authProvider, System.StringComparison.Ordinal) ||
            !string.Equals(authState, profile.authState, System.StringComparison.Ordinal) ||
            !string.Equals(sessionId, profile.sessionId, System.StringComparison.Ordinal) ||
            isBot != profile.isBot;

        if (!string.IsNullOrWhiteSpace(profile.networkPlayerId))
            playerId = profile.networkPlayerId;
        playerName = profile.playerName;
        accountPlayerId = profile.accountPlayerId;
        authProvider = profile.authProvider;
        authState = profile.authState;
        sessionId = profile.sessionId;
        isBot = profile.isBot;

        if (changed)
            IdentityChanged?.Invoke(this);
    }

    public void ApplyStats(PurrPlayerStatsData stats)
    {
        if (stats == null)
            return;

        replicatedStats = stats.Clone();
        StatsChanged?.Invoke(this);
    }

    public bool TryGetNumberStat(string key, out float value)
    {
        value = 0.0f;
        return replicatedStats != null && replicatedStats.TryGetNumber(key, out value);
    }

    public bool TryGetIntegerStat(string key, out long value)
    {
        value = 0;
        return replicatedStats != null && replicatedStats.TryGetInteger(key, out value);
    }

    public bool TryGetBooleanStat(string key, out bool value)
    {
        value = false;
        return replicatedStats != null && replicatedStats.TryGetBoolean(key, out value);
    }

    public bool TryGetNormalizedHealth(out float healthNormalized)
    {
        healthNormalized = 1.0f;
        if (replicatedStats == null)
            return false;
        if (!replicatedStats.TryGetNumber("current_health", out float currentHealth) ||
            !replicatedStats.TryGetNumber("max_health", out float maxHealth) ||
            maxHealth <= 0.0001f)
        {
            return false;
        }

        healthNormalized = Mathf.Clamp01(currentHealth / maxHealth);
        return true;
    }
}
