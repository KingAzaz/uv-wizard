# NDMF plugin before AAO, with AAO doing the mesh merge

UV Wizard runs as an NDMF plugin in the Optimizing phase, after Modular Avatar and TexTransTool and before Avatar Optimizer (AAO). It lists AAO as a `vpmDependency`. UV Wizard merges materials only; AAO's mesh merging then reduces the avatar to one skinned mesh. Skinned-mesh merging (bones, bindposes, blendshapes) is a large, bug-prone subsystem that AAO already does well under MIT. Running inside NDMF is how every current VRChat build-time tool composes; a destructive one-shot tool would fight Modular Avatar and AAO over ordering.

## Considered Options

- **Our own mesh merger**: no dependency, but it duplicates mature free code and is the most likely place for bugs that break avatars.
- **Destructive bake to assets** (the prototype's approach): simpler, but it modifies the user's assets and ignores the NDMF ecosystem.

## Consequences

- The "1 skinned mesh" half of every preset depends on AAO's behaviour. An AAO change can break the headline result, so our tests must cover AAO's output too.
- We use AAO's interop API (UV evacuation, `MeshRemovalProvider`) so AAO's UV-based removals stay correct and triangles AAO will delete get no atlas space.
- AAO merges skinned meshes automatically only through its Trace and Optimize component. On mobile builds, UV Wizard adds one with default settings when the avatar lacks it, which AAO documents as supported. A setting that is on by default turns this off. The creator then also gets Trace and Optimize's other default optimizations.
- Even with Trace and Optimize, AAO won't merge meshes on different toggles, static meshes, or meshes with different bounds or root bones. The report must say which ones kept the avatar above one skinned mesh.
- Trace and Optimize's **MMD World Compatibility**, on by default, keeps a skinned mesh named `Body` directly under the avatar root out of the merge, because MMD worlds animate its blend shapes. Most avatar bases name their main mesh `Body`, so a typical avatar ends with two skinned meshes. We accept this rather than rename the mesh or add an undocumented `MergeSkinnedMesh`. The report names `Body` as the reason and tells the creator they can turn the option off on their own Trace and Optimize if they don't use MMD worlds (#65).
- Trace and Optimize's **Optimize Texture** repacks the merged material's atlas to drop its empty space. The avatar looks the same and uses less texture memory, and AAO's API offers no supported way to opt out, so we let it. The budget still sizes the atlas before AAO runs, and the repack can only lower that figure. The texture memory the report shows, and the check that a bake met its preset, come from the build's final textures after AAO, never from UV Wizard's own atlas size (#66).
