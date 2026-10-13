# v1 optimizes Android/Quest builds only, outputting Toon Standard

v1 bakes only for Android/Quest builds. PC builds pass through untouched. The merged material always uses VRChat's Toon Standard shader. Quest's Good and Excellent ranks need a single material slot, and Android avatars may only use VRChat's mobile shaders, so a single opaque Toon Standard material is the target. Source shaders (lilToon, Poiyomi, Standard) are only *read* to bake their colours into the atlas, so we never have to rewrite lilToon or Poiyomi material settings, which would roughly double the material-handling work.

## Consequences

- Toon Standard doesn't support transparency (SDK 3.8.1 release notes). Transparent and cutout materials become opaque flagged materials. Keeping them see-through (a second material on Medium, or removing hidden layers such as the Canis eye) is deferred to Pro.
- Materials swapped by animations or Modular Avatar become excluded materials in v1. Baking swap variants is the hardest case in the tool, and it is deferred.
- PC atlasing, which needs per-shader adapters to keep each material's original shader, is a later feature.
