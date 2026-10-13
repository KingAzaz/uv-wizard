# NDMF plugin before AAO, with AAO doing the mesh merge

UV Wizard runs as an NDMF plugin in the Optimizing phase, after Modular Avatar and TexTransTool and before Avatar Optimizer (AAO). It lists AAO as a `vpmDependency`. UV Wizard merges materials only; AAO's mesh merging then reduces the avatar to one skinned mesh. Skinned-mesh merging (bones, bindposes, blendshapes) is a large, bug-prone subsystem that AAO already does well under MIT. Running inside NDMF is how every current VRChat build-time tool composes; a destructive one-shot tool would fight Modular Avatar and AAO over ordering.

## Considered Options

- **Our own mesh merger**: no dependency, but it duplicates mature free code and is the most likely place for bugs that break avatars.
- **Destructive bake to assets** (the prototype's approach): simpler, but it modifies the user's assets and ignores the NDMF ecosystem.

## Consequences

- The "1 skinned mesh" half of every preset depends on AAO's behaviour. An AAO change can break the headline result, so our tests must cover AAO's output too.
- We use AAO's interop API (UV evacuation, `MeshRemovalProvider`) so AAO's UV-based removals stay correct and triangles AAO will delete get no atlas space.
