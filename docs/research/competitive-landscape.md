# UV Wizard: competitive landscape

Research date: 2026-10-12. Scope: tools that merge several materials on a model into fewer materials backed by a texture atlas, especially for VRChat avatars (Unity 2022.3, VCC/VPM), plus packers and bakers outside Unity that are worth learning from.

**Labels used throughout**

- **[F] Observed fact.** Read directly in source code, official docs, changelogs, release pages, issue trackers or store pages. A URL is given.
- **[I] Inference.** My interpretation of the facts. It may be wrong.
- **[R] Recommendation** for UV Wizard.
- **[U] Unverified.** I looked but could not confirm it from a primary source.

Source code links are pinned to the commit I read (TexTransTool `741b7dc3febc`, TexTransCore `d2d0db32cb60`, AAO `a415011c0cda`, d4rkAvatarOptimizer `d61f29341477`, Material Combiner `eed9ca25baff`, Blender `e2c18d2375b3`). GitHub star counts, release dates and per-release download counts were read through the GitHub API on 2026-10-12.

**Baseline: UV Wizard today.** [F] The prototype (`Packages/com.clnelson888.uv-wizard/Editor/UVWizard.cs`, package version 0.1.1) is a `MonoBehaviour` that calls `Texture2D.PackTextures` on each material's `mainTexture`, remaps each submesh's UVs linearly into the returned rect, and assigns a new `Standard` material. [F] `PackTextures` packs one rectangle per input texture and, if the inputs don't fit `maximumAtlasSize`, "they will be scaled down to fit". It returns RGBA32 unless every input shares a compatible compressed format ([Unity 2022.3 docs](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture2D.PackTextures.html)). [I] In short, the current code uses rect-of-whole-texture packing. It doesn't yet implement the island packing the README promises.

---

## 1. Summary

- **[I] The core idea is already shipped by TexTransTool (TTT) AtlasTexture.** It finds UV islands with union-find, merges overlapping and mirrored islands, packs the island bounding boxes with a shelf packer that allows 90° rotation (NFDH plus "floor-ceiling"), searches for the largest uniform scale that fits, copies the island rectangles on the GPU, and rewrites the UVs. All of this runs non-destructively at NDMF build time, for lilToon, Standard, VRChat Standard Lite and Toon Lit ([source](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasTexture.cs#L406-L407), [packer](https://github.com/ReinaS-64892/TexTransCore/blob/d2d0db32cb60/src/Island/IslandRelocator/NFDHPlasFC.cs), [docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)). "Island packing instead of a grid" is therefore not new on its own.
- **[I] TTT and AAO together already deliver "one material, one mesh".** TTT AtlasTexture merges the materials when a merge reference is set ([docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)). AAO's Trace & Optimize then merges skinned meshes and material slots automatically, and it is blendshape-aware ([AAO T&O docs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/trace-and-optimize/index.md), [Merge Skinned Mesh](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/merge-skinned-mesh/index.md)). The pipeline still takes setup work: you add a component, pick the materials, set a merge reference, and tune the TextureFineTuning list.
- **[F] AAO's own island packer ("Optimize Texture") is not a material merger.** It only repacks the islands of textures that are already shared inside one connected group of materials. It gives up unless the islands cover less than 50% of the texture, never rotates, and copies compressed blocks without re-encoding ([source](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L1072-L1165)). AAO's cross-material tool ("Merge Material") places whole textures by hand into X/Y/W/H rects ([docs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/merge-material/index.md)).
- **[F] d4rkAvatarOptimizer doesn't atlas at all.** It merges materials by rewriting shaders and building `Texture2DArray`s from same-size, same-format textures ([README](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/blob/d61f29341477/README.md)). It never generates shaders on Android ([CHANGELOG v3.0.0](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/blob/d61f29341477/CHANGELOG.md)), and texture arrays break the in-game fallback shader ([issue #17](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/17)). [I] Real atlasing that stays fallback-safe and works on Quest is the thing d4rk structurally can't do.
- **[F] The biggest strategic threat is VRChat's own SDK "Avatar Optimizer".** It was announced to auto-generate atlases and merge meshes and materials on build. It was "in progress" (closed-beta goal) in Nov 2025, initially for Standard Lite and Toon Standard only ([VRChat Canny](https://feedback.vrchat.com/avatar-30/p/automatic-avatar-optimization-in-sdk)). [F] It does not appear in the SDK release notes up to 3.10.5 (2026-09-04) ([SDK releases](https://creators.vrchat.com/releases/)).
- **[I] Genuinely open gaps for UV Wizard:**
  1. A zero-config "make it Quest Good/Excellent" button. Quest's Excellent and Good ranks require 1 material slot, 1 skinned mesh and ≤10/18 MB of texture memory ([VRChat ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)). That needs automatic texture-budget sizing plus baking the material color into the texture.
  2. Tighter packing than axis-aligned bounding boxes: convex or exact shapes and free rotation, as in Blender ≥3.6 and xatlas.
  3. Correct handling of UVs outside 0–1 (still an open TTT bug, [#1056](https://github.com/ReinaS-64892/TexTransTool/issues/1056)).
  4. Rotating normal-map vectors when an island is rotated (open TTT bug, [#1106](https://github.com/ReinaS-64892/TexTransTool/issues/1106)).
  5. Support for Toon Standard and Poiyomi. TTT's shader table lacks both ([source](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AAOCode/ShaderInformation.VRCSDK.cs)).
  6. Clear English-first UX and reporting. TTT's docs and changelog are Japanese-first.
- **[R] Positioning.** UV Wizard should not pitch "island packing" as its novelty. It should pitch "one button → Quest-ready single material, with island-level, texel-density-preserving packing that handles tiling, mirrored UVs and normal maps correctly". Build it as an NDMF plugin so it composes with AAO and Modular Avatar instead of competing with them.
- **[R] Borrow freely from the MIT code.** Usable sources include TTT's union-find island builder (AAO already copied it, with attribution) ([AAO source](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L1192-L1196)), AAO's tile-shift and block-copy tricks, and the xatlas packer design. Don't copy GPL code (Blender's packer, Material Combiner) into an MIT package; re-implement from the description instead.

---

## 2. Comparison table

"Packing approach" uses: **grid** (equal cells), **whole-texture rect** (one rectangle per material/texture), **UV-bounds rect** (one rectangle per material, cropped to the UV extent), **island AABB** (one bounding box per UV island), **island shape** (convex or exact outline).

| Tool | Platform | Packing approach | Non-destructive? | Shaders | Extra channels | Mesh merge | License | Last release | Notes |
|---|---|---|---|---|---|---|---|---|---|
| **UV Wizard (today)** | Unity 2022.3, VPM | whole-texture rect (`PackTextures`) | No (runtime MonoBehaviour) | Standard output only | mainTexture only | No | MIT | 0.1.1 (prototype) | Hackathon prototype |
| **TexTransTool AtlasTexture** | Unity 2022.3, VPM, NDMF | **island AABB**, 90° rotation, NFDH+FC shelf, GPU copy | Yes (NDMF, Optimizing phase) | lilToon, Standard, VRC Standard Lite, VRC Toon Lit; others: `_MainTex` on UV0 only | Yes, every texture property using the target UV channel; per-property resize, compress, merge | No (meshes replaced, renderers kept); relies on AAO | MIT (logo images excepted) | v1.0.2 (2026-06-26); v1.1.0-beta.9 (2026-06-26) | Closest competitor; open bugs: tiling, normal-map rotation, CustomAspect |
| **AAO: Optimize Texture (T&O)** | Unity 2022.3, VPM, NDMF | **island AABB**, shelf, no rotation, block-aligned, lossless block copy | Yes | Built-in Standard etc., lilToon, VRC Standard Lite/Toon Lit/Toon Standard; third-party via API | All textures in the connected group | n/a (shrinks textures only) | MIT | v1.9.20 (2026-10-01) | Does **not** merge different materials |
| **AAO: Merge Material** | same | whole-texture rect, **manual** X/Y/W/H | Yes | same ShaderInformation set | Yes, per-property size/format | Use with Merge Skinned Mesh | MIT | v1.9.20 | `MapUV` uses `uv % 1`, so tiling is lost |
| **AAO: Merge Skinned Mesh / T&O** | same | n/a | Yes | any | n/a | **Yes**, skinned and blendshape-aware, merges material slots | MIT | v1.9.20 | Pairs naturally with an atlaser |
| **d4rkAvatarOptimizer** | Unity, VPM | **no atlas**: Texture2DArray + shader rewrite | Yes on upload (also "Create Optimized Copy") | Custom shaders it can parse (e.g. Poiyomi); not surface or tessellation shaders | Arrays need equal size and format | **Yes**, skinned, blendshape merging | MIT | v4.6.0 (2026-09-16) | No shader generation on Android; arrays break fallback |
| **NDMF** | Unity, VPM | platform | Yes (the framework) | n/a | n/a | n/a | MIT | 1.14.8 (2026-08-29) | Phases: Resolving, Generating, Transforming, Optimizing |
| **Modular Avatar** | Unity, VPM, NDMF | none | Yes | n/a | n/a | No | MIT (images excepted) | 1.18.7 (2026-09-01) | Material swaps and reactive components interact with atlasing |
| **lilAvatarUtils / lilToon baker** | Unity | none (analysis); lilToon bakes color layers into `_MainTex` | Analysis only; baker writes new textures | lilToon | n/a | No | MIT | lilAvatarUtils 2.1.2 (2025-08-11); lilToon 2.3.4 (2026-06-25) | Useful as a "flatten before atlas" step |
| **Polytool** (Markcreator) | Unity | **[U]** unknown; README image suggests rect-based | Works on copies | **[U]** | **[U]** | Yes, skinned and static, keeps blendshapes | Proprietary, $25 | **[U]** | 4.7★ from 454 ratings on Gumroad |
| **VRCFury** | Unity, VPM | none | Yes | n/a | n/a | No (blendshape optimizer, FX optimizer) | Custom personal/commercial licence | 1.1434.0 (2026-10-12) | Not a competitor |
| **Thry Avatar Performance Tools / ThryEditor** | Unity, VPM | none (VRAM checker; RGBA channel packer) | n/a | Poiyomi inspector | channel packing | No | MIT | 1.3.7 (2024-08-05) | Measurement, not optimization |
| **Poiyomi** | shader | none; UV Tile Discard uses UV0–UV3 tiles | n/a | Poiyomi | n/a | n/a | n/a | n/a | Atlasers must not touch discard UV channels |
| **VRChat SDK Avatar Optimizer** | VRChat SDK | **[U]** not public | On build/upload | Standard Lite, Toon Standard first | **[U]** | Yes (announced) | proprietary | not shipped as of SDK 3.10.5 | Strategic threat |
| **Mesh Baker** | Unity Asset Store | rect per material, cropped to used UV region; tiling can be baked; 5 packers; or texture arrays | Yes (new assets) | any, by property names | Yes, one atlas per texture property | Yes, skinned, blendshapes | Commercial $79 | 3.40.2 (2026-06-26) | General-purpose, not NDMF-aware |
| **Super Combiner** | Unity Asset Store | rect per material ([U] details) | Yes (claimed) | **[U]** | **[U]** | Yes, skinned, blendshapes | Commercial $19.90 | 1.7.0 (2024-03-20) | Low activity |
| **Pro Draw Call Optimizer** | Unity Asset Store | rect per material | Yes (claimed) | **[U]** | **[U]** | static (skinned **[U]**) | Commercial $49.95 | 4.4 (2018-03-14) | UVs outside 0–1 unsupported; stale |
| **Material Combiner** (shotariya) | Blender add-on | whole-texture rect (×UV tile extent), optional crop; bin-tree / MaxRects / rectpack2D | Edits .blend | Blender materials | Diffuse only now (multi-map only in old 2.0.3.3) | No (Blender joins separately) | GPL-3.0 | 2.1.2.9 (2023-11-20); last commit 2025-11-17 | Standard VRChat community answer |
| **CATS / successors** | Blender | delegates to Material Combiner | Edits .blend | n/a | n/a | Yes (join meshes) | MIT (orig.) / GPL-3.0 (Avatar Toolkit) | orig 0.19.0 (2021); Neoneko 5.0.2.3 (2026-01-24, now unmaintained) | Fragmented |
| **Tuxedo Blender Plugin** | Blender | **re-bake** onto a fresh UV (UVPackmaster if present) | Bakes copies | Blender PBR | Yes, full PBR passes | Yes | GPL-3.0 | last push 2025-11-24 | Bake-everything approach |
| **UVPackmaster 4** | Blender/Maya | island shape, GPU (CUDA/Vulkan) | n/a (UV tool) | n/a | n/a | n/a | Commercial ~$53 | 4.0 (Jan 2026) | Best-in-class packing |
| **Blender built-in Pack Islands** | Blender ≥3.6 | AABB / convex / exact concave; any or 90° rotation; xatlas-style brute force | n/a | n/a | n/a | n/a | GPL-2.0+ | Blender main | Reference algorithms |
| **xatlas** | C++ library | chart (island) shape packing, brute force or random, rotation, block align | n/a | n/a | n/a | n/a | MIT | no releases; last push 2024-06-16 | Embeddable packer |
| **Unreal Merge Actors** | UE 5.8 | bakes materials onto a **new** UV layout | Creates new assets | UE materials | Yes (Normal, Metallic, Roughness, …) | Yes | proprietary (engine) | UE 5.8 docs | "Merge all materials" doesn't preserve UVs |
| **Simplygon** | SDK/DCC plugins | **Chart Aggregator** repacks original UV charts; `OriginalPixelDensity` mode | Creates new assets | material casting | Yes | Yes | Commercial | SDK 10.x | Conceptually closest to UV Wizard |
| **RizomUV** | standalone | island shape, GPU (CUDA, 2025) | n/a | n/a | n/a | n/a | Commercial (VS €149.90 perpetual) | 2027.0 (Oct 2026), headless/Python | Batch packer option |
| **UV-Packer / Packer-IO** | standalone + plugins | island packing | n/a | n/a | n/a | n/a | Free, closed source | Packer-IO 1.4.0 | No SDK/CLI documented |

Sources for every cell are in the per-tool sections below.

---

## 3. Per-tool analysis

### 3.1 TexTransTool (TTT) AtlasTexture (ReinaS-64892), the closest competitor

**Status and adoption.**
- [F] Repo [ReinaS-64892/TexTransTool](https://github.com/ReinaS-64892/TexTransTool) has 150 stars. Latest stable is v1.0.2 (2026-06-26) and latest prerelease is v1.1.0-beta.9 (2026-06-26). The last commit was 2026-06-26 ([releases](https://github.com/ReinaS-64892/TexTransTool/releases), [CHANGELOG](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/CHANGELOG.md)).
- [F] The latest release asset had been downloaded 22,471 times on 2026-10-12 (GitHub API). [I] This is a lower bound on users, because VCC installs come from `vpm.rs64.net`.
- [F] License: MIT, except logo and image files, which can only be redistributed inside the official package ([LICENSE.md](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/LICENSE.md)).
- [F] The package targets Unity 2022.3 and depends on `net.rs64.tex-trans-core` ^0.2.0 (MIT, [TexTransCore](https://github.com/ReinaS-64892/TexTransCore)) ([package.json](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/package.json)).
- [F] The docs say AtlasTexture's v1.0.0 form should be "the last breaking change". Any future redesign would ship as a separate "AtlasTextureV2", and the Unity version of TTT "will not become v2.0.0" ([docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)). [I] The Unity product looks feature-frozen. The author's recent GitHub activity is mostly unrelated projects such as a Wayland compositor and a WebGPU wrapper (GitHub profile, 2026-10). Expect bug-fix-level maintenance, not big new features.

**How it builds the atlas (source-verified).**

1. **Islands.**
   - [F] Islands are built by union-find over triangles that share UV vertices ([TexTransCore IslandUtility.cs](https://github.com/ReinaS-64892/TexTransCore/blob/d2d0db32cb60/src/Island/IslandUtility.cs)).
   - [F] Islands of the same material group are merged when they share UV-identical vertices across submeshes, or when they "overlap substantially" ([AtlasContext.cs design notes and options `CrossSubMeshUsedIslandMerge`, `OverCrossIslandMerge`](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasContext.cs#L14-L71)).
   - [I] Mirrored or stacked islands are therefore kept together and share texels, not duplicated.
2. **Grouping.**
   - [F] An atlas unit is identified by "Mesh + SubMeshIndex + MaterialGroupID". Materials whose textures don't conflict share a group, to avoid wasting VRAM (same file, design notes).
3. **Initial size = native texel density.**
   - [F] Each island's size is first scaled by `sourceTexture.width / atlasWidth`, with an aspect fix for non-square textures. Islands with no texture are shrunk to 1% ([`PrimaryTextureSizeScaling`](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasContext.cs#L173-L194)).
   - [I] Relative texel density across materials is preserved. A 4K body and a 1K accessory keep their 4:1 pixel ratio.
4. **Packing.**
   - [F] The only implemented relocator is `NFDHPlasFC`. Custom relocators throw `NotImplementedException` ([AtlasTexture.cs L406-L407](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasTexture.cs#L406-L407)).
   - [F] It rotates every island so it's wider than tall (90° only), sorts by height, and fills shelves ("UVWidthBox"). Islands go on both the floor and the ceiling of each shelf ([NFDHPlasFC.cs](https://github.com/ReinaS-64892/TexTransCore/blob/d2d0db32cb60/src/Island/IslandRelocator/NFDHPlasFC.cs)).
   - [F] Packing uses island bounding boxes (`IslandTransform` with Position, Size, Rotation), not island outlines (same file).
5. **Fitting loop.**
   - [F] It first tries native size. If that fails, it lerps islands toward their "size priority" minimum in 5% steps.
   - [F] If that still fails, it scales everything to about half area and grows it by 1% per step until packing fails (up to 2048 iterations). This repeats from smaller start scales if needed ([IslandRelocationManager.cs](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/IslandRelocationManager.cs#L28-L143)).
   - [F] Afterwards the atlas height is trimmed to the smallest power of two that still fits ([AtlasTexture.cs](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasTexture.cs#L170-L183)).
6. **Pixel snapping.**
   - [F] `PixelNormalize` (on by default) snaps island rectangles to whole pixels. It warns if padding is 0 ([AtlasSetting.cs](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasSetting.cs), [AtlasTexture.cs](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasTexture.cs#L426-L432)).
7. **Texture transfer on the GPU.**
   - [F] Every texture property is drawn into a `RenderTexture` by `AtlasingUtility.TransMoveRectangle`. That calls a compute shader that copies each source rectangle, with padding and rotation, into its target rectangle ([AtlasContext.GenerateAtlasedTextures](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasContext.cs#L280-L380), [RectangleTransMapping.ttcomp](https://github.com/ReinaS-64892/TexTransCore/blob/d2d0db32cb60/src/ShaderAssets/AtlasTexture/RectangleTransMapping.ttcomp)).
   - [F] An optional wgpu backend ("TTCE-Wgpu") exists ([CHANGELOG v0.10.7–0.10.8](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/CHANGELOG.md)).
8. **UV rewrite.**
   - [F] Each original triangle's UV is moved relative to its island's source position, rotated, and placed at the target ([`GenerateAtlasedMesh`](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasContext.cs#L196-L275)).
9. **Channels and defaults.**
   - [F] By default, non-main textures are downscaled to 512 except `_MainTex` and lilToon `_BumpMap` and `_Bump2ndMap`.
   - [F] `_MainTex` is reference-copied into `_BaseMap` and `_BaseColorMap`.
   - [F] Normal maps are set linear and compressed at "High" (BC7 on PC). A code comment says BC5 would be better but breaks on ASTC ([AtlasSetting.cs defaults](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasSetting.cs)).
10. **Shaders.**
    - [F] lilToon, Standard, VRCSDK Standard Lite and VRCSDK Toon Lit are supported. Other shaders are treated as "`_MainTex` on UV0 only" ([docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)).
    - [F] The shader tables were adapted from AAO (folder `AAOCode/` with an AAO license file). The VRCSDK table has Standard Lite and Toon Lit but **no Toon Standard** ([ShaderInformation.VRCSDK.cs](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AAOCode/ShaderInformation.VRCSDK.cs)).
    - [F] v1.0.0 removed PropertyBake (baking `_Color` and similar into textures). It moved to a separate lilToon-only tool ([docs](https://ttt.rs64.net/docs/Reference/AtlasTexture)).
11. **Material and mesh merge.**
    - [F] Materials are merged into one only if a merge reference material is assigned. AtlasTexture replaces meshes but does not combine renderers ([docs](https://ttt.rs64.net/docs/Reference/AtlasTexture), [`ReplaceMesh`](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasTexture.cs#L233-L280)).
    - [I] Draw-call reduction happens when AAO's Merge Skinned Mesh or T&O later merges the slots that now share one material.

**Strengths.**
- [I] Real island-level packing that preserves texel density.
- [I] Overlap and mirror merging.
- [I] Fast GPU texture transfer.
- [I] NDMF preview and non-destructive builds.
- [I] Per-island size priority through island selectors.
- [I] Mature handling of lilToon's many texture slots.
- [I] Users already trust it next to AAO.

**Limits and contrary evidence.**
- [F] UVs outside 0–1 aren't handled correctly. Issue [#1056](https://github.com/ReinaS-64892/TexTransTool/issues/1056) (open, 2025-08-20) shares a root cause with #918. A user asked for a "clamp UVs to 0–1" toggle as a workaround.
- [F] Rotated islands don't rotate the normal vectors in normal maps ([#1106](https://github.com/ReinaS-64892/TexTransTool/issues/1106), open, 2026-07-14). The author suspects this explains "many AtlasTexture normal-breaking reports". Related: [#1096](https://github.com/ReinaS-64892/TexTransTool/issues/1096), open, and the earlier closed [#578](https://github.com/ReinaS-64892/TexTransTool/issues/578).
- [F] With CustomAspect, a 4096×2048 atlas came out 4096×1024 with UV.y up to 1.955 ([#1116](https://github.com/ReinaS-64892/TexTransTool/issues/1116), open, 2026-08-23, v1.0.2).
- [F] Quality drops even at equal atlas size. The author called this "close to spec" and pointed to the internal RenderTexture precision setting ([#975](https://github.com/ReinaS-64892/TexTransTool/issues/975)).
- [F] It is incompatible with MA Material Swap ([#1044](https://github.com/ReinaS-64892/TexTransTool/issues/1044), open).
- [F] It wrongly reports "no UV" when the mesh isn't Read/Write enabled ([#1084](https://github.com/ReinaS-64892/TexTransTool/issues/1084), open).
- [I] Packing quality is capped by AABB shelf packing with 90° rotation. L-shaped or diagonal islands waste their bounding-box area.
- [I] The UX is a per-avatar component with a material list and a long TextureFineTuning list. It isn't one button, and the docs are Japanese-first.

**Lessons for UV Wizard.**
- [R] Treat TTT as the bar to beat. Benchmark against it on the same avatars, measuring atlas fill %, effective texels per island, and visual diff.
- [R] Fix exactly where TTT is weak:
  - Tiling. Shift islands that sit in a single tile to tile 0, AAO-style; bake tiling for islands that span tiles.
  - Rotation-aware normal-map transfer (rotate the tangent-space XY by the island rotation).
  - Exact-shape packing.
  - Toon Standard and Poiyomi tables.
  - Respecting Read/Write-disabled meshes. Read them through `Mesh.AcquireReadOnlyMeshData`. [I] Under NDMF the build works on copies, so this is feasible.
- [R] Copy TTT's good ideas (MIT): density-preserving initial scale, overlap-merge heuristics, the iterative scale search, and power-of-two height trimming.

### 3.2 Avatar Optimizer (AAO, anatawa12)

**Status.**
- [F] [anatawa12/AvatarOptimizer](https://github.com/anatawa12/AvatarOptimizer): MIT, 374 stars, v1.9.20 (2026-10-01). Latest release asset downloads: 28,299 (GitHub API, 2026-10-12).
- [F] The changelog shows releases roughly monthly through 2026 ([CHANGELOG](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/CHANGELOG.md)).

**Trace & Optimize: "Optimize Texture".**
- [F] Added in v1.8.0 (2024-11-30) ("Avatar Optimizer will pack texture and tries to reduce the VRAM usage") ([CHANGELOG](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/CHANGELOG.md)).
- [F] The docs describe it as optimizing "without affecting the appearance", with UV packing for supported shaders ([T&O docs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/trace-and-optimize/index.md)).
- [F] **Source.** A union-find groups submesh UVs, materials and textures into connected components. Each component is atlased on its own ([OptimizeTexture.cs L102-L420](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs)).
- [F] **Eligibility.** It bails if any material slot or UV usage is unknown, if any UV transform is non-identity (`_ST` tiling or offset), if a material is used by other renderers, or if any texture isn't a `Texture2D` ([L427-L449](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L427-L449)).
- [F] **Tiles.** Every island must sit entirely inside one UV tile. With `Repeat` or `Mirror` wrap, islands from other tiles are allowed and moved to tile 0. With `Clamp` or unknown wrap it bails; `MirrorOnce` allows tiles −1 and 0 ([L676-L730](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L676-L730), [move-to-tile-0](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L990-L1005)).
- [F] **Islands.** It uses TTT's IslandUtility, "Copied from TexTransTool … Licensed under MIT" ([L1192-L1196](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L1192-L1196)). Islands fully contained in another are merged ([L737-L762](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L737-L762)).
- [F] **Packing.**
  - Islands snap to the compression block size, with padding ([L764-L836](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L764-L836)).
  - It only proceeds if total island area is under 0.5 and the largest island is under 0.5 of the texture.
  - It tries power-of-two fractions of the original size from small to large, with a simple shelf packer.
  - There is no rotation ("`//TODO: rotate`") ([L1072-L1172](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L1072-L1172)).
- [F] **Copy.** For non-crunched compressed textures without mipmaps, it copies raw compressed blocks with no re-encode or resample. Otherwise it renders through a helper shader ([L843-L960](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L843-L960)).
- [I] This is a "crop away unused texture space" optimizer. It's island-level, but it never puts two different materials' textures into one atlas, so it doesn't reduce material count.
- [F] Contrary evidence from the changelog: it broke textures used through multiple UV channels (#1700), broke lilToon outline masks (#1357), broke ToonStandard EmissionMap (#1525), and its padding was too small for masks (#1478) ([CHANGELOG](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/CHANGELOG.md)).

**Merge Material (cross-material atlas).**
- [F] It merges materials with the *same shader*, but the user must hand-place each source texture with X/Y/W/H ([docs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/merge-material/index.md)).
- [F] Limits: material-replacing animations, Parallax and UV-scroll are unsupported.
- [F] The UV remap is `uv % 1 * rect.size + rect.pos` ([MergeMaterialProcessor.cs L460-L462](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/SkinnedMeshes/MergeMaterialProcessor.cs#L460-L462)). [I] Islands that cross a tile boundary are torn, the same weakness UV Wizard's prototype has.
- [F] Merge ToonLit Material is the obsolete predecessor ([docs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/merge-toonlit-material/index.md)).

**Merge Skinned Mesh, Remove Unused Objects, and the rest of T&O.**
- [F] T&O automatically:
  - merges skinned meshes that don't need to be separate, optionally shuffling material slots to reduce draw calls;
  - removes unused GameObjects, components and textures ("Remove unused Objects");
  - freezes, removes or merges BlendShapes;
  - optimizes PhysBones and the Animator ([T&O docs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/trace-and-optimize/index.md)).
- [F] Merge Skinned Mesh has BlendShape modes (rename or merge same-name) and merges material slots that share a material ([docs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/merge-skinned-mesh/index.md)).

**Shader support.**
- [F] AAO has a public `ShaderInformation` API ([API-Editor/ShaderInformation.cs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/API-Editor/ShaderInformation.cs)).
- [F] It ships built-in tables for lilToon, Unity built-ins, and VRCSDK Standard Lite, Toon Lit and Toon Standard (with and without outline) ([ShaderInformation.VRCSDK.cs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/APIInternal/ShaderInformation.VRCSDK.cs)).
- [F] Third-party shaders implement it, for example [s-ilent/scss](https://github.com/s-ilent/scss) and [bigibas123/BigiShader](https://github.com/bigibas123/BigiShader) (GitHub code search).
- [U] I found no Poiyomi `ShaderInformation` implementation on public GitHub.

**Lessons.**
- [R] UV Wizard should run *before* AAO's Merge Skinned Mesh in the NDMF Optimizing phase, so AAO can merge the now-identical material slots. AAO's T&O does that automatically.
- [R] Use AAO's `ShaderInformation` API as a read source when it exists. It already describes which UV channel and `_ST` each texture property uses. [I] Integrating with it, rather than writing a fourth shader-table format, gives Toon Standard and third-party shaders for free.
- [R] Borrow the tile-shift rule and the "bail on unknown, don't break appearance" philosophy. Consider lossless block copying when no rescale is needed.

### 3.3 d4rkAvatarOptimizer (d4rkpl4y3r / d4rkc0d3r)

**Status.**
- [F] [d4rkc0d3r/d4rkAvatarOptimizer](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer): MIT, 615 stars, v4.6.0 (2026-09-16). Latest release asset downloads: 41,098, the highest of the Unity tools surveyed (GitHub API).

**How it merges.**
- [F] It merges skinned meshes that are animated the same way, merges identical material slots, and can merge meshes toggled differently by injecting "shader toggles" ([README](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/blob/d61f29341477/README.md)).
- [F] "Merge Different Property Materials" writes per-material values into a cbuffer indexed by a material ID stored in `uv.z`.
- [F] "Merge Same Dimension Textures" "Creates a Texture2DArray from the original textures" when width, height and format match (same README).
- [F] It can't handle surface shaders or tessellation (same README).
- [F] **No atlas is created.** Since v3.0.0, Android builds use a mode that generates no shaders and hides the shader options (["Support for Android build target…"](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/blob/d61f29341477/CHANGELOG.md)).
- [I] On Quest it can only merge meshes and identical materials, never textures.

**Contrary evidence.**
- [F] Texture-array merging breaks the fallback shader (white body parts when shaders are blocked). `_MainTex` merging was made a separate option, off by default, in v3.3.0 ([issue #17](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/17)).
- [F] Shader toggles "fundamentally trade GPU performance for CPU performance". A user measured worse frame times, and another user recommended real atlasing with Material Combiner instead ([issue #92](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/92)).

**Lesson.**
- [I] A real atlas is complementary to d4rk. It survives fallback shaders and works on Quest.
- [R] Document a recommended stack: UV Wizard (atlas) → AAO or d4rk (mesh merge).
- [R] Test that UV Wizard's output stays mergeable by d4rk: same material, no extra UV channels in `uv.z`/`w` that d4rk needs.

### 3.4 NDMF (bdunderscore): the platform

- [F] [bdunderscore/ndmf](https://github.com/bdunderscore/ndmf): MIT, 158 stars, 1.14.8 (2026-08-29).
- [F] It is a framework for non-destructive avatar plugins. It provides phases (Resolving, Generating, Transforming, Optimizing), runs-before/after ordering between plugins, play-mode and build hooks, and temporary asset management ([README](https://github.com/bdunderscore/ndmf), [docs](https://ndmf.nadena.dev)).
- [F] TTT AtlasTexture already runs in NDMF's Optimizing phase and uses NDMF preview ([TTT CHANGELOG v0.10.6](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/CHANGELOG.md), [docs "Phase: Optimizing"](https://ttt.rs64.net/docs/Reference/AtlasTexture)).
- [R] Make UV Wizard an NDMF plugin. Declare `InPhase(BuildPhase.Optimizing).BeforePlugin("com.anatawa12.avatar-optimizer")` (verify the exact plugin ID) and order it explicitly against TTT. Use the NDMF preview system so users see the atlas before upload. Drop the `MonoBehaviour.Start` runtime approach.

### 3.5 Modular Avatar (bdunderscore): relevance only

- [F] [bdunderscore/modular-avatar](https://github.com/bdunderscore/modular-avatar): MIT except images, 813 stars, 1.18.7 (2026-09-01).
- [F] No MA component atlases or merges materials. The reference covers armature, animator, menu, reactive components and mesh settings ([reference index](https://modular-avatar.nadena.dev/docs/reference)).
- [F] MA's material swaps conflict with TTT AtlasTexture ([TTT #1044](https://github.com/ReinaS-64892/TexTransTool/issues/1044)). AAO Merge Material doesn't support animations that replace materials ([docs](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/.docs/content/docs/reference/merge-material/index.md)).
- [R] Detect materials that are the target or source of MA reactive material swaps or animation material swaps, and exclude them from the atlas, or atlas each swap variant consistently.

### 3.6 lilAvatarUtils and lilToon's tools (lilxyzw)

- [F] [lilAvatarUtils](https://github.com/lilxyzw/lilAvatarUtils): MIT, 142 stars, 2.1.2 (2025-08-11). Its editor code has analysis tables for textures, materials, renderers, PhysBones, animation clips and lighting, plus a `MaterialCleaner`. It contains no atlas code (file list in [repo tree](https://github.com/lilxyzw/lilAvatarUtils)).
- [F] lilToon (MIT, 1,588 stars, 2.3.4 on 2026-06-25) has a texture baker. It bakes main color, HSV/gradation and the 2nd and 3rd main layers into `_MainTex`, and has "AutoBake" for main, shadow and matcap ([lilEditorTextureBaker.cs](https://github.com/lilxyzw/lilToon/blob/72fc09625b24/Assets/lilToon/Editor/lilInspector/lilEditorTextureBaker.cs)).
- [I] This is what you need before collapsing a lilToon avatar to a Quest Toon Lit or Toon Standard material that has fewer texture slots.
- [R] For the Quest path, flatten each material's color, tint and layers into its albedo before atlasing, the way lilToon does. Otherwise merged materials lose per-material `_Color`. TTT dropped PropertyBake in v1.0.0, so this is a gap.

### 3.7 Mesh Baker (Digital Opus, Asset Store)

- [F] $79 per seat, v3.40.2 (2026-06-26), 433 reviews, compatible with Unity 2019.4 and 2022.3 on Built-in, URP and HDRP ([Asset Store](https://assetstore.unity.com/packages/tools/modeling/mesh-baker-5017)).
- [F] From the author's feature list ([Unity forum thread](https://discussions.unity.com/t/mesh-baker-by-digital-opus-released/490738)):
  - "Only parts of source textures (atlases) used by source meshes are copied to the target atlas."
  - "Tiled textures can have tiling baked", with the warning that this can produce large atlases.
  - "Generate either Texture Atlases or Texture Arrays."
  - It builds an atlas per texture property.
  - It combines SkinnedMeshRenderers and MeshRenderers into one SkinnedMeshRenderer and can bake blend shapes.
  - "Choose between five different texture packers."
- [F] The free version only supports Diffuse and Bumped Diffuse shaders ([Vket guide](https://vket.com/en/docs/submission_tips_how_to_mesh_baker)).
- [I] The copied region appears to be the used UV rectangle per material or mesh, not individual islands. I couldn't read the closed source to confirm. [U]
- [I] It's general-purpose and not NDMF- or VRChat-shader-aware.
- **Lesson.** [R] Copy the "bake tiling into the atlas" option for islands that span tiles, and its warning about atlas blow-up.

### 3.8 Super Combiner (LunarCatsStudio, Asset Store)

- [F] $19.90, v1.7.0 (2024-03-20), 47 reviews, original Unity 2021.3.22 ([Asset Store](https://assetstore.unity.com/packages/tools/modeling/super-combiner-92129)).
- [U] The store page I could read had no feature text. Claims about tiled-texture support and skinned/blendshape preservation appear only in third-party copies of its manual, which I don't cite as authoritative.
- [I] Low activity. Not a VRChat-specific threat.

### 3.9 Pro Draw Call Optimizer (PencilSquareGames, Asset Store)

- [F] $49.95, v4.4, last release 2018-03-14, original Unity 4.6.2 ([Asset Store](https://assetstore.unity.com/packages/tools/utilities/pro-draw-call-optimizer-54360)).
- [F] In reviews, the publisher says tiling with UVs outside the standard square "is not supported", and users report errors on Unity 2019 ([reviews](https://assetstore.unity.com/packages/tools/utilities/pro-draw-call-optimizer-54360/reviews)).
- [I] Effectively abandoned. It shows the classic failure mode of rect atlasing with tiled UVs.

### 3.10 Polytool (Markcreator, Gumroad), credited in the UV Wizard README

- [F] A Unity tool to optimize avatars with presets. Listed features: automatic decimation, "Merging skinned and static meshes into a single mesh", "Material merging with automatic texture atlasing", bone reduction, preservation of blendshapes, animations, UVs and weights, and "Built-in optimization presets for VRChat Quest avatars" ([Gumroad](https://markcreator.gumroad.com/l/Polytool)).
- [F] Price $25. A free trial can't save or upload. Rated 4.7 from 454 ratings. Closed source. Also sold on Lemon Squeezy and Jinxxy (same page).
- [F] The UV Wizard README uses a Polytool image as the "existing material/mesh combining method" ([README](https://github.com/clnelson888/uv-wizard)).
- [U] I couldn't verify Polytool's packing algorithm, current version or last update date.
- [I] Polytool proves there's paid demand for preset-driven one-button optimization, especially for Quest. UV Wizard's differentiator against it is open source, NDMF and island-level quality.

### 3.11 VRCFury

- [F] [VRCFury/VRCFury](https://github.com/VRCFury/VRCFury): 455 stars, release `com.vrcfury.vrcfury/1.1434.0` (2026-10-12). It has a custom licence with personal and commercial options ([LICENSE.md](https://github.com/VRCFury/VRCFury/blob/main/LICENSE.md)).
- [F] Its optimization-related features are a Blendshape Optimizer, a Direct Tree (FX) optimizer, a parameter compressor and removal of non-Quest materials. There's no atlas or material-merge code in the source tree ([repo](https://github.com/VRCFury/VRCFury), [components](https://vrcfury.com/components/)).
- [I] Not a competitor, but a very common co-installed NDMF-style tool. Test compatibility with it.

### 3.12 Poiyomi and Thry's tools

- [F] Poiyomi's UV Tile Discard hides parts of a mesh by UV tile on a chosen channel, UV0–UV3 ([docs](https://www.poiyomi.com/special-fx/uv-tile-discard/)).
- [U] I found no Poiyomi atlas or material-merge tool.
- [F] ThryEditor (the inspector Poiyomi uses) includes an RGBA channel-packing texture tool with a compute shader ([ThryEditor/Editor/TexturePacker](https://github.com/Thryrallo/ThryEditor)).
- [F] Thry's [VRC-Avatar-Performance-Tools](https://github.com/Thryrallo/VRC-Avatar-Performance-Tools) (MIT, 420 stars, 1.3.7 on 2024-08-05) is an avatar evaluator and VRAM checker. It doesn't optimize.
- [R] Never rewrite UV channels other than the atlas channel. Islands used for tile discard on UV1–3 must keep their coordinates.
- [R] Support Poiyomi by reading which textures sample which UV channel. At minimum, warn and skip unknown Poiyomi textures instead of silently atlasing only `_MainTex`, which is what TTT does for unknown shaders.

### 3.13 VRChat SDK "Avatar Optimizer" (official, announced)

- [F] Canny post, Nov 2025, status "in progress": the optimizer "runs automatically when you build or upload your avatar", "generates atlas textures and combines meshes and materials". Settings can preserve blend shapes or skip atlas generation, and there's a preview. Material merging and atlasing support only Standard Lite and Toon Standard at first. The goal is a closed beta ([VRChat feedback](https://feedback.vrchat.com/avatar-30/p/automatic-avatar-optimization-in-sdk)).
- [F] A staff reply (2026-02-27) says merging materials that use UV Tile Discard toggles "probably won't be in the initial release" and is "not something we can do on Android/iOS" (same thread).
- [F] The VRChat wiki's developer-update log lists "avatar optimizer" under "Announced" in the 2026-03-19 update ([wiki](https://wiki.vrchat.com/wiki/Developer_Update)).
- [F] SDK release notes through 3.10.5 (2026-09-04) don't mention it ([SDK releases](https://creators.vrchat.com/releases/)).
- [U] Its packing method is unknown.
- [I] When it ships, it will cover the "one button for Toon Standard and Standard Lite avatars" niche by default.
- [R] UV Wizard should differentiate on:
  - quality (island packing, exact shapes, density preservation);
  - wider shader coverage (lilToon, Poiyomi);
  - transparency and control (preview, per-island priority, reports);
  - open source.
- [R] Track this closely. If the SDK exposes a shader-author compatibility API, support it.

### 3.14 Other 2025–2026 newcomers (Booth, minor)

- [F] [VRC Model Optimizer](https://booth.pm/en/items/8833530) (KaleidoVR, free, v1.0.70, ©2026): merges identical material slots and skinned meshes and has Quest workspaces. No atlasing is mentioned.
- [F] [EasyAvatarOptimizer](https://booth.pm/en/items/8382092) (kobokits, ¥500, v1.0.1 2026-06-04): analyzes rank and "generates atlases to merge materials", lilToon only. The packing method isn't stated [U].
- [F] [Avatar Texture Optimizer](https://booth.pm/en/items/5902500) (cooki, ¥400, v1.0 2024-07-09): bulk texture size and compression settings only.
- [F] Older Unity atlas tools on Booth: [お手軽UV編集キット](https://booth.pm/en/items/7957016) (a manual UV atlas organizer and texture remapper), [テクスチャのアトラス化ツール](https://booth.pm/en/items/1430024) (v2.5.1, Unity 2022.3, May 2024), and [Six-Atlas Generator](https://booth.pm/en/items/8060646) (an image-slot atlas, v1.6.2 2026-03-07). I didn't verify their packing methods [U].
- [F] [Naraenda/PerfHammer](https://github.com/Naraenda/PerfHammer) (59 stars, last push 2022-01-24): atlases arbitrary texture sets and "Trims input textures to UV maps". Inactive.
- [I] None of these threatens TTT or AAO on quality. Together they show steady demand for "make my avatar Quest/Good-rank automatically".

### 3.15 Blender Material Combiner (shotariya / Grim-es)

- [F] [Grim-es/material-combiner-addon](https://github.com/Grim-es/material-combiner-addon): GPL-3.0, 727 stars. Last release 2.1.2.9 (2023-11-20), last commit 2025-11-17.
- [F] Its goal is "combining textures without quality loss and avoiding issues with UV bounds larger than 0–1".
- [F] Multi-map atlases (normal, specular) and UV packing by face splitting exist only in old versions (2.0.3.3 and 1.1.6.3). The current version handles diffuse only (README).
- [F] **Source.**
  - Each material gets one rectangle sized `image size × max UV extent`, with the extent clamped to [1, 25] ([`get_size`](https://github.com/Grim-es/material-combiner-addon/blob/eed9ca25baff/operators/combiner/combiner_ops.py#L261-L293), [`_get_max_uv_coordinates`](https://github.com/Grim-es/material-combiner-addon/blob/eed9ca25baff/operators/combiner/combiner_ops.py#L357-L377)).
  - UVs beyond 1 get a tiled copy of the image ([`_get_uv_image`](https://github.com/Grim-es/material-combiner-addon/blob/eed9ca25baff/operators/combiner/combiner_ops.py#L646-L675)).
  - Rects are packed by a selectable bin-tree, MaxRects or rectpack2D packer ([utils/packers](https://github.com/Grim-es/material-combiner-addon/tree/eed9ca25baff/utils/packers)).
  - Images are composed on the CPU with Pillow and NumPy.
- [I] It's whole-texture rect packing with tiling baked in. It's the de facto community recommendation (see [d4rk #92](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/92)), and it's exactly the "grid/rect" approach UV Wizard aims to beat.
- [R] Its "tile the texture when UVs go past 1" behavior is a simple fallback worth offering. The code is GPL-3.0, so re-implement it rather than port it.

### 3.16 CATS Blender Plugin and successors

- [F] The original [absolute-quantum/cats-blender-plugin](https://github.com/absolute-quantum/cats-blender-plugin) (MIT, 4,109 stars) last released 0.19.0 in 2021-06 and last pushed in 2024-05.
- [F] The Neoneko continuation [teamneoneko/Cats-Blender-Plugin](https://github.com/teamneoneko/Cats-Blender-Plugin) (830 stars, 5.0.2.3 on 2026-01-24) is archived. Its README says "no longer maintained". Its atlasing step uses Material Combiner.
- [F] The successors are [Avatar Toolkit](https://github.com/teamneoneko/Avatar-Toolkit) (GPL-3.0, 114 stars, last push 2025-12-21) and community forks such as [KaikoClanworth1/Cats-Blender-Plugin-Unofficial](https://github.com/KaikoClanworth1/Cats-Blender-Plugin-Unofficial) (Blender 5.2, 2026-08-18).
- [F] The related [Tuxedo Blender Plugin](https://github.com/teamneoneko/Tuxedo-Blender-Plugin) (GPL-3.0) takes a different route. It **re-bakes** all meshes into one fresh UV space (using UVPackmaster when available), supports full PBR passes, and rebakes normals after decimation (README).
- [I] The Blender side is fragmented and losing maintainers. That's an opening for an in-Unity tool that removes the Blender round-trip.
- [R] Consider an optional "re-bake" mode later. A Unity GPU bake onto a new xatlas-style UV set would let maximally fragmented UVs collapse into one atlas, at a quality cost.

### 3.17 UVPackmaster

- [F] It's a commercial packing engine for Blender and Maya. The CPU packer is claimed most efficient, and a GPU variant runs on CUDA and Vulkan ([uvpackmaster.com](https://uvpackmaster.com/)).
- [F] Version 4 shipped in January 2026 with dynamic tiles, per-island texel-density tiers, and a "Group Layout Mode" for atlas workflows. It supports Blender 2.83+ and costs $53 for a single user ([CG Channel, 2026-01-19](https://www.cgchannel.com/2026/01/uvpackmaster-4-for-blender-is-out/)).
- [I] It's the quality ceiling for packing. Groups and texel-density tiers map neatly onto "materials as groups, keep relative density" for UV Wizard.

### 3.18 Blender's built-in UV packer (3.6+)

- [F] Blender's `Pack Islands` exposes:
  - shape methods "Exact Shape (Concave)", "Boundary Shape (Convex)" and "Bounding Box";
  - rotation "Any", "Cardinal" (90° only) and axis-aligned variants;
  - margin methods "Scaled", "Add" and "Fraction";
  - "Merge Overlapping" ("Overlapping islands stick together");
  - pin and lock modes;
  - UDIM targets ([uvedit_unwrap_ops.cc](https://github.com/blender/blender/blob/e2c18d2375b3/source/blender/editors/uvedit/uvedit_unwrap_ops.cc#L1864-L1931)).
- [F] The implementation in `geometry/intern/uv_pack.cc` (GPL-2.0-or-later, ©2023) combines several strategies ([uv_pack.cc](https://github.com/blender/blender/blob/e2c18d2375b3/source/blender/geometry/intern/uv_pack.cc)):
  - an "Alpaca" L-packer for AABBs, with and without rotation;
  - an optimal table for small counts;
  - `BLI_box_pack_2d`;
  - an "xatlas strategy" brute-force packer with a bitmap occupancy grid, loosely based on jpcy/xatlas, for irregular islands.
- [F] Blender's 3.6 release notes returned HTTP 403 to my fetch, so the version history is not re-verified here [U].
- [R] Use this as the algorithm ladder. Ship AABB + 90° rotation first (TTT parity), then add a bitmap-occupancy brute-force pass for concave islands. Read Blender's design comments, but don't copy GPL code into the MIT package.

### 3.19 xatlas (jpcy)

- [F] [jpcy/xatlas](https://github.com/jpcy/xatlas): MIT, 2,581 stars, last push 2024-06-16, no tagged releases.
- [F] `AddUvMesh` packs *existing* UVs as charts. A `faceMaterialData` array keeps overlapping UVs of different materials apart.
- [F] `PackOptions` includes `padding`, `texelsPerUnit`, `resolution`, `bilinear`, `blockAlign` ("Align charts to 4x4 blocks"), `bruteForce`, `rotateChartsToAxis` and `rotateCharts` ([xatlas.h](https://github.com/jpcy/xatlas/blob/master/source/xatlas/xatlas.h)).
- [I] It's the most directly reusable MIT packer, but it's native C++. Shipping it in a VPM package means per-OS editor binaries (Windows, macOS, Linux), or a C# port of its packer. Its `texelsPerUnit` and `blockAlign` ideas match UV Wizard's needs (density preservation, BCn/ASTC alignment).
- [R] Prototype the packer in C# (Burst) following xatlas's chart-bitmap brute-force design. Keep xatlas as a validation baseline in tests.

### 3.20 Unreal Engine Merge Actors / HLOD material baking

- [F] In UE 5.8 Merge Actors, "Merge" keeps one section per material and preserves UVs. With "Merge Materials" on, the materials "will be combined into a single Material with a new UV layout" ([Epic docs](https://dev.epicgames.com/documentation/en-us/unreal-engine/merging-actors-in-unreal-engine)).
- [F] It bakes selected maps (Normal, Metallic, Roughness, Emissive, Opacity, AO, …). "Gutter Size" sets the pixel spacing between islands, and "Use Texture Binning" sizes textures "by importance".
- [F] Approximate mode generates UVs with UVAtlas, XAtlas or PatchBuilder (same page).
- [I] Unreal goes the "bake onto new UVs" route, not "repack original islands".
- [R] The idea of "texture binning by importance" is worth borrowing as a per-material priority (for example, face > body > shoes).

### 3.21 Simplygon

- [F] `ChartAggregatorSettings` repacks the original UV charts into a new atlas. Its modes are `TextureSizeProportions` (default, keeps relative pixel density), `SurfaceArea`, `OriginalPixelDensity` ("all charts retain the same amount of pixels as they originally had", overriding texture size) and `UVSizeProportions`. Options include `SeparateOverlappingCharts`, `LockUVRotation` and `OriginalChartProportionsChannel` (default Diffuse) ([Simplygon SDK 10.1 docs](https://documentation.simplygon.com/SimplygonSDK_10.1.400.0/api/reference/processingandmaterialcasting/settingsobject/chartaggregatorsettings.html)).
- [I] This is UV Wizard's concept, done industrially. Its mode list is a good spec for UV Wizard's sizing options.
- [U] I didn't verify the current Simplygon licensing or pricing.
- [R] Offer the same density modes:
  - "keep relative density" (TTT's default behavior);
  - "keep original pixels", growing the atlas to fit;
  - "fit a budget" (a texture-memory target per VRChat rank);
  - an explicit "separate overlapping/mirrored islands" toggle, defaulting off to save space.

### 3.22 RizomUV and UV-Packer

- [F] RizomUV 2025 added CUDA GPU packing on Windows, with layouts 2–4% more efficient than CPU. The Virtual Spaces indie licence costs €149.90 perpetual ([Digital Production, 2025-09-15](https://digitalproduction.com/2025/09/15/rizomuv-2025-faster-packing-but-only-if-youre-on-windows-with-cuda/)).
- [F] RizomUV 2027.0 (October 2026) adds a headless batch mode with Python scripting ([CG Channel, 2026-10-08](https://www.cgchannel.com/?p=179230)).
- [F] UV-Packer / Packer-IO is free, Windows and Mac, v1.4.0, with plugins for Blender, Unreal and 3ds Max. No SDK or CLI is documented on its site ([uv-packer.com](https://www.uv-packer.com/)).
- [I] Neither can realistically be embedded in a free VPM package. They're useful only as quality benchmarks: export islands, pack, compare fill %.

---

## 4. Reference and inspiration picks

| Borrow | From | Why | License note |
|---|---|---|---|
| Union-find island builder + merge islands that overlap or share UV-identical vertices | TTT [IslandUtility](https://github.com/ReinaS-64892/TexTransCore/blob/d2d0db32cb60/src/Island/IslandUtility.cs), [AtlasContext notes](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasContext.cs#L14-L48) | Keeps mirrored and stacked UVs sharing texels | MIT, copy with attribution (AAO did) |
| Density-preserving initial island scale + "shrink then grow 1%" fit loop + power-of-two height trim | TTT [AtlasContext L173](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/AtlasContext.cs#L173-L194), [IslandRelocationManager](https://github.com/ReinaS-64892/TexTransTool/blob/741b7dc3febc/Runtime/TextureAtlas/IslandRelocationManager.cs) | Simple, robust "use the whole atlas" search | MIT |
| Shelf (NFDH + floor-ceiling) packer with 90° rotation as v1 packer | TexTransCore [NFDHPlasFC](https://github.com/ReinaS-64892/TexTransCore/blob/d2d0db32cb60/src/Island/IslandRelocator/NFDHPlasFC.cs) | Known-good baseline, parity with TTT | MIT |
| Tile handling: an island inside one tile + Repeat wrap → shift to tile 0; Clamp → bail | AAO [OptimizeTexture L676-L730](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L676-L730) | Fixes the TTT #1056 class of bugs cheaply | MIT |
| Bake tiling for islands that span tiles (with size warning) | Mesh Baker ([forum](https://discussions.unity.com/t/mesh-baker-by-digital-opus-released/490738)), Material Combiner ([source](https://github.com/Grim-es/material-combiner-addon/blob/eed9ca25baff/operators/combiner/combiner_ops.py#L646-L675)) | Fallback when shifting isn't possible | Idea only (commercial / GPL) |
| Block-aligned islands + raw compressed-block copy when not rescaling | AAO [L764-L960](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/Editor/Processors/TraceAndOptimize/OptimizeTexture.cs#L764-L960), xatlas `blockAlign` | Lossless output, no re-compression artifacts | MIT |
| Shader texture-usage tables (which property uses which UV channel and `_ST`) | AAO [ShaderInformation API](https://github.com/anatawa12/AvatarOptimizer/blob/a415011c0cda/API-Editor/ShaderInformation.cs) | Toon Standard, lilToon and third-party shaders for free | MIT; prefer calling the API |
| Exact/convex-shape brute-force packing on a bitmap occupancy grid | Blender [uv_pack.cc](https://github.com/blender/blender/blob/e2c18d2375b3/source/blender/geometry/intern/uv_pack.cc), [xatlas](https://github.com/jpcy/xatlas) | Beats AABB packing on L-shaped and diagonal islands, the key quality edge over TTT | Blender GPL (idea only); xatlas MIT |
| Density modes (relative / original pixels / budget) and "separate overlapping charts" | Simplygon [ChartAggregatorSettings](https://documentation.simplygon.com/SimplygonSDK_10.1.400.0/api/reference/processingandmaterialcasting/settingsobject/chartaggregatorsettings.html) | Clear user-facing sizing model | Idea only |
| Per-material importance weighting | Unreal "Use Texture Binning" ([docs](https://dev.epicgames.com/documentation/en-us/unreal-engine/merging-actors-in-unreal-engine)), TTT island size priority | Face > body > shoes when shrinking | Idea only |
| Flatten color, tint and layers into albedo before merging for Quest | lilToon [texture baker](https://github.com/lilxyzw/lilToon/blob/72fc09625b24/Assets/lilToon/Editor/lilInspector/lilEditorTextureBaker.cs) | Merged materials otherwise lose per-material `_Color` | MIT |
| Presets ("Quest Good", "PC Medium") | Polytool ([Gumroad](https://markcreator.gumroad.com/l/Polytool)), d4rk presets ([README](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/blob/d61f29341477/README.md)) | Matches what non-technical users pay for | Idea only |
| Budget targets from VRChat rank limits | [VRChat ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/) | e.g. Quest Good = 1 material, 1 skinned mesh, ≤18 MB textures | n/a |

---

## 5. Gaps and open questions

**Gaps that still look open (all [I] unless noted).**

1. **One-button, budget-driven, Quest-ready output.** No open-source tool takes an avatar and produces "1 material, 1 skinned mesh, ≤ N MB textures" with island-level quality and no manual material lists.
   - TTT plus AAO gets close, but only after manual setup.
   - Polytool has presets, but it's closed source and its method is unverified [U].
   - The VRChat SDK optimizer is not shipped yet and is Standard Lite/Toon Standard-only at first [F].
2. **Packing quality beyond AABB shelves.** TTT (AABB + 90°) and AAO (AABB, no rotation) both leave space Blender's concave packer or xatlas brute force would use [F for their methods; I for the waste].
3. **UVs outside 0–1.** TTT [#1056](https://github.com/ReinaS-64892/TexTransTool/issues/1056) is open [F]. AAO only handles islands that sit in a single tile [F]. AAO Merge Material uses `uv % 1` [F].
4. **Normal maps under rotation.** TTT [#1106](https://github.com/ReinaS-64892/TexTransTool/issues/1106) is open [F]. Correct handling means rotating the decoded tangent-space XY by the island angle, plus mirrored-island sign awareness.
5. **Shader coverage.** TTT lacks Toon Standard [F]. Neither TTT nor AAO ships Poiyomi tables [F for TTT; U for AAO and Poiyomi integration].
6. **Material-property flattening** (`_Color`, HSV, layers) before merging. TTT removed PropertyBake [F].
7. **Animated material swaps and MA reactive components.** These are known pain points for TTT and AAO [F].
8. **English-first docs and clear reports** (fill %, effective texel density per material, before/after VRAM). [I] TTT's docs are Japanese-first.

**Open questions to resolve before committing to a design.**

- [U] Will VRChat's SDK optimizer ship in 2026–27, and will it expose a shader-compatibility API that UV Wizard should implement or defer to?
- [U] What packer does the SDK optimizer use, and does it keep original UV islands or re-unwrap?
- [U] Polytool's actual atlas algorithm, current version and update cadence. Ask Markcreator directly, since the README credits him.
- [U] Whether TTT's author would accept upstream fixes for #1056 and #1106. [R] If yes, contributing upstream might do more for users than duplicating, and UV Wizard could focus on the one-button and Quest pipeline and the exact-shape packer.
- [U] AAO's NDMF plugin ID and pass names, needed for ordering constraints. Read AAO's `PluginDefinition` before implementing.
- [U] Whether a C#/Burst port of an xatlas-style bitmap packer is fast enough at editor time for avatars with 1,000–5,000 islands. Needs a benchmark.
- [U] Mesh Baker's and Super Combiner's exact cropping granularity (per material UV rect vs per island). Both are closed source, and I saw no primary documentation of it.
- [U] Simplygon's current licence terms for hobbyist use. Only relevant as a benchmark.
- [I] How to handle a texel-density conflict when the same texture is used at very different UV scales by two meshes. TTT merges overlapping islands, AAO bails. Decide a policy.
