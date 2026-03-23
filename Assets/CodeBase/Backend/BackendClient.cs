using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed class BackendClient
{
    private readonly BackendConfig config;
    private readonly BackendHttpClient http;
    private readonly BackendWebSocketClient socket;

    public BackendSessionResponse Session { get; private set; }
    public BackendLobbyDetails CurrentLobby { get; private set; }
    public BackendMatchInfo CurrentMatchInfo { get; private set; }
    public BackendMatchStateMessage LatestMatchState { get; private set; }

    public event Action<BackendSessionResponse> SessionChanged;
    public event Action<BackendLobbyDetails> LobbyChanged;
    public event Action<BackendMatchInfo> MatchInfoChanged;
    public event Action<BackendMatchStateMessage> MatchStateReceived;
    public event Action<BackendDamageStateMessage> DamageStateReceived;
    public event Action<BackendCollisionEventMessage> CollisionEventReceived;
    public event Action<BackendRealtimeErrorMessage> RealtimeErrorReceived;
    public event Action<string> RawRealtimeMessageReceived;

    public bool IsRealtimeConnected => socket.IsConnected;
    public string SessionToken => Session != null ? Session.session_token : null;

    public BackendClient(BackendConfig config, Action<Action> dispatchToMainThread)
    {
        this.config = config;
        http = new BackendHttpClient(config);
        socket = new BackendWebSocketClient(config, dispatchToMainThread);
        socket.RawMessageReceived += HandleRealtimeMessage;
        socket.Error += HandleSocketError;
    }

    public Task<BackendHealthResponse> GetHealthAsync()
    {
        return http.GetAsync<BackendHealthResponse>("health");
    }

    public async Task<BackendSessionResponse> CreateGuestSessionAsync(string playerName)
    {
        BackendGuestSessionRequest request = new BackendGuestSessionRequest
        {
            player_name = string.IsNullOrWhiteSpace(playerName)
                ? "Guest_" + UnityEngine.Random.Range(1000, 9999)
                : playerName.Trim()
        };

        Session = await http.PostAsync<BackendGuestSessionRequest, BackendSessionResponse>("sessions/guest", request);
        SessionChanged?.Invoke(Session);
        return Session;
    }

    public Task<BackendLobbiesResponse> GetLobbiesAsync(string status = null, string mapId = null, int page = 1, int pageSize = 50)
    {
        List<KeyValuePair<string, string>> query = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("page", Mathf.Max(1, page).ToString()),
            new KeyValuePair<string, string>("page_size", Mathf.Clamp(pageSize, 1, 100).ToString())
        };

        if (!string.IsNullOrWhiteSpace(status))
            query.Add(new KeyValuePair<string, string>("status", status));
        if (!string.IsNullOrWhiteSpace(mapId))
            query.Add(new KeyValuePair<string, string>("map_id", mapId));

        string path = http.BuildPathWithQuery("lobbies", query);
        return http.GetAsync<BackendLobbiesResponse>(path, SessionToken);
    }

    public async Task<BackendCreateLobbyResponse> CreateLobbyAsync(string name, string mapId, int maxPlayers, PlayerCarSelectionPayload carConfig = null)
    {
        EnsureSession();
        BackendCreateLobbyRequest request = new BackendCreateLobbyRequest
        {
            name = name,
            map_id = string.IsNullOrWhiteSpace(mapId) ? "city_default" : mapId,
            max_players = Mathf.Clamp(maxPlayers, 2, 8),
            car_config = BackendCarConfigPayload.FromPlayerSelection(carConfig ?? ResolveCurrentCarConfig())
        };

        BackendCreateLobbyResponse response = await http.PostAsync<BackendCreateLobbyRequest, BackendCreateLobbyResponse>("lobbies", request, SessionToken);
        if (!string.IsNullOrWhiteSpace(response.lobby_id))
            CurrentLobby = await GetLobbyAsync(response.lobby_id);
        return response;
    }

    public async Task<BackendLobbyDetails> GetLobbyAsync(string lobbyId)
    {
        BackendLobbyDetails lobby = await http.GetAsync<BackendLobbyDetails>(string.Format("lobbies/{0}", lobbyId), SessionToken);
        CurrentLobby = lobby;
        UpdateCurrentMatchFromLobby(CurrentLobby);
        LobbyChanged?.Invoke(CurrentLobby);
        return lobby;
    }

    public async Task<BackendJoinLobbyResponse> JoinLobbyAsync(string lobbyId, PlayerCarSelectionPayload carConfig = null)
    {
        EnsureSession();
        BackendJoinLobbyRequest request = new BackendJoinLobbyRequest
        {
            car_config = BackendCarConfigPayload.FromPlayerSelection(carConfig ?? ResolveCurrentCarConfig())
        };

        BackendJoinLobbyResponse response = await http.PostAsync<BackendJoinLobbyRequest, BackendJoinLobbyResponse>(
            string.Format("lobbies/{0}/join", lobbyId),
            request,
            SessionToken);

        if (response.joined)
            await GetLobbyAsync(lobbyId);

        return response;
    }

    public async Task<BackendStartSoloResponse> StartSoloAsync(string lobbyId)
    {
        EnsureSession();
        BackendStartSoloResponse response = await http.PostAsync<object, BackendStartSoloResponse>(
            string.Format("lobbies/{0}/start-solo", lobbyId),
            null,
            SessionToken);

        if (!string.IsNullOrWhiteSpace(lobbyId))
            await GetLobbyAsync(lobbyId);

        return response;
    }

    public async Task<BackendLeaveLobbyResponse> LeaveLobbyAsync(string lobbyId)
    {
        EnsureSession();
        BackendLeaveLobbyResponse response = await http.PostAsync<object, BackendLeaveLobbyResponse>(
            string.Format("lobbies/{0}/leave", lobbyId),
            null,
            SessionToken);

        if (response.left)
        {
            CurrentLobby = null;
            LobbyChanged?.Invoke(null);
        }

        return response;
    }

    public Task<BackendUpdateCarConfigResponse> UpdateCarConfigAsync(string lobbyId, PlayerCarSelectionPayload carConfig = null)
    {
        EnsureSession();
        BackendUpdateCarConfigRequest request = new BackendUpdateCarConfigRequest
        {
            car_config = BackendCarConfigPayload.FromPlayerSelection(carConfig ?? ResolveCurrentCarConfig())
        };

        return http.PutAsync<BackendUpdateCarConfigRequest, BackendUpdateCarConfigResponse>(
            string.Format("lobbies/{0}/car-config", lobbyId),
            request,
            SessionToken);
    }

    public async Task<BackendMatchInfo> GetMatchAsync(string matchId)
    {
        EnsureSession();
        CurrentMatchInfo = await http.GetAsync<BackendMatchInfo>(string.Format("matches/{0}", matchId), SessionToken);
        MatchInfoChanged?.Invoke(CurrentMatchInfo);
        return CurrentMatchInfo;
    }

    public async Task ConnectRealtimeAsync()
    {
        EnsureSession();
        if (socket.IsConnected)
            return;

        await socket.ConnectAsync(SessionToken);
    }

    public Task DisconnectRealtimeAsync()
    {
        return socket.DisconnectAsync();
    }

    public Task SubscribeLobbyAsync(string lobbyId)
    {
        return socket.SendAsync(new BackendSubscribeLobbyMessage
        {
            lobby_id = lobbyId
        });
    }

    public Task UnsubscribeLobbyAsync(string lobbyId)
    {
        return socket.SendAsync(new BackendUnsubscribeLobbyMessage
        {
            lobby_id = lobbyId
        });
    }

    public Task SendMatchLoadedAsync(string matchId)
    {
        return socket.SendAsync(new BackendMatchLoadedMessage
        {
            match_id = matchId
        });
    }

    public Task SendPingAsync()
    {
        return socket.SendAsync(new BackendPingMessage
        {
            time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    public Task SendPlayerInputAsync(string matchId, int sequence, BackendCarControlInputPayload input, BackendPlayerStateSnapshot state)
    {
        BackendPlayerInputMessage message = new BackendPlayerInputMessage
        {
            match_id = matchId,
            seq = sequence,
            client_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            input = input,
            state = state
        };

        return socket.SendAsync(message);
    }

    public Task SendPlayerStateAsync(string matchId, int sequence, BackendPlayerStateSnapshot state)
    {
        return SendPlayerInputAsync(matchId, sequence, null, state);
    }

    public Task SendDamageStateAsync(BackendDamageStateMessage message)
    {
        return socket.SendAsync(message);
    }

    public Task SendCollisionEventAsync(BackendCollisionEventMessage message)
    {
        return socket.SendAsync(message);
    }

    private void HandleRealtimeMessage(string json)
    {
        RawRealtimeMessageReceived?.Invoke(json);

        BackendSocketEnvelope envelope = JsonUtility.FromJson<BackendSocketEnvelope>(json);
        if (envelope == null || string.IsNullOrWhiteSpace(envelope.type))
            return;

        switch (envelope.type)
        {
            case "lobby_snapshot":
                BackendLobbySnapshotMessage snapshot = JsonUtility.FromJson<BackendLobbySnapshotMessage>(json);
                CurrentLobby = snapshot.lobby;
                UpdateCurrentMatchFromLobby(CurrentLobby);
                LobbyChanged?.Invoke(CurrentLobby);
                break;
            case "match_created":
                BackendMatchCreatedMessage created = JsonUtility.FromJson<BackendMatchCreatedMessage>(json);
                CurrentMatchInfo = new BackendMatchInfo
                {
                    match_id = created.match_id,
                    lobby_id = created.lobby_id,
                    status = "starting",
                    map_id = created.map_id,
                    room_id = created.room_id,
                    room_status = created.room_status,
                    room_http_url = created.room_http_url,
                    room_ws_url = created.room_ws_url,
                    room_token = created.room_token,
                    players = created.players != null ? new List<BackendMatchPlayerInfo>(created.players) : new List<BackendMatchPlayerInfo>()
                };
                if (CurrentLobby != null && string.Equals(CurrentLobby.lobby_id, created.lobby_id, StringComparison.OrdinalIgnoreCase))
                    CurrentLobby.match_id = created.match_id;
                MatchInfoChanged?.Invoke(CurrentMatchInfo);
                break;
            case "lobby_closed":
                BackendLobbyClosedMessage lobbyClosed = JsonUtility.FromJson<BackendLobbyClosedMessage>(json);
                if (CurrentLobby != null &&
                    lobbyClosed != null &&
                    string.Equals(CurrentLobby.lobby_id, lobbyClosed.lobby_id, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentLobby = null;
                    CurrentMatchInfo = null;
                    LatestMatchState = null;
                    LobbyChanged?.Invoke(null);
                    MatchInfoChanged?.Invoke(null);
                }
                break;
            case "match_started":
                if (CurrentMatchInfo != null)
                {
                    CurrentMatchInfo.status = "running";
                    MatchInfoChanged?.Invoke(CurrentMatchInfo);
                }
                break;
            case "match_state":
                LatestMatchState = JsonUtility.FromJson<BackendMatchStateMessage>(json);
                MergeMatchPlayersFromState(LatestMatchState);
                MatchStateReceived?.Invoke(LatestMatchState);
                break;
            case "damage_state":
                BackendDamageStateMessage damage = JsonUtility.FromJson<BackendDamageStateMessage>(json);
                DamageStateReceived?.Invoke(damage);
                break;
            case "collision_event":
                BackendCollisionEventMessage collision = JsonUtility.FromJson<BackendCollisionEventMessage>(json);
                CollisionEventReceived?.Invoke(collision);
                break;
            case "match_finished":
                if (CurrentMatchInfo != null)
                {
                    CurrentMatchInfo.status = "finished";
                    MatchInfoChanged?.Invoke(CurrentMatchInfo);
                }
                break;
            case "player_disconnected":
                BackendPlayerDisconnectedMessage disconnected = JsonUtility.FromJson<BackendPlayerDisconnectedMessage>(json);
                MarkPlayerDisconnected(disconnected);
                break;
            case "error":
                BackendRealtimeErrorMessage error = JsonUtility.FromJson<BackendRealtimeErrorMessage>(json);
                RealtimeErrorReceived?.Invoke(error);
                break;
        }
    }

    private void HandleSocketError(Exception exception)
    {
        RealtimeErrorReceived?.Invoke(new BackendRealtimeErrorMessage
        {
            type = "error",
            code = "CLIENT_SOCKET_ERROR",
            message = exception != null ? exception.Message : "Unknown socket error"
        });
    }

    private void EnsureSession()
    {
        if (Session == null || string.IsNullOrWhiteSpace(Session.session_token))
            throw new InvalidOperationException("Backend session is missing. Call CreateGuestSessionAsync first.");
    }

    private static PlayerCarSelectionPayload ResolveCurrentCarConfig()
    {
        PlayerCarSelection.TryGetPayload(out PlayerCarSelectionPayload payload);
        return payload;
    }

    private void UpdateCurrentMatchFromLobby(BackendLobbyDetails lobby)
    {
        if (lobby == null || string.IsNullOrWhiteSpace(lobby.match_id))
            return;

        if (CurrentMatchInfo == null || !string.Equals(CurrentMatchInfo.match_id, lobby.match_id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentMatchInfo = new BackendMatchInfo
            {
                match_id = lobby.match_id,
                lobby_id = lobby.lobby_id,
                status = ResolveMatchStatusFromLobby(lobby.status),
                map_id = lobby.map_id,
                players = ConvertLobbyPlayers(lobby.players)
            };
            MatchInfoChanged?.Invoke(CurrentMatchInfo);
            return;
        }

        bool changed = false;
        if (!string.Equals(CurrentMatchInfo.lobby_id, lobby.lobby_id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentMatchInfo.lobby_id = lobby.lobby_id;
            changed = true;
        }

        if (!string.Equals(CurrentMatchInfo.map_id, lobby.map_id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentMatchInfo.map_id = lobby.map_id;
            changed = true;
        }

        string resolvedStatus = ResolveMatchStatusFromLobby(lobby.status);
        if (!string.Equals(CurrentMatchInfo.status, resolvedStatus, StringComparison.OrdinalIgnoreCase))
        {
            CurrentMatchInfo.status = resolvedStatus;
            changed = true;
        }

        if ((CurrentMatchInfo.players == null || CurrentMatchInfo.players.Count == 0) && lobby.players != null && lobby.players.Count > 0)
        {
            CurrentMatchInfo.players = ConvertLobbyPlayers(lobby.players);
            changed = true;
        }

        if (changed)
            MatchInfoChanged?.Invoke(CurrentMatchInfo);
    }

    private void MergeMatchPlayersFromState(BackendMatchStateMessage state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.match_id))
            return;

        if (CurrentMatchInfo == null || !string.Equals(CurrentMatchInfo.match_id, state.match_id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentMatchInfo = new BackendMatchInfo
            {
                match_id = state.match_id,
                lobby_id = CurrentLobby != null ? CurrentLobby.lobby_id : null,
                status = "running",
                map_id = CurrentLobby != null ? CurrentLobby.map_id : null,
                players = new List<BackendMatchPlayerInfo>()
            };
            MatchInfoChanged?.Invoke(CurrentMatchInfo);
        }

        if (CurrentMatchInfo.players == null)
            CurrentMatchInfo.players = new List<BackendMatchPlayerInfo>();

        if (state.players == null)
            return;

        for (int i = 0; i < state.players.Count; i++)
        {
            BackendMatchPlayerState playerState = state.players[i];
            if (playerState == null || string.IsNullOrWhiteSpace(playerState.player_id))
                continue;

            FindOrCreateMatchPlayer(playerState.player_id);
        }
    }

    private void MarkPlayerDisconnected(BackendPlayerDisconnectedMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.player_id))
            return;

        if (CurrentMatchInfo != null && CurrentMatchInfo.players != null)
        {
            BackendMatchPlayerInfo matchPlayer = FindMatchPlayer(CurrentMatchInfo.players, message.player_id);
            if (matchPlayer != null)
                matchPlayer.connection_state = "disconnected";
        }

        if (CurrentLobby != null && CurrentLobby.players != null)
        {
            for (int i = 0; i < CurrentLobby.players.Count; i++)
            {
                BackendLobbyPlayer player = CurrentLobby.players[i];
                if (player != null && string.Equals(player.player_id, message.player_id, StringComparison.OrdinalIgnoreCase))
                {
                    player.connection_state = "disconnected";
                    break;
                }
            }
        }
    }

    private BackendMatchPlayerInfo FindOrCreateMatchPlayer(string playerId)
    {
        BackendMatchPlayerInfo existing = FindMatchPlayer(CurrentMatchInfo.players, playerId);
        if (existing != null)
            return existing;

        existing = new BackendMatchPlayerInfo { player_id = playerId };
        CurrentMatchInfo.players.Add(existing);
        return existing;
    }

    private static BackendMatchPlayerInfo FindMatchPlayer(List<BackendMatchPlayerInfo> players, string playerId)
    {
        if (players == null || string.IsNullOrWhiteSpace(playerId))
            return null;

        for (int i = 0; i < players.Count; i++)
        {
            BackendMatchPlayerInfo player = players[i];
            if (player != null && string.Equals(player.player_id, playerId, StringComparison.OrdinalIgnoreCase))
                return player;
        }

        return null;
    }

    private static List<BackendMatchPlayerInfo> ConvertLobbyPlayers(List<BackendLobbyPlayer> players)
    {
        List<BackendMatchPlayerInfo> converted = new List<BackendMatchPlayerInfo>();
        if (players == null)
            return converted;

        for (int i = 0; i < players.Count; i++)
        {
            BackendLobbyPlayer player = players[i];
            if (player == null || string.IsNullOrWhiteSpace(player.player_id))
                continue;

            converted.Add(new BackendMatchPlayerInfo
            {
                player_id = player.player_id,
                player_name = player.player_name,
                connection_state = player.connection_state,
                is_server_controlled = player.is_server_controlled,
                car_config = player.car_config
            });
        }

        return converted;
    }

    private static string ResolveMatchStatusFromLobby(string lobbyStatus)
    {
        if (string.Equals(lobbyStatus, "in_game", StringComparison.OrdinalIgnoreCase))
            return "running";
        if (string.Equals(lobbyStatus, "starting", StringComparison.OrdinalIgnoreCase))
            return "starting";
        return string.IsNullOrWhiteSpace(lobbyStatus) ? "starting" : lobbyStatus;
    }
}
