# Server-Authoritative Simulation Plan

## Current State

The project already has a split between:

- Unity client in `C:\Work\Prototyping\Russian Road Rage`
- FastAPI backend in `C:\Work\RRRBack`

What exists today:

- lobby and match orchestration on the backend
- WebSocket transport for realtime match messages
- deterministic spawn assignment on the backend
- local Unity vehicle physics driven by `Rigidbody` + `WheelCollider`
- remote player visualization based on snapshots received from backend

What does not exist today:

- server-side vehicle simulation
- server-side authoritative collision resolution
- server-side room process lifecycle
- dedicated server build pipeline

## Key Findings From The Codebase

### Backend is authoritative only for session/lobby/match flow

Backend runtime in `C:\Work\RRRBack\app\services\runtime.py` currently:

- creates lobbies and matches
- waits for `match_loaded`
- accepts `player_state`, `damage_state`, and `collision_event`
- stores the latest state per player
- rebroadcasts `match_state` snapshots

This means the backend is currently a state relay with validation and telemetry, not a simulation authority.

### Client sends full state, not input

Unity client code in `Assets/CodeBase/Backend/MultiplayerMatchRuntime.cs` captures and sends:

- `position`
- `rotation`
- `velocity`
- `angular_velocity`
- `wheel_states`

That is the main architectural blocker. Anti-cheat, deterministic authority, and replayable simulation are impossible while clients submit resolved physics states.

### Vehicle simulation is reusable, but input is coupled to the controller

`Assets/CodeBase/Vehicle/CarControllerBase.cs` is promising because:

- actual motion is driven through Unity physics
- gameplay motion is centralized in `FixedUpdate()`
- the low-level force application is already factored into `VehicleDynamics`

But input is still hardcoded inside the controller:

- keyboard and legacy input are read directly in `FixedUpdate()`
- there is no input buffer or input provider interface

This is the first code refactor required before a headless server can simulate the same cars cleanly.

### Visual setup and gameplay setup are still mixed

`PlayerCar`, `RiggedCarController`, and `GameSceneBootstrap` currently mix:

- loadout resolution
- wheel/body assembly
- paint/customization
- camera setup
- local multiplayer runtime hookup

That is acceptable for the client, but the dedicated simulation build should instantiate only:

- the map collision scene
- authoritative car prefabs
- network room coordinator

It should not depend on camera, HUD, visual-only renderers, or presentation-only bootstrap steps.

## Target Architecture

Use three processes, not one:

1. Unity WebGL or desktop client
2. FastAPI orchestration backend
3. Unity Linux Dedicated Server simulation service

### Backend responsibilities

FastAPI should remain the source of truth for:

- sessions
- lobbies
- match creation
- player roster
- spawn assignment
- room allocation
- room discovery tokens
- admin visibility
- post-match metadata

FastAPI should stop simulating or relaying raw client vehicle state.

### Dedicated simulation responsibilities

The Unity dedicated service should become the source of truth for:

- scene loading per map
- authoritative car spawning
- input ingestion
- fixed-tick physics simulation
- collision detection
- damage application
- match snapshots
- disconnect timeout inside the room
- match end condition inside the room

### Client responsibilities

The Unity client should:

- send input commands only
- predict its own local movement
- reconcile to server snapshots
- interpolate remote players
- render VFX, audio, UI, and camera locally

## Recommended Room Model

Do not create one OS process per match as the first implementation.

For the first production-capable version:

- run one dedicated Unity simulation process
- host multiple matches inside that process
- give each match its own additive scene or isolated simulation scene
- keep a strict cap on concurrent rooms per process
- start more dedicated processes only when room capacity is exhausted

Reason:

- process-per-match is operationally heavier
- startup latency will be worse
- memory overhead will be much higher
- orchestration becomes harder too early

Process-per-match can be revisited later if isolation becomes more important than density.

## Transport Recommendation

Short term:

- keep FastAPI as the public entry point
- add room allocation and room token exchange there
- let clients connect to the dedicated simulation service after match creation

Recommended flow:

1. client creates or joins lobby through FastAPI
2. backend creates match and reserves a room on the simulation service
3. backend returns room connection info and a short-lived room token
4. client connects directly to the simulation room
5. room sends snapshots and accepts input
6. room reports lifecycle and final state back to FastAPI

This keeps orchestration simple and avoids tunneling high-frequency physics traffic through FastAPI.

## Protocol Changes

Replace `player_state` with `player_input`.

Minimum input payload:

- `match_id`
- `player_id` or room-authenticated identity
- `seq`
- `client_time`
- `throttle`
- `steer`
- `brake`
- `handbrake`
- `nitro`

Optional later:

- gearbox actions
- weapon fire
- aim or turret input
- horn or emotes

Server snapshots should contain:

- `server_tick`
- `ack_input_seq`
- authoritative transform
- velocity
- angular velocity
- wheel visual state if needed
- damage state revision
- event stream for collisions, hits, eliminations

## Unity Refactor Order

### Phase 1: decouple vehicle simulation from raw input APIs

Create an input abstraction for `CarControllerBase`:

- `CarControlFrame`
- `ICarInputSource`
- local keyboard input source
- network/server input source

Goal:

- the same controller code can run from local keyboard on the client
- the same controller code can run from buffered network input on the server

### Phase 2: split gameplay and presentation bootstrap

Separate:

- authoritative gameplay prefab
- visual/client prefab
- room bootstrap
- client bootstrap

Goal:

- dedicated build can instantiate cars and maps without camera and HUD setup

### Phase 3: add dedicated server scene bootstrap

Add a dedicated-only bootstrap that can:

- register room capacity
- create and unload match scenes
- spawn authoritative cars from `car_config`
- tick matches at fixed rate

### Phase 4: change realtime protocol

Backend/client contracts should move from:

- client state upload

to:

- client input upload
- authoritative room snapshot download

### Phase 5: add client prediction and reconciliation

Only after the dedicated room is authoritative should the client gain:

- local prediction
- correction smoothing
- rollback or partial resimulation if needed

## Build And Deploy Recommendation

### Build

Add a new Unity editor build pipeline for:

- `BuildTarget.StandaloneLinux64`
- `StandaloneBuildSubtarget.Server`

Output should be versioned the same way as existing WebGL and desktop release flows.

Suggested artifact shape:

- `Builds/DedicatedServer/<release-id>/`
- executable
- `release.json`
- server runtime config template

### Deploy

Do not place the dedicated Unity process inside the same Docker container as FastAPI.

Recommended deployment on the current host:

- keep FastAPI as one service
- add a second service for Unity dedicated simulation
- put both behind nginx if public WebSocket exposure is needed

Suggested runtime options:

- `systemd` service for the Unity simulation binary
- or a dedicated Docker image only for the simulation service

The first path is simpler if you are publishing raw Unity artifacts directly to the server already.

### Match coordinator contract

FastAPI should be able to call the simulation service for:

- room reservation
- room release
- room health
- match start
- match abort
- match result submission

This can be plain internal HTTP at first.

## What Should Not Be In Version 1

Avoid these in the first server-authoritative release:

- deterministic lockstep
- full rollback netcode for all players
- process-per-match orchestration
- Kubernetes
- database persistence for every simulation tick
- replay system

They will slow delivery and are not required to prove server authority.

## Practical First Implementation Step

The first safe code milestone is:

1. introduce `CarControlFrame` and `ICarInputSource`
2. refactor `CarControllerBase` to consume abstracted input instead of reading keyboard directly
3. keep current client behavior unchanged by using a local keyboard input source
4. add a second input source that can be driven by network messages

That change is small enough to verify locally and is the foundation for everything else:

- dedicated server simulation
- direct room input protocol
- client prediction
- bot drivers
- replayable inputs

## Conclusion

The dedicated Linux server approach is viable for this project.

The codebase is already close enough because:

- vehicle dynamics are centralized
- backend orchestration already exists
- release and deploy tooling patterns already exist

The main rule for the migration is:

first move from state replication to input-driven simulation, then add the dedicated room process, then switch clients over to authoritative snapshots.
