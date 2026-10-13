# CMA Clothing Fix: design

**Date:** 2026-10-11  
**Target:** Corrupted Modeling Agency 0.66, BepInEx 6 IL2CPP  
**Status:** Draft for user review

## Problem

Clothing meshes intersect in-game. The supplied examples show body skin visible through a top below the bust, and a blue dress visible through a green outer garment. The intended result is to remove only the parts that should be covered while keeping visible portions of layered items, such as a skirt below a jacket.

## Goal and scope

Create a standalone BepInEx plugin that applies explicit, extensible clipping rules to known clothing combinations. Rules may target the body or an under-layer. The first rules will address the two supplied examples after their runtime clothing IDs and renderer structure are identified.

The plugin will not edit the game's archives or existing mod assemblies. Unknown items and combinations remain untouched. The first version will not promise automatic repair of every arbitrary outfit.

## Findings from the installed mod source

The local `CMA-Scenes` source targets .NET 6 and references BepInEx 6 IL2CPP plus the game's interop assemblies. Its wardrobe integration uses `RopaAdminDeCharacter`, `IRopaManager`, `ClothingItemMap`, worn clothing IDs, and `RopaCubre` coverage flags. The game manager exposes `OcultarPieza(id, ...)`, which hides a whole clothing item. Hiding a whole item is insufficient when only its torso should be hidden and its skirt should remain visible.

The runtime renderer/submesh layout of the pictured garments has not yet been identified. That is a required discovery step before choosing a rule target. The plugin must not hide a whole garment when doing so would remove an uncovered region.

## Design options

1. **Whole-item rules.** Use the game's wardrobe manager to hide an entire under-layer. Simple and compatible with the game API, but loses visible regions of partly covered items.
2. **Region-aware rules (recommended).** Resolve a worn item to a distinct renderer or use a confirmed game-provided region-aware visibility API, then hide only the covered piece. This can preserve skirts and sleeves when the assets or game API separate those regions. A Unity submesh is not assumed to be independently toggleable; any submesh-specific method must be proven against the installed runtime before it is used.
3. **Automatic mesh intersection and triangle masking.** Compute overlaps between skinned meshes at runtime. This could cover unknown combinations, but requires robust skinned-mesh processing, can create holes or artifacts, and adds performance and game-version risk. Defer unless item-specific rules prove insufficient.

## Components and flow

- **Plugin:** standalone assembly with its own GUID, version, BepInEx configuration, and logs.
- **Wardrobe observer:** finds active female characters and reads each manager's worn IDs when the outfit changes. Prefer a game wardrobe callback/Harmony hook if a stable method is confirmed; otherwise use throttled state snapshots rather than per-frame mesh work.
- **Rule resolver:** matches exact normalized clothing IDs and optional `RopaCubre` regions. No broad substring matching for actions.
- **Rule applier:** toggles only a confirmed independently controllable target. It records original enabled states, reapplies after outfit changes, and restores states when a rule stops matching or a character/scene is removed.
- **Diagnostics:** an opt-in mode logs character, worn item IDs, coverage flags, renderer paths, submesh counts, and material names to identify mappings. It does not change visibility.
- **Rules:** stored in a separate config file under `BepInEx/config`, with exact item IDs and explicit targets. Invalid or unresolved rules are logged and skipped.

If the game exposes no stable renderer or region-aware target for a region, the plugin will report that limitation and leave the outfit unchanged. Triangle-level mesh editing is a separate design decision requiring a demonstrated need.

## Safety and failure handling

- Only active female characters in a valid game scene are considered.
- Every rule is opt-in by exact item IDs; unrelated clothing is not modified.
- Errors are isolated per character/rule and logged; a failed mapping does not disable other rules.
- Original visibility is restored when conditions stop matching. Repeated updates must not accumulate cloned meshes or material instances.
- Diagnostics default off; no continuous verbose logging in normal play.

## Verification and acceptance

1. Build against the installed game's 0.66 interop assemblies without changing game files.
2. With diagnostics on, identify the exact IDs and renderer layout for both supplied outfits.
3. Add targeted rules and confirm the skin/body and lower garment no longer show through the outer garment.
4. Confirm uncovered parts (including the blue skirt), other clothing, and outfit changes remain visible and recover their original state.
5. Confirm unknown combinations are unchanged and unresolved rules produce a clear log message.

Visual acceptance requires in-game captures of both examples after the fix. A successful build alone does not establish that clipping is fixed.

## Open implementation constraint

The screenshots do not reveal whether the affected garment regions are separate renderers/submeshes. The first implementation phase must inspect this in-game. If neither model separation nor an existing region-aware game API can hide only the covered portion, stop before shipping a rule that removes the whole dress; present the smallest viable alternative for review.
