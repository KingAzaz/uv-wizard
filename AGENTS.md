## Agent skills

### Issue tracker

Issues live in GitHub Issues for this repo, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The seven default triage labels, used as-is. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context. See `docs/agents/domain.md`.

### Project owner

Two local scheduled tasks develop UV Wizard unattended. A scheduled run can't start another scheduled task, so an interactive "UV Wizard conductor" session starts each one when the other finishes:
- **"UV Wizard owner"** merges, reviews, triages, runs Unity checks, plans new work, and starts slice runs. Its playbook is `docs/agents/project-owner.md`. Agent PRs merge themselves through GitHub auto-merge once CI is green. The required **Merge gate** check holds a PR while it has a hold label or a changes-requested review; `.github/scripts/merge_gate.py` has the list.
- **"UV Wizard next slice"** fixes agent PRs and builds the next frontier ticket. It runs `.claude/skills/next-slice/SKILL.md` unattended, and you can run the same skill yourself with `/next-slice`.

Both stand down while another session in this repo is running, another run holds the **checkout lease**, or the checkout is dirty. Agent PRs carry the `owner-agent` label. Disable either task to pause the loop.

**Checkout lease.** A run that switches branches or runs Unity in `C:\Users\Azaz\AI\uv-wizard` holds the lease from then until it ends, because `list_sessions` reports a run waiting on sub-agents as `isRunning: false`. Run `.github/scripts/checkout_lease.py` from `origin/main`, since the checkout may be on an older branch: `MSYS_NO_PATHCONV=1 git -C C:/Users/Azaz/AI/uv-wizard show origin/main:.github/scripts/checkout_lease.py | python - <take|check|release> <session id> [<title>]`. Get the session id from `get_session("self")`. Exit 1 means another run holds it: stand down and quote its output. The holder's `take` renews it; a lease not renewed for 3 hours is stale, and the next `take` replaces it. Release it however the run ends, including "nothing to do".

## Dev environment

- **Dev Unity project:** `%LOCALAPPDATA%\VRChatCreatorCompanion\VRChatProjects\uv-wizard` (Unity 2022.3.22f1, VRChat SDK 3.10.5, AAO 1.9.20, NDMF 1.14.8). It is not a git repo. Its `Packages/manifest.json` links this package with `"com.kingazaz.uv-wizard": "file:C:/Users/Azaz/AI/uv-wizard/Packages/com.kingazaz.uv-wizard"`, so Unity compiles whatever branch this checkout is on. Its test avatars (Rexouium, Furyon Raccoon) are paid assets: never copy them into this repo.
- **Tests:** the dev project's `Packages/manifest.json` lists `"testables": ["com.kingazaz.uv-wizard"]`. Without it, Unity doesn't compile a `file:`-linked package's test assemblies. Run them from the Test Runner, or through MCP `run_tests` (EditMode, every test assembly: `find Packages/com.kingazaz.uv-wizard/Tests -name '*.asmdef'`). Those gated on the VRChat SDK, Poiyomi Toon, lilToon or NDMF compile out in CI.
  - **From the shell:** `sh tools/unity-tests.sh` refreshes, recompiles, runs every test assembly in the open editor and prints the failing and Inconclusive tests. `-g REGEX` picks a group, `--no-refresh` and `--refresh-only` split the two halves. It exits 1 on a failing or Inconclusive test (CI fails on both), a compile error or no tests run, and 2 when it can't reach the editor. It talks to the MCP bridge directly, and each of its requests takes the bridge from the MCP server, so an MCP call in flight loses its reply: make no MCP calls while it runs. `python -m unittest discover -s tools` tests it without Unity.
    - `mutate.sh` reads any non-zero TEST exit as red. A mutation whose output says `couldn't run the tests` (exit 2) proved nothing: rerun it.
  - `run_tests` ANDs `test_names` with `group_names`. Select tests with one `group_names` regex (e.g. `Ns\.Class\.(TestA|TestB)`), and count a `total: 0` result as a failed run.
  - **Benchmarks** are menu commands under **Tools → UV Wizard → Benchmarks** (MCP `execute_menu_item`). They log to the console.
  - A run longer than a few seconds can drop the MCP call, which then retries it, and almost every `get_test_job` poll loses the bridge. Wait instead until the count of `Restored Interaction Mode after test run` in `%LOCALAPPDATA%\Unity\Editor\Editor.log` rises (`grep -c` only: the file is over 1 GB), then call `set_active_instance` and `get_test_job` once. Wait with a Bash `until` loop run with `run_in_background`: the harness blocks a foreground `sleep`.
- **CI** (`.github/workflows/test.yml`) runs the EditMode tests in this repo's own Unity project, which has no VRChat SDK, NDMF or AAO. Like VRChat projects, it uses Linear colour space, so colour tests mean the same as on real avatars.
  - Declare every Unity-registry package the code uses in `package.json` `dependencies`.
  - Put code and tests that need the SDK, NDMF or AAO in assemblies gated by `versionDefines` and `defineConstraints`, so CI compiles them out, and label those PRs `needs-unity`.
  - CI's Linux GPU differs from the Windows editor's, so a test can pass locally and fail in CI:
    - It can't create compressed `Texture3D`s (ASTC, for example). Build 3D textures in tests with an uncompressed format.
    - It samples a bake about a quarter texel away. Keep a colour test's sample points over a texel from any sharp edge in its source texture, such as a Repeat gradient's seam.
    - An `Assume.That` that holds in the editor can fail there (#138's mesh metrics did). The test then reads Inconclusive, and the test runner fails the job on it. The job summary lists each Inconclusive test with its message.
  - CI runs on Linux as root, so a read-only attribute or permission bit stops no delete or write there. A test that needs a file it can't delete holds only on Windows (#175's eviction test): make its assertions and cleanup hold either way.
- **This checkout is Unity's.** A `git switch` here changes the code under the open editor. Do docs-only or other non-Unity work in a `git worktree` (e.g. `../uv-wizard-docs`).
- **Unity MCP:** MCP for Unity (CoplayDev) **v10.3.0**. The package is in the dev project's manifest.
  - Each agent has its own server config: Claude Code in `.mcp.json`, Codex in `.codex/config.toml`. Both are stdio, `mcpforunityserver==10.3.0` via uvx, with telemetry off, and pass `--default-instance uv-wizard`.
  - A second editor, KingAzaz's `World` project, is often open too, and each editor takes its own bridge port (`World` may hold 6400). The server's default instance keeps calls on the dev project. Pass `set_active_instance` the `uv-wizard` instance's full `id` from `mcpforunity://instances` (`uv-wizard@<hash>`): it reads a bare `uv-wizard` as a hash and rejects it. `tools/unity_bridge.py` picks the port from the `uv-wizard` status file in `~/.unity-mcp/`.
  - Both configs hold machine-specific paths, so they're excluded locally via `.git/info/exclude`, not committed.
  - In Unity, **Window → MCP for Unity** uses the **stdio** transport. Edit the agent config files by hand: the window's **"Configure"** buttons overwrite them with unpinned entries.
  - MCP servers load only when a session starts.
  - After a script recompile, the bridge drops for a few seconds.
  - `refresh_unity` can report the editor ready before the recompile starts, or without compiling at all. After editing scripts, touch a marker file, refresh, and wait until the DLL of the assembly you edited (not its test assembly, which an internals-only change doesn't rebuild) in the dev project's `Library/ScriptAssemblies/` is newer than the marker. Then call `set_active_instance` again and run tests. Otherwise they fail with "No Unity Editor instances found" or run against the old code.
  - A file restored from a copy (a mutation-check backup, `git stash pop`, `cp -p`) can keep a timestamp Unity's refresh skips. `touch` it before `refresh_unity`, or the tests run the old compiled code.
  - If a tool reports "No Unity Editor instances found" while Unity is running, read `mcpforunity://instances` and call `set_active_instance` again.
  - A modal dialog in the editor makes every MCP call time out, while `mcpforunity://instances` still lists the editor. Entering Play Mode in the dev scene opens one easily: NDMF's apply-on-play runs the VRChat upload callbacks on both test avatars, and Poiyomi's Thry once opened a first-run prompt there that only KingAzaz could answer (#134). For a Play Mode check, open a new empty scene first, or write a PlayMode test, and leave Play Mode before the run ends: the next run found the editor still playing.
  - Only the `core` tool group is on by default. Use `execute_code` for reads. It compiles C# 6 (CodeDom): no local functions, tuples or switch expressions, and write `UnityEngine.Object` in full.
- **Compile-check without the editor:** run Unity's own Roslyn (`<Editor>/Data/NetCoreRuntime/dotnet.exe <Editor>/Data/DotNetSdkRoslyn/csc.dll`) against:
  - `Editor/Data/Managed/UnityEngine/*.dll`
  - `NetStandard/ref/2.1.0/netstandard.dll`
  - the `netfx/` shims `mscorlib.dll`, `System.dll` and `System.Core.dll`
  - the dev project's `Library/ScriptAssemblies/<name>.dll` for each assembly the asmdef references (for the core: `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`)
  - for test assemblies, also the core DLL you just built and the dev project's `Library/PackageCache/com.unity.ext.nunit@*/net35/unity-custom/nunit.framework.dll`. Name the test output `UVWizard.Core.Editor.Tests.dll`, so the core's `InternalsVisibleTo` applies.

  Pass the arguments in a response file, because the paths contain spaces.
- **Prototypes** live on `prototype/*` branches and never merge into `main`. `main` keeps only the validated decision; for the packer, that's ADR 0002's Evidence section.
