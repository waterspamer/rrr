using System;
using System.Collections.Generic;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

[Serializable]
public sealed class PurrPlayerProfileData
{
    public int version = 1;
    public string networkPlayerId;
    public string accountPlayerId;
    public string playerName;
    public string authProvider;
    public string authState;
    public string sessionId;
    public string platform;
    public bool isBot;
    public long updatedAtUnixMs;

    public string DisplayName => string.IsNullOrWhiteSpace(playerName) ? networkPlayerId : playerName;

    public PurrPlayerProfileData Clone()
    {
        return new PurrPlayerProfileData
        {
            version = version,
            networkPlayerId = networkPlayerId,
            accountPlayerId = accountPlayerId,
            playerName = playerName,
            authProvider = authProvider,
            authState = authState,
            sessionId = sessionId,
            platform = platform,
            isBot = isBot,
            updatedAtUnixMs = updatedAtUnixMs
        };
    }
}

[Serializable]
public sealed class PurrPlayerRosterEntry
{
    public PurrPlayerProfileData profile;
    public PurrPlayerStatsData stats;
    public string connectionState;
    public int authorityOrder;

    public PurrPlayerRosterEntry Clone()
    {
        return new PurrPlayerRosterEntry
        {
            profile = profile != null ? profile.Clone() : null,
            stats = stats != null ? stats.Clone() : null,
            connectionState = connectionState,
            authorityOrder = authorityOrder
        };
    }
}

[Serializable]
public enum PurrPlayerStatValueKind
{
    Number = 0,
    Integer = 1,
    Boolean = 2,
    Text = 3
}

[Serializable]
public sealed class PurrPlayerStatEntry
{
    public string key;
    public string displayName;
    public PurrPlayerStatValueKind valueKind;
    public float numberValue;
    public long integerValue;
    public bool boolValue;
    public string textValue;
    public bool hasRange;
    public float minValue;
    public float maxValue;

    public PurrPlayerStatEntry Clone()
    {
        return new PurrPlayerStatEntry
        {
            key = key,
            displayName = displayName,
            valueKind = valueKind,
            numberValue = numberValue,
            integerValue = integerValue,
            boolValue = boolValue,
            textValue = textValue,
            hasRange = hasRange,
            minValue = minValue,
            maxValue = maxValue
        };
    }
}

[Serializable]
public sealed class PurrPlayerStatsData
{
    public int version = 1;
    public long updatedAtUnixMs;
    public List<PurrPlayerStatEntry> entries = new List<PurrPlayerStatEntry>();

    public void SetNumber(string key, string displayName, float value, float minValue = 0.0f, float maxValue = 0.0f, bool hasRange = false)
    {
        PurrPlayerStatEntry entry = GetOrCreateEntry(key, displayName);
        entry.valueKind = PurrPlayerStatValueKind.Number;
        entry.numberValue = value;
        entry.hasRange = hasRange;
        entry.minValue = minValue;
        entry.maxValue = maxValue;
    }

    public void SetInteger(string key, string displayName, long value, long minValue = 0, long maxValue = 0, bool hasRange = false)
    {
        PurrPlayerStatEntry entry = GetOrCreateEntry(key, displayName);
        entry.valueKind = PurrPlayerStatValueKind.Integer;
        entry.integerValue = value;
        entry.hasRange = hasRange;
        entry.minValue = minValue;
        entry.maxValue = maxValue;
    }

    public void SetBoolean(string key, string displayName, bool value)
    {
        PurrPlayerStatEntry entry = GetOrCreateEntry(key, displayName);
        entry.valueKind = PurrPlayerStatValueKind.Boolean;
        entry.boolValue = value;
        entry.hasRange = false;
    }

    public void SetText(string key, string displayName, string value)
    {
        PurrPlayerStatEntry entry = GetOrCreateEntry(key, displayName);
        entry.valueKind = PurrPlayerStatValueKind.Text;
        entry.textValue = value ?? string.Empty;
        entry.hasRange = false;
    }

    public bool TryGetNumber(string key, out float value)
    {
        value = 0.0f;
        if (!TryFindEntry(key, out PurrPlayerStatEntry entry))
            return false;

        switch (entry.valueKind)
        {
            case PurrPlayerStatValueKind.Number:
                value = entry.numberValue;
                return true;
            case PurrPlayerStatValueKind.Integer:
                value = entry.integerValue;
                return true;
            case PurrPlayerStatValueKind.Boolean:
                value = entry.boolValue ? 1.0f : 0.0f;
                return true;
            default:
                return false;
        }
    }

    public bool TryGetInteger(string key, out long value)
    {
        value = 0;
        if (!TryFindEntry(key, out PurrPlayerStatEntry entry))
            return false;

        switch (entry.valueKind)
        {
            case PurrPlayerStatValueKind.Integer:
                value = entry.integerValue;
                return true;
            case PurrPlayerStatValueKind.Number:
                value = Mathf.RoundToInt(entry.numberValue);
                return true;
            case PurrPlayerStatValueKind.Boolean:
                value = entry.boolValue ? 1 : 0;
                return true;
            default:
                return false;
        }
    }

    public bool TryGetBoolean(string key, out bool value)
    {
        value = false;
        if (!TryFindEntry(key, out PurrPlayerStatEntry entry))
            return false;

        switch (entry.valueKind)
        {
            case PurrPlayerStatValueKind.Boolean:
                value = entry.boolValue;
                return true;
            case PurrPlayerStatValueKind.Number:
                value = entry.numberValue > 0.5f;
                return true;
            case PurrPlayerStatValueKind.Integer:
                value = entry.integerValue != 0;
                return true;
            default:
                return false;
        }
    }

    public bool TryGetText(string key, out string value)
    {
        value = string.Empty;
        if (!TryFindEntry(key, out PurrPlayerStatEntry entry))
            return false;

        switch (entry.valueKind)
        {
            case PurrPlayerStatValueKind.Text:
                value = entry.textValue ?? string.Empty;
                return true;
            case PurrPlayerStatValueKind.Boolean:
                value = entry.boolValue ? "true" : "false";
                return true;
            case PurrPlayerStatValueKind.Integer:
                value = entry.integerValue.ToString();
                return true;
            case PurrPlayerStatValueKind.Number:
                value = entry.numberValue.ToString("0.##");
                return true;
            default:
                return false;
        }
    }

    public PurrPlayerStatsData Clone()
    {
        PurrPlayerStatsData clone = new PurrPlayerStatsData
        {
            version = version,
            updatedAtUnixMs = updatedAtUnixMs
        };

        if (entries == null)
            return clone;

        for (int i = 0; i < entries.Count; i++)
        {
            PurrPlayerStatEntry entry = entries[i];
            if (entry != null)
                clone.entries.Add(entry.Clone());
        }

        return clone;
    }

    private PurrPlayerStatEntry GetOrCreateEntry(string key, string displayName)
    {
        if (!TryFindEntry(key, out PurrPlayerStatEntry entry))
        {
            entry = new PurrPlayerStatEntry
            {
                key = key,
                displayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName
            };
            entries.Add(entry);
        }
        else if (!string.IsNullOrWhiteSpace(displayName))
        {
            entry.displayName = displayName;
        }

        updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return entry;
    }

    private bool TryFindEntry(string key, out PurrPlayerStatEntry entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(key) || entries == null)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            PurrPlayerStatEntry candidate = entries[i];
            if (candidate == null)
                continue;
            if (!string.Equals(candidate.key, key, StringComparison.Ordinal))
                continue;

            entry = candidate;
            return true;
        }

        return false;
    }
}

[Serializable]
public sealed class PurrPlayerRosterSnapshot
{
    public int version = 1;
    public long updatedAtUnixMs;
    public List<PurrPlayerRosterEntry> players = new List<PurrPlayerRosterEntry>();

    public PurrPlayerRosterSnapshot Clone()
    {
        PurrPlayerRosterSnapshot clone = new PurrPlayerRosterSnapshot
        {
            version = version,
            updatedAtUnixMs = updatedAtUnixMs
        };

        if (players == null)
            return clone;

        for (int i = 0; i < players.Count; i++)
        {
            PurrPlayerRosterEntry entry = players[i];
            if (entry != null)
                clone.players.Add(entry.Clone());
        }

        return clone;
    }
}

[Serializable]
public struct PurrPlayerProfileMessage : IPackedAuto
{
    public string profileJson;

    public static PurrPlayerProfileMessage FromProfile(PurrPlayerProfileData profile)
    {
        return new PurrPlayerProfileMessage
        {
            profileJson = Serialize(profile)
        };
    }

    public PurrPlayerProfileData ToProfile()
    {
        return Deserialize<PurrPlayerProfileData>(profileJson);
    }

    private static string Serialize<T>(T value)
    {
        return value != null ? JsonUtility.ToJson(value) : string.Empty;
    }

    private static T Deserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PurrPlayerProfileMessage: failed to deserialize {typeof(T).Name}. {ex.Message}");
            return null;
        }
    }
}

[Serializable]
public struct PurrPlayerRosterSnapshotMessage : IPackedAuto
{
    public string snapshotJson;

    public static PurrPlayerRosterSnapshotMessage FromSnapshot(PurrPlayerRosterSnapshot snapshot)
    {
        return new PurrPlayerRosterSnapshotMessage
        {
            snapshotJson = snapshot != null ? JsonUtility.ToJson(snapshot) : string.Empty
        };
    }

    public PurrPlayerRosterSnapshot ToSnapshot()
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;

        try
        {
            return JsonUtility.FromJson<PurrPlayerRosterSnapshot>(snapshotJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PurrPlayerRosterSnapshotMessage: failed to deserialize snapshot. {ex.Message}");
            return null;
        }
    }
}

[Serializable]
public struct PurrPlayerStatsMessage : IPackedAuto
{
    public string statsJson;

    public static PurrPlayerStatsMessage FromStats(PurrPlayerStatsData stats)
    {
        return new PurrPlayerStatsMessage
        {
            statsJson = stats != null ? JsonUtility.ToJson(stats) : string.Empty
        };
    }

    public PurrPlayerStatsData ToStats()
    {
        if (string.IsNullOrWhiteSpace(statsJson))
            return null;

        try
        {
            return JsonUtility.FromJson<PurrPlayerStatsData>(statsJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PurrPlayerStatsMessage: failed to deserialize stats. {ex.Message}");
            return null;
        }
    }
}

public static class PurrLocalPlayerProfile
{
    private const string PlayerNamePrefKey = "rrr.local_profile.player_name";
    private const string AccountIdPrefKey = "rrr.local_profile.account_id";

    public static string ResolvePreferredPlayerName(string preferredPrefix = null)
    {
        return BuildCurrentProfile(preferredPrefix).DisplayName;
    }

    public static PurrPlayerProfileData BuildCurrentProfile(string preferredPrefix = null)
    {
        BackendSessionResponse session = Backend.Client.Session;
        if (session != null)
        {
            string resolvedPlayerName = !string.IsNullOrWhiteSpace(session.player_name)
                ? session.player_name.Trim()
                : ResolveStoredPlayerName(preferredPrefix);
            PersistPlayerName(resolvedPlayerName);

            return new PurrPlayerProfileData
            {
                accountPlayerId = session.player_id,
                playerName = resolvedPlayerName,
                authProvider = "backend_guest",
                authState = "guest_session",
                sessionId = session.session_id,
                platform = Application.platform.ToString(),
                updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        return new PurrPlayerProfileData
        {
            accountPlayerId = ResolveStoredAccountId(),
            playerName = ResolveStoredPlayerName(preferredPrefix),
            authProvider = "local_placeholder",
            authState = "guest_placeholder",
            sessionId = string.Empty,
            platform = Application.platform.ToString(),
            updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static string ResolveStoredPlayerName(string preferredPrefix)
    {
        string stored = PlayerPrefs.GetString(PlayerNamePrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored.Trim();

        string prefix = string.IsNullOrWhiteSpace(preferredPrefix) ? "Guest" : preferredPrefix.Trim().Replace(" ", string.Empty);
        string generated = $"{prefix}_{UnityEngine.Random.Range(1000, 9999)}";
        PersistPlayerName(generated);
        return generated;
    }

    private static string ResolveStoredAccountId()
    {
        string stored = PlayerPrefs.GetString(AccountIdPrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored.Trim();

        string generated = "guest_" + Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(AccountIdPrefKey, generated);
        PlayerPrefs.Save();
        return generated;
    }

    private static void PersistPlayerName(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return;

        PlayerPrefs.SetString(PlayerNamePrefKey, playerName.Trim());
        PlayerPrefs.Save();
    }
}

[DefaultExecutionOrder(360)]
[DisallowMultipleComponent]
public sealed class PurrVehiclePlayerRoster : PurrMonoBehaviour
{
    [SerializeField, Min(0.1f)] private float serverBroadcastIntervalSeconds = 1.0f;
    [SerializeField, Min(0.1f)] private float entityRefreshIntervalSeconds = 0.5f;
    [SerializeField, Min(0.05f)] private float clientStatsSendIntervalSeconds = 0.15f;

    private readonly Dictionary<string, PurrPlayerProfileData> profilesByNetworkId =
        new Dictionary<string, PurrPlayerProfileData>(StringComparer.Ordinal);
    private readonly Dictionary<string, PurrPlayerStatsData> statsByNetworkId =
        new Dictionary<string, PurrPlayerStatsData>(StringComparer.Ordinal);

    private NetworkManager activeManager;
    private PlayersManager playersManager;
    private bool isServerAuthority;
    private bool isClientPublisher;
    private bool localProfilePublished;
    private float nextClientStatsPublishAt;
    private float nextServerBroadcastAt;
    private float nextEntityRefreshAt;
    private PurrPlayerRosterSnapshot currentSnapshot;

    public PurrPlayerRosterSnapshot CaptureSnapshot()
    {
        if (isServerAuthority)
            return BuildServerSnapshot().Clone();

        return currentSnapshot != null ? currentSnapshot.Clone() : new PurrPlayerRosterSnapshot();
    }

    public bool TryGetProfile(string networkPlayerId, out PurrPlayerProfileData profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(networkPlayerId))
            return false;

        if (profilesByNetworkId.TryGetValue(networkPlayerId, out PurrPlayerProfileData stored) && stored != null)
        {
            profile = stored.Clone();
            return true;
        }

        if (currentSnapshot == null || currentSnapshot.players == null)
            return false;

        for (int i = 0; i < currentSnapshot.players.Count; i++)
        {
            PurrPlayerRosterEntry entry = currentSnapshot.players[i];
            if (entry?.profile == null)
                continue;
            if (!string.Equals(entry.profile.networkPlayerId, networkPlayerId, StringComparison.Ordinal))
                continue;

            profile = entry.profile.Clone();
            return true;
        }

        return false;
    }

    public bool TryGetStats(string networkPlayerId, out PurrPlayerStatsData stats)
    {
        stats = null;
        if (string.IsNullOrWhiteSpace(networkPlayerId))
            return false;

        if (statsByNetworkId.TryGetValue(networkPlayerId, out PurrPlayerStatsData stored) && stored != null)
        {
            stats = stored.Clone();
            return true;
        }

        if (currentSnapshot == null || currentSnapshot.players == null)
            return false;

        for (int i = 0; i < currentSnapshot.players.Count; i++)
        {
            PurrPlayerRosterEntry entry = currentSnapshot.players[i];
            if (entry?.profile == null || entry.stats == null)
                continue;
            if (!string.Equals(entry.profile.networkPlayerId, networkPlayerId, StringComparison.Ordinal))
                continue;

            stats = entry.stats.Clone();
            return true;
        }

        return false;
    }

    private void Update()
    {
        ResolveReferences();

        if (isClientPublisher)
        {
            TryPublishLocalProfile();
            TryPublishLocalStats();
        }

        if (isServerAuthority && Time.unscaledTime >= nextServerBroadcastAt)
        {
            nextServerBroadcastAt = Time.unscaledTime + serverBroadcastIntervalSeconds;
            BroadcastRosterSnapshot();
        }

        if (Time.unscaledTime >= nextEntityRefreshAt)
        {
            nextEntityRefreshAt = Time.unscaledTime + entityRefreshIntervalSeconds;
            ApplyProfilesToEntities();
        }
    }

    public override void Subscribe(NetworkManager manager, bool asServer)
    {
        activeManager = manager;
        ResolveReferences();

        if (playersManager != null)
        {
            playersManager.Subscribe<PurrPlayerProfileMessage>(OnProfileMessage);
            playersManager.Subscribe<PurrPlayerStatsMessage>(OnStatsMessage);
            playersManager.Subscribe<PurrPlayerRosterSnapshotMessage>(OnRosterSnapshotMessage);
        }

        if (asServer)
        {
            isServerAuthority = true;
            manager.onPlayerLeft += OnPlayerLeft;
            BroadcastRosterSnapshot();
            return;
        }

        isClientPublisher = true;
        localProfilePublished = false;
    }

    public override void Unsubscribe(NetworkManager manager, bool asServer)
    {
        if (playersManager != null)
        {
            playersManager.Unsubscribe<PurrPlayerProfileMessage>(OnProfileMessage);
            playersManager.Unsubscribe<PurrPlayerStatsMessage>(OnStatsMessage);
            playersManager.Unsubscribe<PurrPlayerRosterSnapshotMessage>(OnRosterSnapshotMessage);
        }

        if (asServer)
        {
            manager.onPlayerLeft -= OnPlayerLeft;
            isServerAuthority = false;
        }
        else
        {
            isClientPublisher = false;
            localProfilePublished = false;
        }

        activeManager = null;
    }

    private void ResolveReferences()
    {
        if (activeManager == null)
            return;

        if (playersManager == null)
        {
            if (!activeManager.TryGetModule(out playersManager, true))
                activeManager.TryGetModule(out playersManager, false);
        }
    }

    private void TryPublishLocalProfile()
    {
        if (localProfilePublished || playersManager == null || !playersManager.localPlayerId.HasValue)
            return;

        PlayerCarSelectionPayload loadout = null;
        PlayerCarSelection.TryGetPayload(out loadout);
        string preferredPrefix = !string.IsNullOrWhiteSpace(loadout?.loadoutDisplayName)
            ? loadout.loadoutDisplayName
            : !string.IsNullOrWhiteSpace(loadout?.loadoutName)
                ? loadout.loadoutName
                : "Guest";

        PurrPlayerProfileData profile = PurrLocalPlayerProfile.BuildCurrentProfile(preferredPrefix);
        if (profile == null)
            return;

        playersManager.SendToServer(PurrPlayerProfileMessage.FromProfile(profile), Channel.ReliableOrdered);
        localProfilePublished = true;
    }

    private void TryPublishLocalStats()
    {
        if (playersManager == null || !playersManager.localPlayerId.HasValue)
            return;
        if (Time.unscaledTime < nextClientStatsPublishAt)
            return;

        nextClientStatsPublishAt = Time.unscaledTime + clientStatsSendIntervalSeconds;
        if (!TryBuildLocalVehicleStats(out PurrPlayerStatsData stats) || stats == null)
            return;

        playersManager.SendToServer(PurrPlayerStatsMessage.FromStats(stats), Channel.ReliableOrdered);
    }

    private void OnProfileMessage(PlayerID player, PurrPlayerProfileMessage message, bool asServer)
    {
        if (!asServer)
            return;

        PurrPlayerProfileData profile = message.ToProfile();
        if (profile == null)
            return;

        StampProfile(player, profile);
        profilesByNetworkId[player.ToString()] = profile;
        BroadcastRosterSnapshot();
    }

    private void OnStatsMessage(PlayerID player, PurrPlayerStatsMessage message, bool asServer)
    {
        if (!asServer)
            return;

        PurrPlayerStatsData stats = message.ToStats();
        if (stats == null)
            return;

        stats.updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        statsByNetworkId[player.ToString()] = stats;
        BroadcastRosterSnapshot();
    }

    private void OnRosterSnapshotMessage(PlayerID player, PurrPlayerRosterSnapshotMessage message, bool asServer)
    {
        if (asServer)
            return;

        PurrPlayerRosterSnapshot snapshot = message.ToSnapshot();
        if (snapshot == null)
            return;

        currentSnapshot = snapshot;
        RebuildProfileCache(snapshot);
        ApplyProfilesToEntities();
    }

    private void OnPlayerLeft(PlayerID player, bool asServer)
    {
        if (!asServer)
            return;

        profilesByNetworkId.Remove(player.ToString());
        statsByNetworkId.Remove(player.ToString());
        BroadcastRosterSnapshot();
    }

    private void BroadcastRosterSnapshot()
    {
        if (playersManager == null)
            return;

        PurrPlayerRosterSnapshot snapshot = BuildServerSnapshot();
        playersManager.SendToAll(PurrPlayerRosterSnapshotMessage.FromSnapshot(snapshot), Channel.ReliableOrdered);
        ApplyProfilesToEntities();
    }

    private PurrPlayerRosterSnapshot BuildServerSnapshot()
    {
        PurrPlayerRosterSnapshot snapshot = new PurrPlayerRosterSnapshot
        {
            updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        IReadOnlyList<PlayerID> players = playersManager != null ? playersManager.players : null;
        if (players != null)
        {
            List<PlayerID> sortedPlayers = new List<PlayerID>(players);
            sortedPlayers.Sort(ComparePlayers);

            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                PlayerID player = sortedPlayers[i];
                string networkPlayerId = player.ToString();
                PurrPlayerProfileData profile = profilesByNetworkId.TryGetValue(networkPlayerId, out PurrPlayerProfileData stored) && stored != null
                    ? stored.Clone()
                    : CreateFallbackProfile(player);
                StampProfile(player, profile);

                snapshot.players.Add(new PurrPlayerRosterEntry
                {
                    profile = profile,
                    stats = statsByNetworkId.TryGetValue(networkPlayerId, out PurrPlayerStatsData stats) && stats != null
                        ? stats.Clone()
                        : null,
                    connectionState = "connected",
                    authorityOrder = i
                });
            }
        }

        currentSnapshot = snapshot;
        RebuildProfileCache(snapshot);
        return snapshot;
    }

    private void ApplyProfilesToEntities()
    {
        if (currentSnapshot == null || currentSnapshot.players == null || currentSnapshot.players.Count == 0)
            return;

        NetworkVehicleEntity[] entities =
            FindObjectsByType<NetworkVehicleEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < entities.Length; i++)
        {
            NetworkVehicleEntity entity = entities[i];
            if (entity == null || string.IsNullOrWhiteSpace(entity.PlayerId))
                continue;

            if (!TryGetProfile(entity.PlayerId, out PurrPlayerProfileData profile) || profile == null)
                continue;

            entity.ApplyProfile(profile);
            if (TryGetStats(entity.PlayerId, out PurrPlayerStatsData stats) && stats != null)
                entity.ApplyStats(stats);
        }
    }

    private void RebuildProfileCache(PurrPlayerRosterSnapshot snapshot)
    {
        profilesByNetworkId.Clear();
        statsByNetworkId.Clear();
        if (snapshot == null || snapshot.players == null)
            return;

        for (int i = 0; i < snapshot.players.Count; i++)
        {
            PurrPlayerRosterEntry entry = snapshot.players[i];
            if (entry?.profile == null || string.IsNullOrWhiteSpace(entry.profile.networkPlayerId))
                continue;

            profilesByNetworkId[entry.profile.networkPlayerId] = entry.profile.Clone();
            if (entry.stats != null)
                statsByNetworkId[entry.profile.networkPlayerId] = entry.stats.Clone();
        }
    }

    private static bool TryBuildLocalVehicleStats(out PurrPlayerStatsData stats)
    {
        stats = null;
        NetworkVehicleEntity[] entities =
            FindObjectsByType<NetworkVehicleEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < entities.Length; i++)
        {
            NetworkVehicleEntity entity = entities[i];
            if (entity == null || !entity.IsLocalPlayer)
                continue;

            CarControllerBase controller = entity.GetComponent<CarControllerBase>();
            if (controller == null)
                controller = entity.GetComponentInParent<CarControllerBase>();
            CarDamageController damageController = entity.GetComponent<CarDamageController>();
            if (damageController == null)
                damageController = entity.GetComponentInParent<CarDamageController>();

            float damageFraction = damageController != null ? damageController.EstimateDamageFraction() : 0.0f;
            float healthNormalized = 1.0f - damageFraction;
            float currentHealth = healthNormalized * 100.0f;

            stats = new PurrPlayerStatsData();
            stats.SetNumber("current_health", "Health", currentHealth, 0.0f, 100.0f, hasRange: true);
            stats.SetNumber("max_health", "Max Health", 100.0f, 0.0f, 100.0f, hasRange: true);
            stats.SetNumber("damage_ratio", "Damage", damageFraction, 0.0f, 1.0f, hasRange: true);
            stats.SetInteger("damage_revision", "Damage Revision", damageController != null ? damageController.DamageRevision : 0);
            stats.SetNumber("speed_kph", "Speed", controller != null ? controller.SpeedKph : 0.0f, 0.0f, 320.0f, hasRange: true);
            stats.SetInteger("gear", "Gear", controller != null ? controller.CurrentGear : 0, -1, 8, hasRange: true);
            stats.SetNumber("nitro", "Nitro", controller != null ? controller.NitroAmount : 0.0f, 0.0f, 1.0f, hasRange: true);
            stats.SetBoolean("nitro_active", "Nitro Active", controller != null && controller.NitroActive);
            return true;
        }

        return false;
    }

    private static void StampProfile(PlayerID player, PurrPlayerProfileData profile)
    {
        if (profile == null)
            return;

        profile.networkPlayerId = player.ToString();
        profile.isBot = player.isBot;
        profile.platform = string.IsNullOrWhiteSpace(profile.platform) ? Application.platform.ToString() : profile.platform;
        profile.updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (string.IsNullOrWhiteSpace(profile.playerName))
            profile.playerName = player.isBot ? $"Bot {profile.networkPlayerId}" : profile.networkPlayerId;
        if (string.IsNullOrWhiteSpace(profile.authProvider))
            profile.authProvider = player.isBot ? "purrnet_bot" : "unknown";
        if (string.IsNullOrWhiteSpace(profile.authState))
            profile.authState = player.isBot ? "server_bot" : "connected";
    }

    private static PurrPlayerProfileData CreateFallbackProfile(PlayerID player)
    {
        return new PurrPlayerProfileData
        {
            networkPlayerId = player.ToString(),
            accountPlayerId = string.Empty,
            playerName = player.isBot ? $"Bot {player}" : player.ToString(),
            authProvider = player.isBot ? "purrnet_bot" : "unknown",
            authState = player.isBot ? "server_bot" : "connected",
            sessionId = string.Empty,
            platform = Application.platform.ToString(),
            isBot = player.isBot,
            updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static int ComparePlayers(PlayerID a, PlayerID b)
    {
        if (a.isBot != b.isBot)
            return a.isBot ? 1 : -1;

        return string.CompareOrdinal(a.ToString(), b.ToString());
    }
}

[DefaultExecutionOrder(1320)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkVehicleEntity))]
public sealed class PurrVehicleNameplate : MonoBehaviour
{
    [SerializeField] private NetworkVehicleEntity entity;
    [SerializeField] private bool showForLocalPlayer;
    [SerializeField, Min(0.5f)] private float verticalOffset = 2.2f;
    [SerializeField, Min(0.25f)] private float maxVisibleDistance = 80.0f;

    private Transform root;
    private TextMesh nameText;
    private TextMesh statText;
    private Transform healthBackground;
    private Transform healthFill;
    private Camera targetCamera;

    private void Awake()
    {
        if (entity == null)
            entity = GetComponent<NetworkVehicleEntity>();

        EnsureVisuals();
        RefreshVisualState();
    }

    private void OnEnable()
    {
        if (entity == null)
            entity = GetComponent<NetworkVehicleEntity>();

        if (entity != null)
        {
            entity.IdentityChanged -= HandleEntityChanged;
            entity.IdentityChanged += HandleEntityChanged;
            entity.StatsChanged -= HandleEntityChanged;
            entity.StatsChanged += HandleEntityChanged;
        }

        RefreshVisualState();
    }

    private void OnDisable()
    {
        if (entity == null)
            return;

        entity.IdentityChanged -= HandleEntityChanged;
        entity.StatsChanged -= HandleEntityChanged;
    }

    private void LateUpdate()
    {
        EnsureVisuals();
        targetCamera = ResolveCamera();
        if (root == null)
            return;

        bool visible = entity != null && (!entity.IsLocalPlayer || showForLocalPlayer) && targetCamera != null;
        if (visible)
        {
            float distance = Vector3.Distance(targetCamera.transform.position, transform.position);
            visible = distance <= maxVisibleDistance;
        }

        if (root.gameObject.activeSelf != visible)
            root.gameObject.SetActive(visible);
        if (!visible)
            return;

        Vector3 worldPosition = transform.position + Vector3.up * verticalOffset;
        root.position = worldPosition;
        Vector3 toCamera = worldPosition - targetCamera.transform.position;
        if (toCamera.sqrMagnitude > 0.0001f)
            root.rotation = Quaternion.LookRotation(toCamera.normalized, targetCamera.transform.up);
    }

    private void HandleEntityChanged(NetworkVehicleEntity changedEntity)
    {
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        EnsureVisuals();
        if (entity == null || nameText == null || statText == null || healthFill == null)
            return;

        nameText.text = entity.PlayerName;
        nameText.color = entity.IsBot
            ? new Color(1.0f, 0.83f, 0.28f, 1.0f)
            : entity.IsLocalPlayer
                ? new Color(0.32f, 0.95f, 1.0f, 1.0f)
                : Color.white;

        float healthNormalized = 1.0f;
        if (!entity.TryGetNormalizedHealth(out healthNormalized))
            healthNormalized = 1.0f;

        long gear = 0;
        entity.TryGetIntegerStat("gear", out gear);
        float speedKph = 0.0f;
        entity.TryGetNumberStat("speed_kph", out speedKph);
        statText.text = $"HP {Mathf.RoundToInt(healthNormalized * 100.0f)}%  |  {Mathf.RoundToInt(speedKph)} km/h  |  G{gear}";

        float clampedHealth = Mathf.Clamp01(healthNormalized);
        healthFill.localScale = new Vector3(Mathf.Max(0.001f, clampedHealth), 1.0f, 1.0f);
        healthFill.localPosition = new Vector3(-0.5f + clampedHealth * 0.5f, 0.0f, 0.0f);
        if (TryGetHealthColor(clampedHealth, out Color healthColor) && healthFill.TryGetComponent(out MeshRenderer fillRenderer))
            fillRenderer.material.color = healthColor;
    }

    private void EnsureVisuals()
    {
        if (root != null)
            return;

        GameObject rootObject = new GameObject("PurrVehicleNameplate");
        rootObject.transform.SetParent(transform, false);
        rootObject.transform.localPosition = Vector3.up * verticalOffset;
        rootObject.transform.localRotation = Quaternion.identity;
        root = rootObject.transform;

        nameText = CreateText("Name", new Vector3(0.0f, 0.22f, 0.0f), 38, FontStyle.Bold);
        statText = CreateText("Stats", new Vector3(0.0f, 0.0f, 0.0f), 24, FontStyle.Normal);
        healthBackground = CreateBar("HealthBackground", new Vector3(0.0f, -0.18f, 0.0f), new Vector3(1.05f, 0.10f, 0.02f), new Color(0.05f, 0.05f, 0.05f, 0.75f));
        healthFill = CreateBar("HealthFill", new Vector3(0.0f, -0.18f, -0.01f), new Vector3(1.0f, 0.07f, 0.01f), new Color(0.32f, 1.0f, 0.46f, 0.95f));
    }

    private TextMesh CreateText(string nodeName, Vector3 localPosition, int fontSize, FontStyle style)
    {
        GameObject textObject = new GameObject(nodeName);
        textObject.transform.SetParent(root, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;
        textObject.transform.localScale = Vector3.one * 0.03f;

        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.1f;
        textMesh.fontSize = fontSize;
        textMesh.fontStyle = style;
        textMesh.color = Color.white;
        return textMesh;
    }

    private Transform CreateBar(string nodeName, Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = nodeName;
        bar.transform.SetParent(root, false);
        bar.transform.localPosition = localPosition;
        bar.transform.localRotation = Quaternion.identity;
        bar.transform.localScale = localScale;

        Collider collider = bar.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        MeshRenderer renderer = bar.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material.color = color;
        }

        return bar.transform;
    }

    private static Camera ResolveCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        return FindFirstObjectByType<Camera>();
    }

    private static bool TryGetHealthColor(float healthNormalized, out Color color)
    {
        if (healthNormalized >= 0.6f)
        {
            color = new Color(0.32f, 1.0f, 0.46f, 0.95f);
            return true;
        }

        if (healthNormalized >= 0.3f)
        {
            color = new Color(1.0f, 0.82f, 0.26f, 0.95f);
            return true;
        }

        color = new Color(1.0f, 0.34f, 0.32f, 0.95f);
        return true;
    }
}
