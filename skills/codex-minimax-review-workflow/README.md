# Codex MiniMax Review Workflow

Reusable Codex skill for the Codex main-agent, Codex subagent A/B, and MiniMax review workflow.

## Local Skill Installation

Keep this directory as the source of truth in Git:

```text
D:\Git\MinimaxRemix\CodexGadget\skills\codex-minimax-review-workflow
```

Install locally by creating a symbolic link:

```powershell
New-Item -ItemType SymbolicLink `
  -Path C:\Users\popyoung\.codex\skills\codex-minimax-review-workflow `
  -Target D:\Git\MinimaxRemix\CodexGadget\skills\codex-minimax-review-workflow
```

Do not copy the skill into the local Codex skills directory. A symlink keeps the local skill and Git source synchronized.

## Contents

- `SKILL.md`: trigger rules and core workflow.
- `references/protocol.md`: fixed-file protocol, templates, and decision rules.
- `scripts/mini-agent-acp-controller.mjs`: persistent Mini-Agent ACP controller with heartbeat, stage report monitoring, and git proxy support.
