# Backend Fix Spec: Multiplayer `car_config.customizations`

## Goal

Нужно починить backend для `Russian Road Rage`, чтобы он корректно принимал, валидировал, сохранял и возвращал `car_config.customizations` в multiplayer flow.

Base URL:

```text
https://rrr-demo.tonforspeed.space
```

Health endpoint:

```text
https://rrr-demo.tonforspeed.space/api/v1/health
```

## Current Verified State

На `March 19, 2026` вручную подтверждено:

1. `POST /api/v1/sessions/guest` работает.
2. `GET /api/v1/lobbies` работает.
3. `POST /api/v1/lobbies` работает, если `car_config` уходит в `snake_case`.
4. Lobby создается и виден в списке.

## Critical Bug

Backend сейчас отвечает `500 Internal Server Error`, если в `car_config.customizations` передается непустой массив.

То есть проблема находится на стороне backend, не на стороне Unity-клиента.

## What Must Be Fixed

Нужно:

1. Найти причину `500` при передаче `car_config.customizations`.
2. Починить backend так, чтобы он:
   - принимал `customizations` без ошибки `500`
   - валидировал структуру
   - сохранял `customizations` в lobby state
   - возвращал их в lobby details
   - возвращал их в realtime lobby snapshot
   - возвращал их в match state / match metadata, если это предусмотрено текущей архитектурой
3. Убедиться, что серверный контракт использует именно `snake_case`, а не `camelCase`.
4. Не сломать уже работающий multiplayer flow.

## Required Payload Contract

Backend должен принимать такой JSON:

```json
{
  "name": "Manual Test Lobby",
  "map_id": "city_default",
  "max_players": 2,
  "car_config": {
    "version": 1,
    "loadout_name": "Cooper_Loadout",
    "loadout_display_name": "Mini Cooper",
    "body_set_option_index": 0,
    "engine_index": 0,
    "suspension_index": 0,
    "paint_index": 0,
    "body_set_name": "",
    "engine_name": "Engine",
    "suspension_name": "Suspension",
    "paint_name": "Green",
    "has_paint": true,
    "paint": {
      "r": 0.1,
      "g": 0.25,
      "b": 0.14,
      "a": 1.0
    },
    "customizations": [
      {
        "selector_path": "BumperF",
        "variant_name": "SetA"
      },
      {
        "selector_path": "Skirts",
        "variant_name": "SetB"
      }
    ]
  }
}
```

## Required `car_config` Schema

Backend должен поддерживать:

```json
{
  "version": 1,
  "loadout_name": "Cooper_Loadout",
  "loadout_display_name": "Mini Cooper",
  "body_set_option_index": 0,
  "engine_index": 0,
  "suspension_index": 0,
  "paint_index": 0,
  "body_set_name": "",
  "engine_name": "Engine",
  "suspension_name": "Suspension",
  "paint_name": "Green",
  "has_paint": true,
  "paint": {
    "r": 0.1,
    "g": 0.25,
    "b": 0.14,
    "a": 1.0
  },
  "customizations": [
    {
      "selector_path": "BumperF",
      "variant_name": "SetA"
    }
  ]
}
```

## Important Backend Rules

1. Использовать `snake_case` поля.
2. Не падать на непустом `customizations`.
3. Максимум `128` customization entries.
4. Каждый `selector_path` и `variant_name` должен валидироваться как строка разумной длины.
5. Если payload невалиден, вернуть `400 INVALID_REQUEST`, а не `500`.

## Endpoints That Must Be Verified

Нужно проверить и при необходимости починить:

1. `POST /api/v1/sessions/guest`
2. `GET /api/v1/lobbies`
3. `POST /api/v1/lobbies`
4. `GET /api/v1/lobbies/{lobby_id}`
5. `POST /api/v1/lobbies/{lobby_id}/join`
6. `POST /api/v1/lobbies/{lobby_id}/leave`
7. `PUT /api/v1/lobbies/{lobby_id}/car-config`
8. realtime lobby snapshot через WebSocket

## Required Smoke Test

Перед сдачей обязательно прогнать:

1. Создать `guest session #1`
2. Создать `guest session #2`
3. От имени первого игрока создать lobby с непустым `customizations`
4. Проверить, что create lobby проходит без `500`
5. Проверить, что `GET /lobbies/{id}` возвращает сохраненный `car_config.customizations`
6. От имени второго игрока войти в lobby с непустым `customizations`
7. Проверить, что join проходит без `500`
8. Вызвать `PUT /lobbies/{id}/car-config` с новыми `customizations`
9. Проверить, что update проходит без `500`
10. Проверить, что realtime snapshot содержит актуальный `car_config`

## Expected Result

После фикса backend должен:

1. Принимать `customizations` без падения.
2. Хранить их в состоянии lobby.
3. Возвращать их в API и realtime.
4. Отдавать корректные validation errors вместо `500`.

## Important Client Note

Unity-клиент уже частично адаптирован:

1. Он шлет `car_config` в `snake_case`.
2. Сейчас в клиенте добавлен временный fallback:
   - сначала отправляется полный payload
   - если backend падает на `customizations`, клиент повторяет запрос без них

Это временный костыль. Цель этой задачи: починить backend так, чтобы fallback больше не был нужен.

## What You Must Return

По завершении верни:

1. Причину бага.
2. Какие backend-файлы изменены.
3. Какие endpoint'ы проверены.
4. Результат smoke test.
5. Публичный URL, на котором фикс уже работает.
