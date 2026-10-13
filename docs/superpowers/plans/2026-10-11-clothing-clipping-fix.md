# CMA Clothing Fix implementation plan

Spec: ../specs/2026-10-11-clothing-clipping-fix-design.md

User explicitly authorized implementation; execution is local in this chat.

- [x] Inspect installed wardrobe APIs and use exact worn-item IDs and renderer ownership.
- [x] Build independent net6.0 BepInEx IL2CPP plugin, configuration and report hotkeys.
- [x] Add validated JSON rules with exact renderer/mesh guards; fail closed on invalid configuration.
- [x] Add reversible whole-renderer and readable triangle-submesh masking with reusable mesh copies.
- [x] Compile against the installed game libraries.
- [x] Install the diagnostic build with Diagnostics=true (DLL hash matched source build).
- [ ] Collect reports from both user-provided outfits.
- [ ] Identify independently controllable geometry and add exact rules for the two examples.
- [ ] Confirm visually in-game that covered surfaces disappear while exposed clothing remains visible.

Files: Plugin.cs (lifecycle), Wardrobe.cs (inventory/reports), RuleStore.cs (configuration), VisibilityLedger.cs (reversible masks), README.md (usage/limitations).

Do not invent garment IDs or ship blanket body/dress hiding as a fix. If the required region is not separable, document that evidence and choose the next geometry approach with the user.
