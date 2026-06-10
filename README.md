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

See `v2rayn-control/README.md` for patch, build, and upgrade commands.
