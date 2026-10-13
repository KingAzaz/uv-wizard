## Agent skills

### Issue tracker

Issues live in GitHub Issues for this repo, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The seven default triage labels, used as-is. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context. See `docs/agents/domain.md`.

### Project owner

Two local scheduled tasks develop UV Wizard unattended, and each starts the other:
- **"UV Wizard owner"** merges, reviews, triages, runs Unity checks, plans new work, and starts slice runs. Its playbook is `docs/agents/project-owner.md`. It merges only on green CI and never with `needs-unity` set.
- **"UV Wizard next slice"** fixes agent PRs and builds the next frontier ticket. It runs `.claude/skills/next-slice/SKILL.md` unattended, and you can run the same skill yourself with `/next-slice`.

Both stand down while another session in this repo is running or the checkout is dirty. Agent PRs carry the `owner-agent` label. Disable either task to pause the loop.

## Dev environment

- **Dev Unity project:** `%LOCALAPPDATA%\VRChatCreatorCompanion\VRChatProjects\uv-wizard` (Unity 2022.3.22f1, VRChat SDK 3.10.5, AAO 1.9.20, NDMF 1.14.8). It is not a git repo. Its `Packages/manifest.json` links this package with `"com.kingazaz.uv-wizard": "file:C:/Users/Azaz/AI/uv-wizard/Packages/com.kingazaz.uv-wizard"`, so Unity compiles whatever branch this checkout is on. Its test avatars (Rexouium, Furyon Raccoon) are paid assets: never copy them into this repo.
- **Tests:** the dev project's `Packages/manifest.json` lists `"testables": ["com.kingazaz.uv-wizard"]`. Without it, Unity doesn't compile a `file:`-linked package's test assemblies. Run them from the Test Runner, or through MCP `run_tests` (EditMode, assembly `UVWizard.Core.Editor.Tests`).
  - **Benchmarks** are menu commands under **Tools → UV Wizard → Benchmarks** (MCP `execute_menu_item`). They log to the console.
  - A run longer than a few seconds can drop the MCP call, which then retries it. Read the results from the console.
- **CI** (`.github/workflows/test.yml`) runs the EditMode tests in this repo's own Unity project, which has no VRChat SDK, NDMF or AAO.
  - Declare every Unity-registry package the code uses in `package.json` `dependencies`.
  - Put code and tests that need the SDK, NDMF or AAO in assemblies gated by `versionDefines` and `defineConstraints`, so CI compiles them out, and label those PRs `needs-unity`.
- **This checkout is Unity's.** A `git switch` here changes the code under the open editor. Do docs-only or other non-Unity work in a `git worktree` (e.g. `../uv-wizard-docs`).
- **Unity MCP:** MCP for Unity (CoplayDev) **v10.2.0**. The package is in the dev project's manifest.
  - Each agent has its own server config: Claude Code in `.mcp.json`, Codex in `.codex/config.toml`. Both are stdio, `mcpforunityserver==10.2.0` via uvx, with telemetry off.
  - Both configs hold machine-specific paths, so they're excluded locally via `.git/info/exclude`, not committed.
  - In Unity, **Window → MCP for Unity** uses the **stdio** transport. Edit the agent config files by hand: the window's **"Configure"** buttons overwrite them with unpinned entries.
  - MCP servers load only when a session starts.
  - After a script recompile, the bridge (port 6400) drops for a few seconds.
  - If a tool reports "No Unity Editor instances found" while Unity is running, read `mcpforunity://instances` and call `set_active_instance` again.
  - Only the `core` tool group is on by default. Use `execute_code` for reads.
- **Compile-check without the editor:** run Unity's own Roslyn (`<Editor>/Data/NetCoreRuntime/dotnet.exe <Editor>/Data/DotNetSdkRoslyn/csc.dll`) against:
  - `Editor/Data/Managed/UnityEngine/*.dll`
  - `NetStandard/ref/2.1.0/netstandard.dll`
  - the `netfx/` shims `mscorlib.dll`, `System.dll` and `System.Core.dll`
  - the dev project's `Library/ScriptAssemblies/<name>.dll` for each assembly the asmdef references (for the core: `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`)
  - for test assemblies, also the core DLL you just built and the dev project's `Library/PackageCache/com.unity.ext.nunit@*/net35/unity-custom/nunit.framework.dll`. Name the test output `UVWizard.Core.Editor.Tests.dll`, so the core's `InternalsVisibleTo` applies.

  Pass the arguments in a response file, because the paths contain spaces.
- **Prototypes** live on `prototype/*` branches and never merge into `main`. `main` keeps only the validated decision; for the packer, that's ADR 0002's Evidence section.
