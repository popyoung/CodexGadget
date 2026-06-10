---
name: vpn
description: Use when the user types command-style VPN/v2rayN requests such as "vpn group", "vpn list", "vpn auto 1", "vpn reload", "vpn help", or sends a standalone number immediately after "vpn list" or "vpn group".
---

# VPN Command Router

Route command-style requests through the v2rayN Control `UserPromptSubmit` hook. The hook is a .NET 8 executable and communicates only with v2rayN's local named pipe.

## Commands

- `vpn group`: list all v2rayN groups. The next standalone number sets the plugin current group only.
- `vpn list`: list nodes in the plugin current group and automatically start v2rayN realping for that group.
- `vpn auto N`: real-ping the plugin current group, filter usable nodes with `delay > 0`, and switch to the Nth usable node in current-group list order.
- `N`: after `vpn list`, switch to node `N` in the plugin current group.
- `N`: after `vpn group`, set the plugin current group to group `N`.
- `vpn help`: show supported command syntax.
- `vpn ping`: check IPC availability.
- `vpn reload`: ask v2rayN to reload the IPC plugin DLL only; do not reload proxy core.

Do not use `vpn switch`, `vpn test`, `vpn status`, or `vpn stop`; those commands are intentionally removed. Do not read v2rayN SQLite or JSON files from this skill. The plugin must communicate only through the local named pipe exposed by the custom v2rayN build.
