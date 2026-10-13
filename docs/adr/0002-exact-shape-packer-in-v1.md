# Exact-shape packer from v1, not a bounding-box shelf packer

v1 packs charts by their exact rasterized shape (xatlas-style pixel-mask placement, Burst-accelerated C#) rather than porting TexTransTool's bounding-box shelf packer, even though the research recommended the shelf packer first. Packing quality is part of why UV Wizard exists: the README's whole complaint is wasted atlas space. A bounding-box packer would match TexTransTool, not beat it, and swapping packers later changes every user's output.

## Consequences

- The packer is the biggest technical risk in v1. Benchmark it early on real avatars with 1,000–5,000 islands. Keep a bounding-box fallback for when it is too slow or fails to converge.
- Native xatlas is deliberately not used: Unity can't unload native DLLs in the editor, unsigned macOS binaries get blocked, and it would need builds for three operating systems.
