# UV Wizard

UV Wizard turns a VRChat avatar with many materials into a mobile-ready avatar with one material, by packing the parts of its textures that the avatar actually uses into a single atlas.

## Language

### Inputs

**Avatar**:
The VRChat avatar a creator is uploading, including every renderer and material on it.
_Avoid_: Model, character, project

**Source material**:
A material on the avatar before UV Wizard runs.
_Avoid_: Original material, input material

**Source shader**:
The shader a source material uses, such as lilToon, Poiyomi or Standard. It decides where UV Wizard reads colours and textures from.
_Avoid_: Input shader

### Islands and packing

**Island**:
A connected patch of an avatar's mesh surface that stays connected in UV space, and so occupies one contiguous region of a source texture.
_Avoid_: Shell, UV chart, piece, UV group

**Chart**:
One or more islands that would bake to identical atlas texels (same source texture, same tint, overlapping UVs), so they share one place in the atlas. Mirrored and stacked islands form one chart. So do islands of one mesh that meet only at a shared vertex, since apart they would give that vertex two atlas UVs.
_Avoid_: Cluster, island group. xatlas uses "chart" to mean a single island, but this project does not.

**Priority**:
How strongly a chart resists shrinking when the atlas is over budget. Charts on the head get extra priority automatically.
_Avoid_: Importance, weight, size priority

**Packing**:
Choosing where each chart sits in the atlas, and at what scale and rotation. The exact packer uses each chart's exact shape; the shelf packer, its fallback for very many charts or when the exact packer does no better, uses bounding boxes.
_Avoid_: Layout, arrangement, atlasing (for this step alone)

### Outputs

**Atlas**:
The set of output textures, one per enabled channel, that share a single packing.
_Avoid_: Texture sheet, combined texture, sprite sheet

**Channel**:
One kind of texture data the atlas carries: Albedo, Emission or Normal. Each can be turned on or off.
_Avoid_: Map, texture slot, layer, property

**Merged material**:
The single Toon Standard material that replaces the merged source materials and samples the atlas.
_Avoid_: Combined material, output material, atlas material

**Bake**:
The run that produces the atlas and merged material from an avatar. It happens during upload, or on demand through a manual bake.
_Avoid_: Combine, optimize, process

### Targets and results

**Mobile**:
VRChat's Android and iOS platforms, Quest included. They share one set of performance ranks and allow only VRChat's mobile shaders. A bake changes only mobile builds.
_Avoid_: Quest, Android (when both platforms are meant)

**Preset**:
The mobile performance rank a bake aims for: Excellent, Good or Medium. A preset fixes the budget and how many material slots are allowed.
_Avoid_: Quality level, mode, profile

**Budget**:
The most texture memory a preset allows, counted across all channels: 10 MB for Excellent, 18 MB for Good and 25 MB for Medium.
_Avoid_: VRAM limit, size limit, texture cap

**Excluded material**:
A source material that the bake keeps out of the atlas because merging it would break the avatar, such as one swapped by an animation. It still gets its own Toon Standard material, since mobile allows no other shader, and so costs a material slot of its own.
_Avoid_: Skipped material, ignored material, unmerged material

**Flagged material**:
A source material that is merged but may look different afterwards, such as a transparent or cutout material that becomes opaque.
_Avoid_: Warning material, lossy material

**Report**:
The before/after summary of a bake: material slots, skinned meshes and texture memory, the rank reached, and every excluded or flagged material with the reason.
_Avoid_: Log, stats, summary

### Product

**Core**:
The free, MIT-licensed UV Wizard package: everything that turns an avatar into a mobile-ready one.
_Avoid_: Free version, lite, base

**Pro**:
The planned paid add-on that extends the core with power features. It never gates the core's one-button result.
_Avoid_: Premium, paid version, plus

## Relationships

- An **Avatar** has many **Source materials**. Each uses one **Source shader**.
- A **Source material**'s mesh surface splits into many **Islands**. One or more **Islands** form a **Chart**.
- A **Bake** takes one **Avatar** and one **Preset**. It produces one **Atlas**, one **Merged material** and one **Report**.
- An **Atlas** holds one texture per enabled **Channel**. Together they must fit the **Preset**'s **Budget**.
- Every **Source material** ends up merged, merged and **Flagged**, or **Excluded**. The **Report** lists the last two.
- **Pro** depends on **Core**. **Core** never depends on **Pro**.
