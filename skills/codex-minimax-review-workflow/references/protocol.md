# Codex MiniMax Review Protocol

This file contains the detailed protocol for the Codex MiniMax review workflow.

## Durable MiniMax Constraints Template

Create `.minimax-remix/AGENTS.md` in each project workspace:

```markdown
# MiniMax Worker Constraints

You are the MiniMax review worker for this workspace.

## Role

- Review only. Do not modify files unless Codex explicitly changes this project mode with user approval.
- Communicate with Codex only through `.minimax-remix/minimax-to-codex.md`, `.minimax-remix/minimax-to-codex.partial.md`, and `.minimax-remix/minimax-heartbeat.json`.
- Read `.minimax-remix/codex-to-minimax.md` for each task.

## No Silent Fallbacks

- Do not install SDKs, runtimes, packages, global tools, or system components.
- Do not switch tools, models, providers, build targets, transport, test strategy, or implementation scope without Codex/user approval.
- Do not mock, stub, skip tests, or degrade behavior as a fallback without Codex/user approval.
- If blocked by dependency, runtime, permission, network, proxy, missing context, or contradictory requirements, write the blocker to `.minimax-remix/minimax-to-codex.md`.

## Progress Files

- Refresh `.minimax-remix/minimax-heartbeat.json` at least every 2 minutes while working.
- Write `.minimax-remix/minimax-to-codex.partial.md` at least every 5 minutes with current stage, completed work, current uncertainty, and recoverable findings.
- Write the final answer to `.minimax-remix/minimax-to-codex.md`.

## Windows Git

- Use `cmd.exe /c "git -C <workspace> ... <nul"` for git commands that might prompt.
- Use the proxy supplied by the ACP controller environment.
```

## Codex To MiniMax Request Template

Overwrite `.minimax-remix/codex-to-minimax.md` for each MiniMax turn:

```markdown
# MiniMax Review Request

## Task

<What MiniMax should review or discuss.>

## User Requirements

- <Requirement 1>
- <Requirement 2>

## Hard Constraints

- MiniMax reviews only.
- No silent installs.
- No silent fallback.
- Findings, including non-blocking findings, must remain visible until resolved by agreement or user decision.

## Review Package

### Git State

```text
<branch, status, relevant commits>
```

### Diff Summary

```diff
<bounded relevant diff>
```

### Relevant Files

```text
<paths and focused snippets, not the entire repo>
```

### Verification

```text
<commands run and output summary>
```

## Questions For MiniMax

1. Identify blocking correctness, build, runtime, data-loss, security, or requirement gaps.
2. Identify non-blocking UX, maintainability, or documentation findings that should be left for user decision if not fixed.
3. Provide evidence for each finding.
4. If you disagree with Codex's prior judgment, explain the disagreement narrowly.
```

## MiniMax Final Response Contract

MiniMax writes `.minimax-remix/minimax-to-codex.md`:

```markdown
# MiniMax Final Response

status: approved | changes_requested | blocked

## Blocking Findings

- id: MM-BLOCK-001
  severity: blocking
  file: <path or n/a>
  evidence: <line, behavior, command, or reasoning>
  recommendation: <specific change or decision>

## Non-Blocking Findings For User Decision

- id: MM-NB-001
  severity: non-blocking
  evidence: <why this matters>
  recommendation: <possible change>
  can_ship_without_fix: yes | no | uncertain

## Disagreements With Codex

- id: MM-DISPUTE-001
  Codex position: <summary>
  MiniMax position: <summary>
  evidence_needed: <what would settle it>

## Verification Notes

- <Commands or checks MiniMax inspected, or why it could not inspect them>

## Final Recommendation

<Ship, fix listed items, ask user, or provide missing evidence.>
```

## Finding Decision Loop

Use the same loop for blocking and non-blocking findings:

1. Main Codex agent reads MiniMax final output and deletes the active final file.
2. If the finding is clear and valid, main writes a final conclusion and assigns subagent A.
3. If the finding is unclear, main asks MiniMax for narrower evidence through `codex-to-minimax.md`.
4. If Codex disagrees, main sends evidence or counterargument to MiniMax.
5. If Codex and MiniMax agree no change is needed, record the reason in the decision log.
6. If the same dispute repeats for 3 rounds, ask the user to decide.
7. If the user decides, record the decision and assign subagent A only if a code change is needed.

## Decision Log Template

Keep unresolved or intentionally deferred findings visible in the final handoff, or in `.minimax-remix/review-decisions.md` if the project needs a local record:

```markdown
# Review Decisions

## Fixed

- <finding id>: <what changed, commit or file reference, verification>

## Closed By Codex And MiniMax Agreement

- <finding id>: <why no change is needed, evidence>

## User Decisions

- <finding id>: <user decision, date, resulting action>

## Still Optional Or Deferred

- <finding id>: <current tradeoff and why it remains visible>
```

## Subagent Lifecycle

- Prefer one implementation subagent A and one review-package subagent B for a project.
- Reuse them until their work is complete, they explicitly finish, or they are blocked.
- Do not spawn replacement subagents silently to hide context loss.
- If a subagent is unavailable or the tool cannot provide persistent communication, report that limitation and ask before changing the workflow.
- The main agent may inspect files and run verification. It must not directly edit product code to resolve MiniMax findings.

## Review Package Checklist

- User requirements and latest clarifications.
- Explicit constraints, including no silent install and no silent fallback.
- Git branch, clean/dirty status, relevant commits.
- Focused diff or snippets for changed files.
- Relevant config files and project files.
- Build/test/publish commands and result summaries.
- Known limitations and unresolved user decisions.

## Cleanup

- After Codex reads `.minimax-remix/minimax-to-codex.md`, delete it.
- Keep `minimax-to-codex.partial.md` only while the MiniMax turn is active.
- Keep heartbeat files small JSON objects.
- Archive only durable summaries or final review artifacts.
- Add generated active communication files to `.gitignore` unless the user wants them versioned.
