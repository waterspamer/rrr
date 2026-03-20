# Spec: Full Multiplayer Player Spawn And Client Preparation

## Goal

Нужно довести multiplayer в `Russian Road Rage` до полноценного спавна игроков на карте, а не только до lobby flow и прокси-обновления координат.

Итоговая цель:

1. все игроки lobby переходят в `Game`
2. сервер назначает каждому игроку spawn point
3. клиент создает локального игрока в своей spawn position
4. клиент создает remote игроков в их spawn position
5. все игроки синхронизируются через server-authoritative match state
6. архитектура клиента должна быть подготовлена под дальнейшее развитие netcode

## Current State

На текущий момент уже есть:

1. backend session / lobby / match flow
2. переход из garage в `Game`
3. отправка `match_loaded`
4. отправка локального `player_input`
5. прием `match_state`
6. временные remote proxies по входящим данным

Этого недостаточно для полноценного multiplayer match.

## What Must Be Implemented

Нужно реализовать:

1. полноценный spawn pipeline на сервере
2. контракт spawn data в match payload
3. клиентский spawn manager
4. разделение `local player entity` и `remote player entity`
5. подготовку клиента к дальнейшему prediction / reconciliation

## Core Design Rule

Сервер authoritative.

Это значит:

1. сервер решает, где спавнится каждый игрок
2. сервер формирует итоговый список spawn positions
3. клиент не выбирает себе spawn point сам
4. клиент только читает spawn assignment и создает сущности по нему

## Required Backend Changes

Backend должен уметь:

1. хранить список spawn points для карты
2. при старте match назначать каждому игроку конкретный spawn point
3. включать spawn data в `match_created` или отдельный `match_setup`
4. включать spawn data в `GET /api/v1/matches/{match_id}`
5. включать актуальный player transform state в realtime

## Required Match Data

Для каждого игрока в match backend должен знать:

1. `player_id`
2. `player_name`
3. `car_config`
4. `spawn_point_id`
5. `spawn_position`
6. `spawn_rotation`
7. `input_state`
8. `transform_state`
9. `connection_state`

## Required Map Spawn Model

Для каждой карты нужен список spawn points:

```json
[
  {
    "spawn_point_id": "sp_01",
    "position": { "x": 0.0, "y": 0.5, "z": 0.0 },
    "rotation": { "x": 0.0, "y": 90.0, "z": 0.0 }
  }
]
```

## Spawn Assignment Rules

Нужно:

1. назначать уникальный spawn point каждому игроку
2. не выдавать один и тот же spawn двум игрокам
3. порядок назначения должен быть детерминированным
4. если игроков больше, чем spawn points, матч не должен стартовать
5. в таком случае backend должен вернуть явную ошибку, а не undefined behavior

## Recommended Spawn Assignment Strategy

Для MVP:

1. взять фиксированный список spawn points карты
2. отсортировать игроков по `joined_at`
3. назначать spawn points по порядку

## Required Backend REST Additions

Если их еще нет, добавить:

### `GET /api/v1/matches/{match_id}`

Ответ должен содержать:

```json
{
  "match_id": "match_01",
  "lobby_id": "lobby_01",
  "status": "starting",
  "map_id": "city_default",
  "tick_rate": 20,
  "players": [
    {
      "player_id": "player_01",
      "player_name": "Guest_1001",
      "spawn_point_id": "sp_01",
      "spawn_position": { "x": 0.0, "y": 0.5, "z": 0.0 },
      "spawn_rotation": { "x": 0.0, "y": 90.0, "z": 0.0 },
      "car_config": {}
    }
  ]
}
```

## Required WebSocket Messages

Нужно реализовать или расширить:

### `match_created`

Должен содержать:

1. `match_id`
2. `lobby_id`
3. `map_id`
4. список игроков
5. spawn assignment для каждого игрока

Пример:

```json
{
  "type": "match_created",
  "match_id": "match_01",
  "lobby_id": "lobby_01",
  "map_id": "city_default",
  "players": [
    {
      "player_id": "player_01",
      "player_name": "Guest_1001",
      "spawn_point_id": "sp_01",
      "spawn_position": { "x": 0.0, "y": 0.5, "z": 0.0 },
      "spawn_rotation": { "x": 0.0, "y": 90.0, "z": 0.0 },
      "car_config": {}
    }
  ]
}
```

### `match_started`

Должен приходить только после того, как:

1. всем клиентам выдан spawn assignment
2. клиенты загрузили сцену
3. клиенты отправили `match_loaded`

### `match_state`

Должен содержать:

1. `player_id`
2. `position`
3. `rotation`
4. `velocity`

## Required Client Changes

На клиенте нужно сделать отдельную архитектуру для multiplayer match.

Нужно реализовать:

1. `MultiplayerMatchManager`
2. `NetworkPlayerFactory`
3. `SpawnPointResolver`
4. `RemotePlayerController` или аналогичный компонент
5. подготовку для локального и remote entity разделения

## Client Responsibilities

Клиент должен:

1. получить `match_created`
2. сохранить spawn assignments
3. загрузить `Game`
4. создать локального игрока в назначенной позиции
5. создать remote игроков в их позициях
6. назначить камеру только локальному игроку
7. не давать remote игрокам локальный input
8. обновлять remote игроков только из server state

## Required Client Architecture

### 1. Local Player Entity

Локальный игрок:

1. имеет физику
2. имеет input
3. имеет follow camera
4. отправляет `player_input`

### 2. Remote Player Entity

Remote игрок:

1. не имеет локального input
2. не должен перехватывать камеру
3. обновляется из `match_state`
4. визуально использует `car_config` соответствующего игрока

## Required Scene Behavior

Сцена `Game` должна уметь:

1. стартовать как singleplayer без backend match
2. стартовать как multiplayer с активным `match_id`

То есть multiplayer логика должна включаться только когда реально есть сетевой матч.

## Spawn Workflow

Правильный flow:

1. lobby filled
2. backend создает match
3. backend назначает spawn points
4. backend рассылает `match_created` со spawn data
5. клиенты загружают `Game`
6. клиентский `MultiplayerMatchManager` читает spawn data
7. локальный player entity ставится на свой spawn
8. remote players создаются на чужих spawn positions
9. клиент отправляет `match_loaded`
10. backend переводит матч в `running`
11. начинается realtime sync

## Client Spawn Rules

Нужно:

1. не искать spawn для локального игрока через сцену случайным образом
2. не создавать всех игроков как одинаковые `PlayerCar`
3. не использовать один и тот же singleton объект для всех машин
4. отделить `local player root` от `remote player root`

## Required Preparation For Future Work

Клиент нужно подготовить под:

1. client-side prediction
2. reconciliation
3. interpolation buffer для remote players
4. spectator mode
5. reconnect flow

Даже если это не реализуется сейчас, архитектура не должна этому мешать.

## Recommended Unity Structure

Рекомендуется сделать:

1. `MultiplayerMatchRuntime`
2. `NetworkPlayerSpawnManager`
3. `NetworkPlayerAvatar`
4. `RemoteCarVisual`
5. `LocalCarInputSender`

## Required Data Sources On Client

Клиент должен брать:

1. `player_id` текущего клиента из session
2. `match_id` из backend runtime
3. `spawn assignment` из `match_created` или `GET /matches/{id}`
4. `car_config` игроков из match data

## Acceptance Criteria

Работа считается выполненной, если:

1. два клиента могут войти в один lobby
2. backend создает match
3. каждому игроку назначается свой spawn point
4. оба клиента загружают `Game`
5. локальная машина каждого клиента спавнится в правильной позиции
6. remote игрок виден на сцене как отдельная машина
7. камера прикреплена только к локальному игроку
8. remote игрок обновляется по server state
9. singleplayer режим остается рабочим и не ломается

## Required Smoke Test

Нужно вручную проверить:

1. `client A` создает lobby
2. `client B` входит в lobby
3. backend стартует match
4. оба клиента получают `match_created`
5. оба клиента загружают `Game`
6. у каждого клиента локальный игрок стоит в своем spawn point
7. каждый клиент видит второго игрока на сцене
8. при движении одного клиента второй видит движение remote машины

## Nice To Have

Если хватит времени:

1. debug overlay с `player_id`, `spawn_point_id`, `match_id`
2. gizmo visualization spawn points в Unity
3. fallback `GET /matches/{id}` если `match_created` потерян

## What To Return

После выполнения нужно вернуть:

1. какие backend-файлы изменены
2. какие client-файлы изменены
3. как хранится spawn assignment
4. какие websocket messages используются
5. результат smoke test на 2 клиента
6. какие ограничения еще остались
