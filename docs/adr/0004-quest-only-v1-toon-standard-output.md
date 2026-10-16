# v1 optimizes mobile builds only, outputting Toon Standard

v1 bakes only for mobile builds: Android, Quest included, and iOS. The VRChat SDK treats those two alike, with the same ranks and the same shader rules, and it decides by the active build target. PC builds pass through untouched. The merged material always uses VRChat's Toon Standard shader. Mobile's Good and Excellent ranks need a single material slot, and mobile avatars may only use VRChat's mobile shaders, so a single opaque Toon Standard material is the target. Source shaders (lilToon, Poiyomi, Standard) are only *read* to bake their colours into the atlas, so we never have to rewrite lilToon or Poiyomi material settings, which would roughly double the material-handling work.

## Consequences

- Toon Standard doesn't support transparency (SDK 3.8.1 release notes). Transparent and cutout materials become opaque flagged materials. Keeping them see-through (a second material on Medium, or removing hidden layers such as the Canis eye) is deferred to Pro.
- Materials swapped by animations or Modular Avatar become excluded materials in v1. Baking swap variants is the hardest case in the tool, and it is deferred.
- An excluded material still gets its own Toon Standard material. The SDK validates every renderer's shaders on a mobile upload, and a lilToon or Poiyomi material left in place would block it. Each excluded material therefore costs a material slot, and animations that swap it must point at its replacement.
  - Exceptions: a material whose shader mobile already allows stays as it is, and so does one on a renderer that doesn't upload (tagged EditorOnly). Two kinds can't be converted yet, and stay as unconverted materials that the build reports, since the SDK refuses them: a material on a particle, trail or line renderer, and a lilToon add-on layer, which has no surface of its own. #106 decided both: particles get VRChat's mobile particle shaders (#148), and add-on layers on extra slots are removed and reported (#149).
- A manual bake in a PC-target project may run the mobile path as a preview. Its figures are estimates, because other tools and source textures keep their PC formats.
- PC atlasing, which needs per-shader adapters to keep each material's original shader, is a later feature.

Amended 2026-10-13: widened from Android to all mobile builds, and excluded materials get their own Toon Standard material. See [triage research](../research/triage-open-questions.md), §3.
