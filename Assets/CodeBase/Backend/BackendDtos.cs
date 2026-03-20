using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BackendHealthResponse
{
    public string status;
    public int lobbies;
    public int matches;
    public int sessions;
}

[Serializable]
public sealed class BackendGuestSessionRequest
{
    public string player_name;
}

[Serializable]
public sealed class BackendSessionResponse
{
    public string session_id;
    public string player_id;
    public string player_name;
    public string session_token;
    public string created_at;
    public string expires_at;
}

[Serializable]
public sealed class BackendLobbiesResponse
{
    public List<BackendLobbySummary> items = new List<BackendLobbySummary>();
    public int total;
}

[Serializable]
public sealed class BackendLobbySummary
{
    public string lobby_id;
    public string name;
    public string status;
    public string map_id;
    public int max_players;
    public int current_players;
}

[Serializable]
public sealed class BackendLobbyDetails
{
    public string lobby_id;
    public string name;
    public string status;
    public string map_id;
    public int max_players;
    public int current_players;
    public string owner_player_id;
    public string created_at;
    public string match_id;
    public List<BackendLobbyPlayer> players = new List<BackendLobbyPlayer>();
}

[Serializable]
public sealed class BackendLobbyPlayer
{
    public string player_id;
    public string player_name;
    public string connection_state;
    public string joined_at;
    public BackendCarConfigPayload car_config;
}

[Serializable]
public sealed class BackendCreateLobbyRequest
{
    public string name;
    public string map_id;
    public int max_players;
    public BackendCarConfigPayload car_config;
}

[Serializable]
public sealed class BackendCreateLobbyResponse
{
    public string lobby_id;
    public string status;
}

[Serializable]
public sealed class BackendJoinLobbyRequest
{
    public BackendCarConfigPayload car_config;
}

[Serializable]
public sealed class BackendJoinLobbyResponse
{
    public string lobby_id;
    public string player_id;
    public bool joined;
}

[Serializable]
public sealed class BackendLeaveLobbyResponse
{
    public bool left;
}

[Serializable]
public sealed class BackendUpdateCarConfigRequest
{
    public BackendCarConfigPayload car_config;
}

[Serializable]
public sealed class BackendUpdateCarConfigResponse
{
    public bool updated;
}

[Serializable]
public sealed class BackendMatchInfo
{
    public string match_id;
    public string lobby_id;
    public string status;
    public string map_id;
    public int tick_rate;
    public List<BackendMatchPlayerInfo> players = new List<BackendMatchPlayerInfo>();
}

[Serializable]
public sealed class BackendErrorResponse
{
    public string code;
    public string message;
}

[Serializable]
public sealed class BackendSocketEnvelope
{
    public string type;
}

[Serializable]
public sealed class BackendSubscribeLobbyMessage
{
    public string type = "subscribe_lobby";
    public string lobby_id;
}

[Serializable]
public sealed class BackendUnsubscribeLobbyMessage
{
    public string type = "unsubscribe_lobby";
    public string lobby_id;
}

[Serializable]
public sealed class BackendMatchLoadedMessage
{
    public string type = "match_loaded";
    public string match_id;
}

[Serializable]
public sealed class BackendPingMessage
{
    public string type = "ping";
    public long time;
}

[Serializable]
public sealed class BackendPlayerStateMessage
{
    public string type = "player_state";
    public string match_id;
    public int seq;
    public long client_time;
    public BackendPlayerStateSnapshot state;
}

[Serializable]
public sealed class BackendPlayerStateSnapshot
{
    public BackendVector3 position;
    public BackendVector3 rotation;
    public BackendVector3 velocity;
    public List<BackendWheelPose> wheel_states = new List<BackendWheelPose>();
}

[Serializable]
public sealed class BackendWheelPose
{
    public BackendVector3 position;
    public BackendVector3 rotation;
}

[Serializable]
public sealed class BackendDamageStateMessage
{
    public string type = "damage_state";
    public string match_id;
    public string player_id;
    public int revision;
    public int width;
    public int height;
    public string map_b64;
    public BackendVector3 world_point;
    public BackendVector3 world_normal;

    public Vector3 WorldPointVector => world_point != null ? world_point.ToVector3() : Vector3.zero;
    public Vector3 WorldNormalVector => world_normal != null ? world_normal.ToVector3() : Vector3.up;
}

[Serializable]
public sealed class BackendWelcomeMessage
{
    public string type;
    public string player_id;
    public long server_time;
}

[Serializable]
public sealed class BackendLobbySnapshotMessage
{
    public string type;
    public BackendLobbyDetails lobby;
}

[Serializable]
public sealed class BackendLobbyPlayerJoinedMessage
{
    public string type;
    public string lobby_id;
    public BackendLobbyPlayer player;
}

[Serializable]
public sealed class BackendLobbyPlayerLeftMessage
{
    public string type;
    public string lobby_id;
    public string player_id;
}

[Serializable]
public sealed class BackendLobbyStartingMessage
{
    public string type;
    public string lobby_id;
    public int countdown_sec;
}

[Serializable]
public sealed class BackendLobbyClosedMessage
{
    public string type;
    public string lobby_id;
    public string reason;
}

[Serializable]
public sealed class BackendMatchCreatedMessage
{
    public string type;
    public string match_id;
    public string lobby_id;
    public string map_id;
    public List<BackendMatchPlayerInfo> players = new List<BackendMatchPlayerInfo>();
}

[Serializable]
public sealed class BackendMatchStartedMessage
{
    public string type;
    public string match_id;
    public int server_tick;
}

[Serializable]
public sealed class BackendMatchFinishedMessage
{
    public string type;
    public string match_id;
    public string reason;
}

[Serializable]
public sealed class BackendPlayerDisconnectedMessage
{
    public string type;
    public string match_id;
    public string player_id;
}

[Serializable]
public sealed class BackendMatchStateMessage
{
    public string type;
    public string match_id;
    public int server_tick;
    public long server_time;
    public List<BackendMatchPlayerState> players = new List<BackendMatchPlayerState>();
}

[Serializable]
public sealed class BackendMatchPlayerInfo
{
    public string player_id;
    public string player_name;
    public string connection_state;
    public string spawn_point_id;
    public BackendVector3 spawn_position;
    public BackendVector3 spawn_rotation;
    public BackendCarConfigPayload car_config;

    public Vector3 SpawnPositionVector => spawn_position != null ? spawn_position.ToVector3() : Vector3.zero;
    public Vector3 SpawnRotationVector => spawn_rotation != null ? spawn_rotation.ToVector3() : Vector3.zero;
    public bool HasSpawnAssignment => spawn_position != null || spawn_rotation != null || !string.IsNullOrWhiteSpace(spawn_point_id);
}

[Serializable]
public sealed class BackendMatchPlayerState
{
    public string player_id;
    public long client_time;
    public long server_received_time;
    public BackendVector3 position;
    public BackendVector3 rotation;
    public BackendVector3 velocity;
    public List<BackendWheelPose> wheel_states = new List<BackendWheelPose>();

    public Vector3 PositionVector => position != null ? position.ToVector3() : Vector3.zero;
    public Vector3 RotationVector => rotation != null ? rotation.ToVector3() : Vector3.zero;
    public Vector3 VelocityVector => velocity != null ? velocity.ToVector3() : Vector3.zero;
}

[Serializable]
public sealed class BackendVector3
{
    public float x;
    public float y;
    public float z;

    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }

    public static BackendVector3 FromVector3(Vector3 value)
    {
        return new BackendVector3
        {
            x = value.x,
            y = value.y,
            z = value.z
        };
    }
}

[Serializable]
public sealed class BackendRealtimeErrorMessage
{
    public string type;
    public string code;
    public string message;
}

[Serializable]
public sealed class BackendCarConfigPayload
{
    public int version = 1;
    public string loadout_name;
    public string loadout_display_name;
    public int body_set_option_index = -1;
    public int engine_index = -1;
    public int suspension_index = -1;
    public int paint_index = -1;
    public string handling_name;
    public string body_set_name;
    public string engine_name;
    public string suspension_name;
    public string paint_name;
    public bool has_paint;
    public SerializableColor paint;
    public List<BackendCarCustomizationPayload> customizations = new List<BackendCarCustomizationPayload>();

    public static BackendCarConfigPayload FromPlayerSelection(PlayerCarSelectionPayload payload)
    {
        if (payload == null)
            return null;

        BackendCarConfigPayload converted = new BackendCarConfigPayload
        {
            version = payload.version,
            loadout_name = payload.loadoutName,
            loadout_display_name = payload.loadoutDisplayName,
            body_set_option_index = payload.bodySetOptionIndex,
            engine_index = payload.engineIndex,
            suspension_index = payload.suspensionIndex,
            paint_index = payload.paintIndex,
            handling_name = payload.handlingName,
            body_set_name = payload.bodySetName,
            engine_name = payload.engineName,
            suspension_name = payload.suspensionName,
            paint_name = payload.paintName,
            has_paint = payload.hasPaint,
            paint = payload.paint
        };

        if (payload.customizations != null)
        {
            for (int i = 0; i < payload.customizations.Count; i++)
            {
                PlayerCarCustomizationPayload selection = payload.customizations[i];
                if (selection == null)
                    continue;

                converted.customizations.Add(new BackendCarCustomizationPayload
                {
                    selector_path = selection.selectorPath,
                    variant_name = selection.variantName
                });
            }
        }

        return converted;
    }

    public string ResolveDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(loadout_display_name))
            return loadout_display_name;
        if (!string.IsNullOrWhiteSpace(loadout_name))
            return loadout_name;
        return "car config pending";
    }

    public PlayerCarSelectionPayload ToPlayerSelectionPayload()
    {
        PlayerCarSelectionPayload converted = new PlayerCarSelectionPayload
        {
            version = version,
            loadoutName = loadout_name,
            loadoutDisplayName = loadout_display_name,
            bodySetOptionIndex = body_set_option_index,
            engineIndex = engine_index,
            suspensionIndex = suspension_index,
            paintIndex = paint_index,
            handlingName = handling_name,
            bodySetName = body_set_name,
            engineName = engine_name,
            suspensionName = suspension_name,
            paintName = paint_name,
            hasPaint = has_paint,
            paint = paint
        };

        if (customizations != null)
        {
            for (int i = 0; i < customizations.Count; i++)
            {
                BackendCarCustomizationPayload selection = customizations[i];
                if (selection == null || string.IsNullOrWhiteSpace(selection.selector_path))
                    continue;

                converted.customizations.Add(new PlayerCarCustomizationPayload
                {
                    selectorPath = selection.selector_path,
                    variantName = selection.variant_name
                });
            }
        }

        return converted;
    }
}

[Serializable]
public sealed class BackendCarCustomizationPayload
{
    public string selector_path;
    public string variant_name;
}
