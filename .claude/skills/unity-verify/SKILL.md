---
name: unity-verify
description: "Verify a PR branch or main in the local Unity editor through the Unity MCP: compile, run the EditMode tests, read the console, and post the result on the PR. Use when the user asks to check a PR in Unity, verify main locally, or run the local half of the project-owner loop."
argument-hint: "[PR number | main]  (no argument: every open PR labelled needs-unity, then main)"
disable-model-invocation: true
---

The cloud project-owner routine cannot run Unity, so this is the local check. It only works on the dev machine: the dev Unity project (see AGENTS.md, **Dev environment**) compiles `C:\Users\Azaz\AI\uv-wizard` through a `file:` link, and the Unity MCP must be connected. If the MCP tools are missing or Unity does not answer, say so and stop.

## Targets

- A PR number: that PR's head branch.
- `main`: `origin/main`.
- No argument: each open PR labelled `needs-unity` (`gh pr list --label needs-unity --json number,headRefName`), oldest first, then `main`.

## For each target

1. Record the current branch (`git branch --show-current`). Run `git status --porcelain`. If anything is listed, stop and report it: never stash or discard the user's work.
2. Never switch to a `prototype/*` branch. `git fetch origin`, then `git switch <headRefName>` and `git reset --hard origin/<headRefName>` only if that local branch has no unpushed commits (`git log origin/<headRefName>..<headRefName>` is empty; otherwise stop and report). For `main`, use `git switch --detach origin/main`. Say which branch Unity now compiles.
3. Call `refresh_unity`, wait for the compile, then `read_console` for errors. Compile errors fail the target. Report them and skip the tests.
4. `run_tests` with mode EditMode and every test assembly: `UVWizard.Core.Editor.Tests`, plus the SDK-gated ones that CI compiles out (`UVWizard.Core.Editor.Tests.VRChat`). Poll `get_test_job` until it finishes.
5. Post the result:
   - For a PR, use `gh pr comment <n>`. Start with `> *Local Unity check (unity-verify).*`, then: commit SHA, Unity version, pass/fail counts, each failing test with its message, and any console errors. On failure, first add `unity-failed` (`gh pr edit <n> --add-label unity-failed`; create it with `gh label create unity-failed --force` if missing). Then, in a separate command, remove `needs-unity` (`gh pr edit <n> --remove-label needs-unity`). Adding before removing keeps a hold label on the PR throughout, so the Merge gate never passes a PR that just failed. On success, only remove `needs-unity`.
   - For `main`, comment only on failure: open an issue titled `main fails in Unity: <short cause>` with labels `bug` and `needs-triage`.

## Afterwards

Switch back to the branch from step 1, `refresh_unity`, and finish with a table: target, SHA, result.

The test avatars in the dev project are paid assets. Never copy, commit or upload anything from them, including screenshots.
