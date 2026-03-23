# Dedicated Server Release Workflow

## Goal

Build and deploy a Unity Linux Dedicated Server artifact from the main Unity repository without committing build outputs.

## Output Model

Dedicated server releases are versioned and atomic:

- local releases: `C:\Work\BuildAgents\RRR-Dedicated\releases\<release-id>`
- remote releases: `/opt/rrr-dedicated/releases/<release-id>`
- active release symlink: `/opt/rrr-dedicated/current`

Each release contains:

- Linux server executable
- Unity data folder
- `run.sh`
- `release.json`
- `server.env.example`

Important runtime variables for the internal-only setup:

- `RRR_DEDICATED_CONTROL_TOKEN`
- `RRR_DEDICATED_PUBLIC_HTTP_BASE_URL`
- `RRR_DEDICATED_PUBLIC_WS_BASE_URL`
- `RRR_DEDICATED_BIND=127.0.0.1`
- `RRR_MATCH_BACKEND_URL=http://127.0.0.1:8083`

## Unity Build Pipeline

Build entry point:

- [DedicatedServerBuildPipeline.cs](/C:/Work/Prototyping/Russian%20Road%20Rage/Assets/CodeBase/Editor/DedicatedServerBuildPipeline.cs)

Default dedicated scene list:

- `Assets/Scenes/Game.unity`

The dedicated build uses:

- target: `StandaloneLinux64`
- subtarget: `Server`

## Scripts

- [Prepare-DedicatedWorkspace.ps1](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Prepare-DedicatedWorkspace.ps1)
  Creates or refreshes the clean dedicated build workspace from the committed repo.

- [Release-DedicatedServer.ps1](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Release-DedicatedServer.ps1)
  Full pipeline:
  1. refresh workspace
  2. run Unity batch build for Linux Dedicated Server
  3. create versioned local release
  4. upload release to server
  5. switch `current` symlink
  6. restart `systemd` service
  7. prune old releases

- [Publish-DedicatedServerRelease.ps1](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Publish-DedicatedServerRelease.ps1)
  Publishes an already built dedicated release folder and restarts the configured service.

- [Bootstrap-DedicatedServerHost.ps1](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Bootstrap-DedicatedServerHost.ps1)
  One-time host setup:
  1. create release directories
  2. create `server.env` if missing
  3. install `systemd` unit
  4. enable the service

## Typical Command

One-time host bootstrap:

```powershell
.\Scripts\Bootstrap-DedicatedServerHost.ps1 -Password '<server-password>'
```

```powershell
.\Scripts\Release-DedicatedServer.ps1 -Password '<server-password>'
```

With explicit service and remote root:

```powershell
.\Scripts\Release-DedicatedServer.ps1 `
  -Password '<server-password>' `
  -RemoteRoot '/opt/rrr-dedicated' `
  -ServiceName 'rrr-dedicated'
```

## Installed Host Layout

The bootstrap script creates:

- release root at `/opt/rrr-dedicated`
- release folders under `/opt/rrr-dedicated/releases`
- mutable environment file at `/opt/rrr-dedicated/server.env`
- `systemd` service file at `/etc/systemd/system/rrr-dedicated.service`

Example `ExecStart`:

```text
/opt/rrr-dedicated/current/run.sh
```

## Current Limitation

This workflow now builds and deploys the dedicated binary together with the internal room control API used by FastAPI.

Current known limitations:

- one Unity dedicated process serves one active match room
- FastAPI remains the public realtime gateway for clients
- dedicated room damage/collision sync is still transitional; vehicle transform/physics authority is already on the dedicated side
