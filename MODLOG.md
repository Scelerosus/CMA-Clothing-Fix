## 0.5.1 — Remove regressed native subdivision

- User reports broken skinned clothing and stutters in 0.5.0. Removed BoundaryMesh and runtime refinement call; native meshes now retain original vertex count, attributes, skinning and blend shapes. Removed waist seam candidate and height filter. Pure managed refinement experiments/tests remain disconnected from runtime; do not claim their assertions validate the active mask.
- Restored index-only mesh reuse and native wardrobe polling interval 300 ms (previously 100 ms). Main-thread subdivision allocation and blend-shape reconstruction removed. Still uses background geometry worker and periodic native mesh captures; frame pacing needs live comparison.
- Build zero warnings/errors; managed checks pass, 554 Unity API audit passes. Installed after user closed game, prior DLL backed up; SHA256 equality verified. Visual/performance validation pending.

## 0.5.0 — Partial boundary subdivision and restricted waist seam

- User close-ups show retained angular lingerie along shirt opening and trouser fragments above belt. Boundary triangles now recursively split to depth 3 / 3 mm edge size only where samples disagree. Shared original indices retained elsewhere; generated vertices carry original triangle barycentrics. 20,000 generated-vertex budget fails closed. Body and jewellery retain existing masking.
- BoundaryMesh interpolates position, normals, tangents, colours, UV0–7, normalized strongest four bone weights and all blend-shape frame deltas. No game meshes mutated. New clone only after completed worker job. Added vertices disable index-only mesh reuse.
- Same-layer top/lower remains globally unordered. A separate candidate allows top coverage only in the upper 10 mm of posed trouser geometry; per-surface minimum target height persists through Combine so dresses retain unrestricted coverage. Limitation: world-Y standing-waist heuristic; unusual belts and bent poses need live validation.
- 18,274 order assertions and geometry checks pass, including partial retention, barycentrics/index validity, unselected parts, holes and restricted/ordinary mixed covers. Release build clean; 600 Unity API entry points audited. Runtime visual/animation and frame-time validation pending.

## 0.4.6 — Preserve native tucked top/trouser seams

- User comparison confirms the mod destroys the native intact waistband/belt. Report 20261012-164130-515-482922 identifies native tvalle shirt (SleevedTop) and pants (Lower), both layer 3, sharing belly coverage. Sleeve rank wrongly let the shirt hide trouser belt geometry.
- Same-layer Upper/SleevedTop versus Lower candidates are now blocked in both directions. No new tuck settings or garment-specific rules. FullBody and Jacket coverage, body/underwear masks and piercing fixes retained.
- 18,272 order assertions and geometry checks pass; regression uses the exact native shirt/pants coverage flags. Release build without warnings/errors. Visual comparison after restart remains pending.

## 0.4.5 — Underwear accessory metadata and curved surface sampling

- User screenshot and report 20261012-163557-298-482922 show Barbell Piercing LR: UpperUnderwearAccessory, native layer 4, Coverage None, 594 vertices. Correct its effective ordering through category (not name/ID): underwear accessories cap layer at 1 and use chest/pelvic flags if absent. Regular zero-flag accessories stay protected.
- Add cloth face-normal fallback where smooth normals miss a covering surface. Jewellery additionally samples three 15 mm axis segments, retaining per-hit opacity and boundary checks; this handles round metal normals without hiding the whole renderer.
- 18,270 order assertions and geometry checks pass; fewer generic-native precedence assertions reflect the explicit underwear-accessory exception. Release build clean; 554 Unity API audit passes. Runtime visual verification pending. Partially covered triangles are still preserved; this update does not implement exact boundary clipping.

## 0.4.4 — Common clothing order policy

- Replaced pairwise sleeve/dress heuristics with ClothingOrder, covering all 14 native catalog categories plus full-body and sleeved coverage variants. Verified category and coverage constants against installed interop metadata (TValle.Modding.Tools and Assembly-D_E_Chuchi_ReSc). Native layers remain authoritative, same-layer ranks then strict region containment define candidates, no names/IDs. Unknown category versus known category and zero-region targets fail closed; native uncatalogued torso/pelvis pieces derive a role from flags.
- Wardrobe caches catalog categories for two seconds; reports include Kind, signatures include role/layer/flags to invalidate masks after metadata changes. Tests cover all category pairs, eight native layers, mutual-mask exclusion and three-piece cycles.
- 21,915 order assertions and remaining geometry checks pass. Release builds without warnings/errors; 554 Unity API audit passes. Geometric openings and cloth/body boundary safeguards unchanged. Runtime appearance across all garments is not verified.

## 0.4.3 — Full torso garments over trousers

- User screenshot and report 20261012-162321-834-482922 confirm Slit Minidress and Aero Yoga Pants both at native layer 3 with partially overlapping torso/pelvis/leg flags. Neither was scheduled for inter-garment coverage.
- Full torso/pelvis coverage now orders over leg-only clothing on the same native layer. Sleeve priority remains first, then this full torso relation, then strict region containment. No garment IDs; geometric and alpha tests retain uncovered trouser legs and openings.
- Release builds without warnings/errors; 65 managed checks pass. Runtime visual confirmation and installation pending game closure.

## 0.4.2 — Faster wardrobe response and cloth boundary tolerance

- Removed initial 2-second delay; wardrobe scans every 100 ms, garment targets precede body targets. Geometry remains a single background worker; heavy meshes still require compute time.
- Cloth-only edge samples move inward at most 2 mm (maximum 10% toward triangle center) to reduce boundary fringe. Body uses zero tolerance; alpha and penetration limits unchanged. Whole-triangle masking still cannot exactly trim crossing boundaries.
- 61 geometry checks pass, including boundary tolerance and exact body preservation. Runtime screenshot validation pending.

# CMA Clothing Fix — field notes

## Current update — 0.4.1

- User screenshot shows dress clipping through hoodie. Report `20261012-160815-426-482922.json` confirms AeroSportsHoodie and Casual Dress both use native layer 3 with partially overlapping flags; neither passed the former strict-containment ordering. No dress coverage job was scheduled.
- Shared ordering now treats torso-and-arm coverage as the outer garment over a sleeveless overlapping target on the same layer. No garment IDs; higher native layers still win, disjoint regions and equally sleeved pieces remain unordered. Distance and alpha safeguards unchanged.
- Release build passed with zero warnings/errors; 59 geometry checks passed, including five ordering regressions. Visual confirmation of this updated pair remains pending. Sleeve-region inference cannot determine arbitrary layering of two sleeved pieces or a sleeveless outer vest.

## Current update — 0.4.0

- Generic native layers and region flags, including strict containment within the same layer. No garment-ID pairs.
- Per-vertex posed normals and interior triangle samples fix curved body areas and narrow straps; cached per-vertex results and unchanged-mesh reuse reduce repeated work.
- Alpha-aware ray hits preserve texture openings; texture UV0 scale/offset and wrap modes are checked per material. One alpha texture read and one target capture per scan, one geometry worker, oldest-ready job priority. Small readable textures use their pixels; large or unreadable/compressed textures use a temporary GPU copy capped at 256 × 256. Cached alpha follows texture updateCount and current wardrobe references.
- English `/` menu uses the native cursor/input driver. Automatic pose and outfit updates replace F6/F7 manual actions. File changes reload configuration and rules automatically.
- Final validation on 2026-10-12: 54 geometry checks pass, Release builds have no warnings/errors; combined audit checks 555 Unity API entry points against installed game interop. CMA Scenes 0.4.0 accompanies this build with 42 wardrobe fixtures, 38 catalog/storage/source checks and 29 UI source guards.
- Live checks are left to the user. Ambiguous layers, unsupported UV/shader mapping, subpixel alpha openings and partial triangle coverage remain limitations. Earlier confirmed underbust fix is historical evidence, not a validation of every 0.4.0 combination.
- Installed both 0.4.0 plugins while game closed; matching Release hashes, old DLLs and config backups are recorded in `.rune/install-0.4.0-20261012-054917.json`. Config enables automatic masks at a 2-second pose interval with `/` as the only clipping-menu binding. Existing saved outfits were preserved.

The entries below document earlier builds and their controls; use the current README for 0.4.0.

- Game: `C:/Games/Corrupted Modeling Agency 0.66`, Unity IL2CPP, BepInEx 6.
- Installed plugin: `BepInEx/plugins/CMAClothingFix/CMAClothingFix.dll`, version 0.2.0. Previous DLL kept as `.v0.1.0.bak`.
- Scope: skin under the bust through Slit Minidress; Slit/Club Dress through Aero Sports Hoodie. Runtime garment IDs and geometry signatures are in `CoverageFitter.cs`.
- Build and ten managed geometry checks passed. In-game activation is confirmed: on 2026-10-11, report `20261011-181622-882-482922.json` and log show 3603/18118 body triangles masked under Slit Minidress. The front screenshot shows an intact dress and no visible underbust skin; the original low-angle view and hoodie still need checking.
- Runtime reports: `BepInEx/CMAClothingFix-reports`. Historical 0.2.0 reports include both target combinations. A fresh run produced coverage calculations without code changes; absence of messages in the previous session alone did not establish a matching bug.
- Universal Modder CLI is available through `python -m um` in its installed plugin directory. `um win setup` installed GPU capture tools and checksum-verified FFmpeg. Screenshot capture works; omit `--scale` in this Python environment because Pillow is absent.
- User chose to operate wardrobe controls themselves. Do not send game input without new authorization.
- Auto HDR is enabled; captures can have shifted brightness/colour. Judge geometry, not colour.
- Detailed implementation history: `.rune/progress.md`. Controls: Ctrl+F10 report, Ctrl+F11 recalculate, Ctrl+F12 restore/toggle.
- Second runtime activation: `20261011-181653-260-482922.json`; Club Dress under Aero Sports Hoodie masked 12570/17460 triangles. Subsequent screenshots were taken after hoodie visibility became false, so they do not verify the visual result of that pair.
- User confirmed the original low-angle body comparison using Ctrl+F12: protruding underbust skin disappears with the fix enabled. First clipping case verified in the user's scene; other poses remain unchecked.

## CMA Scenes compatibility — 0.3.38

User reported Outfit leftovers. Active orphan Dress(Clone) without a wardrobe owner was confirmed in historical reports, including after clipping masks were disabled. Updated the matching CMA Scenes worktree at C:/Users/Scelerosus/source/repos/copilot-worktrees/CMAScenes-technical: capture/remove detached garment renderers after successful native wardrobe removal, preserving references held by equipped garments. CMA Scenes 0.3.38 installed with backup while game closed; Release SHA256 B2E2929245AEE1E7F8C5A5F950BA23778976287367C27808AF2E664EA5796670. Six isolated cleanup checks pass. Clothing Fix remains unchanged. Restart/apply/remove runtime verification pending; do not claim native cleanup is visually confirmed.

## 0.3.0
Native layer coverage for all eligible garments; combined outer surfaces, periodic pose refresh; / menu, F6 recompute, F7 toggle. One target capture per scan; worker-side surface combine; removed unused state. Release build and 13 geometry checks pass; runtime verification pending.
- Installed 0.4.1 after the user closed the game; previous DLL backed up beside the installed plugin. SHA256 equality verified; installation manifest is in `.rune/install-0.4.1-*.json`.
- Installed both plugins at 0.4.2 while game closed; previous DLLs backed up and installed SHA256 equality verified. See `.rune/install-0.4.2-*.json`.
- Installed 0.4.4 while game was closed; prior DLL backed up and SHA256 verified. Manifest `.rune/install-0.4.4-*.json`.
- Installed 0.4.5 while game was closed; prior DLL backed up and SHA256 verified. Manifest `.rune/install-0.4.5-*.json`.
- Installed 0.4.6 after user closed game; previous DLL backed up and hashes verified.
- Installed 0.5.0 while game closed; prior DLL backed up and hash equality verified. Manifest `.rune/install-0.5.0-*.json`.

## 0.5.2 — exposed skin at garment openings
- Body masks now require opaque coverage at the sample and four neighbouring tangent probes (4 mm). Garment masks retain their existing policy.
- No vertex, skin-weight or blend-shape changes. Preserves more skin near necklines and underbust boundaries.
- Added three geometry regressions for exposed skin, fully covered skin and unchanged garment masks; managed checks, Release build and installed interop audit passed.
- Installed with a backup of 0.5.1. In-game appearance and frame-time cost have not been verified. Additional body ray queries can increase background calculation time; native mesh baking is unchanged.

## 0.5.3 — proportional skin-mask reach
- Skin was hidden whenever any garment lay within a fixed 5.5 cm above / 3.5 cm below it along the vertex normal, so loose cups and straps hid skin that stays visible from other angles. Reach is now configurable (`[Coverage] CoverReachCm` 2, `CoverDepthCm` 5, defined for a 170 cm character) and scaled by body-bounds height. Geometry defaults are unchanged; managed checks and Release build pass.
- Not installed and not verified in game. Per-model bust/hip proportions are only approximated by height; raise `CoverDepthCm` if skin pokes through on large busts.

## 0.5.4 — shallow window for cloth under cloth
- Screenshot showed stocking triangles cut away at the ankle inside the shoe opening (log: veiled_stockings 942/3118 masked). The 5 cm depth added in 0.5.3 reached shoe soles through the instep. Depth now applies only to skin (`CoverDepthCm`); garments under garments use 1.5 cm scaled by height. Not installed or verified in game.

## 0.5.5 — shallow skin window at the feet
- Feet still missing after 0.5.4: body skin used the deep window (CoverDepthCm) at insteps, reaching shoe soles. Skin below 15 cm above the body-bounds floor (scaled) now uses a 1 cm window. Standing-pose heuristic; not verified in game.
