# Spec: Realtime Admin / Observer Panel For Multiplayer

## Goal

Нужно сделать отдельную admin/observer панель для backend `Russian Road Rage`, чтобы в realtime видеть:

1. список активных lobby
2. список активных match
3. состав игроков
4. connection state игроков
5. `car_config` игроков
6. текущие координаты, rotation и velocity игроков в матче

Base URL backend:

```text
https://rrr-demo.tonforspeed.space
```

## Main Use Case

Панель нужна для ручного контроля multiplayer во время разработки и тестов.

Админ должен иметь возможность:

1. открыть страницу
2. увидеть waiting lobby
3. увидеть lobby, которые перешли в `starting` или `in_game`
4. открыть конкретный lobby / match
5. наблюдать в realtime, где находятся игроки

## Required Scope

В MVP панели должны быть:

1. экран списка lobby
2. экран списка match
3. экран деталей одного match
4. realtime обновление через WebSocket или server-side subscription
5. таблица игроков с координатами
6. мини-карта или 2D top-down projection позиций игроков

## Required Data

Для каждого lobby показать:

1. `lobby_id`
2. `name`
3. `status`
4. `map_id`
5. `max_players`
6. `current_players`
7. `owner_player_id`

Для каждого игрока в lobby показать:

1. `player_id`
2. `player_name`
3. `connection_state`
4. `joined_at`
5. `car_config.loadout_display_name`
6. `car_config.paint_name`
7. `car_config.customizations`

Для каждого игрока в match показать:

1. `player_id`
2. `position`
3. `rotation`
4. `velocity`
5. timestamp последнего snapshot

## Realtime Requirements

Панель должна получать обновления в realtime.

Подходящий вариант:

1. либо отдельный admin websocket endpoint
2. либо reuse существующего websocket с правами наблюдателя
3. либо серверный fan-out stream для lobby/match state

Панель не должна полагаться только на polling.

Polling можно оставить только как fallback.

## Required Screens

## 1. Lobby List

Нужно показать:

1. все `waiting`
2. все `starting`
3. все `in_game`

Сортировка:

1. `waiting` сверху
2. `starting` потом
3. `in_game` потом

## 2. Match List

Нужно показать:

1. `match_id`
2. `lobby_id`
3. `status`
4. `map_id`
5. число игроков
6. `server_tick`

## 3. Match Details

Нужно показать:

1. заголовок с `match_id`
2. статус матча
3. таблицу игроков
4. блок raw JSON snapshot
5. 2D top-down схему позиций игроков

## 4. Player Table

Колонки:

1. `player_id`
2. `player_name`
3. `x`
4. `y`
5. `z`
6. `rot_y`
7. `speed`
8. `connection_state`

## 5. Map View

Минимально:

1. 2D canvas
2. точки игроков
3. подписи с `player_name`
4. обновление при каждом snapshot

Не обязательно:

1. полноценная карта трассы
2. 3D viewer

Но если можно быстро сделать, это плюс.

## Backend Changes Allowed

Если текущего backend контракта недостаточно, разрешается добавить:

1. `GET /api/v1/admin/lobbies`
2. `GET /api/v1/admin/matches`
3. `GET /api/v1/admin/matches/{match_id}`
4. `GET /api/v1/admin/lobbies/{lobby_id}`
5. `WS /api/v1/admin/ws`

## Suggested Admin REST Endpoints

### `GET /api/v1/admin/lobbies`

Возвращает список lobby:

```json
{
  "items": []
}
```

### `GET /api/v1/admin/matches`

Возвращает список match:

```json
{
  "items": []
}
```

### `GET /api/v1/admin/matches/{match_id}`

Возвращает:

```json
{
  "match_id": "match_01",
  "status": "running",
  "players": []
}
```

## Suggested Admin WebSocket Messages

Server -> Admin client:

1. `admin_lobbies_snapshot`
2. `admin_lobby_updated`
3. `admin_matches_snapshot`
4. `admin_match_updated`
5. `admin_match_state`

### Example

```json
{
  "type": "admin_match_state",
  "match_id": "match_01",
  "server_tick": 812,
  "players": [
    {
      "player_id": "player_01",
      "player_name": "Guest_1284",
      "position": { "x": 12.4, "y": 0.8, "z": -34.1 },
      "rotation": { "x": 0.0, "y": 182.3, "z": 0.0 },
      "velocity": { "x": 0.0, "y": 0.0, "z": 19.4 },
      "connection_state": "in_game"
    }
  ]
}
```

## UI Requirements

Панель должна быть:

1. максимально понятной
2. ориентированной на debug / QA
3. без тяжелого лишнего дизайна
4. с автопереподключением websocket
5. с индикацией `connected / disconnected`

## Suggested Stack

Если делать быстро:

1. `Next.js` или `React + Vite`
2. `TypeScript`
3. `TanStack Query` для REST
4. native `WebSocket`
5. `Canvas` или `SVG` для карты

Если хочется совсем быстро:

1. простой `React`
2. одна страница
3. таблицы + SVG map

## Acceptance Criteria

Работа считается выполненной, если:

1. админ видит список lobby
2. админ видит список match
3. админ может открыть match details
4. координаты игроков обновляются в realtime
5. в таблице видны `player_id`, `player_name`, `position`, `velocity`
6. при движении игроков это меняется на экране без ручного refresh

## What To Return

После выполнения вернуть:

1. URL панели
2. список новых backend endpoint'ов
3. список websocket admin events
4. где лежит frontend код
5. где лежат backend правки
6. краткий smoke test результата
