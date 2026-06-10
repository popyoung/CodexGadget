# v2rayN Control

This directory contains the Codex `vpn` hook plugin and the v2rayN IPC integration.

## Layout

- `codex-plugin/`: Codex `UserPromptSubmit` hook plugin source.
- `v2rayN/`: upstream v2rayN git submodule, currently pinned to v2rayN 7.21.3.
- `patches/`: local v2rayN changes, stored as git patches.
- `artifacts/`: ignored local build outputs.
- `tmp-v2rayn-test/`: ignored local v2rayN test data.

## Patch Stack

The submodule should stay on a clean upstream v2rayN commit. Apply local changes with:

```powershell
git -C v2rayn-control\v2rayN apply --3way ..\patches\0001-add-v2rayn-plugin-host.patch ..\patches\0002-add-v2rayn-ipc-plugin.patch
```

Current patches:

- `0001-add-v2rayn-plugin-host.patch`: plugin interfaces, plugin loader, and plugin reload event wiring.
- `0002-add-v2rayn-ipc-plugin.patch`: named-pipe IPC plugin used by the Codex hook.

## Build

Publish the plugin-capable v2rayN host:

```powershell
dotnet publish v2rayn-control\v2rayN\v2rayN\v2rayN\v2rayN.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o v2rayn-control\artifacts\v2rayN-win-x64
```

Publish the IPC plugin DLL into the host plugin directory:

```powershell
dotnet publish v2rayn-control\v2rayN\v2rayN\V2rayN.IpcPlugin\V2rayN.IpcPlugin.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o v2rayn-control\artifacts\v2rayN-win-x64\guiPlugins
```

Publish the Codex hook:

```powershell
dotnet publish v2rayn-control\codex-plugin\hooks\V2rayN.Control.Hook\V2rayN.Control.Hook.csproj -c Release -r win-x64 --self-contained false -o v2rayn-control\artifacts\codex-hook
```

## Upgrade v2rayN

Fetch and test a newer upstream tag or commit:

```powershell
git -C v2rayn-control\v2rayN fetch --tags origin
git -C v2rayn-control\v2rayN checkout <tag-or-commit>
git -C v2rayn-control\v2rayN apply --3way ..\patches\0001-add-v2rayn-plugin-host.patch ..\patches\0002-add-v2rayn-ipc-plugin.patch
```

If the patch applies and builds, commit the new submodule pointer from the root repo:

```powershell
git add v2rayn-control\v2rayN
git commit -m "Update v2rayN base"
```

## GitHub Actions

`.github/workflows/build.yml` applies the patches and publishes win-x64 artifacts on Windows.
The manual `workflow_dispatch` input `v2rayn_ref` can test the patch stack against a newer v2rayN tag, branch, or commit without changing the repo.

## Hook Commands

- `vpn group`
- `vpn list`
- `vpn auto N`
- `vpn help`
- `vpn ping`
- `vpn reload`
- `N` after `vpn group` or `vpn list`

The local hook install points at the mirrored personal plugin under `C:\Users\popyoung\plugins\v2rayn-control`.
