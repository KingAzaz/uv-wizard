# UV Wizard: technology stack research

Date: 2026-10-12. Scope: how to rebuild UV Wizard (VPM package `com.clnelson888.uv-wizard`, currently 0.1.1) as a production material merger and UV-island atlas tool for Unity and VRChat.

Labels used throughout:

- **Fact** is something I observed in a primary source, with a link.
- **Inference** is my reasoning from those facts. It has not been verified by running anything.
- **Rec** is a recommendation.

No code was compiled or run for this report. Every claim about behaviour comes from docs or source reading.

---

## Summary of the recommended stack

1. **Language:** C# 9 inside Unity 2022.3.22f1. That is the only version VRChat accepts for uploads today, even though the VRChat *client* moved to Unity 6 on 2026-10-02. No native C/C++ or Rust in v1. ([VRChat current Unity version](https://creators.vrchat.com/sdk/upgrade/current-unity-version/), [VRChat 2026.3.3 notes](https://docs.vrchat.com/docs/vrchat-202633), [Unity 2022.3 C# compiler](https://docs.unity3d.com/2022.3/Documentation/Manual/CSharpCompiler.html))
2. **Pipeline:** non-destructive, as an **NDMF plugin** that runs in `BuildPhase.Optimizing`, before AAO (`com.anatawa12.avatar-optimizer`) and after TexTransTool and Modular Avatar. Add a thin "bake to assets" command for plain-Unity and destructive use. Both front ends call the same VRChat-free core.
3. **Assemblies:** a `Core.Editor` assembly with no references to NDMF or VRChat; an `Ndmf.Editor` adapter gated by `versionDefines`; an optional `Aao.Editor` adapter; a tiny `Runtime` assembly that holds only the settings component (`IEditorOnly`). Everything else is editor-only.
4. **Islands:** union-find over triangles that share a *UV position* (not a vertex index), per UV channel and per texture-set. Group overlapping and mirrored islands into one packable chart. Keep integer UDIM tile offsets separate from the fractional position.
5. **Sizing:** each chart's size comes from its *used* source texels: the rasterized coverage of the island at source resolution, the maximum over all atlased textures, and a content-aware downscale for flat-colour regions. It does not come from whole-texture size.
6. **Packing:** a C# raster ("bit-image") packer modelled on xatlas's brute-force chart placement, with 0/90° rotation, block alignment and pixel padding, accelerated with Burst. A ported MaxRects (juj, public domain) serves as a fast bounding-box mode and fallback. No native xatlas in v1.
7. **Baking:** GPU re-rendering of UV-space triangles into a `RenderTexture` with a small HLSL shader, driven by `CommandBuffer`. Source textures are sampled directly, so no readability or importer changes are needed. The shader bakes tint and `_ST` and decodes normal maps to XYZ. Then jump-flood dilation for gutters, mip generation, readback, and `EditorUtility.CompressTexture` (BC7/BC5 on PC, ASTC on Android). Crunch is off by default.
8. **Materials:** a shader-adapter registry modelled on AAO's ShaderInformation API, with built-in adapters for Standard, VRChat Mobile (Standard Lite and Toon Standard), lilToon and Poiyomi, plus a generic fallback that uses `Shader.GetPropertyType` and `GetPropertyFlags`. Merge only within the same shader, render queue, blend mode and keyword set. On Android, emit a VRChat Mobile shader.
9. **Meshes:** `Mesh.MeshData` with `MeshUtility.AcquireReadOnlyMeshData` (which ignores `isReadable` in the editor), plus Burst, Collections and Mathematics. These are already transitive dependencies of both the VRChat SDK and NDMF. Split vertices that sit on chart boundaries. Collapse submeshes once materials are unified. Leave cross-renderer skinned-mesh merging to AAO when it is present.
10. **Testing and release:** Unity Test Framework EditMode tests for pure-C# stages; CPU reference baker plus tolerance-based image comparison for golden atlases; GameCI `unity-test-runner` v4 plus `vrc-get`, following AAO's matrix, including a no-SDK leg. Keep the existing VRChat `package-list-action` release flow.

**Contrary evidence to weigh before committing.** Island-level atlasing for VRChat already exists. TexTransTool's AtlasTexture does UV-island relocation in the Optimizing phase, supports lilToon, and lets you set island size priority. AAO 1.9 ships Merge Material, which uses manual rectangle placement. VRChat also has an official SDK auto-optimizer "in progress" that will atlas, but only for Standard Lite and Toon Standard. UV Wizard needs a clear differentiator. The candidates are fully automatic texel-usage-driven sizing, tighter raster packing, overlap sharing, and Poiyomi support. See [Risks](#risks-and-open-questions).

---

## 0. Baseline: the repo today and the platform

**Fact (repo, read 2026-10-12):**

- `UVWizard.cs` is a `MonoBehaviour` that runs `CombineMaterials()` in `Start()`. It lives in an Editor-only asmdef (`includePlatforms: ["Editor"]`), so the component cannot exist in a build.
- It uses `Texture2D.PackTextures` on `mainTexture` only, flipping the importer's `isReadable` (a destructive importer change).
- It remaps UVs linearly per submesh. A vertex shared by two submeshes is therefore remapped twice, and tiling UVs outside 0–1 bleed into neighbouring rects.
- It assigns a new `Standard` material.
- `package.json` has no `vpmDependencies`, `license` or `url`.
- `ProjectVersion.txt` is 2022.3.22f1.
- `vpm-manifest.json` is empty, so the VRChat SDK is not installed in the dev project.
- The release and listing workflows are the VRChat `template-package` ones.

**Fact:** `Texture2D.PackTextures` scales inputs down if they don't fit, and it falls back to RGBA32 in several mixed-format cases ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D.PackTextures.html)). It packs whole textures, which is the "grid" behaviour the README wants to move away from.

**Fact (Unity version):**

- VRChat's creator docs say "The current Unity version used by VRChat is 2022.3.22f1" (page updated 2025-10-03) ([source](https://creators.vrchat.com/sdk/upgrade/current-unity-version/)).
- The June 25 2026 dev update opened a `unity-6` client beta on 6000.0.60f1. It said: "DO NOT UPGRADE YOUR UNITY SDK… Our servers will reject uploads from Unity 6" ([dev update](https://ask.vrchat.com/t/developer-update-25-june-2026/48618)).
- VRChat 2026.3.3 (build 1919, 2026-10-02) made Unity 6 live for players. Its notes say "Nothing has changed about the SDK side" and tell creators not to upgrade SDK projects ([release notes](https://docs.vrchat.com/docs/vrchat-202633)).
- Latest SDK in the official VPM listing is `com.vrchat.base` 3.10.5, `unity: 2022.3` (listing `https://packages.vrchat.com/official?download`, fetched 2026-10-12).

**Fact:** Unity marks the Built-in Render Pipeline deprecated in 6.5. It remains supported through the 6.7 LTS lifecycle ([Unity render pipelines strategy 2026](https://unity.com/topics/render-pipelines-strategy-for-2026), [6.5 upgrade guide](https://docs.unity3d.com/Manual/UpgradeGuideUnity65.html)). VRChat says it is still on Built-in with single-pass stereo, with no URP plan announced ([dev update](https://ask.vrchat.com/t/developer-update-25-june-2026/48618)).

**Inference:** Target 2022.3.22f1 now and expect an SDK move to Unity 6.0.x within the life of v1. Avoid APIs removed or changed in 6000.x. Write bake shaders as plain HLSL with no pipeline-specific includes, so they survive a later move to URP.

**Fact:** AAO already runs CI against Unity 6000.0 through 6000.4 without the VRChat SDK. A comment there notes "NDMF is not compilable for Unity 6000.5" ([AAO gameci.yml](https://github.com/anatawa12/AvatarOptimizer/blob/master/.github/workflows/gameci.yml)).

**Fact (why this matters):** On Android/Quest, Material Slots "Excellent"/"Good" means exactly **1**, Medium 2, Poor 4. On PC, Excellent is 4 ([performance ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/), updated 2026-04-21). Android avatars may use only the VRChat Mobile shaders, and the guidance says "Atlasing textures is essential" and recommends ≤1k textures ([Quest optimization](https://creators.vrchat.com/platforms/android/quest-content-optimization/), updated 2025-06-25).

---

## 1. Pipeline architecture

### Findings

**Fact (NDMF, `bdunderscore/ndmf`, MIT):**

- Versions: latest prerelease 1.15.0-beta.1 (2026-10-05), latest stable 1.14.8 (2026-08-29) (GitHub releases API).
- Its `package.json` declares `unity: 2022.3` and UPM dependencies `com.unity.collections 2.1.4` and `com.unity.mathematics 1.2.6` ([package.json](https://github.com/bdunderscore/ndmf/blob/main/package.json)).
- It is distributed from the VPM repo `https://vpm.nadena.dev/vpm.json`.

**Fact (phases):** The built-in phases run in this order: `FirstChance`, internal pre-platform-init, `PlatformInit`, `Resolving`, `Generating`, `Transforming`, `Optimizing`, `PlatformFinish`. Optimizing is "intended for pure optimizations that need to run late in the build process" ([BuildPhase.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/API/Attributes/BuildPhase.cs), [execution model](https://github.com/bdunderscore/ndmf/blob/main/docfx~/execution-model.md)).

**Fact (registration):**

- An `[assembly: ExportsPlugin(typeof(MyPlugin))]` attribute points at a `Plugin<T>` subclass.
- That subclass overrides `QualifiedName` and `DisplayName`, and declares passes in `Configure()` via `InPhase(phase).Run(...)`.
- Ordering uses `BeforePlugin`/`AfterPlugin` (string names allowed, so the other plugin doesn't need to be installed), `BeforePass`/`AfterPass` and `WaitFor`.
- `.PreviewingWith(IRenderFilter…)` attaches preview filters.
- Sources: [Plugin.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/API/Fluent/Plugin.cs), [Sequence.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/API/Fluent/Sequence/Sequence.cs), [execution model](https://github.com/bdunderscore/ndmf/blob/main/docfx~/execution-model.md).

**Fact (platforms):** By default, passes run only on `WellKnownPlatforms.VRChatAvatar30` unless `[RunsOnAllPlatforms]` or `[RunsOnPlatforms]` is applied. A `Generic` platform exists that "assumes nothing beyond basic ability to render meshes" ([WellKnownPlatforms.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/API/Fluent/WellKnownPlatforms.cs), [RunsOnPlatforms.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/API/Attributes/RunsOnPlatforms.cs)). AAO and TTT both use `[RunsOnAllPlatforms]`.

**Fact (build hook):** NDMF hooks VRChat builds through `IVRCSDKPreprocessAvatarCallback` with `callbackOrder => -11000`, and an optimize hook at -1025 ("just before RemoveAvatarEditorOnly") ([BuildFrameworkPreprocessHook.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/VRChat/BuildFrameworkPreprocessHook.cs)). `AvatarProcessor.ManualProcessAvatar` and `ProcessAvatar` are public entry points ([AvatarProcessor.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/AvatarProcessor.cs)).

**Fact (best practices, from NDMF docs):** ([best-practices.md](https://github.com/bdunderscore/ndmf/blob/main/docfx~/best-practices.md))

- Store per-build data in `ctx.GetState<T>()`, not in lambda captures.
- Don't call `AssetDatabase.SaveAssets` or `Refresh` during builds. Use `ctx.AssetSaver.SaveAsset`; NDMF auto-saves assets referenced by the avatar.
- Register clones with `ObjectRegistry.RegisterReplacedObject`.
- Use `BuildContext.IsTemporaryAsset` to avoid re-cloning.
- Don't assume other plugins leave objects untouched between sequences.

**Fact (errors and localization):**

- `ErrorReport.ReportError(Localizer, ErrorSeverity, key, params object[] args)` takes Unity objects in `args` to link errors to scene objects. `ErrorReport.WithContextObject(obj, …)` scopes them. `ErrorReport.CaptureErrors(Action)` is "intended for unit testing" ([ErrorReport.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/ErrorReporting/ErrorReport.cs)).
- `Localizer(defaultLanguage, Func<List<LocalizationAsset>>)` loads Unity `LocalizationAsset`s. NDMF ships its own `en-US`, `ja-JP`, `zh-Hans` and `zh-Hant` `.po` files ([Localizer.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/UI/Localization/Localizer.cs), [Localization folder](https://github.com/bdunderscore/ndmf/tree/main/Editor/UI/Localization)).

**Fact (preview):** `IRenderFilter.GetTargetGroups(ComputeContext)` and `Instantiate(group, proxyPairs, context)` build proxy renderers. A filter "must create new instances of [meshes, textures, materials], and destroy them in `IRenderFilterNode.Dispose`" ([IRenderFilter.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/PreviewSystem/IRenderFilter.cs)).

**Fact (ordering of neighbours):**

- **Modular Avatar** is `nadena.dev.modular-avatar`, with a second plugin `nadena.dev.modular-avatar.late-transform-stages`. It works in Resolving, Transforming and Optimizing. Version 1.19.0-alpha.1 requires NDMF `>=1.15.0-beta.1 <2.0.0-a` ([PluginDefinition.cs](https://github.com/bdunderscore/modular-avatar/blob/main/Editor/PluginDefinition/PluginDefinition.cs)).
- **TexTransTool** is `net.rs64.tex-trans-tool`. Its UV and atlas work runs in Transforming (MaterialModification → UVModification → …). An Optimizing sequence runs `BeforePlugin("com.anatawa12.avatar-optimizer")` ([TTT NDMFPlugin.cs](https://github.com/ReinaS-64892/TexTransTool/blob/master/Editor/NDMF/NDMFPlugin.cs)). TTT docs say AtlasTexture runs in the Optimizing phase ([TTT AtlasTexture docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)). That differs from what the plugin source shows, so the exact placement is unverified.
- **AAO** is `com.anatawa12.avatar-optimizer`. Almost everything runs in Optimizing, including `AutoMergeSkinnedMesh`, `MergeMaterialSlots`, `RemoveUnusedMaterialTextures` and the RemoveMeshBy* filters. The plugin class sits in a `ￜￜￜ` namespace so it sorts last ([OptimizerPlugin.cs](https://github.com/anatawa12/AvatarOptimizer/blob/master/Editor/OptimizerPlugin.cs)). AAO 1.9.20 was released 2026-10-01.

**Fact (AAO interop API, since AAO 1.8.0)** ([UVUsageCompabilityAPI.cs](https://github.com/anatawa12/AvatarOptimizer/blob/master/API-Editor/UVUsageCompabilityAPI.cs), [MeshRemovalProvider.cs](https://github.com/anatawa12/AvatarOptimizer/blob/master/API-Editor/MeshRemovalProvider.cs)):

- `UVUsageCompabilityAPI.IsTexCoordUsed(smr, channel)` reports whether AAO will use a UV channel.
- `UVUsageCompabilityAPI.RegisterTexCoordEvacuation(...)` lets a UV-packing tool copy the original UVs to a spare channel. AAO then uses those instead. The API's doc comment calls out "UV packing or atlas texture" tools by name.
- `MeshRemovalProvider.GetForRenderer(smr).WillRemovePrimitive(...)` predicts which triangles AAO will delete, and is designed to have no false positives.
- TTT already calls both from its `NegotiateAAOPass` ([source](https://github.com/ReinaS-64892/TexTransTool/blob/master/Editor/NDMF/NegotiateAAOPass.cs)).

**Fact (VRChat validation):** Custom scripts "are not permitted on avatars, and will be removed at upload and runtime" ([allowed components](https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components)). `IEditorOnly` makes the SDK skip a script during validation ([build callbacks](https://creators.vrchat.com/sdk/build-pipeline-callbacks-and-interfaces)). NDMF's `INDMFEditorOnly` derives from VRChat's `IEditorOnly` when the SDK is present ([NDMF API docs](https://ndmf.nadena.dev/api/nadena.dev.ndmf.INDMFEditorOnly.html)).

### Options compared

| | Destructive one-shot (current style) | NDMF build-time (non-destructive) |
|---|---|---|
| Source assets | Modified or duplicated; easy to lose originals | Untouched; works on a clone |
| Interop with MA, AAO, TTT | Order is up to the user; breaks MA's merging | Explicit ordering constraints; AAO interop API |
| Preview | Only after a bake | `IRenderFilter` live preview |
| Error UX | `Debug.Log` | NDMF error window, localized |
| Non-VRChat use | Trivial | NDMF Generic platform, or call the core directly |
| Cost | One bake | Re-runs on every build or play-mode entry, so caching matters |

**Inference:** Nobody in this ecosystem builds non-destructive avatar tools without NDMF any more. MA, AAO and TTT all depend on it, and running outside it would mean racing their callbacks. Running *before* AAO is better than after:

- AAO is designed to run last.
- AAO's `MergeMaterialSlots` and `AutoMergeSkinnedMesh` can then combine renderers whose materials UV Wizard has just unified.
- `MeshRemovalProvider` lets UV Wizard skip triangles AAO will delete, so they get no atlas space.
- UV evacuation keeps AAO's UV-based removal (RemoveMeshByMask, RemoveMeshByUVTile) correct.

### Recommendation

**Rec:** Build an NDMF plugin with `QualifiedName = "com.clnelson888.uv-wizard"` and `[RunsOnAllPlatforms]`. Configure it as follows:

```csharp
InPhase(BuildPhase.Optimizing)
  .AfterPlugin("net.rs64.tex-trans-tool")          // don't re-atlas TTT output
  .AfterPlugin("nadena.dev.modular-avatar")
  .BeforePlugin("com.anatawa12.avatar-optimizer")
  .Run(CollectPass.Instance).Then
  .Run(AtlasPass.Instance).PreviewingWith(new AtlasPreviewFilter());
```

- Use `AnimatorServicesContext` (as TTT and AAO do) to find materials swapped by animations, and exclude them.
- Report errors through `ErrorReport.ReportError(localizer, …)` with English and Japanese `.po` files from day one. The VRChat tooling audience is heavily Japanese, as NDMF, AAO, TTT and lilToon all show.
- Declare NDMF in `package.json` as `"vpmDependencies": { "nadena.dev.ndmf": ">=1.8.0 <2.0.0" }`. That is the range AAO uses; raise the floor to whatever API you actually need. VCC recommends ranges of the form `"x.y.x"` ([VPM packages docs](https://vcc.docs.vrchat.com/vpm/packages)).
- Don't hard-depend on the VRChat SDK. Gate VRChat-specific code with `versionDefines` on `com.vrchat.avatars` ([asmdef version defines](https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html)).

**Rec (standalone and plain Unity):**

- Keep all algorithms in a `Core.Editor` assembly that references only UnityEngine, UnityEditor, Collections, Mathematics and Burst.
- Provide an `EditorWindow` or menu command, "UV Wizard → Bake to Assets". It runs the same core on a selected GameObject, writes a new mesh, material and PNG atlas under `Assets/UVWizard Output/`, and uses `Undo`.
- Guard the NDMF adapter assembly with a `versionDefines` symbol, so a git-URL UPM install without NDMF still compiles.
- Drop the README's "Runtime Support" milestone. VRChat strips custom runtime scripts, and every stage here depends on `UnityEditor`.

---

## 2. UV island detection

### Findings

**Fact (prior art):** TexTransCore (MIT) builds islands with array-based union-find. It first deduplicates vertices that share an identical UV position, then merges triangle corners, and tracks bounding boxes and triangle counts in the union-find nodes ([IslandUtility.cs](https://github.com/ReinaS-64892/TexTransCore/blob/master/src/Island/IslandUtility.cs)).

**Fact:** In xatlas's UV-mesh mode, charts come from UV connectivity. Its header warns: "Overlapping UVs should be assigned a different material" (`UvMeshDecl.faceMaterialData`). In other words, xatlas does *not* share space between overlapping charts ([xatlas.h](https://github.com/jpcy/xatlas/blob/master/source/xatlas/xatlas.h)).

**Fact:** Blender's Pack Islands has "Merge Overlapping: Before the main packing operation, overlapping islands are detected and temporarily combined. During packing, the relative rotation and position of the merged islands are preserved." It also has "Pack To: Closest UDIM / Active UDIM" ([Blender manual source](https://projects.blender.org/blender/blender-manual/src/branch/main/manual/modeling/meshes/uv/editing.rst)).

**Fact (UDIM discard):** lilToon and Poiyomi both implement UV-tile ("UDIM") discard. lilToon has `_UDIMDiscard*` properties in `lil_common_input_base.hlsl`; Poiyomi has a `Razgriz_UDIMDiscard` module ([lilToon](https://github.com/lilxyzw/lilToon/tree/master/Assets/lilToon/Shader/Includes), [Poiyomi](https://github.com/poiyomi/PoiyomiToonShader/tree/master/_PoiyomiShaders/ModularShader/Editor/Poi_FeatureModules/Toon/Razgriz_UDIMDiscard)). AAO has `RemoveMeshByUVTile`. VRChat says its built-in shaders don't support UV tile discard ([Canny](https://feedback.vrchat.com/avatar-30/p/automatic-avatar-optimization-in-sdk)).

**Fact (multiple UV channels):** Shaders can sample textures from UV0–UV3 or from non-mesh coordinates. Poiyomi exposes per-texture `{_Tex}UV` enums (UV0–UV3, Panosphere, World Pos, Matcap, Screen Space, …) and `{_Tex}Pan` panning ([Poiyomi main module](https://github.com/poiyomi/PoiyomiToonShader/blob/master/_PoiyomiShaders/ModularShader/Editor/Poi_FeatureModules/Toon/Poi_Main/VRLTC_PoiMain.poiTemplateCollection)). AAO's lilToon info marks `_MatCapTex`, `_DitherTex`, gradation maps and others as `UsingUVChannels.NonMesh` ([ShaderInformation.Liltoon.cs](https://github.com/anatawa12/AvatarOptimizer/blob/master/Editor/APIInternal/ShaderInformation.Liltoon.cs)).

### Recommended algorithm

**Inference and Rec:**

1. **Key the problem correctly.** A *chart source* is the tuple (texture set, UV channel). The texture set is the ordered list of the source textures for every atlased slot, plus per-material bake parameters such as tint and `_ST`. Two materials that share textures and parameters share the same source space. Two that don't must not share it, even if their UVs overlap.
2. **Build islands.** Use union-find over triangles that share an edge in UV space. Weld corners by quantized UV position (for example, round to 1/2^20) *and* by mesh vertex index, so seams split islands and UV-welded vertices join them. Use edges rather than lone shared vertices so that "bow-tie" contacts don't merge unrelated shells. TTT merges on shared positions; edge-based merging is the stricter choice. Run this on `Mesh.MeshData` with Burst.
3. **Handle tiling and UDIM.**
   - Compute each island's UV bounds. If the island fits within one integer tile `[n, n+1)`, store `tile = floor(min)` and work in fractional space. The final atlas UV becomes `atlasUV + tile`. With a Repeat-wrap atlas sampler this samples identical texels, and shader UDIM discard keeps working.
   - If an island *crosses* tile boundaries, it genuinely tiles the texture. Its footprint is its full UV extent in source texels.
   - Above a configurable repeat count (for example, more than 2 tiles per axis), exclude the material. Otherwise the atlas would hold the repeated pattern, and quality drops sharply.
   - **This is the main correctness fix over the prototype.**
4. **Handle overlap and mirroring.** Within a chart source, rasterize each island's triangles into a coarse mask (for example, 1/4 of source resolution). Group islands whose masks intersect into one *chart*, keeping their relative transform, as Blender's Merge Overlapping does. Exactly stacked or mirrored halves then share one region. Mirrored geometry has flipped winding in UV space but the same texel region, so it is handled the same way.
5. **Multiple UV channels.** In v1, atlas only textures sampled from one mesh UV channel (normally UV0) with a static transform. Leave textures sampled from non-mesh coordinates (matcap, screen-space, dither) un-atlased, and take them from a representative material. If a merge group needs atlased textures from two different UV channels, split the group. Preserve UV1–UV7 untouched, apart from any AAO evacuation.

**Texel density and used-pixel analysis (Rec):**

- For each chart, the *demand* is the rasterized area of its triangles in source-texel units. Take the maximum over the chart's textures, each at its own resolution. A 4k normal map with a 1k albedo uses the 4k density.
- Add a content term. Sample the island region at a low mip. If variance is near zero (a flat colour), the chart can be scaled down to a few texels plus padding. TTT's manual "Island size priority" shows users want this control ([TTT AtlasTexture docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)); UV Wizard can set it automatically.
- Scale factor per chart = target density ÷ source density. Use a single global scale, chosen by binary search (as TTT and Blender do) to fill the atlas. Snap chart scales to powers of two where possible, so placements can be lossless texel copies (see §4).

---

## 3. Packing algorithms

### Findings

**Fact (xatlas, MIT, `jpcy/xatlas`, last push 2024-06-16, not archived):**

- C++11 with no dependencies. It has a C API (`xatlasAddUvMesh`) and a "pack multiple atlases" mode, `AddUvMesh` + `PackCharts`, that packs *existing* UV charts without re-parameterising them ([README](https://github.com/jpcy/xatlas)).
- `PackOptions` has `padding`, `texelsPerUnit`, `resolution`, `bilinear` ("leave space around charts for texels that would be sampled by bilinear filtering"), `blockAlign` (4×4), `bruteForce`, `rotateChartsToAxis` and `rotateCharts` ([xatlas.h](https://github.com/jpcy/xatlas/blob/master/source/xatlas/xatlas.h)).
- Charts are rasterized into a `BitImage`. Brute-force placement scans every position for two orientations (0/90°) and minimises `extents² + area`. Otherwise it tries random placement ([xatlas.cpp](https://github.com/jpcy/xatlas/blob/master/source/xatlas/xatlas.cpp), `findChartLocation_bruteForce`).
- For UV meshes, `surfaceArea = parametricArea`, so every chart gets the same `texelsPerUnit` scale. Per-chart weighting would have to be applied by pre-scaling the inputs.
- Only a Python binding is listed. A C# wrapper, `vicfergar/XAtlasSharp` (MIT), was last pushed 2020-11. `z3y/XatlasLightmap` (MIT, a VRChat-community lightmap packer) ships **only a Windows `xatlas.dll`** ([repo tree](https://github.com/z3y/XatlasLightmap)).

**Fact (Blender, GPL-2.0-or-later):** `geometry/intern/uv_pack.cc` combines several strategies ([source](https://projects.blender.org/blender/blender/src/branch/main/source/blender/geometry/intern/uv_pack.cc)):

- "Alpaca" L-shaped AABB packers, with and without rotation.
- `box_pack_2d`.
- An "optimal pack" table for small counts.
- An irregular-shape packer "Loosely based on the 'xatlas' code", using a bitmap `Occupancy`, "Witness Pixel" and "Triangle Hint" accelerators.

It binary-searches scale to fit the unit square. The manual offers Exact Shape (concave), Convex Hull or Bounding Box; rotation Any, Axis-aligned or Cardinal; and margin Scaled, Add or Fraction. It notes that Exact and Convex shape methods are "much slower" ([manual source](https://projects.blender.org/blender/blender-manual/src/branch/main/manual/modeling/meshes/uv/editing.rst)). **GPL code cannot be copied into an MIT package. Only the ideas can be reused.**

**Fact (TexTransCore, MIT):** Its `NFDHPlasFC` packer is a next-fit decreasing-height shelf algorithm with "floor-ceiling", 90° rotation of tall islands, and padding in UV units. It wraps an outer loop that rescales all islands until they fit ([NFDHPlasFC.cs](https://github.com/ReinaS-64892/TexTransCore/blob/master/src/Island/IslandRelocator/NFDHPlasFC.cs), [IslandRelocationManager.cs](https://github.com/ReinaS-64892/TexTransTool/blob/master/Runtime/TextureAtlas/IslandRelocationManager.cs)).

**Fact (rectangle packers):**

- Jukka Jylänki's survey code covers MaxRects, Skyline, Guillotine and Shelf. "All the code is released to Public Domain" ([juj/RectangleBinPack](https://github.com/juj/RectangleBinPack), including the survey PDF *A Thousand Ways to Pack the Bin*).
- C# options:
  - `RectangleBinPack.CSharp` on NuGet: v1.0.4, licence expression MIT, published 2026-01-28 ([NuGet](https://www.nuget.org/packages/rectanglebinpack.csharp)). Its GitHub repo has no detected licence file, so verify before use.
  - `ThomasMiz/RectpackSharp`: MIT, last push 2024-07 ([repo](https://github.com/ThomasMiz/RectpackSharp)).

**Fact (UVPackmaster):** Proprietary and paid. Vendor material describes a C++ heuristic search with CUDA and Vulkan GPU acceleration and pixel-exact margins ([Superhive listing](https://superhivemarket.com/products/uvpackmaster)). **I could not find a primary description of its algorithm, so this is unverified.**

**Fact (academic):**

- *Box Cutter* (Limper, Vining, Sheffer, SIGGRAPH 2018) improves packing efficiency by finding empty "void boxes", making axis-aligned cuts to charts, and repacking. That trades longer chart boundaries for density ([project page](https://www.cs.ubc.ca/labs/imager/tr/2018/BoxCutter)).
- Nöll & Stricker (EGSR 2011) pack arbitrary charts in a "modulo valid packings" search space ([EG digital library](https://diglib.eg.org/items/afd8b777-0553-497c-9760-a89de28d91aa/full)).
- Bennell & Oliveira, "The geometry of nesting problems: A tutorial" (EJOR 184, 2008), is the standard reference for no-fit-polygon (NFP) methods ([PDF](https://cs-gent.cs.kuleuven.be/vakgroepcw/sites/cs-gent.cs.kuleuven.be/files/nesting_problems_tutorialEJOR-184-2008.pdf)).

### Options compared

| Option | Packing density | Speed | Fit for UV Wizard | Licence and distribution |
|---|---|---|---|---|
| Whole-texture grid (`PackTextures`) | Poor | Fast | Contradicts the product goal | Built in |
| AABB shelf (NFDH, as in TTT) | Moderate | Very fast | Proven in TTT; weak on L-shaped islands | MIT reference |
| MaxRects / Skyline on chart AABBs | Good for boxy charts | Fast (n log n to n²) | Good "fast mode" and fallback | Public domain (juj) |
| Raster bit-image (xatlas-style) | High; uses concavities and holes | Slow without acceleration; good with coarse grid plus Burst | **Best match**: works in pixel space, so padding is exact | MIT (xatlas) or own port |
| NFP / polygon nesting | Highest in theory | Complex and fragile (non-convex Minkowski sums) | Overkill; raster is a practical substitute | Research code |
| Box-Cutter-style chart cutting | Highest | Expensive; adds seams | Not v1; seams cost vertices and bleeding risk | Paper only |

| Using xatlas from Unity | Pros | Cons |
|---|---|---|
| Native plugin (build xatlas.cpp for win-x64, osx-universal, linux-x64) | Mature code | Native plugins "cannot be unloaded" in an editor session ([Unity plug-in inspector](https://docs.unity3d.com/2022.3/Documentation/Manual/PluginInspector.html)), so VPM updates lock the DLL on Windows. Unsigned or unnotarized macOS dylibs are blocked by Gatekeeper (seen across vendors, for example [Draco for Unity troubleshooting](https://docs.unity3d.com/Packages/com.unity.cloud.draco@5.0/manual/troubleshooting.html)). Needs a 3-OS build matrix. No per-chart weight or overlap sharing. |
| Existing C# wrapper (XAtlasSharp) | — | Still needs native binaries; unmaintained since 2020 |
| **C# reimplementation of the packing core only** | No native code; Burst-compiled; per-chart weights, overlap groups, UDIM and block-alignment are under our control | About 1–2k lines to own; must port carefully (MIT allows it with attribution) |

### Recommendation

**Rec:** Write `UVWizard.Packing` in C# with two packers behind one interface:

1. **`RasterPacker` (default).**
   - Rasterize each chart, already scaled, into a bit-mask at *packing resolution*: the atlas size ÷ `blockSize`, where `blockSize` is 4 by default to match BC and ASTC 4×4 blocks.
   - Dilate the mask by `padding` (in blocks).
   - Sort charts by area, descending.
   - For each chart, scan candidate positions for 0° and 90° using a row-occupancy bitset (64-bit words, Burst job). Score by the xatlas metric `extents² + area`, with a bottom-left tie-break.
   - Use Blender's "witness pixel" idea as an early-reject accelerator.
   - Wrap it in an outer binary search on global scale until everything fits the target atlas (TTT and Blender both do this).
2. **`MaxRectsPacker` (fast or fallback).** A port of juj's `MaxRectsBinPack` (best-short-side-fit, with rotation) over chart AABBs, used for previews and huge chart counts.
3. Keep **rotation to multiples of 90°** and *no mirroring* by default. Arbitrary rotation forces resampling and makes block alignment impossible (see §4). Offer 180°/270° only when it helps the score.
4. **Padding and mips.** At mip level *k* a gutter of *g* texels shrinks to *g*/2^k. Protecting down to mip *k* therefore needs ≥ 2^k texels, or alignment to 2^k blocks. Default: 4×4 block alignment plus a 2-block gutter at 2048. This covers bilinear sampling down to about mip 3. Below that, the dilated colour fill in §4 hides the remaining bleed. This is my own arithmetic, consistent with xatlas's `bilinear` and `blockAlign` options; it has not been tested.

Don't ship native xatlas in v1. Revisit only if the C# packer is measurably worse on real avatars. Add a benchmark corpus first (see §7).

---

## 4. Atlas texture baking

### Findings

**Fact:** `Texture.isReadable` is "not required for methods that perform all their work on the GPU", for example `Graphics.CopyTexture` and `Graphics.Blit` ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture-isReadable.html)). So GPU sampling of non-readable, compressed source textures needs **no importer change**. The current code's `isReadable` flip is unnecessary and destructive.

**Fact:** `Graphics.Blit` sets `RenderTexture.active` and draws a full-screen quad. For anything other than a full-screen copy, the docs point to manual `SetRenderTarget`/`GL` drawing ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Graphics.Blit.html)). `RenderTextureReadWrite.sRGB` converts linear→sRGB on write and sRGB→linear on sample; `Linear` does no conversion "for non-color data" ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RenderTextureReadWrite.html)).

**Fact (normal maps):**

- `NormalMapEncoding` is `XYZ` or `DXT5nm`. It is configurable only on Android, iOS and tvOS; "on other platforms the encoding is always DXT5nm" ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/NormalMapEncoding.html)).
- Unity's shader library unpacks "DXT5nm (1, y, 0, x) or BC5 (x, y, 0, 1)" via `packedNormal.a *= packedNormal.r; normal.xy = packedNormal.ag*2-1` ([Packing.hlsl](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl)).
- Mesh tangents follow "the horizontal (U) texture direction", and `w` flips the binormal ([Mesh.tangents](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh-tangents.html)).

**Fact (compression):**

- `EditorUtility.CompressTexture(Texture2D, TextureFormat, quality)` compresses a CPU texture explicitly. For non-power-of-two textures with mips, only Crunch, BC4, BC5 and ASTC are supported ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorUtility.CompressTexture.html)).
- VRChat: "Crunch compression only helps with download size", and Crunch "may break avatars if a newer Unity version uses an incompatible Crunch version" ([Quest optimization](https://creators.vrchat.com/platforms/android/quest-content-optimization/)).
- Unity: ASTC block size trades quality for size, from 8 bpp (4×4) to 0.89 bpp (12×12) ([texture formats](https://docs-alpha.unity3d.com/2023.2/Documentation/Manual/texture-compression-formats.html)).

**Fact (VRChat mip streaming):** NDMF ships a `CheckMipStreamingPass` that warns on avatar textures with mipmaps where `m_StreamingMipmaps == false` ([CheckMipStreamingPass.cs](https://github.com/bdunderscore/ndmf/blob/main/Editor/VRChat/CheckMipStreamingPass.cs)). Generated atlases should enable it.

**Fact (prior art):**

- TTT moved from "triangle barycentric transfer" to "rectangle-based transfer". It exposes "Pixel normalize" (snap island moves to whole pixels to reduce quality loss) and a background colour that "affects mipmaps and compression" ([TTT docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)).
- TTT uses compute shaders (`TTCE-Unity`) and depends on Burst 1.8 and Mathematics 1.3.2 ([TTT package.json](https://github.com/ReinaS-64892/TexTransTool/blob/master/package.json)).

### GPU vs CPU

| | GPU UV-space rendering (CommandBuffer) | CPU `GetPixels` |
|---|---|---|
| Non-readable or compressed sources | Works directly | Needs a readable copy (Blit → RT → `ReadPixels`), which is the GPU path anyway |
| Wrap modes, tiling, mip selection, sRGB decode | Done by the sampler for free | Must be reimplemented |
| Speed at 2k–4k with many islands | Milliseconds | Seconds |
| Determinism across GPUs | Small differences (filtering, rounding) | Bit-exact |
| CI without a GPU (`-nographics`) | Not available; "Unity doesn't initialize the graphics device" ([CLI docs](https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html)) | Works |

### Recommendation

**Rec (bake algorithm), per merge group and per atlased texture slot:**

1. Allocate an `RGBA32` (or `ARGBHalf` for HDR emission) `RenderTexture`. Use the `sRGB` read/write mode for colour slots and `Linear` for data slots (normal, mask, metallic and smoothness). Choose per slot from adapter metadata or the `ShaderPropertyFlags`/importer `sRGBTexture` hint.
2. Build one "bake mesh" per chart source:
   - Positions = atlas UV mapped to clip space.
   - `TEXCOORD0` = original UV in the source texture's space, *including* tile offset and `_ST` (`uv * ST.xy + ST.zw`).
   - Draw it with `CommandBuffer.SetRenderTarget` + `DrawMesh` + `Graphics.ExecuteCommandBuffer`.
   - The fragment shader samples the source with its own sampler, so Repeat or Clamp and tiling are honoured. It multiplies in per-material tint (`_Color` and equivalents) and writes the result.
   - **Data textures are never tinted.**
3. **Lossless path.** When a chart's scale is exactly 1 (or a power of two) and its offset is pixel-aligned with 0 or 90° rotation, sample with point filtering (`Texture.Load`). The atlas then contains the original texels bit-for-bit. This is the strongest quality claim UV Wizard can make, and it is why the packer prefers power-of-two scales and 90° rotation.
4. **Normal maps.**
   - Decode with the `RGorAG` rule above, renormalise, and write `(x, y, z, 1)` as `xyz*0.5+0.5` into a linear RT.
   - That layout satisfies both the DXT5nm/BC5 unpack path (`a *= r` makes `a = x`) and the Android `XYZ` path. This is my inference from the `Packing.hlsl` code; verify it per shader, since lilToon and Poiyomi have their own unpack helpers.
   - Tangent correctness: if islands are rotated or mirrored *and the mesh's stored tangents are kept*, copying texels unchanged stays correct. The tangent frame belongs to the vertex, not to the atlas. **Rule: never recalculate tangents after repacking, and generate tangents *before* repacking if a mesh has none** (otherwise Unity would derive them from the new, rotated UVs). Inference from the Mesh.tangents docs; needs a visual test.
   - Compress to BC5 on PC and ASTC 4×4 or 5×5 on Android.
5. **Gutter dilation.** Run a jump-flood pass ([Rong & Tan 2006, JFA](https://www.comp.nus.edu.sg/~tants/jfa.html)) or iterative push-pull over the coverage mask. It fills every empty texel with the nearest island colour (normal maps: nearest normal). Then generate mips from the dilated image, so low mips stay island-coloured.
6. **Readback and compression.**
   - Use `AsyncGPUReadback`, or `ReadPixels` into a `Texture2D(linear: !sRGB)`, then `Apply(updateMipmaps: true)`.
   - Then `EditorUtility.CompressTexture`. PC: BC7 for colour with alpha, DXT1/BC1 for opaque colour, BC5 for normals. Android: ASTC 6×6 for colour and 4×4 for normals by default, target ≤1024² (VRChat guidance).
   - Crunch is off by default (VRChat's own warning above).
   - Enable streaming mipmaps via `SerializedObject("m_StreamingMipmaps")` (the pattern NDMF uses to read it). **Writing this field on a non-imported `Texture2D` is unverified; test it.**
7. **Saving.**
   - In NDMF: assign the textures to the new material and let NDMF serialise them; call `ctx.AssetSaver.SaveAsset(tex)` only for early persistence. Never call `SaveAssets` or `Refresh` ([NDMF best practices](https://github.com/bdunderscore/ndmf/blob/main/docfx~/best-practices.md)).
   - In standalone mode: `EncodeToPNG` → write under `Assets/` → set `TextureImporter` (type, sRGB, per-platform overrides). The user can then retune compression.
8. **CPU reference baker.** Write a small, slow, deterministic C# rasterizer with point and bilinear sampling, used only by tests and as a fallback when `SystemInfo.graphicsDeviceType == Null`.

**Rec (compute shaders):** Use plain vertex and fragment rasterization for the bake. That is the rasterizer's job, and it gives clipping, interpolation and mip derivatives. A compute shader is optional for dilation and used-pixel coverage counting. Both have straightforward C#/Burst fallbacks.

---

## 5. Material and shader handling

### Findings

**Fact (Unity APIs):**

- `Shader.GetPropertyCount`, `GetPropertyType` (`ShaderPropertyType.Texture`), `GetPropertyTextureDimension`, `GetPropertyFlags` and `Material.GetTexturePropertyNameIDs` enumerate texture slots generically.
- `ShaderPropertyFlags` includes `NoScaleOffset`, `Normal` ("normalized vector data"), `HDR`, `Gamma`, `MainTexture`, `MainColor`, `PerRendererData` and `NonModifiableTextureData` ([ShaderPropertyFlags](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Rendering.ShaderPropertyFlags.html), [Shader.GetPropertyFlags](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Shader.GetPropertyFlags.html)).
- NDMF's own mip pass uses `GetTexturePropertyNameIDs` this way ([source](https://github.com/bdunderscore/ndmf/blob/main/Editor/VRChat/CheckMipStreamingPass.cs)).

**Fact (AAO ShaderInformation API, since AAO 1.8.0):** ([docs](https://vpm.anatawa12.com/avatar-optimizer/en/docs/developers/shader-information/), [API source](https://github.com/anatawa12/AvatarOptimizer/blob/master/API-Editor/ShaderInformation.cs))

- Shader authors subclass `ShaderInformation` and register it by GUID.
- They report, per texture, the UV channel (`UV0..UV7` or `NonMesh`), the sampler state, and a `Matrix2x3` UV transform (`null` if it is animated or dynamic, for example `_ST` plus lilToon `_ScrollRotate`).
- Without it, AAO "treats a shader conservatively".
- The registry's *read* side is internal. There are only `Register*` methods, so other tools can't consume it without reflection.
- Built-in info exists for Standard, VRChat SDK shaders and lilToon ([Liltoon info](https://github.com/anatawa12/AvatarOptimizer/blob/master/Editor/APIInternal/ShaderInformation.Liltoon.cs)).
- Third-party shaders that register include `s-ilent/scss` and BigiShader (GitHub code search). **I found no registration in the Poiyomi repo.**

**Fact (AAO Merge Material):** Merge Material merges only materials with "exactly the same shader". The atlas layout is **manual** (X, Y, W, H per source). Screen-space textures (MatCap, dither, noise) come from a reference material. "Textures must have a fixed transform, so features like Parallax and UV-Scroll are not supported" ([docs](https://vpm.anatawa12.com/avatar-optimizer/en/docs/reference/merge-material/), AAO 1.9.20).

**Fact (lilToon, MIT, 2.3.4 released 2026-06-25):**

- UV-mapped slots include `_MainTex`, `_Main2ndTex`, `_Main3rdTex`, `_BumpMap`, `_Bump2ndMap`, `_ShadowColorTex`, `_EmissionMap`, `_AlphaMask`, `_OutlineTex`, `_RimColorTex` and masks. `_MainTex` uses `_MainTex_ST` plus `_MainTex_ScrollRotate`.
- `_MatCapTex`, `_DitherTex` and gradation textures are non-mesh ([AAO lilToon info](https://github.com/anatawa12/AvatarOptimizer/blob/master/Editor/APIInternal/ShaderInformation.Liltoon.cs)).
- lilToon ships its own optimizer (`lilOptimizer.cs`, which rewrites input HLSL based on the materials and animation clips used) and a texture baker (`lilEditorTextureBaker.cs`, `ltspass_baker.shader`) ([repo](https://github.com/lilxyzw/lilToon/tree/master/Assets/lilToon)).

**Fact (Poiyomi, MIT, v10.0.25 released 2026-10-11):**

- Each texture has a `{tex}UV` selector (UV0–3, Panosphere, World/Local Pos, Polar, Distorted, Matcap, Screen Space), `{tex}Pan` and `{tex}Stochastic`. The main sampler is a shared `sampler_linear_repeat` ([Poi_Main template](https://github.com/poiyomi/PoiyomiToonShader/blob/master/_PoiyomiShaders/ModularShader/Editor/Poi_FeatureModules/Toon/Poi_Main/VRLTC_PoiMain.poiTemplateCollection)).
- Locking generates "a unique version [of the shader] that removes unused code, defines fixed values for non-animated shader properties". "Renamed (when locked)" properties get a material-name suffix. Auto-locking "runs automatically for any Poiyomi Materials found in an Avatar or World whenever a Build/Upload is triggered" ([Poiyomi locking docs](https://www.poiyomi.com/general/locking)).

**Fact (VRChat Mobile shaders):** Android avatars may use only the SDK's VRChat Mobile shaders ([Quest optimization](https://creators.vrchat.com/platforms/android/quest-content-optimization/)). Toon Standard (SDK 3.8.1+) "can be used on PC, Android and iOS". It supports detail and emission on UV0 or UV1 and packed mask textures, but does not support transparency ([release 3.8.1](https://creators.vrchat.com/releases/release-3-8-1)).

**Fact (alternative strategy):** d4rkAvatarOptimizer (MIT, v4.6.0 released 2026-09-16) merges same-shader materials *without atlasing* by rewriting shaders ("Merged materials with the same shader even if their properties differ"; "Applies a generalized version of 'lock in'"). It warns this "will slightly increase the GPU cost" ([README](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer)). This is not usable on Android, which allows only VRChat Mobile shaders.

### Mergeability rules (Rec)

Two materials can go in one merge group only if **all** of the following hold:

1. Same shader asset. For locked Poiyomi, same *unlocked* source shader and same lock state; in practice, run before locking or group by generated shader.
2. Same `renderQueue`, blend state (`_SrcBlend`, `_DstBlend`, `_ZWrite`, cull) and same enabled keyword set. Opaque never merges with transparent, and cutout merges only with cutout at the same `_Cutoff` (or the cutoff is baked into alpha).
3. Every *non-atlased* property is equal within tolerance: scalar lighting parameters, non-mesh textures (matcap etc.), and stencil. Otherwise either refuse or, if the user opts in, take the reference material's value with a warning. That is AAO's behaviour.
4. No animated material swaps or animated material properties on atlased transforms. Detect this via NDMF `AnimatorServicesContext`.
5. Atlased textures use a single mesh UV channel with a static transform. No scroll or pan (lilToon `_ScrollRotate`, Poiyomi `{tex}Pan`), no stochastic sampling, no parallax.

**Rec (adapter design):**

- Define an `IShaderAdapter` with the same semantics as AAO's `ShaderInformation`: slot → {UV channel, transform, sampler, colour space, tint property, normal-map flag}.
- Ship adapters for Standard, VRChat Mobile Standard Lite / Toon Lit / Toon Standard, lilToon (port the property map from AAO, MIT, with attribution) and Poiyomi (derive slots from the `{tex}UV` and `{tex}Pan` naming convention).
- The generic fallback atlases only `MainTexture`-flagged or `_MainTex`, assumed UV0. That is TTT's documented fallback ([TTT docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)).
- Bake tint into the colour atlas and reset the tint to white on the output material. Bake `_ST` into the bake mesh's source UVs and reset `_ST` to (1,1,0,0).
- **Output material.** On PC, clone a representative source material (same shader) and swap in the atlases. On Android, map to VRChat Mobile Toon Standard or Standard Lite. Poiyomi: run before Poiyomi's auto-lock, or treat locked materials by their generated shader. **I have not verified when Poiyomi's lock runs relative to NDMF's `-11000` callback; test this early.**

---

## 6. Mesh merging (SkinnedMeshRenderer)

### Findings

**Fact:**

- `Mesh.AcquireReadOnlyMeshData` returns a `MeshDataArray` usable "from any thread". It does no copy by default, and must be disposed ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.AcquireReadOnlyMeshData.html)).
- It throws if `isReadable` is false. The Editor-only `MeshUtility.AcquireReadOnlyMeshData` "skips the isReadable check" ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MeshUtility.AcquireReadOnlyMeshData.html)). Avatar FBXs often have Read/Write off, so use the `MeshUtility` variant.

**Fact:** `Mesh.CombineMeshes` documents only submesh merging, transforms and lightmap UVs. Its page says nothing about bones, bindposes or blend shapes ([docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.CombineMeshes.html)), so it is not a skinned-mesh merger.

**Fact (packages in VRChat projects):**

- `com.vrchat.base` 3.10.5 declares UPM dependencies `com.unity.burst 1.8.7`, `com.unity.collections 2.1.4` and `com.unity.mathematics 1.2.6` (official VPM listing, fetched 2026-10-12).
- NDMF itself depends on Collections 2.1.4 and Mathematics 1.2.6 ([package.json](https://github.com/bdunderscore/ndmf/blob/main/package.json)).
- AAO adds `com.unity.burst 1.8.7` to its own `dependencies` ([package.json](https://github.com/anatawa12/AvatarOptimizer/blob/master/package.json)), and TTT depends on Burst 1.8.0 ([package.json](https://github.com/ReinaS-64892/TexTransTool/blob/master/package.json)).
- **So Burst, Jobs, Collections and Mathematics are already present in every VRChat SDK project.** Editor-only use raises no upload concern, because no runtime script ships.

**Fact (AAO):** AAO already merges skinned meshes automatically (`AutoMergeSkinnedMesh`) and merges submeshes that share a material (`MergeMaterialSlots`) in its Optimizing sequence. Merge Material outputs are not merged by Merge Skinned Mesh unless the components are co-located ([OptimizerPlugin.cs](https://github.com/anatawa12/AvatarOptimizer/blob/master/Editor/OptimizerPlugin.cs), [Merge Material docs](https://vpm.anatawa12.com/avatar-optimizer/en/docs/reference/merge-material/)).

### Recommendation

**Rec, v1 scope:**

1. **Per renderer.**
   - Rewrite UVs of the atlased channel. **Split shared vertices.** A vertex used by triangles in different charts, submeshes or materials gets one copy per distinct new UV, with position, normal, tangent, colour, bone weights, other UVs and *every blend-shape frame delta* copied (`GetBlendShapeFrameVertices` / `AddBlendShapeFrame`).
   - Then merge submeshes whose materials are now identical into one submesh (index-buffer concatenation).
   - Use `Mesh.AllocateWritableMeshData` + `ApplyAndDisposeWritableMeshData` with Burst jobs.
   - Register the new mesh via `ObjectRegistry.RegisterReplacedObject`.
   - If AAO is present, call `UVUsageCompabilityAPI` to evacuate the original UV0 into a free channel before rewriting, and use `MeshRemovalProvider` to skip triangles AAO will delete.
2. **Across renderers.** In the NDMF path, **defer to AAO** (or MA) for bone remapping, bindposes and blend-shape name merging. When renderers share one material they become trivially mergeable by AAO. In standalone mode, implement a conservative merger:
   - Union bones by Transform.
   - Remap bone indices.
   - Bindpose = `bone.worldToLocal × renderer.localToWorld`, recomputed against the target root.
   - Union blend shapes by name, with zero deltas for meshes that lack a shape.
   - Refuse renderers with differing animation or toggle bindings.
3. Never recalculate normals or tangents (see §4).

---

## 7. Testing and tooling

### Findings

**Fact:** GameCI `unity-test-runner` is at v4.4.0 (2026-09-09), with v5.0.0-beta.1 (2026-08-18). The action now wraps the `game-ci` CLI ([action.yml](https://github.com/game-ci/unity-test-runner/blob/main/action.yml)). `packageMode` exists but runs Linux-only and needs an explicit `unityVersion` ([docs](https://game.ci/docs/github/test-runner)).

**Fact (licensing):** Personal licences need `UNITY_LICENSE` (the `.ulf` contents), `UNITY_EMAIL` and `UNITY_PASSWORD`. Pro licences use `UNITY_SERIAL` and should return the licence after use ([activation docs](https://game.ci/docs/github/activation)). The `.ulf` is produced via Unity Hub, and the docs warn one "may not have been created" even when Hub shows a licence.

**Fact (precedent):** AAO's CI builds a throwaway project:

- It writes `manifest.json` and `ProjectVersion.txt`.
- It installs `com.vrchat.avatars` and NDMF with `vrc-get` (`anatawa12/sh-actions/setup-vrc-get`).
- It runs a matrix over Unity 2022.3.6f1 and 22f1, VRChat SDK 3.7–3.10.x, a **no-SDK** leg, an **oldest-NDMF** leg and Unity 6000.x legs, pinning `com.unity.test-framework 1.1.33` ([gameci.yml](https://github.com/anatawa12/AvatarOptimizer/blob/master/.github/workflows/gameci.yml)).

**Fact:** `-nographics` means "Unity doesn't initialize the graphics device" ([CLI args](https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html)). **I could not verify whether GameCI's Linux containers run with `-nographics` or provide a usable GPU or software renderer.** Treat GPU baking in CI as unavailable until proven.

**Fact:** Unity's Graphics Test Framework (`ImageAssert.AreEqual`, reference images per colour space, platform and graphics API) is 8.9.1-exp.1, i.e. experimental ([docs](https://docs.unity3d.com/Packages/com.unity.testframework.graphics@8.9/manual/index.html)).

**Fact (release):** The repo uses VRChat's `template-package` workflows. `release.yml` zips the package and makes a `.unitypackage` and GitHub release. `build-listing.yml` runs `vrchat-community/package-list-action` (`BuildRepoListing`) and deploys `Website/` to Pages ([template-package README](https://github.com/vrchat-community/template-package)).

**Fact (C# level):** Unity 2022.3 uses C# 9.0, except init-only setters, covariant returns, module initializers and a couple of others ([docs](https://docs.unity3d.com/2022.3/Documentation/Manual/CSharpCompiler.html)).

### Recommendation

**Rec, tests:**

- **EditMode, pure C#, CI-safe.**
  - Island builder: seams, welded UVs, bow-ties, mirrored and stacked islands, tiling and UDIM.
  - Packer invariants: no overlap at packing resolution, padding respected, everything inside bounds, deterministic for a fixed seed.
  - UV rewrite: vertex split count, blend-shape delta preservation, bone weights.
  - Material grouping rules.
  - Shader adapters, using real lilToon, Poiyomi and VRChat Mobile shaders only when the packages are installed (`versionDefines`).
- **Golden atlases.** Run the CPU reference baker in CI and compare against checked-in PNGs with tolerances: max-abs-diff and PSNR thresholds, with a masked comparison that ignores gutters. Run GPU-vs-CPU comparison tests locally, or on a self-hosted GPU runner, tagged `[Category("GPU")]`. Avoid depending on the experimental Graphics Test Framework; a 50-line comparer is enough.
- **NDMF integration.** Construct a `BuildContext` on a test avatar prefab, run the passes, and assert with `ErrorReport.CaptureErrors`.
- **Benchmark corpus.** A few CC0 avatars (for example VRoid-exported test models) plus synthetic meshes. Track packing efficiency, atlas PSNR versus the source, and wall time. This is the evidence for "better than grid or TTT".

**Rec, CI:** Copy AAO's pattern (GameCI v4 + `vrc-get`, throwaway project) with the matrix {2022.3.22f1 × (VRChat SDK latest, no-SDK)} plus one experimental 6000.0.x leg. Keep the existing release and listing workflows. Add `"license": "MIT"` to `package.json` (VCC "highly recommend[s]" a licence; see the [VPM packages docs](https://vcc.docs.vrchat.com/vpm/packages)) and `vpmDependencies` for NDMF.

**Rec, code layout and style:**

```
Packages/com.clnelson888.uv-wizard/
  package.json                      vpmDependencies: nadena.dev.ndmf; dependencies: com.unity.burst 1.8.7, collections, mathematics
  Runtime/  UVWizard.Runtime.asmdef      (any platform; only the settings component, IEditorOnly)
  Editor/Core/  UVWizard.Core.Editor.asmdef   (Editor; refs Unity.Collections, Unity.Mathematics, Unity.Burst; NO ndmf/vrc)
  Editor/Ndmf/  UVWizard.Ndmf.Editor.asmdef   (Editor; refs nadena.dev.ndmf; versionDefines -> UVW_NDMF; defineConstraints UVW_NDMF)
  Editor/Aao/   UVWizard.Aao.Editor.asmdef    (optional; versionDefines com.anatawa12.avatar-optimizer >=1.8.0 -> UVW_AAO)
  Editor/UI/    (EditorWindow, inspector, standalone bake)
  Editor/Localization/ en-US.po, ja-JP.po
  Shaders/  UVWizardBake.shader, UVWizardDilate.shader (Hidden/)
  Tests~/ or Tests/Editor/  UVWizard.Tests.Editor.asmdef (UNITY_INCLUDE_TESTS)
```

- Namespaces: `UVWizard.Core.{Islands,Packing,Baking,Materials,Meshes}`, `UVWizard.Ndmf` and so on.
- Make classes `internal` by default, with `InternalsVisibleTo` for tests.
- Use `#nullable enable`, as NDMF and TTT do.
- Keep nothing in the global namespace. The current `UVWizard` class is in it.

---

## 8. Language and stack conclusion

| Candidate | Role | Verdict |
|---|---|---|
| **C# (Unity 2022.3, C# 9)** + Burst / Jobs / Collections / Mathematics | Everything: islands, packing, mesh rewrite, orchestration, UI | **Core language.** Burst and Collections are already in every VRChat project. |
| **HLSL shaders** (vertex + fragment, built-in RP compatible, no pipeline includes) | Atlas bake, normal decode and encode, dilation | **Yes.** |
| **Compute shaders** | Optional: dilation (JFA), coverage counting | Optional, with a C# fallback. |
| **Native C/C++ (xatlas)** | Packing | **Not in v1.** Editor DLLs can't be unloaded, macOS signing and notarization are a burden, a 3-OS build is required, and it does not natively do overlap sharing or per-chart weights. Port the ideas to C# (MIT, with attribution). |
| **Rust** | — | **No.** Same native-plugin costs as C++ with no ecosystem gain for this problem. |
| **Blender companion** | Offline re-UV or re-pack for power users | **No for v1.** It would fork the workflow and pull in GPL code paths. Blender's packer is good *reference* reading. |

**Recommended stack:** C# 9 + Burst 1.8 + Collections 2.1 + Mathematics 1.2, HLSL bake shaders, NDMF ≥1.8 (tested against 1.14.8 and 1.15 beta), optional AAO ≥1.8 interop, Unity Test Framework 1.1.33, GameCI v4 + vrc-get for CI, and the existing VRChat package-list-action for distribution.

---

## Proposed architecture sketch

```
            ┌──────────────── Front ends ────────────────┐
            │ NDMF plugin (Optimizing, before AAO)       │  Standalone "Bake to Assets" (Undo, writes PNG/asset)
            │  - AnimatorServicesContext → animated mats │
            │  - AAO adapter (evacuate UV, removals)     │
            │  - IRenderFilter preview (low-res atlas)   │
            │  - ErrorReport + Localizer                 │
            └──────────────────────┬─────────────────────┘
                                   ▼  AtlasRequest (renderers, settings, platform, IReporter, IAssetSink)
 UVWizard.Core.Editor
 1 Collect    renderers → (renderer, submesh, material) slots; skip excluded/animated/unsupported
 2 Classify   IShaderAdapter per material → SlotInfo {uvChannel, transform, sampler, sRGB, tint, isNormal}
              → MergeGroups (shader, queue, blend, keywords, non-atlased props equal)
 3 Islands    MeshData → union-find per (textureSet, uvChannel) → Islands → overlap groups → Charts
              chart demand = max_tex(rasterized texel area) × content factor; UDIM tile split out
              (optional) drop triangles AAO will remove
 4 Pack       global-scale binary search { RasterPacker | MaxRectsPacker } → ChartPlacement {offset, rot90, scale}
 5 Bake       per slot: bake mesh (atlasUV→clip, srcUV incl. _ST/tile) → RT (sRGB/linear)
              → tint/normal decode → JFA dilation → mips → readback → CompressTexture(platform)
 6 Rewrite    new Mesh: split boundary vertices, write atlas UV (+tile), merge same-material submeshes
              new Material: clone representative (PC) or VRChat Mobile Toon Standard (Android); reset tint/_ST
 7 Emit       assign to renderer; ObjectRegistry.RegisterReplacedObject; AssetSaver / AssetDatabase
```

The data model is plain C# structs and `NativeArray`s between stages (`IslandSet`, `ChartSet`, `PackResult`, `BakePlan`). Stages 3–4 are pure and Burst-friendly, so they are fully unit-testable without a GPU. Stage 5 is the only GPU stage, and it has a CPU reference implementation.

**Caching (Rec):** NDMF re-runs on every play-mode entry and build. Hash (mesh asset GUIDs and versions, material property hashes, texture `imageContentsHash`, settings) to cache `PackResult` and the baked atlases in `Library/UVWizardCache`. The NDMF README lists cross-build asset caching as *planned*, not available ([NDMF README](https://github.com/bdunderscore/ndmf)).

---

## Risks and open questions

1. **Differentiation (high).** TexTransTool AtlasTexture already does island relocation with lilToon support. AAO 1.9 has (manual) Merge Material. VRChat's official SDK optimizer, which will atlas and merge materials, is "in progress" (Canny post 2025-11-18; no closed beta confirmed by 2026-10). It will initially support only Standard Lite and Toon Standard ([Canny](https://feedback.vrchat.com/avatar-30/p/automatic-avatar-optimization-in-sdk), [Ask forum thread](https://ask.vrchat.com/t/any-news-on-the-built-in-sdk-avatar-optimization-tool/47650)). **Before writing code, decide which of these UV Wizard does better:** fully automatic texel-usage sizing, lossless texel copies, overlap sharing, Poiyomi support, or Quest conversion. Then build a benchmark to prove it.
2. **Unity 6 SDK migration (medium).** The client is on 6000.0 and the SDK is not; no date is given. NDMF reportedly doesn't compile on 6000.5. Keep to APIs stable across 2022.3 and 6000.x, and add a 6000.0 CI leg.
3. **Poiyomi locking order (medium, unverified).** When Poiyomi's auto-lock runs relative to NDMF's `-11000` preprocess hook, and whether atlasing should run on locked or unlocked materials. Test on a real avatar.
4. **Normal-map correctness (medium).** The "copy texels, keep stored tangents" rule is inferred. Verify visually with rotated and mirrored islands, with lilToon, Poiyomi and VRChat Mobile shaders, and with meshes that lack imported tangents.
5. **Streaming mipmaps on generated textures (low, unverified).** Whether `m_StreamingMipmaps` can be set on a non-imported `Texture2D` saved as a sub-asset, and whether the VRChat SDK accepts it.
6. **GPU in CI (low, unverified).** Whether GameCI containers can run the GPU bake. The plan assumes they can't.
7. **Tiling and scrolling materials (product decision).** Excluding them is safe but reduces the merge rate. Measure how common they are on real avatars.
8. **Atlas size versus Quest texture-memory ranks.** Memory limits are tight: 10 MB is Excellent on Android. The atlas planner should target a memory budget, not just a size.
9. **Licensing hygiene.** Blender's packer is GPL. Read it, but don't copy it. xatlas, TexTransCore, AAO and juj's code are MIT or public domain; keep attribution in `THIRD_PARTY_NOTICES.md` if porting. TTT's image assets have extra redistribution limits ([TTT LICENSE](https://github.com/ReinaS-64892/TexTransTool/blob/master/LICENSE.md)).
10. **AAO API stability.** The ShaderInformation registry can't be read from outside AAO, and `MeshRemovalProvider` is advisory ("prediction might be wrong" if other tools run in between). Wrap both behind the optional `Aao.Editor` assembly.

**Things I could not verify:**

- UVPackmaster internals.
- Box Cutter's algorithm details beyond the abstract and project page.
- MikkTSpace documentation (the site failed TLS).
- Whether a VRChat SDK optimizer beta has started in the last few weeks.
- GameCI's graphics setup.
- The `RectangleBinPack.CSharp` repo's licence file.
- The exact Blender version in which each Pack Islands option appeared. The manual read was the current `main` branch.

---

## References

**VRChat**

- Current Unity version: https://creators.vrchat.com/sdk/upgrade/current-unity-version/
- Dev Update 2026-06-25 (Unity 6 beta): https://ask.vrchat.com/t/developer-update-25-june-2026/48618
- VRChat 2026.3.3 release notes: https://docs.vrchat.com/docs/vrchat-202633
- Performance ranks: https://creators.vrchat.com/avatars/avatar-performance-ranking-system/
- Quest content optimization: https://creators.vrchat.com/platforms/android/quest-content-optimization/
- Allowed avatar components: https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components
- Build pipeline callbacks (IEditorOnly): https://creators.vrchat.com/sdk/build-pipeline-callbacks-and-interfaces
- SDK 3.8.1 release (Toon Standard): https://creators.vrchat.com/releases/release-3-8-1
- Automatic Avatar Optimization (Canny): https://feedback.vrchat.com/avatar-30/p/automatic-avatar-optimization-in-sdk
- Built-in optimizer forum thread: https://ask.vrchat.com/t/any-news-on-the-built-in-sdk-avatar-optimization-tool/47650
- VPM package format: https://vcc.docs.vrchat.com/vpm/packages
- Template package: https://github.com/vrchat-community/template-package
- package-list-action: https://github.com/vrchat-community/package-list-action
- Official VPM listing: https://packages.vrchat.com/official?download

**NDMF, MA, AAO, TTT and other tools**

- NDMF repo: https://github.com/bdunderscore/ndmf
- NDMF docs: https://ndmf.nadena.dev/
- NDMF execution model: https://github.com/bdunderscore/ndmf/blob/main/docfx~/execution-model.md
- NDMF best practices: https://github.com/bdunderscore/ndmf/blob/main/docfx~/best-practices.md
- NDMF versioning policy: https://github.com/bdunderscore/ndmf/blob/main/docfx~/versioning-policy.md
- NDMF BuildPhase.cs: https://github.com/bdunderscore/ndmf/blob/main/Editor/API/Attributes/BuildPhase.cs
- NDMF ErrorReport.cs: https://github.com/bdunderscore/ndmf/blob/main/Editor/ErrorReporting/ErrorReport.cs
- NDMF IRenderFilter.cs: https://github.com/bdunderscore/ndmf/blob/main/Editor/PreviewSystem/IRenderFilter.cs
- NDMF IAssetSaver.cs: https://github.com/bdunderscore/ndmf/blob/main/Editor/API/Serialization/IAssetSaver.cs
- NDMF ObjectRegistry.cs: https://github.com/bdunderscore/ndmf/blob/main/Editor/API/ObjectRegistry.cs
- Modular Avatar plugin definition: https://github.com/bdunderscore/modular-avatar/blob/main/Editor/PluginDefinition/PluginDefinition.cs
- AAO OptimizerPlugin.cs: https://github.com/anatawa12/AvatarOptimizer/blob/master/Editor/OptimizerPlugin.cs
- AAO ShaderInformation docs: https://vpm.anatawa12.com/avatar-optimizer/en/docs/developers/shader-information/
- AAO Merge Material docs: https://vpm.anatawa12.com/avatar-optimizer/en/docs/reference/merge-material/
- AAO API-Editor source: https://github.com/anatawa12/AvatarOptimizer/tree/master/API-Editor
- AAO CI workflow: https://github.com/anatawa12/AvatarOptimizer/blob/master/.github/workflows/gameci.yml
- TexTransTool: https://github.com/ReinaS-64892/TexTransTool
- TTT AtlasTexture docs: https://ttt.rs64.net/docs/Reference/AtlasTexture
- TexTransCore: https://github.com/ReinaS-64892/TexTransCore
- d4rkAvatarOptimizer: https://github.com/d4rkc0d3r/d4rkAvatarOptimizer
- lilToon: https://github.com/lilxyzw/lilToon
- Poiyomi Toon Shader: https://github.com/poiyomi/PoiyomiToonShader
- Poiyomi locking docs: https://www.poiyomi.com/general/locking

**Packing and algorithms**

- xatlas: https://github.com/jpcy/xatlas
- XAtlasSharp: https://github.com/vicfergar/XAtlasSharp
- XatlasLightmap: https://github.com/z3y/XatlasLightmap
- juj/RectangleBinPack: https://github.com/juj/RectangleBinPack
- RectangleBinPack.CSharp (NuGet): https://www.nuget.org/packages/rectanglebinpack.csharp
- RectpackSharp: https://github.com/ThomasMiz/RectpackSharp
- Blender uv_pack.cc: https://projects.blender.org/blender/blender/src/branch/main/source/blender/geometry/intern/uv_pack.cc
- Blender manual, UV editing: https://projects.blender.org/blender/blender-manual/src/branch/main/manual/modeling/meshes/uv/editing.rst
- UVPackmaster: https://superhivemarket.com/products/uvpackmaster
- Box Cutter (SIGGRAPH 2018): https://www.cs.ubc.ca/labs/imager/tr/2018/BoxCutter
- Nöll & Stricker, EGSR 2011: https://diglib.eg.org/items/afd8b777-0553-497c-9760-a89de28d91aa/full
- Bennell & Oliveira, EJOR 2008: https://cs-gent.cs.kuleuven.be/vakgroepcw/sites/cs-gent.cs.kuleuven.be/files/nesting_problems_tutorialEJOR-184-2008.pdf
- Jump Flooding Algorithm: https://www.comp.nus.edu.sg/~tants/jfa.html

**Unity**

- C# compiler (2022.3): https://docs.unity3d.com/2022.3/Documentation/Manual/CSharpCompiler.html
- Assembly definitions: https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html
- Texture2D.PackTextures: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D.PackTextures.html
- Texture.isReadable: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture-isReadable.html
- Graphics.Blit: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Graphics.Blit.html
- RenderTextureReadWrite: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RenderTextureReadWrite.html
- EditorUtility.CompressTexture: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorUtility.CompressTexture.html
- NormalMapEncoding: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/NormalMapEncoding.html
- PlayerSettings.SetNormalMapEncoding: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PlayerSettings.SetNormalMapEncoding.html
- Packing.hlsl (Unity Graphics): https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl
- Mesh.tangents: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh-tangents.html
- ShaderPropertyFlags: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Rendering.ShaderPropertyFlags.html
- Shader.GetPropertyFlags: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Shader.GetPropertyFlags.html
- Mesh.AcquireReadOnlyMeshData: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.AcquireReadOnlyMeshData.html
- MeshUtility.AcquireReadOnlyMeshData: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MeshUtility.AcquireReadOnlyMeshData.html
- Mesh.CombineMeshes: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.CombineMeshes.html
- Plug-in Inspector (native plug-ins): https://docs.unity3d.com/2022.3/Documentation/Manual/PluginInspector.html
- Command line arguments: https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html
- Graphics Test Framework: https://docs.unity3d.com/Packages/com.unity.testframework.graphics@8.9/manual/index.html
- Render pipelines strategy 2026: https://unity.com/topics/render-pipelines-strategy-for-2026
- Unity 6.5 upgrade guide: https://docs.unity3d.com/Manual/UpgradeGuideUnity65.html
- Draco for Unity troubleshooting (macOS unsigned bundles): https://docs.unity3d.com/Packages/com.unity.cloud.draco@5.0/manual/troubleshooting.html

**CI**

- GameCI test runner: https://game.ci/docs/github/test-runner
- GameCI activation: https://game.ci/docs/github/activation
- unity-test-runner action.yml: https://github.com/game-ci/unity-test-runner/blob/main/action.yml
