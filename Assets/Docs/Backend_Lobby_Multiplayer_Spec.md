# Backend Spec: Lobby And Server-Authoritative Multiplayer

## 1. Goal

Нужно реализовать backend для игры `Russian Road Rage` со следующими возможностями:

1. Игрок может получить игровую сессию.
2. Игрок может посмотреть список доступных лобби.
3. Игрок может создать лобби или подключиться к существующему.
4. Когда в лобби набирается нужное число игроков, матч стартует автоматически.
5. После старта матча позиции игроков синхронизируются через сервер.
6. Сервер является авторитетным источником истины для состояния матча.

Документ ниже является ТЗ для реализации MVP backend.

## 2. Scope MVP

В MVP должны быть реализованы:

1. Гостевая авторизация без пароля.
2. Создание, просмотр, подключение и выход из лобби.
3. Автоматический старт матча при заполнении лобби.
4. WebSocket-канал для событий лобби и игрового матча.
5. Серверная синхронизация позиций игроков.
6. Передача конфигурации машины игрока в матч.

Не входят в MVP:

1. Рейтинги.
2. Друзья и инвайты.
3. Persistent inventory/garage progression.
4. Match replay.
5. Античит уровня production.
6. Voice chat.

## 3. Architecture

Рекомендуемая архитектура:

1. `REST API` для сессий, списка лобби, создания/подключения.
2. `WebSocket API` для realtime-событий лобби и матча.
3. `Authoritative Game Session` на сервере для каждого активного матча.
4. `In-memory state` для MVP.
5. Опционально Redis/PostgreSQL как следующий шаг, но в MVP можно без них.

Рекомендуемый lifecycle:

1. Клиент получает guest session.
2. Клиент запрашивает список лобби.
3. Клиент создает лобби или входит в существующее.
4. Клиент открывает WebSocket.
5. Сервер рассылает обновления лобби.
6. Когда число игроков равно `max_players`, сервер переводит лобби в `starting`.
7. Сервер создает `match`.
8. Клиенты переключаются в игровую сцену и начинают слать input.
9. Сервер тикает матч с фиксированным шагом и рассылает authoritative state.

## 4. Transport Rules

### 4.1 REST

Использовать JSON over HTTP.

Base URL:

```text
/api/v1
```

### 4.2 WebSocket

Использовать один realtime endpoint:

```text
/api/v1/ws?session_token=...
```

Через этот сокет приходят:

1. События лобби.
2. События старта матча.
3. Игровые state snapshots.
4. Ошибки и системные сообщения.

## 5. Core Entities

## 5.1 Session

```json
{
  "session_id": "sess_01",
  "player_id": "player_01",
  "player_name": "Guest_1284",
  "session_token": "opaque_token",
  "created_at": "2026-03-19T18:00:00Z",
  "expires_at": "2026-03-20T18:00:00Z"
}
```

## 5.2 Lobby

```json
{
  "lobby_id": "lobby_01",
  "name": "Downtown Drift",
  "status": "waiting",
  "map_id": "city_default",
  "max_players": 4,
  "current_players": 2,
  "owner_player_id": "player_01",
  "players": [],
  "created_at": "2026-03-19T18:00:00Z"
}
```

`status`:

1. `waiting`
2. `starting`
3. `in_game`
4. `closed`

## 5.3 Lobby Player

```json
{
  "player_id": "player_01",
  "player_name": "Guest_1284",
  "connection_state": "connected",
  "joined_at": "2026-03-19T18:01:00Z",
  "car_config": {
    "loadout_name": "Cooper_Loadout",
    "paint_name": "Racing Red"
  }
}
```

`connection_state`:

1. `connected`
2. `disconnected`
3. `loading`
4. `in_game`

## 5.4 Car Configuration Payload

Backend должен принимать и хранить JSON-конфиг машины, который уже собирается клиентом.

Минимальный контракт:

```json
{
  "version": 1,
  "loadout_name": "Cooper_Loadout",
  "loadout_display_name": "Mini Cooper",
  "body_set_option_index": 0,
  "engine_index": 0,
  "suspension_index": 0,
  "paint_index": 2,
  "body_set_name": "",
  "engine_name": "Cooper_Engine",
  "suspension_name": "Cooper_Suspension",
  "paint_name": "British Green",
  "has_paint": true,
  "paint": {
    "r": 0.1,
    "g": 0.25,
    "b": 0.14,
    "a": 1.0
  },
  "customizations": [
    {
      "selector_path": "Bumper/Front",
      "variant_name": "SetD"
    },
    {
      "selector_path": "Skirts",
      "variant_name": "SetE"
    }
  ]
}
```

Важно:

1. Backend не обязан понимать визуальный смысл всех полей.
2. Backend обязан валидировать размер payload и базовую структуру.
3. Backend обязан хранить payload и передавать его другим игрокам/матчу.

## 5.5 Match

```json
{
  "match_id": "match_01",
  "lobby_id": "lobby_01",
  "status": "running",
  "map_id": "city_default",
  "tick_rate": 20,
  "players": []
}
```

`status`:

1. `starting`
2. `running`
3. `finished`
4. `aborted`

## 6. Authoritative Sync Rules

Синхронизация позиций должна идти через сервер.

### 6.1 Required Model

Сервер authoritative.

Это значит:

1. Клиент не отправляет итоговую позицию как истину.
2. Клиент отправляет input state.
3. Сервер обновляет состояние игроков на своем тике.
4. Сервер рассылает authoritative transforms всем клиентам.

### 6.2 Input Packet

Клиент отправляет input с частотой `20 Hz`.

```json
{
  "type": "player_input",
  "match_id": "match_01",
  "seq": 154,
  "client_time": 1710871200123,
  "input": {
    "throttle": 1.0,
    "brake": 0.0,
    "steer": -0.42,
    "handbrake": false,
    "nitro": true
  }
}
```

### 6.3 Server State Packet

Сервер рассылает snapshot с частотой `10-20 Hz`.

```json
{
  "type": "match_state",
  "match_id": "match_01",
  "server_tick": 812,
  "server_time": 1710871200450,
  "players": [
    {
      "player_id": "player_01",
      "position": { "x": 12.4, "y": 0.8, "z": -34.1 },
      "rotation": { "x": 0.0, "y": 182.3, "z": 0.0 },
      "velocity": { "x": 0.0, "y": 0.0, "z": 19.4 }
    }
  ]
}
```

### 6.4 Simulation

Для MVP допустимы два варианта:

1. Предпочтительный: сервер сам считает простую физику/кинематику машины.
2. Временный fallback: сервер принимает transform от выделенного host-client и реплицирует остальным.

Но целевой вариант для реализации:

1. Клиенты шлют `input`.
2. Сервер считает позицию.
3. Сервер отправляет authoritative state.

## 7. Lobby Lifecycle

### 7.1 Creation

Игрок создает лобби:

1. Выбирает `name`.
2. Выбирает `map_id`.
3. Выбирает `max_players`.
4. Передает свою `car_config`.

### 7.2 Joining

Игрок может подключиться в `waiting` lobby, если:

1. Есть свободные слоты.
2. Лобби не в `starting`.
3. Лобби не в `in_game`.

### 7.3 Auto Start

Матч стартует автоматически, когда:

1. `current_players == max_players`
2. Все игроки имеют активное websocket-подключение.
3. Все игроки передали `car_config`.

Рекомендуемый алгоритм:

1. Сервер переводит lobby в `starting`.
2. Сервер рассылает `lobby_starting` с countdown `3-5 сек`.
3. Сервер создает `match_id`.
4. Сервер рассылает `match_created`.
5. Клиенты грузят сцену `Game`.
6. Клиенты отправляют `match_loaded`.
7. Когда все игроки загрузились или истек timeout, матч переводится в `running`.

## 8. REST API

## 8.1 Create Guest Session

`POST /api/v1/sessions/guest`

Request:

```json
{
  "player_name": "Guest_1284"
}
```

Response `201 Created`:

```json
{
  "session_id": "sess_01",
  "player_id": "player_01",
  "player_name": "Guest_1284",
  "session_token": "opaque_token",
  "created_at": "2026-03-19T18:00:00Z",
  "expires_at": "2026-03-20T18:00:00Z"
}
```

## 8.2 Get Lobbies

`GET /api/v1/lobbies`

Query params:

1. `status=waiting`
2. `map_id=city_default`
3. `page=1`
4. `page_size=50`

Response `200 OK`:

```json
{
  "items": [
    {
      "lobby_id": "lobby_01",
      "name": "Downtown Drift",
      "status": "waiting",
      "map_id": "city_default",
      "max_players": 4,
      "current_players": 2
    }
  ],
  "total": 1
}
```

## 8.3 Create Lobby

`POST /api/v1/lobbies`

Headers:

```text
Authorization: Bearer <session_token>
```

Request:

```json
{
  "name": "Downtown Drift",
  "map_id": "city_default",
  "max_players": 4,
  "car_config": {}
}
```

Response `201 Created`:

```json
{
  "lobby_id": "lobby_01",
  "status": "waiting"
}
```

## 8.4 Get Lobby Details

`GET /api/v1/lobbies/{lobby_id}`

Response `200 OK`:

```json
{
  "lobby_id": "lobby_01",
  "name": "Downtown Drift",
  "status": "waiting",
  "map_id": "city_default",
  "max_players": 4,
  "current_players": 2,
  "players": [
    {
      "player_id": "player_01",
      "player_name": "Guest_1284",
      "connection_state": "connected"
    }
  ]
}
```

## 8.5 Join Lobby

`POST /api/v1/lobbies/{lobby_id}/join`

Headers:

```text
Authorization: Bearer <session_token>
```

Request:

```json
{
  "car_config": {}
}
```

Response `200 OK`:

```json
{
  "lobby_id": "lobby_01",
  "player_id": "player_02",
  "joined": true
}
```

Errors:

1. `404` lobby not found
2. `409` lobby full
3. `409` lobby already started

## 8.6 Leave Lobby

`POST /api/v1/lobbies/{lobby_id}/leave`

Headers:

```text
Authorization: Bearer <session_token>
```

Response `200 OK`:

```json
{
  "left": true
}
```

## 8.7 Update Car Config In Lobby

`PUT /api/v1/lobbies/{lobby_id}/car-config`

Headers:

```text
Authorization: Bearer <session_token>
```

Request:

```json
{
  "car_config": {}
}
```

Response `200 OK`:

```json
{
  "updated": true
}
```

## 8.8 Get Match Info

`GET /api/v1/matches/{match_id}`

Response `200 OK`:

```json
{
  "match_id": "match_01",
  "lobby_id": "lobby_01",
  "status": "running",
  "map_id": "city_default",
  "tick_rate": 20
}
```

## 9. WebSocket Protocol

## 9.1 Client -> Server Messages

### `subscribe_lobby`

```json
{
  "type": "subscribe_lobby",
  "lobby_id": "lobby_01"
}
```

### `unsubscribe_lobby`

```json
{
  "type": "unsubscribe_lobby",
  "lobby_id": "lobby_01"
}
```

### `match_loaded`

```json
{
  "type": "match_loaded",
  "match_id": "match_01"
}
```

### `player_input`

```json
{
  "type": "player_input",
  "match_id": "match_01",
  "seq": 154,
  "client_time": 1710871200123,
  "input": {
    "throttle": 1.0,
    "brake": 0.0,
    "steer": 0.2,
    "handbrake": false,
    "nitro": false
  }
}
```

### `ping`

```json
{
  "type": "ping",
  "time": 1710871200123
}
```

## 9.2 Server -> Client Messages

### `welcome`

```json
{
  "type": "welcome",
  "player_id": "player_01",
  "server_time": 1710871200000
}
```

### `lobby_snapshot`

```json
{
  "type": "lobby_snapshot",
  "lobby": {}
}
```

### `lobby_player_joined`

```json
{
  "type": "lobby_player_joined",
  "lobby_id": "lobby_01",
  "player": {}
}
```

### `lobby_player_left`

```json
{
  "type": "lobby_player_left",
  "lobby_id": "lobby_01",
  "player_id": "player_02"
}
```

### `lobby_starting`

```json
{
  "type": "lobby_starting",
  "lobby_id": "lobby_01",
  "countdown_sec": 3
}
```

### `match_created`

```json
{
  "type": "match_created",
  "match_id": "match_01",
  "lobby_id": "lobby_01",
  "map_id": "city_default"
}
```

### `match_started`

```json
{
  "type": "match_started",
  "match_id": "match_01",
  "server_tick": 0
}
```

### `match_state`

```json
{
  "type": "match_state",
  "match_id": "match_01",
  "server_tick": 812,
  "players": []
}
```

### `player_disconnected`

```json
{
  "type": "player_disconnected",
  "match_id": "match_01",
  "player_id": "player_02"
}
```

### `match_finished`

```json
{
  "type": "match_finished",
  "match_id": "match_01",
  "reason": "normal"
}
```

### `error`

```json
{
  "type": "error",
  "code": "LOBBY_FULL",
  "message": "Lobby is full"
}
```

## 10. Validation Rules

### 10.1 Sessions

1. Все REST endpoints кроме `POST /sessions/guest` требуют valid `session_token`.
2. WebSocket требует valid `session_token`.

### 10.2 Lobby Rules

1. `max_players` от `2` до `8`.
2. Имя лобби от `3` до `32` символов.
3. Игрок не может быть одновременно в двух lobby.
4. Игрок не может дважды войти в один lobby.
5. Если owner выходит из waiting lobby, ownership передается следующему игроку.

### 10.3 Car Config Rules

1. Максимальный размер `car_config` в JSON: `32 KB`.
2. Максимум `128` customization entries.
3. Каждый `selector_path` и `variant_name` не длиннее `128` символов.

### 10.4 Match Rules

1. Сервер принимает input только от игроков, входящих в матч.
2. Сервер игнорирует packet с устаревшим `seq`.
3. Сервер имеет timeout disconnect, например `10 сек`.

## 11. Error Codes

Использовать machine-readable error codes:

1. `UNAUTHORIZED`
2. `INVALID_REQUEST`
3. `LOBBY_NOT_FOUND`
4. `LOBBY_FULL`
5. `LOBBY_ALREADY_STARTED`
6. `PLAYER_ALREADY_IN_LOBBY`
7. `PLAYER_NOT_IN_LOBBY`
8. `MATCH_NOT_FOUND`
9. `MATCH_NOT_RUNNING`
10. `INVALID_CAR_CONFIG`
11. `INTERNAL_ERROR`

## 12. Suggested Server State Model

### 12.1 In-memory Structures

1. `sessions_by_token`
2. `lobbies_by_id`
3. `player_to_lobby`
4. `matches_by_id`
5. `player_connections`

### 12.2 Match Runtime State

Для каждого игрока хранить:

1. `player_id`
2. `car_config`
3. `input_state`
4. `transform_state`
5. `last_input_seq`
6. `last_packet_at`

## 13. Non-Functional Requirements

1. Tick rate матча: `20 Hz`.
2. Broadcast rate snapshot: `10-20 Hz`.
3. Latency budget для lobby events: `< 250 ms`.
4. Сервер должен держать минимум `100` waiting lobbies в памяти.
5. Логи должны содержать `session_id`, `lobby_id`, `match_id`, `player_id`.

## 14. Security And Abuse Protection

Для MVP достаточно:

1. Rate limit на `POST /sessions/guest`
2. Rate limit на `create/join/leave lobby`
3. Ограничение размера websocket сообщений
4. Валидация числовых диапазонов input
5. Disconnect при flood input packets

## 15. Implementation Notes For The Next AI

Нужно реализовать:

1. HTTP server.
2. WebSocket server.
3. Session manager.
4. Lobby manager.
5. Match manager.
6. Authoritative match tick loop.
7. JSON schema validation для `car_config`.
8. Unit tests на lifecycle lobby.

## 16. Minimum Acceptance Criteria

Backend считается готовым для MVP, если:

1. Два клиента могут получить session.
2. Один клиент может создать lobby.
3. Второй клиент видит lobby и подключается.
4. При заполнении lobby матч стартует автоматически.
5. Оба клиента получают `match_created` и `match_started`.
6. Оба клиента отправляют input в сервер.
7. Сервер рассылает authoritative positions.
8. При дисконнекте одного игрока сервер рассылает событие disconnect.
9. `car_config` каждого игрока сохраняется в lobby и доступен в match state.

## 17. Recommended Stack

Для быстрой реализации MVP можно использовать:

1. `Node.js + TypeScript + Fastify + ws`
2. или `C# ASP.NET Core + WebSockets`

Если нужен максимально быстрый старт для нейросети-исполнителя:

1. `TypeScript`
2. `Fastify`
3. `Zod` для схем
4. `ws` для realtime

## 18. Deliverables

Ожидаемый результат от исполнителя:

1. Исходники backend.
2. `README` с запуском.
3. `.env.example`
4. OpenAPI/Swagger для REST.
5. Документация по WebSocket messages.
6. Тестовый сценарий для двух клиентов.

## 19. Execution Instructions For The Next AI

Ниже не описание системы, а прямая инструкция исполнителю.

### 19.1 What You Must Do

Сделай backend MVP по этому документу.

Обязательный результат:

1. Backend запускается локально.
2. Backend умеет создавать guest session.
3. Backend умеет создавать и возвращать список lobby.
4. Backend умеет подключать игроков в lobby.
5. Backend автоматически стартует match при заполнении lobby.
6. Backend поднимает WebSocket.
7. Backend принимает player input.
8. Backend рассылает authoritative match state.
9. Backend можно захостить и проверить после деплоя.

### 19.2 Required Tech Stack

Используй именно этот стек, если нет прямой причины от него уходить:

1. `Node.js 20+`
2. `TypeScript`
3. `Fastify`
4. `ws`
5. `Zod`
6. `pnpm`
7. `Docker`

### 19.3 Project Structure

Сделай backend как отдельную папку, например:

```text
backend/
```

Рекомендуемая структура:

```text
backend/
  src/
    app/
    config/
    modules/
      sessions/
      lobbies/
      matches/
      realtime/
    domain/
    schemas/
    utils/
    index.ts
  tests/
  package.json
  tsconfig.json
  Dockerfile
  .env.example
  README.md
```

### 19.4 Implementation Order

Делай строго в таком порядке:

1. Инициализируй backend проект.
2. Подними Fastify HTTP server.
3. Добавь health endpoint.
4. Реализуй guest session endpoint.
5. Реализуй in-memory session store.
6. Реализуй lobby store.
7. Реализуй:
   - create lobby
   - list lobbies
   - get lobby
   - join lobby
   - leave lobby
   - update car config
8. Подними WebSocket server.
9. Реализуй lobby realtime events.
10. Реализуй auto-start logic.
11. Реализуй match manager.
12. Реализуй server tick loop.
13. Реализуй player input processing.
14. Реализуй authoritative state broadcast.
15. Реализуй smoke tests.
16. Упакуй в Docker.
17. Захость.
18. Проверь удаленный стенд.

### 19.5 Simplifications Allowed For MVP

Разрешено:

1. Хранить lobby и match state только в памяти.
2. Не использовать PostgreSQL.
3. Не использовать Redis.
4. Делать один server instance.
5. Делать упрощенную физику машины.

Не разрешено:

1. Убирать WebSocket.
2. Делать только polling вместо realtime.
3. Делать клиент authoritative без серверного match state.
4. Убирать auto-start lobby.

### 19.6 Match Simulation Rule

Если нет времени на полную физику:

1. Храни для каждого игрока `position`, `rotationY`, `velocity`.
2. Принимай `throttle`, `brake`, `steer`, `handbrake`, `nitro`.
3. На сервере обновляй position простым kinematic simulation.
4. Главное, чтобы итоговая позиция считалась на сервере, а не принималась как истина от клиента.

### 19.7 Validation Requirements

Обязательно проверь:

1. `session_token` существует.
2. lobby существует.
3. lobby не заполнено.
4. player не состоит уже в другом lobby.
5. `car_config` не превышает допустимый размер.
6. все websocket messages валидируются схемой.
7. input values clamp:
   - `throttle` от `-1` до `1`
   - `brake` от `0` до `1`
   - `steer` от `-1` до `1`

### 19.8 Required REST Endpoints

Сделай и задокументируй:

1. `POST /api/v1/sessions/guest`
2. `GET /api/v1/lobbies`
3. `POST /api/v1/lobbies`
4. `GET /api/v1/lobbies/{lobby_id}`
5. `POST /api/v1/lobbies/{lobby_id}/join`
6. `POST /api/v1/lobbies/{lobby_id}/leave`
7. `PUT /api/v1/lobbies/{lobby_id}/car-config`
8. `GET /api/v1/matches/{match_id}`
9. `GET /api/v1/health`

### 19.9 Required WebSocket Messages

Реализуй:

Client -> Server:

1. `subscribe_lobby`
2. `unsubscribe_lobby`
3. `match_loaded`
4. `player_input`
5. `ping`

Server -> Client:

1. `welcome`
2. `lobby_snapshot`
3. `lobby_player_joined`
4. `lobby_player_left`
5. `lobby_starting`
6. `match_created`
7. `match_started`
8. `match_state`
9. `player_disconnected`
10. `match_finished`
11. `error`

### 19.10 What To Log

Во всех ключевых местах логируй:

1. `session_id`
2. `player_id`
3. `lobby_id`
4. `match_id`
5. event type

Нужны логи для:

1. session create
2. lobby create
3. lobby join
4. lobby leave
5. auto-start
6. match start
7. match finish
8. websocket connect/disconnect

### 19.11 What To Put In README

README должен содержать:

1. как установить зависимости
2. как запустить локально
3. как запустить через Docker
4. список env vars
5. примеры REST запросов
6. описание WebSocket подключения
7. как прогнать smoke test
8. как задеплоить

### 19.12 Required Env Variables

Минимально:

```text
PORT=8080
HOST=0.0.0.0
NODE_ENV=production
LOG_LEVEL=info
CORS_ORIGIN=*
AUTO_START_COUNTDOWN_SEC=3
MATCH_TICK_RATE=20
MATCH_BROADCAST_RATE=10
```

### 19.13 Smoke Test You Must Run

Перед сдачей обязательно сам проверь сценарий:

1. Поднять backend локально.
2. Создать `guest session #1`.
3. Создать `guest session #2`.
4. От имени первого игрока создать lobby на 2 игрока.
5. От имени второго игрока войти в lobby.
6. Проверить, что lobby переходит в `starting`.
7. Проверить, что создается `match_id`.
8. Открыть 2 websocket клиента.
9. Отправить `match_loaded` от обоих.
10. Отправить `player_input` от обоих.
11. Получить `match_state` от сервера.

### 19.14 Deploy Instructions

Если у тебя есть доступ к хостингу:

1. Собери Docker image.
2. Подними backend на выбранной платформе.
3. Укажи публичный URL.
4. Проверь `GET /api/v1/health`.
5. Повтори smoke test уже против удаленного URL.

Подходящие варианты для быстрого MVP:

1. `Railway`
2. `Render`
3. `Fly.io`
4. `VPS + Docker Compose`

### 19.15 Final Output Expected From You

Когда закончишь, верни:

1. путь к backend source code
2. список реализованных endpoint'ов
3. список websocket messages
4. локальную команду запуска
5. docker-команду запуска
6. URL деплоя
7. результат smoke test
8. список ограничений MVP
