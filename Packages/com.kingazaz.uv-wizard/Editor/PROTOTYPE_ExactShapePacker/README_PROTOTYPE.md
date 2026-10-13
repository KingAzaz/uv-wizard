# PROTOTYPE: exact-shape packer benchmark

Throwaway code. It lives only on the `prototype/exact-shape-packer` branch and never merges to `main`.

## The question

ADR 0002 commits v1 to an exact-shape (pixel-mask, xatlas-style) chart packer instead of a bounding-box shelf packer. This prototype answers:

1. **Is it fast enough at editor time** on real avatars (1,000–5,000 islands)? This runs on every upload or manual bake.
2. **How much tighter is it** than a bounding-box shelf packer, measured as texture detail kept at a fixed atlas size?

## How it works

- `PackerCore.cs` is the liftable part: pure functions, no UI.
  - **Islands:** union-find over triangles that share a UV position. Mirrored islands with identical UVs merge into one chart.
  - **Rasterize:** each chart is drawn conservatively onto a block grid (block = 4 px by default, matching BC/ASTC blocks), then dilated by the padding, in both 0° and 90° orientations.
  - **Exact packer:** bottom-left first fit over a 64-bit occupancy grid, in a single Burst job.
  - **Shelf packer:** first-fit decreasing height over each chart's bounding box, oriented wide side down. Roughly TexTransTool's class of packer.
  - **Scale search:** for both packers, geometric bisection finds the largest uniform scale at which every chart fits.
- `PackerBenchWindow.cs` is the throwaway shell (an editor window).

## Run it

1. In Unity: **Tools → UV Wizard → PROTOTYPE Packer Benchmark**.
2. Drag an avatar (scene object or prefab) into **Avatar** and press **Run on avatar**. You can also press **Run synthetic stress test**.
3. Results show in the window and are appended to `Logs/UVWizard_PROTOTYPE_packer_bench.md` in the Unity project.

## Reading the report

- **Detail kept**: atlas pixels per source texel. 100% means the source texture's full detail survives; 50% means half the resolution per axis.
- **Exact vs shelf**: the linear detail gain per axis, and the matching gain in texels.
- **Pack ms / Slowest pack ms**: packing time per scale attempt. The total includes every bisection attempt (usually 8–10).
- **Overlap cells** must be 0. Anything else is a packer bug.

## Known simplifications

- Only UV0 and `_MainTex` size are used (1024 px is assumed when there's no texture).
- Overlapping islands that don't share UV positions stay as separate charts.
- UVs outside 0–1 are packed by their raw extent; tile shifting isn't handled.
- No priority weighting: there's no face priority or importance.
- Rotation is only 0° and 90°.

## Results (2026-10-12, Unity 2022.3.22f1, Burst 1.8.12, 2048 atlas, 4 px blocks, 4 px padding)

Run through MCP for Unity by calling `PackerBenchWindow.RunOnAvatar` and `RunSynthetic`. The full log is at `Logs/UVWizard_PROTOTYPE_packer_bench.md` in the dev project.

| Input | Charts | Shelf detail | Exact detail | Gain (texels) | Exact total ms (optimized) | Exact pack ms / raster ms |
|---|---|---|---|---|---|---|
| Rexouium (5 slots, 62k tris) | 165 | 111.9% | 129.5% | **+34%** | 1,128 | 335 / 794 |
| Furyon Raccoon (3 slots, 45k tris) | 255 | 140.0% | 177.5% | **+61%** | 1,191 | 698 / 494 |
| Synthetic | 1,000 | 28.9% | 36.4% | +58% | 2,865 | 2,379 / 486 |
| Synthetic | 3,000 | 15.1% | 17.8% | +40% | 11,600 | 11,296 / 304 |

No packer produced overlapping charts in any run.

The optimizations (no safety checks, skipping rows that can't fit the chart, failing fast when the atlas runs out, seeding the search from the shelf result) cut the exact packer's time by 2.3–3.4× against the baseline: 36.1 s → 10.5 s on synthetic 3,000, and 3.3 s → 0.7 s pack time on Raccoon.

## Verdict

1. **Tighter? Yes.** At the same 2048 atlas, the exact packer keeps **34–61% more texels than a bounding-box shelf packer on real avatars**. ADR 0002 stands.
2. **Fast enough? On real avatars, yes. At thousands of islands, not yet.**
   - The two real avatars have 165–255 charts, not the 1,000–5,000 the research assumed. A full scale search takes about 1.2 s, of which 0.3–0.7 s is packing and 0.5–0.8 s is managed-code rasterization. That's fine for upload builds.
   - Packing time grows faster than linearly: about 2.4 s at 1,000 charts and 11.3 s at 3,000.
   - Production needs:
     - Burst rasterization.
     - Parallel row scanning.
     - Caching bake results between builds, since NDMF re-runs on play mode.
     - A fallback to the shelf packer above a chart-count or time budget.
3. **Side finding: area-based sizing bounds are wrong for real avatars.** Rex's stacked or overlapping UV triangles double-count area. The first version of this prototype capped both packers at about 58%, when the real answer was 112% (shelf) and 130% (exact). Production sizing must search for the feasible scale rather than trust summed triangle area, and handling overlapping islands matters.
