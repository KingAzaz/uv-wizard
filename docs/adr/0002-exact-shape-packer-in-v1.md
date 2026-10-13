# Exact-shape packer from v1, not a bounding-box shelf packer

v1 packs charts by their exact rasterized shape (xatlas-style pixel-mask placement, Burst-accelerated C#) rather than porting TexTransTool's bounding-box shelf packer, even though the research recommended the shelf packer first. Packing quality is part of why UV Wizard exists: the README's whole complaint is wasted atlas space. A bounding-box packer would match TexTransTool, not beat it, and swapping packers later changes every user's output.

## Consequences

- The packer is the biggest technical risk in v1. Keep a bounding-box fallback for when it is too slow or fails to converge.
- Native xatlas is deliberately not used: Unity can't unload native DLLs in the editor, unsigned macOS binaries get blocked, and it would need builds for three operating systems.

## Evidence (prototype, 2026-10-12)

The throwaway prototype on branch `prototype/exact-shape-packer` (`Packages/com.kingazaz.uv-wizard/Editor/PROTOTYPE_ExactShapePacker/README_PROTOTYPE.md`) confirms this decision.

- **Tighter:** at a 2048 atlas, the exact packer keeps 34% (Rexouium) and 61% (Furyon Raccoon) more texels than a bounding-box shelf packer, with no overlaps.
- **Fast enough for real avatars:** those avatars have 165–255 charts, far fewer than the research assumed. A full scale search takes about 1.2 s.
- **Not yet fast at thousands of charts:** about 11.6 s at 3,000. Production needs Burst rasterization, parallel row scanning, cached bake results (NDMF re-runs on play mode), and the shelf fallback above a chart-count or time budget.
- **Don't size from summed UV triangle area.** Overlapping UVs double-count it (Rexouium). Search for the largest scale that actually fits.

## Evidence (production, 2026-10-13)

Issue #11 split each chart's bottom-left scan across rows in parallel Burst jobs, keeping the sequential scan's placements (a test compares both). On the dev machine, with the prototype's settings:

- **1,000 synthetic charts:** a full scale search takes 0.5 s, down from 2.7 s with the sequential scan.
- **3,000 synthetic charts:** 1.6 s, down from 9.2 s, which meets the 3 s target. The target is now to stay under 2 s at 3,000 charts.
- **Real avatars:** one pack takes about 25 ms, down from about 57 ms, with identical placements.
