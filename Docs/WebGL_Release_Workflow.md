# WebGL Release Workflow

## Principles

- Source code and content live in the main Unity repository only.
- WebGL build artifacts are not committed to git.
- There are two supported release paths:
  - manual release from the opened Unity Editor when visual parity matters most
  - headless release from a dedicated clean workspace when full automation matters more
- Server deploys are versioned and atomic:
  - releases live in `/var/www/rrr-webgl/releases/<release-id>`
  - active public build is `/var/www/rrr-webgl/current`
  - nginx serves `https://rrr-demo.tonforspeed.space/play/`

## Local layout

- Source project: `C:\Work\Prototyping\Russian Road Rage`
- Build workspace: `C:\Work\BuildAgents\RRR-WebGL\workspace`
- Local releases: `C:\Work\BuildAgents\RRR-WebGL\releases`
- Manual editor releases: `C:\Work\BuildAgents\RRR-WebGL\editor-releases`

## Preferred flow right now

Use the Unity Editor window:

- menu: `Tools/Build/WebGL Release`
- buttons:
  - `Build WebGL`
  - `Build + Deploy`
  - `Deploy Last Build`

Why:

- it builds from the currently opened editor instance
- it avoids `-batchmode -nographics`
- it keeps the same graphics path you see in the editor

This is the preferred path for public WebGL releases until the headless pipeline reaches the same visual stability.

## Compression

- `rrr-demo.tonforspeed.space/play/` now supports both `brotli_static` and `gzip_static`
- if the release contains `.br` artifacts, nginx serves them directly
- if the release contains only `.gz`, nginx serves gzip
- `Brotli` is now the preferred compression mode for public WebGL releases

## Scripts

- [`Prepare-WebGLWorkspace.ps1`](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Prepare-WebGLWorkspace.ps1)
  Creates or refreshes the dedicated build workspace from the committed source repo.

- [`Release-WebGL.ps1`](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Release-WebGL.ps1)
  Full pipeline:
  1. refresh workspace
  2. build WebGL from workspace
  3. create local versioned release
  4. upload versioned release to server
  5. switch `current` symlink atomically
  6. prune old local and remote releases

- [`Bootstrap-WebGLHost.ps1`](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Bootstrap-WebGLHost.ps1)
  One-time server setup for nginx and release directories.

- [`Deploy-WebGL.ps1`](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Deploy-WebGL.ps1)
  Backward-compatible wrapper around `Release-WebGL.ps1`.

- [`Publish-WebGLRelease.ps1`](/C:/Work/Prototyping/Russian%20Road%20Rage/Scripts/Publish-WebGLRelease.ps1)
  Uploads an already built WebGL release folder to the server and atomically switches the public symlink.

## Typical commands

One-time host bootstrap:

```powershell
.\Scripts\Bootstrap-WebGLHost.ps1 -Password '<server-password>'
```

Release current committed `main`:

```powershell
.\Scripts\Release-WebGL.ps1 -Password '<server-password>'
```

Release with explicit compression mode:

```powershell
.\Scripts\Release-WebGL.ps1 -Password '<server-password>' -Compression Disabled
```

Publish an already built release folder:

```powershell
.\Scripts\Publish-WebGLRelease.ps1 -ReleasePath 'C:\Work\BuildAgents\RRR-WebGL\editor-releases\20260320-120000-abcd1234' -Password '<server-password>'
```

## Release model

Each release gets:

- local folder: `C:\Work\BuildAgents\RRR-WebGL\releases\<timestamp>-<commit>`
- local archive: `C:\Work\BuildAgents\RRR-WebGL\releases\<timestamp>-<commit>.zip`
- metadata file: `release.json`
- remote folder: `/var/www/rrr-webgl/releases/<timestamp>-<commit>`

The active public version is switched by repointing:

```text
/var/www/rrr-webgl/current
```

This keeps deploys atomic and allows rollback by repointing the symlink.

## Commit policy

- Commit source changes to `main`.
- Do not commit WebGL artifacts into the Unity repository.
- Commit only:
  - source files
  - editor tooling
  - deploy scripts
  - documentation
- Build artifacts live only:
  - on the local release machine
  - on the release server

That keeps git history clean and makes deploys reproducible from source commits.
