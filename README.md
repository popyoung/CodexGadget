# CodexGadget

Local Codex helper plugins and v2rayN control tooling.

## Layout

- `shutdown/`: local shutdown helper and Codex hook plugin.
- `v2rayn-control/`: Codex `vpn` hook plugin, v2rayN submodule, and v2rayN patch stack.
- `.agents/plugins/marketplace.json`: local plugin marketplace entries.

## Clone

```powershell
git clone --recurse-submodules <repo-url> CodexGadget
```

If the repo was cloned without submodules:

```powershell
git submodule update --init --recursive
```

## v2rayN Control

The v2rayN source is kept as a submodule at `v2rayn-control/v2rayN`.
Local changes are stored as patch files under `v2rayn-control/patches`.
Local publish outputs are fixed under `Z:\codex\codexGadget\...` by `Directory.Build.props`.

Default local publish directories:

- `v2rayN`: `Z:\codex\codexGadget\v2rayN-win-x64`
- `V2rayN.IpcPlugin`: `Z:\codex\codexGadget\v2rayN-win-x64\guiPlugins`
- `V2rayN.Control.Hook`: `Z:\codex\codexGadget\codex-hook`
- `AutoShutdownHook`: `Z:\codex\codexGadget\auto-shutdown-hook\win-x64`

See `v2rayn-control/README.md` for patch, build, and upgrade commands.
