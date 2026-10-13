## Agent skills

### Issue tracker

Issues live in GitHub Issues for this repo, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The seven default triage labels, used as-is. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context. See `docs/agents/domain.md`.

### Project owner

A scheduled cloud routine works through the v1 plan unattended. Its playbook is `docs/agents/project-owner.md`. It merges only on green CI and never with `needs-unity` set; clear that label by running `/unity-verify` locally.

## Dev environment

- **Dev Unity project:** `%LOCALAPPDATA%\VRChatCreatorCompanion\VRChatProjects\uv-wizard` (Unity 2022.3.22f1, VRChat SDK 3.10.5, AAO 1.9.20, NDMF 1.14.8). It is not a git repo. Its `Packages/manifest.json` links this package with `"com.kingazaz.uv-wizard": "file:C:/Users/Azaz/AI/uv-wizard/Packages/com.kingazaz.uv-wizard"`, so Unity compiles whatever branch this checkout is on. Its test avatars (Rexouium, Furyon Raccoon) are paid assets: never copy them into this repo.
- **This checkout is Unity's.** A `git switch` here changes the code under the open editor. Do docs-only or other non-Unity work in a `git worktree` (e.g. `../uv-wizard-docs`).
- **Unity MCP:** MCP for Unity (CoplayDev) **v10.2.0**. The package is in the dev project's manifest; the server is in this repo's `.mcp.json` (stdio, `mcpforunityserver==10.2.0` via uvx, telemetry off). `.mcp.json` has a machine-specific path, so it is excluded locally via `.git/info/exclude`, not committed.
  - In Unity, **Window → MCP for Unity** uses the **stdio** transport. **Don't click "Configure" for Claude Code there**: it overwrites `.mcp.json` with an unpinned entry.
  - MCP servers load only when a session starts.
  - After a script recompile, the bridge (port 6400) drops for a few seconds.
  - Only the `core` tool group is on by default. Use `execute_code` for reads and benchmarks.
- **Compile-check without the editor:** run Unity's own Roslyn (`<Editor>/Data/NetCoreRuntime/dotnet.exe <Editor>/Data/DotNetSdkRoslyn/csc.dll`) against:
  - `Editor/Data/Managed/UnityEngine/*.dll`
  - `NetStandard/ref/2.1.0/netstandard.dll`
  - the `netfx/mscorlib.dll` shim
  - the dev project's `Library/ScriptAssemblies/Unity.Burst.dll`

  Pass the arguments in a response file, because the paths contain spaces.
- **Prototypes** live on `prototype/*` branches and never merge into `main`. `main` keeps only the validated decision; for the packer, that's ADR 0002's Evidence section.
