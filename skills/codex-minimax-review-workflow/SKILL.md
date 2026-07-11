---
name: codex-minimax-review-workflow
description: Use when coordinating Codex and MiniMax on coding work that requires persistent sessions, fixed-file communication, independent review, no silent installs or fallbacks, and explicit user decisions for unresolved or deferred findings.
---

# Codex MiniMax Review Workflow

Codex remains the coordinator. Codex subagent A implements and verifies code. Codex subagent B prepares a factual review package. MiniMax independently reviews the package and may inspect the workspace when the package is insufficient. Code changes are made by subagent A only after the main Codex agent writes the task or final modification conclusion.

## Hard Rules

- Do not silently install anything, switch model/provider/tooling, use direct API fallback, skip verification, or replace a failed path with another path. Ask the user first.
- Do not let MiniMax modify product code in this workflow. MiniMax reviews and discusses only.
- Do not let the main Codex agent directly patch findings raised by MiniMax. The main agent may clarify, judge, discuss, and write conclusions. Implementation and fixes go to subagent A.
- Treat blocking and non-blocking MiniMax findings with the same decision loop. Non-blocking findings must not disappear into archives. Codex and MiniMax may close a finding only when they agree, with evidence, that no change is needed for this project. If a finding is valid but not fixed in this delivery, ask the user to approve deferral and record a concrete deadline, trigger, or next-start condition.
- If user requirements conflict, stop and ask. Do not resolve contradictory requirements silently.
- If MiniMax feedback is technically stronger than Codex's initial position, accept it. If the same disagreement cannot be resolved after 3 rounds, ask the user to decide.
- Active communication files are overwritten each turn. Read and then delete consumed active output files so context does not grow.

## Roles

- Codex main agent: clarify requirements, enforce constraints, assign subagents, run or request verification, check package completeness, discuss MiniMax findings, record decisions, and ask the user when needed.
- Codex subagent A: implement code, run local build/tests, report results, and apply fixes only from the main agent's task or final modification conclusion.
- Codex subagent B: generate and update a bounded factual review package. Include requirements, constraints, raw evidence, relevant context, and unresolved decisions without filtering findings or making review judgments.
- MiniMax persistent ACP session: independently review the package, inspect additional workspace files when needed, request missing evidence, and return findings, heartbeat, partial progress, and final reply through fixed files. It does not modify product code.

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
3. Assign or reuse subagent A to implement and verify. Do not spawn replacements silently when the existing subagent can continue.
4. Assign or reuse subagent B to generate the factual review package from the complete requirements, constraints, git state, diff, relevant source, verification output, and unresolved decisions.
5. Main Codex agent checks the package for required sections and obvious omissions. Return incomplete packages to subagent B.
6. Start or reuse the persistent MiniMax ACP session, then send the review package through `codex-to-minimax.md`. MiniMax may inspect additional workspace files instead of treating the package as a sealed boundary.
7. Wait for MiniMax's final `minimax-to-codex.md`. Use heartbeat and partial files only for stall detection and progress recovery.
8. Main Codex agent evaluates every MiniMax finding. Discuss with MiniMax when evidence is missing, wrong, or disputed.
9. After agreement or user decision, write a final modification conclusion. If code must change, assign subagent A to implement it.
10. Repeat subagent B package generation and MiniMax review after every material code change. There is no review shortcut.
11. Finish only after local verification passes and all MiniMax findings are fixed, closed by Codex and MiniMax agreement that no change is needed, or deferred with explicit user approval plus a recorded deadline, trigger, or next-start condition.

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
- Subagent A made code changes and ran relevant verification; the main agent did not patch MiniMax findings directly.
- Subagent B produced or updated a factual review package without making judgments or filtering evidence.
- MiniMax independently reviewed the package, could inspect source or request missing evidence, and returned a final response through `minimax-to-codex.md`.
- Blocking and non-blocking MiniMax findings were all resolved, closed by Codex and MiniMax agreement that no change is needed, or explicitly deferred by the user with a recorded deadline, trigger, or next-start condition.
- Builds/tests/publish checks relevant to the task were run, or the user approved why they were not run.
- Active communication files were cleaned; durable records are small and intentional.
