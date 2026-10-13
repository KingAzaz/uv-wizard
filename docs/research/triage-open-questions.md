# Triage research: open questions for #13, #19, #24 and the Toon Standard output

Date: 2026-10-13. Scope: five questions raised while triaging the v1 issues (Poiyomi locking, AAO mesh merging, NDMF platforms, Toon Standard properties, texture memory).

Labels, as in [tech-stack.md](tech-stack.md):

- **Fact** is something I observed in a primary source, with a link or a local path and line.
- **Inference** is my reasoning from those facts. It has not been verified by running anything.
- **Rec** is a recommendation.

How sources were read:

- NDMF 1.14.8, AAO 1.9.20 and VRChat SDK 3.10.5 come from the dev project's packages. `Packages/` below means `%LOCALAPPDATA%\VRChatCreatorCompanion\VRChatProjects\uv-wizard\Packages\`. The NDMF and AAO files cited are byte-identical to the GitHub tags `1.14.8` and `v1.9.20` (checked with `git hash-object`), so links point at those tags.
- SDK code that ships only as DLLs (`VRCSDKBase.dll`, `VRCSDKBase-Editor.dll`, `VRCSDK3A-Editor.dll`) was read by reflection and IL disassembly in the open editor, read-only. Those citations name a type and method, not a line.
- Poiyomi Toon v10.0.25 (tag, commit `f275eef`) and ThryEditor 2.74.3 (`poiyomi/ThryEditor` tag, commit `16aa9d3`) were read from GitHub.
- Nothing was built or uploaded.

---

## Summary

1. **Poiyomi (#13):** UV Wizard's Optimizing pass runs at SDK callback order −1025. Poiyomi auto-locks at +100, and never in play mode or manual bake. So UV Wizard always sees materials as saved on disk: unlocked, or locked by hand.
2. **AAO (#19):** Only Trace and Optimize (T&O) merges skinned meshes automatically. AAO documents that tools may add T&O with default settings. AAO reads it when its own Optimizing pass runs, so a T&O added earlier in Optimizing is honoured. T&O never merges static meshes, or meshes with different toggles, bounds or root bones.
3. **Platforms (#24):** NDMF has one "VRChat" platform with no Android/PC split. Plugins must read the active build target. `AvatarProcessor.ManualProcessAvatar(obj, platform)` is public. An "Android" bake in a Windows project gets PC texture formats from other plugins and skips the SDK's mobile checks.
4. **Toon Standard (#16):** `_MainTex` and `_Color` on UV0. `_BumpMap` and `_BumpScale` on UV0, behind the local keyword `USE_NORMAL_MAPS`. Emission (`_EmissionMap` × `_EmissionColor` × `_EmissionStrength`) is always compiled, on UV0 or UV1 via `_EmissionUV`. Features are plain local keywords.
5. **Texture memory (#21):** every unique texture on every material of every renderer (inactive ones included), sized from its GPU format with the full mip chain, in MiB. Android limits are 10 / 18 / 25 / 40 MB. Material slots are 1 / 1 / 2 / 4, but the SDK counts submeshes, not materials.

Things that could change the plan:

- An Android upload validates shaders on the *processed* avatar. An excluded material left on a renderer with lilToon or Poiyomi fails the upload (§3).
- A 2048² atlas with Albedo and Emission at ASTC 6×6 and Normal at ASTC 4×4, mips included, is 10.08 MB. That is just over Excellent (§5).
- T&O can't reach one skinned mesh on avatars with toggled outfits or static meshes (§2).
- Toon Standard already has a stub for an official atlased variant (§4).

---

## 1. Poiyomi locking vs NDMF (#13)

**Fact (where locking lives):** Poiyomi v10.0.25 has no locking code. Its `package.json` declares `"com.poiyomi.thryeditor": ">=2.74.3"` ([package.json](https://github.com/poiyomi/PoiyomiToonShader/blob/v10.0.25/package.json)). Locking is ThryEditor's `Thry.ThryEditor.ShaderOptimizer`.

**Fact (when it locks):**

- Avatars: `ShaderOptimizer.LockMaterialsOnUpload : IVRCSDKPreprocessAvatarCallback`, `callbackOrder => 100`. It returns at once if `Application.isPlaying`. Otherwise it locks the materials of every renderer on the avatar it is given (inactive included), plus materials in `m_Materials` curves of the descriptor's base layers and of any Animator's controller. It always returns `true` ([ShaderOptimizer.cs#L3213-L3245](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L3213-L3245)).
- `LockMaterialsOnWorldUpload : IVRCSDKBuildRequestedCallback` (order 100) acts only for `VRCSDKRequestedBuildType.Scene`, so it does nothing for avatars ([#L3249-L3273](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L3249-L3273)).
- `StripUnlockedShadersFromBuild : IPreprocessShaders` strips unlocked ThryEditor shaders from any build ([#L3325-L3358](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L3325-L3358)).
- ThryEditor's `Config` has no play-mode lock option (`Editor/Config.cs`, searched for "lock" and "play").

**Fact (the SDK's order):** `VRCBuildPipelineCallbacks.OnPreprocessAvatar` sorts callbacks with `Enumerable.OrderBy` on `callbackOrder`, so lower runs first (`VRCSDKBase-Editor.dll`). In the dev project the registered avatar callbacks are: NDMF main −11000, `PreprocessCallbackBehaviours` −2048, NDMF optimize −1025, `RemoveAvatarEditorOnly` and `AssignAvatarNetworkIDs` −1024, AAO's `DetectPlayerBuildErrorUnityBug` 0, NDMF's `ForceReinit*` `int.MaxValue`.

**Fact (NDMF's hooks):** The −11000 hook runs phases `First` to `Transforming`. The −1025 hook runs `Optimizing` to `Last` ([BuildFrameworkPreprocessHook.cs#L29-L50](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/VRChat/BuildFrameworkPreprocessHook.cs#L29-L50), [#L65-L84](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/VRChat/BuildFrameworkPreprocessHook.cs#L65-L84)). So UV Wizard's Optimizing pass runs at −1025, not at −11000 as issue #13 assumes.

**Fact (play mode and manual bake):**

- Apply on Play: for an avatar with a `VRCAvatarDescriptor`, NDMF calls `VRCBuildPipelineCallbacks.OnPreprocessAvatar`, so every SDK callback runs, Poiyomi's included ([ApplyOnPlay.cs#L82-L98](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/ApplyOnPlay.cs#L82-L98)). Poiyomi's callback then returns because `isPlaying` is true.
- Manual bake calls `AvatarProcessor.ManualProcessAvatar`, which runs only NDMF passes and no SDK callbacks ([Menus.cs#L46-L62](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/UI/Menus.cs#L46-L62), [AvatarProcessor.cs#L108-L140](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/AvatarProcessor.cs#L108-L140)).

**Fact (no integration either way):** ThryEditor 2.74.3 and Poiyomi 10.0.25 contain no reference to NDMF, Modular Avatar or AAO. NDMF 1.14.8 has no reference to Poiyomi or Thry. AAO knows only Poiyomi's UDIM-discard properties (`Editor/ShaderKnowledge.cs` L19, L63). Poiyomi's [locking docs](https://www.poiyomi.com/general/locking) (updated 2026-09-22) mention only build and upload. A web search found nothing more.

| Context | Does Poiyomi lock? | What UV Wizard's Optimizing pass sees |
|---|---|---|
| Upload or Build & Test | Yes, at +100, after UV Wizard (−1025) | Materials as saved: unlocked, unless the creator locked them |
| Play mode (Apply on Play) | Callback runs, returns at once | As saved |
| Manual bake | Callback never runs | As saved |

**Fact (locked materials exist in practice):** The Furyon test avatar ships two locked Poiyomi 9.x materials. Their shaders are `Hidden/Locked/.poiyomi/Poiyomi Toon/<32 hex>` and `…/Poiyomi Toon World/<32 hex>`, stored in `OptimizedShaders/<material>/` beside the material (dev project `Assets/Furyon Avatar/Materials/`).

**Fact (what a locked material looks like, ThryEditor 2.74.3):**

- **Shader name** is `Hidden/Locked/<original shader name>/<hash>`, filed under `Assets/_LockedShaderCache/` ([LockedShaderCache.cs#L24-L46](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/LockedShaderCache.cs#L24-L46)). Since 2.73.0 the hash is a content hash, so materials with the same settings share one shader (ThryEditor `CHANGELOG.md`, 2.73.0). Older locks used a per-material folder, as on Furyon.
- **Lock test:** the locked shader's optimizer property (the one drawn with `[ThryShaderOptimizerLockButton]`, `_ShaderOptimizerEnabled` in Poiyomi) has default value 1 ([#L1685-L1687](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1685-L1687), [#L3442-L3450](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L3442-L3450)).
- **Tags written on the material:** `OriginalShader`, `OriginalShaderGUID`, `thry_locked_rename_suffix`, and `OriginalKeywords`. All shader keywords are then disabled ([#L1916-L1921](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1916-L1921), [#L1979-L1982](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1979-L1982)).
- **Constants:** every non-animated colour, vector, float, range and int property becomes a constant in the code. So do `<tex>_ST` and `<tex>_TexelSize`. A property whose tag `<prop>Animated` is set stays a uniform ([#L1462-L1579](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1462-L1579)). A property is exempt if its display name ends in `NL` or it has `DoNotLock` ([#L777-L784](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L777-L784)).
- **Renamed properties:** tag value `"2"` renames `<prop>` to `<prop>_<suffix>` throughout the shader text, including `sampler<prop>`, `<prop>_ST` and `<prop>_TexelSize`. The value is then copied to the new name ([#L1362-L1391](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1362-L1391), [#L1984-L1989](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1984-L1989)).
  - Names ending in `UV` or `Pan` are never renamed ([#L1376](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1376)).
  - 19 Unity "magic" names (`_MainTex`, `_Color`, `_EmissionColor`, `_BumpScale`, …) are duplicated rather than renamed, so both names exist ([#L281-L302](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L281-L302), [#L1649-L1665](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1649-L1665)).
- **Suffix:** the `thry_rename_suffix` tag, or else the material name. Spaces are removed and other non-word bytes are hex-encoded ([#L708-L755](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L708-L755)). **Contrary:** Poiyomi's docs say "Body Mat" becomes `_Body_Mat`, but the code removes the space.
- **Disabled features:** code and property declarations inside `//ifex` blocks that are true are dropped. Textures declared there are removed from the material, and their GUIDs are kept in `_stripped_tex_<prop>` tags ([#L2074-L2137](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L2074-L2137), [#L1931-L1961](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L1931-L1961)). Locking deletes only texture entries, never float, colour or vector values.
- **Test API:** `ShaderOptimizer.LockMaterials(IEnumerable<Material>, ProgressBar = None)` and `UnlockMaterials(…)` are public ([#L481-L493](https://github.com/poiyomi/ThryEditor/blob/2.74.3/Editor/ShaderOptimizer.cs#L481-L493)).

**Inference (what is readable on a locked material):**

- For enabled features, `GetTexture`, `GetColor`, `GetFloat` and `GetTextureScale`/`GetTextureOffset` on the original names return the creator's values, because the Properties block keeps them.
- For renamed properties, read `<prop>_<suffix>` instead.
- Keyword checks return false on locked materials. Use Poiyomi's float toggles, or the `OriginalKeywords` tag, instead.
- Textures of disabled features are gone. That is harmless, because the feature is off.
- **Not established:** what `Material.GetFloat` returns for a name the locked shader no longer declares. I did not run it.

**Rec:**

- Decide "locked" from the optimizer property's default value, with the `Hidden/Locked/` prefix as a fallback.
- Resolve each property name: if `HasProperty(name)` is false and the tag `nameAnimated` is `"2"`, read `name + "_" + suffix`. Take the suffix from `thry_locked_rename_suffix`, then `thry_rename_suffix`. Don't derive it from `material.name`, because NDMF clones can carry new names.
- Never rely on keywords for Poiyomi.
- No ordering constraint against Poiyomi is needed.
- **What would change this:** a ThryEditor release that locks below −1025 (for example from an NDMF pass), or a creator tool that calls `LockMaterials` during the build.

**For #13:** The open question is answered: UV Wizard runs before Poiyomi's auto-lock in every context, and play mode and manual bake never lock. The reader must still handle locked materials, because creators lock by hand and avatars ship locked. Test unlocked, hand-locked with renamed properties, and old 9.x-style locks (the Furyon shaders).

---

## 2. AAO mesh merging (#19)

**Fact (what merges skinned meshes):**

- Automatic merging is T&O's `AutoMergeSkinnedMesh`, followed by `MergeMaterialSlots` ([OptimizerPlugin.cs#L117-L118](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/OptimizerPlugin.cs#L117-L118)). Both are driven by flags loaded from a `TraceAndOptimize` component ([TraceAndOptimizeProcessor.cs#L81-L123](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Internal/TraceAndOptimizeBase/TraceAndOptimizeProcessor.cs#L81-L123)).
- The only manual route is the `MergeSkinnedMesh` component.
- So automatic merging requires T&O, an `AvatarGlobalComponent` meant for the avatar root ([T&O docs](https://vpm.anatawa12.com/avatar-optimizer/en/docs/reference/trace-and-optimize/)).

**Fact (T&O's public surface):**

- `[PublicAPI] public sealed class TraceAndOptimize : AvatarGlobalComponent` in namespace `Anatawa12.AvatarOptimizer`. Its constructor and fields are internal. Its doc comment says "The type of the component is the only public API of this component. You cannot configure this component from script." Defaults include `mergeSkinnedMesh = true` and `optimizeTexture = true` ([TraceAndOptimize.cs#L9-L85](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Runtime/TraceAndOptimize.cs#L9-L85)).
- The Component Scripting API docs (since 1.7.0) say "you can create tools or components that add Avatar Optimizer components". They list "`TraceAndOptimize` - Adding with default configuration is supported but configuring is not supported" ([docs source L7-L19](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/.docs/content/docs/developers/component-api/index.md?plain=1#L7-L19)).
- `MergeSkinnedMesh` is **not** in that documented list. Its code still marks `Initialize(int version)` (versions 1 and 2), `SourceSkinnedMeshRenderers`, `SourceStaticMeshRenderers` and `MergeBlendShapes` as `[PublicAPI]` ([MergeSkinnedMesh.cs#L133-L209](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Runtime/MergeSkinnedMesh.cs#L133-L209)). The docs and the code disagree.
- AAO's stability policy: T&O's behaviour may change as optimizations are added, but "the default settings of `AAO Trace and Optimize` component will never change the behavior and appearance of your avatar" ([basic-concept L43-L48](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/.docs/content/docs/basic-concept/index.md?plain=1#L43-L48)).

**Fact (when AAO looks for components):**

- AAO's first Optimizing step sets `AAOEnabled` from `GetComponentInChildren<AvatarTagComponent>(true)` at that moment ([OptimizerPlugin.cs#L60-L69](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/OptimizerPlugin.cs#L60-L69)).
- `LoadTraceAndOptimizeConfiguration` then reads `context.AvatarRootObject.GetComponent<TraceAndOptimize>()` when it runs, and destroys it ([TraceAndOptimizeProcessor.cs#L140-L152](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Internal/TraceAndOptimizeBase/TraceAndOptimizeProcessor.cs#L140-L152)).
- AAO's Resolving passes don't read T&O ([OptimizerPlugin.cs#L41-L56](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/OptimizerPlugin.cs#L41-L56)).
- AAO itself adds components at build time: `RegisterTexCoordEvacuation` adds `InternalEvacuateUVChannel` to the renderer.

**Inference:** A UV Wizard Optimizing pass ordered `BeforePlugin("com.anatawa12.avatar-optimizer")` can `AddComponent<TraceAndOptimize>()` on the avatar root of the build clone, and AAO will honour it with default settings. It must be on the root object, because AAO uses `GetComponent`, not `GetComponentInChildren`. Check for an existing one first (`[DisallowMultipleComponent]`). Not verified by running.

**Fact (what T&O will not merge, [AutoMergeSkinnedMesh.cs](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/Processors/TraceAndOptimize/AutoMergeSkinnedMesh.cs)):**

- Candidates are `SkinnedMeshRenderer`s only (L66). Static `MeshRenderer`s are never auto-merged.
- A candidate needs bones and a root bone, one material per submesh, no material-swap or other forbidden animation, no extra components, and no shader that uses vertex indices (L82-L110).
- Candidates merge only within a group that shares local bounds, root bone, probe anchor, shadow and probe settings, quality, `updateWhenOffscreen` and identical activeness animation (L855-L905).
- `OptimizeTexture` ([OptimizerPlugin.cs#L130](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/OptimizerPlugin.cs#L130)) is AAO's own UV-island atlaser. AAO has shader information for Toon Standard ([ShaderInformation.VRCSDK.cs#L79-L200](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/APIInternal/ShaderInformation.VRCSDK.cs#L79-L200)), so it may try to repack UV Wizard's atlas.

**Inference:** An avatar with outfits on different toggles, or with a static mesh such as glasses, ends with more than one skinned mesh, more than one basic mesh, or more than one slot, even with T&O. That caps it at Medium or below on Android (§5). Whether `OptimizeTexture` changes a tightly packed atlas is untested.

**Fact (interop API signatures).** Namespace `Anatawa12.AvatarOptimizer.API`, assembly `com.anatawa12.avatar-optimizer.api.editor` (editor-only, `autoReferenced: false`):

```csharp
[PublicAPI] public static class UVUsageCompabilityAPI            // since AAO 1.8.0
{
    public static bool IsTexCoordUsed(SkinnedMeshRenderer renderer, int channel);  // channel 0..7
    public static void RegisterTexCoordEvacuation(SkinnedMeshRenderer renderer, int originalChannel, int savedChannel);
}
[PublicAPI] public abstract class MeshRemovalProvider : IDisposable
{
    public static MeshRemovalProvider? GetForRenderer(SkinnedMeshRenderer renderer); // null: nothing will be removed
    public abstract bool WillRemovePrimitive(MeshTopology topology, int subMesh, Span<int> vertexIndices);
    public abstract void Dispose();
}
```

Sources: [UVUsageCompabilityAPI.cs#L39-L78](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/API-Editor/UVUsageCompabilityAPI.cs#L39-L78) and [MeshRemovalProvider.cs#L26-L71](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/API-Editor/MeshRemovalProvider.cs#L26-L71).

- `UVUsageCompabilityAPI` is meant for non-destructive build-time tools (L18-L19). `RegisterTexCoordEvacuation` throws `InvalidOperationException` if `savedChannel` is used by AAO.
- `IsTexCoordUsed` reports only Remove Mesh By Mask (UV0) and Remove Mesh By UV Tile's channels ([UVUsageCompabilityAPIImpl.cs#L22-L36](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/APIInternal/UVUsageCompabilityAPIImpl.cs#L22-L36)).
- `MeshRemovalProvider` predicts Remove Mesh By Mask, By BlendShape and In Box, but **not By UV Tile** ([MeshRemovalProviderImpl.cs#L21-L24](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/APIInternal/MeshRemovalProviderImpl.cs#L21-L24)).
- `MeshRemovalProvider` has no false positives. Don't change the mesh between creating it and AAO's run, except for evacuated UVs (L18).

**Rec:**

- On Android builds, add T&O when it is missing, behind a component setting that is on by default, and report it at `Information` severity.
- This is AAO's documented use, and AAO promises its defaults won't change the avatar's appearance.
- The cost: the creator also gets T&O's other optimizations (animator, blendshapes, `OptimizeTexture`) without choosing them.
- Don't create `MergeSkinnedMesh` components in v1; its API is not in the documented list.
- The Report should name the reason when the avatar ends with more than one mesh: toggles, a static mesh, or different bounds or root bones.
- **What would change this:** AAO dropping T&O from the documented Component API, or test avatars breaking under T&O defaults.

**For #19:** Answer the open question with "add T&O if missing, with an opt-out". Note in the issue that `MeshRemovalProvider` ignores Remove Mesh By UV Tile; UV evacuation still keeps that removal correct. Add acceptance tests for the cases T&O cannot merge, so "one skinned mesh" isn't promised where AAO can't deliver it.

---

## 3. NDMF platforms and manual bake (#24)

**Fact (how NDMF picks a platform):**

- Platforms implement the public `INDMFPlatformProvider` and are registered with `[NDMFPlatformProvider]` through `TypeCache` ([PlatformRegistry.cs#L23-L74](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/Platform/PlatformRegistry.cs#L23-L74)).
- The built-in names are `nadena.dev.ndmf.generic`, `nadena.dev.ndmf.vrchat.avatar3` and `nadena.dev.ndmf.resonite` ([WellKnownPlatforms.cs#L25-L27](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/API/Fluent/WellKnownPlatforms.cs#L25-L27)). `VRChatPlatform` is `internal` ([VRChatPlatform.cs#L17](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/VRChat/VRChatPlatform.cs#L17)).
- **Builds and play mode:** the SDK hook builds with `VRChatPlatform.Instance` ([BuildFrameworkPreprocessHook.cs#L48](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/VRChat/BuildFrameworkPreprocessHook.cs#L48)). Play mode on VRChat avatars goes through the same hook.
- **Manual bake:** it passes no platform, so it uses `AmbientPlatform.CurrentPlatform`. That falls back to `DefaultPlatform`, which `VRChatPlatform` sets at editor load ([AvatarProcessor.cs#L115](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/AvatarProcessor.cs#L115), [VRChatPlatform.cs#L19-L24](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/VRChat/VRChatPlatform.cs#L19-L24)). The NDMF console's platform dropdown is hidden unless a third platform is installed ([ErrorReportWindow.cs#L148-L178](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/ErrorReporting/UI/ErrorReportWindow.cs#L148-L178)).
- **Which passes run:** by default, passes run only on `VRChatAvatar30` ([PluginInfo.cs#L29](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/API/Fluent/PluginInfo.cs#L29)). `[RunsOnAllPlatforms]`, `[RunsOnPlatforms(...)]` and `Sequence.OnPlatforms` override this; the most specific declaration wins ([Sequence.cs#L228-L237](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/API/Fluent/Sequence/Sequence.cs#L228-L237)).

**Fact (public processing API, [AvatarProcessor.cs](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/AvatarProcessor.cs)):**

- `public static GameObject ManualProcessAvatar(GameObject obj, INDMFPlatformProvider platform = null)` (L108-L140) makes a clone 2 m in front, saves assets to `Assets/ZZZ_GeneratedAssets`, returns the clone and raises `OnManualProcessAvatar`.
- `public static BuildContext ProcessAvatar(GameObject root, INDMFPlatformProvider platform)` (L188-L203) processes in place, with temporary assets.
- Get the VRChat provider with `PlatformRegistry.PlatformProviders[WellKnownPlatforms.VRChatAvatar30]`.

**Fact (no Android/PC split):**

- `VRChatPlatform` has no build-target member. NDMF 1.14.8 reads `activeBuildTarget` only for scripting defines in `ExperimentalFeaturesControlWindow.cs`.
- The NDMF 1.15.0-beta.1 changelog has no platform change.
- The SDK's own test is `ValidationEditorHelpers.IsMobilePlatform()`, which returns true when `activeBuildTarget` is Android (13) **or iOS (9)** (`VRCSDKBase-Editor.dll`).

**Fact (prior art):** VRCQuestTools v3.0.1 adds its own override: a `PlatformTargetSettings` component, plus a session flag set to Android for "build with mobile settings and test on PC" ([NdmfAvatarBuilder.cs](https://github.com/kurotu/VRCQuestTools/blob/v3.0.1/Packages/com.github.kurotu.vrc-quest-tools/Editor/Utils/NdmfAvatarBuilder.cs), [BuildTargetConfigurationPass.cs](https://github.com/kurotu/VRCQuestTools/blob/v3.0.1/Packages/com.github.kurotu.vrc-quest-tools/Editor/NDMF/Passes/BuildTargetConfigurationPass.cs)). It also converts materials to Toon Standard before AAO, so its output would reach UV Wizard as source materials.

**Fact (what differs when a bake pretends to be Android in a Windows project):**

- Other plugins decide formats at compile time. AAO's Merge Material defaults to ASTC 6×6 under `UNITY_ANDROID || UNITY_IOS` and to DXT5 otherwise ([MergeMaterialProcessor.cs#L525-L529](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/Processors/SkinnedMeshes/MergeMaterialProcessor.cs#L525-L529)).
- On a Windows editor, the `GraphicsFormat` overload of the `Texture2D` constructor rejects ASTC, so AAO uses the `TextureFormat` overload ([MaxTextureSizeProcessor.cs#L205-L217](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/Processors/MaxTextureSizeProcessor.cs#L205-L217)).
- Source textures carry the active platform's import format, and the SDK's texture memory reads `Texture2D.format` (§5).
- The SDK's mobile shader check only exists under `#if UNITY_ANDROID || UNITY_IOS` (`Packages/com.vrchat.avatars/Editor/VRCSDK/SDK3A/VRCSdkControlPanelAvatarBuilder.cs` L1196-L1202). Its stats use `IsMobilePlatform()` (L305, L327).

**Fact (the Android upload validates the processed avatar):**

- `VRCAvatarBuilder.ExportCurrentAvatarResource` instantiates the avatar, runs `VRCBuildPipelineCallbacks.OnPreprocessAvatar`, then invokes the content-processed callback (`VRCSDK3A-Editor.dll`).
- That callback runs the full avatar check, including `FindIllegalShaders`. Any error aborts the build with "Avatar validation failed" (builder L2976, L3045-L3075, L3988-L4001).
- `FindIllegalShaders` checks only `Renderer.sharedMaterials`, by shader name, against `ShaderWhiteList` (`Packages/com.vrchat.base/Runtime/VRCSDK/Dependencies/VRChat/Scripts/Validation/AvatarValidation.cs` L109-L123; `ValidationUtils` in `VRCSDKBase.dll`). That list contains `VRChat/Mobile/Toon Standard`.

**Inference:**

- A build-time swap to Toon Standard passes Android validation.
- An excluded material (#16, #20) left on a renderer with lilToon or Poiyomi fails the upload.
- A preview "as Android" in a Windows project can only estimate. Other plugins keep PC formats, and source textures not baked by UV Wizard keep PC import formats.

**Rec:**

- The manual-bake button may force UV Wizard's Android path with its own flag, through `ManualProcessAvatar(avatar, vrchatProvider)`. Label the result "Android preview (estimated formats)".
- Never call `SwitchActiveBuildTarget`; it reimports every asset.
- For real builds, gate on `IsMobilePlatform`'s rule (Android or iOS), or record in ADR 0004 that iOS is deliberately excluded.
- Give excluded materials an Android fallback, for example an unatlased Toon Standard material per slot. Otherwise "excluded" means "upload blocked".

**Fact (NDMF error reporting, used by every issue):**

- `ErrorReport.ReportError(Localizer localizer, ErrorSeverity errorSeverity, string key, params object[] args)` (L187). Each argument fills the next `{0}`…`{9}` slot. Unity objects also become clickable references, and collections are flattened ([InlineError.cs#L26-L66](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/ErrorReporting/InlineError.cs#L26-L66)). `ReportError(IError)` (L158) and `ReportException(Exception, string)` (L198) also exist ([ErrorReport.cs](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/ErrorReporting/ErrorReport.cs)).
- `WithContextObject(obj, …)` attaches a context object (L251-L291). `CaptureErrors(Action)` is meant for tests (L334).
- Keys: title `key`, details `key:description`, hint `key:hint` ([SimpleError.cs#L38-L50](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/ErrorReporting/SimpleError.cs#L38-L50)).
- Severities: `Information`, `NonFatal`, `Error`, `InternalError`. Any `Error` or worse makes `BuildContext.Successful` false ([BuildContext.cs#L82](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/API/BuildContext.cs#L82)), and the optimize hook returns that to the SDK, which aborts the upload ([BuildFrameworkPreprocessHook.cs#L92](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/VRChat/BuildFrameworkPreprocessHook.cs#L92)).
- `new Localizer(string defaultLanguage, Func<List<LocalizationAsset>> assetLoader)` ([Localizer.cs#L56](https://github.com/bdunderscore/ndmf/blob/1.14.8/Editor/UI/Localization/Localizer.cs#L56)).
- **Rec:** report excluded and flagged materials as `NonFatal`, "added T&O" as `Information`, and use `Error` only when the upload must stop.

**Not established:** whether `EditorUtility.CompressTexture` to ASTC gives correct output on a Windows editor. I believe Unity's editor encodes ASTC on every host, but I did not run it.

**For #24:** Yes, the button can bake "as Android" in a PC project, using UV Wizard's own flag and NDMF's public `ManualProcessAvatar`. Its figures are estimates, and the Report must say so. Raise the excluded-material finding on #16 and #20 as well: on Android, an unconverted excluded material blocks upload.

---

## 4. Toon Standard properties (#16, #17, #18)

**Fact (properties the bake needs).** Source: `Packages/com.vrchat.base/Runtime/VRCSDK/Sample Assets/Shaders/Mobile/ToonStandard/ToonStandard.shader` (GUID `e765db0afa7ecfc44ade2e4e2491f65a`) and `CG/VertexFragment.cginc`.

| Purpose | Property (type, default) | UV and use | Enabled by |
|---|---|---|---|
| Albedo | `_MainTex` (2D, white), `_Color` (Color, 1,1,1,1) | UV0 with `_MainTex_ST`; `tex × _Color × vertex colour` (VF L48-L50) | always |
| Vertex colour | `_VertexColor` (`[ToggleUI]` Float, 0) | multiplies albedo when 1 (VF L14-L17) | property |
| Normal | `_BumpMap` (`[Normal]` 2D, bump), `_BumpScale` (Float, 1) | UV0 with `_BumpMap_ST`; `UnpackScaleNormal` (VF L63-L68) | keyword `USE_NORMAL_MAPS` |
| Emission | `_EmissionMap` (2D, white), `_EmissionColor` (Color, **0,0,0**, no `[HDR]`), `_EmissionStrength` (Range 0–2, 1), `_EmissionUV` (Enum UV0=0, UV1=1) | `map.rgb × colour × strength` (VF L81-L86) | always: `#define USE_EMISSION_MAP` (shader L148) |

Other textures and features, by shader line: `_Ramp` (shadow ramp, not mesh UV, L15). `_MetallicMap` and `_GlossMap` with channel selectors (`USE_SPECULAR`, L24-L31). `_Matcap` and `_MatcapMask` (`USE_MATCAP`, L33-L37). Rim light (`USE_RIMLIGHT`, L39-L44). `_OcclusionMap` (`USE_OCCLUSION_MAP`, L51-L53). Detail mask, albedo and normal, with `_DetailUV` UV0/UV1 (`USE_DETAIL_MAPS`, L55-L61). `_HueShiftMask` (`USE_HUE_SHIFT`, L63-L67). `_ColorMask` with 4 colours (`USE_COLOR_MASK`, L69-L79). `_AudioLinkMask` on UV0–UV3 (`USE_AUDIOLINK`, L84-L96).

- The `shader_feature_local` keywords and `dynamic_branch_local` switches are declared at L136-L145.
- The shader is opaque only: `RenderType` Opaque, `Blend One Zero`, `ZWrite On`, with cutoff and blend modes commented out (L7-L8, L103-L124). Its fallback is `VRChat/Mobile/Diffuse`, with `VRCFallback` `toonstandard`.
- AAO's shader information for Toon Standard agrees on every UV channel and keyword above ([ShaderInformation.VRCSDK.cs#L101-L200](https://github.com/anatawa12/AvatarOptimizer/blob/v1.9.20/Editor/APIInternal/ShaderInformation.VRCSDK.cs#L101-L200)).

**Fact (custom editor):**

- `CustomEditor "VRC.ToonStandard.ToonStandardShaderEditor"`. Its source is `…/Sample Assets/Shaders/Mobile/GUI/ToonStandardEditor.cs`.
- Features are plain `material.EnableKeyword` and `DisableKeyword` calls on `LocalKeyword`s (L1052-L1069). Nothing infers keywords from assigned textures; the editor only warns when normal maps are on with no texture (L323-L326).
- Ramp presets load from `Resources.Load<Texture2D>("VRChat/ShadowRamp…")` (L1182-L1188). They are 256×32, 8×1 or 1×1 PNGs without mips.
- **Atlas stub:** the editor checks a `VRCHAT_ATLASING_ENABLED` keyword and says "Drive properties with the VRCAvatarAtlasProperties component" (L218-L222). The shader's `multi_compile` for it is commented out (shader L134), and `CG/VRCAtlasingShim.cginc` is a TODO (L5-L6). No `VRCAvatarAtlasProperties` type exists in SDK 3.10.5 (reflection search).

**Inference:**

- `_EmissionColor` defaults to black, so a merged material shows no emission unless the code sets it.
- With `_EmissionColor` white and `_EmissionStrength` 1, the emission atlas carries final colours. Strength up to 2 gives 2× headroom for bright sources.
- If no `_Ramp` is set, the default white texture likely means flat, unshaded lighting.
- VRChat is preparing an official atlased Toon Standard path. That fits the "SDK optimizer in progress" risk in tech-stack.md.

**Rec:**

- Create the merged material from the shader asset by GUID.
- Set `_MainTex`, `_Color` = white, and `_VertexColor` = 0 unless the source used vertex colour.
- For the Emission channel, set `_EmissionMap`, `_EmissionColor` = white, `_EmissionStrength` = 1 and `_EmissionUV` = 0.
- For the Normal channel, set `_BumpMap` and `_BumpScale` = 1, and enable `USE_NORMAL_MAPS` only when that channel is on.
- Set `_Ramp` explicitly. Copy it from a Toon Standard source, or else use a preset; which preset is a product decision.
- Leave every other feature keyword off.

**For #16 (and #17, #18):** "Settings from one representative source" should cover only `_Ramp`, culling and lighting floats. Every atlased value is fixed: white tint, white emission colour, strength 1, UV0. Normal needs the `USE_NORMAL_MAPS` keyword; emission needs no keyword.

---

## 5. Texture memory and Android ranks (#21, #24)

**Fact (the formula).** Source: `VRCSDKBase.dll` 3.10.5, `MeshPerformanceScanner.AnalyzeMaterials` and the scanners around it, read as IL.

- **Renderers:** `GetComponentsInChildren<Renderer>(includeInactive: true)`, minus any that are tagged `EditorOnly` or sit under an `EditorOnly` object (`AbstractPerformanceScanner.ScanAvatarForComponentsOfType`, `AvatarPerformance.IsEditorOnly`).
- **Materials:** a `HashSet` over `Renderer.GetSharedMaterials`, so each material counts once.
- **Textures:** for each material, `GetTexturePropertyNameIDs` then `GetTexture`, into a `HashSet`, so each texture counts once per avatar.
- **Size:**
  - `Texture2D` and `Cubemap`: Σ over levels `i = 0 … mipmapCount−1` of `RoundToInt(((w·h) >> 2i) × bytesPerPixel(format))`. A cubemap counts as one face.
  - `Texture2DArray` and `Texture3D`: `w·h·depth·bpp`, no mips.
  - `RenderTexture`: skipped.
  - Unknown format: 16 bytes per pixel.
  - Total bytes ÷ 1,048,576, so the "MB" is MiB.
- **Bytes per pixel** (`_texture2DBytesPerPixelLookup`):
  - 4: RGBA32, ARGB32, BGRA32, RGB24.
  - 0.5: DXT1, BC4, ETC2_RGB.
  - 1: DXT5, BC5, BC7, ETC2_RGBA8.
  - ASTC: 4×4 = 1, 5×5 = 0.64, 6×6 = 0.445, 8×8 = 0.25, 10×10 = 0.16, 12×12 = 0.11125.
  - Crunched formats count as their uncompressed block format, for example DXT5Crunched = 1.

**Fact (Android limits).** Source: `Packages/com.vrchat.base/Runtime/VRCSDK/Dependencies/VRChat/Resources/Validation/Performance/StatsLevels/Quest/<Rank>_Quest.asset`, L19-L37. They match the [docs table](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/) (updated 2026-04-21).

| Android rank | Texture memory | Material slots | Skinned meshes | Basic meshes |
|---|---|---|---|---|
| Excellent | 10 MB | 1 | 1 | 1 |
| Good | 18 MB | 1 | 1 | 1 |
| Medium | 25 MB | 2 | 2 | 2 |
| Poor | 40 MB | 4 | 2 | 2 |

For comparison, PC Excellent is 40 MB and 4 slots (`…/Windows/Excellent_Windows.asset`).

**Fact (what counts):**

- **Material slots** are Σ `subMeshCount` of each `SkinnedMeshRenderer.sharedMesh` and each `MeshRenderer`'s `MeshFilter.sharedMesh` (`AnalyzeSkinnedMeshRenderers`, `AnalyzeMeshRenderers`). `ParticlePerformanceScanner` also adds slots, and the docs say line renderers do too. `sharedMaterials.Length` is not used.
- **Skinned meshes** are every `SkinnedMeshRenderer` component, even inactive or with no mesh. **Basic meshes** are every `MeshRenderer`. The docs say: "All GameObjects and Components, including those that are currently disabled, count."
- **Public API:** `AvatarPerformance.CalculatePerformanceStats(string avatarName, GameObject avatarObject, AvatarPerformanceStats perfStats, bool isMobile)` (public static), `new AvatarPerformanceStats(bool isMobile)`, the public field `textureMegabytes`, `GetPerformanceRatingForCategory(AvatarPerformanceCategory)`, and `AvatarPerformanceStats.GetStatLevelForRating(PerformanceRating, bool isMobile)`.

**Inference (worked sizes with the SDK formula, full mips):**

- 2048² ASTC 6×6 = 2.37 MB. 2048² ASTC 4×4 = 5.33 MB. 4096² ASTC 6×6 = 9.49 MB.
- Albedo 6×6 + Emission 6×6 + Normal 4×4, all at 2048², totals 10.08 MB. That misses Excellent by 0.08 MB but fits Good.
- The glossary's slot rule holds, with two sharpenings:
  - Slots are submeshes across *all* renderers, so one leftover particle system or static mesh breaks Excellent and Good.
  - Inactive renderers count. AAO's T&O removes unused objects only when it can prove they are unused.

**Rec:**

- Compute the budget with exactly this formula.
- Count shared textures once. Include textures outside the atlas: the ramp, excluded materials and particles.
- Produce the Report's "after" figures by calling `AvatarPerformance.CalculatePerformanceStats(…, isMobile: true)` on the clone in a pass ordered after AAO. Then they match the SDK by construction.
- In a Windows-target preview, label the figures estimates (§3).
- **Not established:** whether `GetTexturePropertyNameIDs` also lists stale saved textures that the shader no longer declares. If it does, an "orphan" texture left on a material would count.

**For #21 (and #24):** The budgets in the glossary are right, and they are MiB. Three channels at 2048 with a 4×4 normal map don't fit Excellent, so the size search must drop a channel's size or format. #24 can get rank-exact numbers from the SDK's public `AvatarPerformance` API. The Report should name the leftover renderer when the slot or mesh count blocks a rank.
