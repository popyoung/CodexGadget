# Codex MiniMax Review Protocol

This file contains the detailed protocol where Codex subagent A implements, Codex subagent B prepares factual evidence, and MiniMax reviews independently.

## Durable MiniMax Constraints Template

Create `.minimax-remix/AGENTS.md` in each project workspace:

```markdown
# MiniMax Worker Constraints

You are the MiniMax review worker for this workspace.

## Role

- Review only. Do not modify product files unless the user explicitly approves a workflow change.
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

- MiniMax reviews only and does not modify product code.
- No silent installs.
- No silent fallback.
- Findings, including non-blocking findings, remain visible until resolved by agreement or user decision.
- Treat this package as a bounded starting point, not a sealed boundary. Inspect additional workspace files when needed and request missing evidence explicitly.

## Review Package Prepared By Codex Subagent B

Subagent B supplies facts, not conclusions. It must not omit evidence because it appears unimportant or make a preliminary quality judgment.

### Requirements And Clarifications

```text
<complete current requirements, preserving user wording where practical>
```

### Constraints And Decisions

```text
<hard constraints, unresolved decisions, and user-approved deferrals>
```

### Git State

```text
<branch, status, relevant commits>
```

### Diff Summary

```diff
<bounded raw relevant diff>
```

### Relevant Files

```text
<paths and focused context needed to understand the changed behavior>
```

### Verification Evidence

```text
<commands run, exact result summaries, warnings, failures, and unrun checks>
```

### Implementation Report

```text
<subagent A changed files, design notes, known uncertainty, and limitations>
```

## Questions For MiniMax

1. Check whether the package is sufficient and internally consistent. Request missing evidence instead of assuming.
2. Identify blocking correctness, build, runtime, data-loss, security, or requirement gaps.
3. Identify non-blocking UX, maintainability, performance, test, or documentation findings.
4. Provide concrete evidence for every finding.
5. Inspect additional workspace files when they materially affect confidence.
6. If you disagree with Codex's prior judgment, explain the disagreement narrowly with evidence.
```

## MiniMax Final Response Contract

MiniMax writes `.minimax-remix/minimax-to-codex.md`:

```markdown
# MiniMax Final Response

status: approved | changes_requested | blocked | needs_evidence

## Package Sufficiency

- sufficient: yes | no
- missing evidence: <items or none>

## Blocking Findings

- id: MM-BLOCK-001
  file: <path or n/a>
  evidence: <line, behavior, command, or reasoning>
  recommendation: <specific change or decision>

## Non-Blocking Findings

- id: MM-NB-001
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

<Approve, fix listed items, ask user, or provide missing evidence.>
```

## Finding Decision Loop

Use the same loop for blocking and non-blocking MiniMax findings:

1. Main Codex agent reads MiniMax final output and deletes the active final file.
2. If MiniMax needs evidence, main assigns subagent B to update the package, checks it, and sends it back to the same MiniMax session.
3. If a finding is clear and valid, main writes a final conclusion and assigns subagent A.
4. If a finding is unclear, main requests narrower evidence from MiniMax or inspects the cited source.
5. If Codex disagrees, main sends evidence or a counterargument to MiniMax.
6. If Codex and MiniMax agree no change is needed for this project, record the reason in the decision log.
7. If the same dispute repeats for 3 rounds, ask the user to decide.
8. If the user decides, record the decision and assign subagent A only if a code change is needed.

Closure and deferral are different decisions:

- Close a finding only when Codex and MiniMax agree that no change is needed, or the user decides no change is needed.
- Defer a valid finding only after explicit user approval. A deferral must record a deadline, trigger, or next-start condition, such as "before v1.0", "next feature round", "if audio quality complaints occur", or an exact date.
- If a valid non-blocking finding is not closed and the user has not approved deferral, do not finish by treating it as silently postponed. Either fix it through subagent A or ask the user for the deferral decision.

## Decision Log Template

Keep unresolved or intentionally deferred findings visible in the final handoff, or in `.minimax-remix/review-decisions.md` if the project needs a local record:

```markdown
# Review Decisions

## Fixed

- <finding id>: <what subagent A changed, commit or file reference, verification>

## Closed By Codex And MiniMax Agreement

- <finding id>: <why no change is needed, evidence>

## Deferred With User Approval

- <finding id>: <user-approved reason for deferral, deadline or trigger, owner/next action>

## User Decisions

- <finding id>: <user decision, date, resulting action>

## Still Optional Or Deferred

- <finding id>: <current tradeoff and why it remains visible; this section must not contain valid work that has been silently postponed without user-approved timing or trigger>
```

## Subagent Lifecycle

- Prefer one implementation subagent A and one review-package subagent B for a project.
- Reuse them until their work is complete, they explicitly finish, or they are blocked.
- Do not spawn replacement subagents silently to hide context loss.
- If a subagent is unavailable or the tool cannot provide persistent communication, report that limitation and ask before changing the workflow.
- The main agent may inspect files and run verification. It must not directly edit product code to resolve MiniMax findings unless the user explicitly changes the workflow.

## Codex Subagent B Review Package Contract

Subagent B always generates or updates the package before MiniMax review. The package is an evidence index that saves MiniMax tokens; it is not a substitute for MiniMax's independent judgment.

- User requirements and latest clarifications.
- Explicit constraints, including no silent install and no silent fallback.
- Git branch, clean/dirty status, relevant commits.
- Focused diff or snippets for changed files.
- Relevant config files and project files.
- Build/test/publish commands and result summaries.
- Known limitations, unresolved user decisions, and user-approved deferral timing or trigger for any valid finding that is not fixed now.
- Subagent A implementation report and any uncertainty.

Subagent B must preserve contradictory or suspicious evidence instead of resolving it. MiniMax may inspect any relevant workspace file and may return `needs_evidence`; subagent B then supplements the package rather than MiniMax rebuilding the entire context itself.

## Cleanup

- After Codex reads `.minimax-remix/minimax-to-codex.md`, delete it.
- Keep `minimax-to-codex.partial.md` only while the MiniMax turn is active.
- Keep heartbeat files small JSON objects.
- Archive only durable summaries or final review artifacts.
- Add generated active communication files to `.gitignore` unless the user wants them versioned.
