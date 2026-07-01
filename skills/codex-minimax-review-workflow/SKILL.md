---
name: codex-minimax-review-workflow
description: Use when coordinating Codex implementation with MiniMax review through fixed files, persistent Mini-Agent ACP, Codex subagents, no silent installs, no silent fallbacks, and user decisions for unresolved disputes or optional findings.
---

# Codex MiniMax Review Workflow

Use this skill when the user wants Codex and MiniMax to collaborate on coding work with a strict review loop. Codex remains the coordinator. MiniMax reviews evidence and discusses disagreements. Code changes are made by Codex implementation subagents only after the main Codex agent writes a final modification conclusion.

## Hard Rules

- Do not silently install anything, switch model/provider/tooling, use direct API fallback, skip verification, or replace a failed path with another path. Ask the user first.
- Do not let MiniMax make code changes in this workflow. MiniMax reviews only.
- Do not let the main Codex agent directly patch findings raised by MiniMax. The main agent may clarify, judge, discuss, and write conclusions. Implementation goes to subagent A.
- Treat blocking and non-blocking MiniMax findings with the same decision loop. Non-blocking findings must not disappear into archives unless the user decides or Codex and MiniMax agree with evidence that no change is needed.
- If user requirements conflict, stop and ask. Do not resolve contradictory requirements silently.
- If MiniMax feedback is technically stronger than Codex's initial position, accept it. If the same disagreement cannot be resolved after 3 rounds, ask the user to decide.
- Active communication files are overwritten each turn. Read and then delete consumed active output files so context does not grow.

## Roles

- Codex main agent: clarify requirements, enforce constraints, assign subagents, run or request verification, discuss MiniMax findings, record decisions, and ask the user when needed.
- Codex subagent A: implement code, run local build/tests, report results, and apply fixes only from the main agent's final conclusion.
- Codex subagent B: generate or update the review package. It does not implement product code.
- MiniMax persistent ACP session: review the package, provide issues, suggestions, evidence, heartbeat, partial progress, and final reply through fixed files.

If subagent tooling is unavailable, stalled, or cannot be reused, report the limitation and ask before changing the execution model. Do not silently collapse roles into the main agent.

## Workspace Protocol

Create or maintain `.minimax-remix/` in the target workspace:

- `AGENTS.md`: MiniMax constraints for the project. This is durable and should be read once at session start.
- `codex-to-minimax.md`: Codex writes one review/discussion request, replacing the previous contents.
- `minimax-to-codex.md`: MiniMax writes one final response. Codex reads and deletes it after processing.
- `minimax-to-codex.partial.md`: MiniMax stage report every 5 minutes. It is not a final answer.
- `minimax-heartbeat.json`: MiniMax heartbeat every 2 minutes.
- `session-heartbeat.json`: controller heartbeat and file path metadata.
- `stop-session`: optional stop signal for the ACP controller.
- `archive/`: only for final records worth preserving; never use archive as a way to hide unresolved findings.

For templates and the full file contract, load `references/protocol.md`.

## Workflow

1. Clarify requirements and constraints. Ask the user about contradictions or decisions that affect architecture, installation, data loss, security, or fallback behavior.
2. Prepare `.minimax-remix/AGENTS.md` and the fixed communication files. Keep `AGENTS.md` durable and overwrite active turn files.
3. Assign subagent A to implement and verify. Reuse the same subagent for the project when practical. Do not repeatedly spawn replacements unless the previous subagent is done, closed, or blocked.
4. Assign subagent B to generate the review package from requirements, constraints, git diff, relevant source snippets, and verification output.
5. Start or reuse the persistent MiniMax ACP session, then send the review package through `codex-to-minimax.md`.
6. Wait for MiniMax's final `minimax-to-codex.md`. Use heartbeat and partial files only for stall detection and progress recovery.
7. Main Codex agent evaluates every MiniMax finding. Discuss with MiniMax when evidence is missing, wrong, or disputed.
8. After agreement or user decision, write a final modification conclusion. If code must change, assign subagent A to implement it.
9. Repeat package generation and MiniMax review after material code changes. There is no separate re-review shortcut; it is the same review step again.
10. Finish only after local verification passes and all MiniMax findings are either fixed, explicitly rejected with MiniMax agreement, or left for user decision.

## ACP Controller

Use the bundled controller for persistent MiniMax sessions:

```powershell
node <skill_dir>\scripts\mini-agent-acp-controller.mjs --workspace <workspace> --git-proxy http://127.0.0.1:10809 --heartbeat-ms 120000 --stage-report-ms 300000 --prompt-timeout-ms 1800000
```

The controller watches `.minimax-remix/codex-to-minimax.md`, sends new prompts to MiniMax ACP, writes controller heartbeat metadata, and keeps the session alive. It also injects git proxy environment variables when `--git-proxy` is supplied.

On Windows, tell MiniMax to run git through `cmd.exe`, for example:

```text
cmd.exe /c "git -C <workspace> status --short <nul"
```

Do not use PowerShell `< nul` syntax for MiniMax git commands.

## Completion Checklist

- Requirements and contradictions were handled by the main agent and user when needed.
- Subagent A made code changes; main agent did not patch MiniMax findings directly.
- Subagent B produced or updated the review package.
- MiniMax returned a final response through `minimax-to-codex.md`.
- Blocking and non-blocking findings were all resolved, deferred to the user, or closed by Codex and MiniMax agreement with evidence.
- Builds/tests/publish checks relevant to the task were run, or the user approved why they were not run.
- Active communication files were cleaned; durable records are small and intentional.
