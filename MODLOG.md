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

## 0.5.6 — never mask skin on the feet
- Bare foot in pumps still showed a jagged hole at the instep after the shallow window (0.5.5), so depth was not the cause. Skin triangles below 15 cm above the body floor (scaled) are now left untouched; the 0.5.5 shallow window was removed as dead. Limitation: lying poses get no skin masking. Not verified in game.

## 0.5.7 — higher foot exemption for heels
- Close-up in 0.5.6 still showed rectangular skin notches at the ankle above red pumps: with heels the ankle sits above the 15 cm cut. Foot exemption raised to 24 cm (scaled) above the body floor. If notches persist, test with ExperimentalCoverage off to separate this mask from the game own foot hiding. Not verified in game.

## 0.5.8 — foot exemption covers hosiery too
- 0.5.7 screenshots (mod on vs off) still showed a jagged dark hole in the pump opening with the mod on and a clean foot with it off. With tights worn the stocking was masked under the shoe, so neither stocking nor skin showed in the opening. The below-ankle exemption now applies to every masked renderer, not only skin. Not verified in game.

## 0.5.9 — never hide hands, feet or head skin
- Report showed body skin uses HDRP/LayeredLitTessellation (no alpha clip), so material-based boundary trimming is impossible for skin; foot behaviour stays as in 0.5.8. Added a bone-based guard: body triangles whose vertices are dominantly weighted to Head/Hand/Foot/Toe/finger bones are never masked. Bust, hips and torso keep their poke-through masking. Not verified in game.

## 0.5.10 — toes stay maskable
- The dark bone-shaped spot on the left pump is toes poking through the shoe (visible with the mod off too); 0.5.8/0.5.9 had disabled the mask that hides them. Toe-bone vertices are now excluded from the foot and extremity exemptions; the rest of the foot stays protected. Toe triangles are small, so whole-triangle masking should not take half the foot. Not verified in game.

## 0.5.11 — keep cloth under a thick outer hem; build targets game 0.67
- Latex jacket over a skirt left a jagged cut under the jacket hem (whole-triangle masking around a flared ribbed hem). New `[Coverage] CoverHemKeepCm` (default 3, scaled by height): a cloth triangle is hidden only if the points 3 cm below its corners are covered too, so a strip under the lower hem of an outer garment stays visible. Does not affect skin; 0 disables.
- Game moved to `C:\Games\Corrupted Modeling Agency 0.67`; csproj GameDir default updated and the build verified against 0.67 interop. Not verified in game.

## 0.5.12 — restore 3.5 cm cloth window
- Screenshot of a latex coat on a large bust shows dark flat patches where an inner layer pokes through the coat. 0.5.4 had cut the cloth-under-cloth depth to 1.5 cm to protect insteps; since feet are now exempt (0.5.8-0.5.10) the original 3.5 cm window (scaled by height) is back. Hypothesis from one screenshot; not verified in game.

## 0.5.13 — hem keep 3 cm -> 1 cm
- With a hoodie over a dress, the 3 cm strip kept visible under the hoodie hem let the flared dress show in front of the ribbed hem band (band visible only in pieces). Default `CoverHemKeepCm` is now 1; the config entry is only read once, so existing configs need the value changed by hand. Trade-off: a narrower strip leaves a thinner jag-smoothing margin. Not verified in game.

## 0.6.0 — experimental Push mode
- New `[Coverage] Mode = Hide|Push` (default Hide, behaviour unchanged). Push never deletes triangles: for every vertex of a masked target it finds the closest outer-garment surface along the vertex normal (either side, within `PushMaxCm`, default 3 cm scaled by height) and, if the vertex pokes out or sits within 2 mm of that surface, moves it back under the surface. World push is converted to mesh rest space through the per-vertex skin matrix captured at bake time, so vertex count, skin weights and blend shapes stay untouched; the replacement mesh is reused and only rewritten when offsets change by more than 1 mm.
- Known risks: offsets are a snapshot of one pose (stale during fast animation); thin parts are guarded by taking the closest hit, not the deepest. Five managed checks added (push depth, margin, skin matrix, ignore far/beneath cloth). Not verified in game.

## 0.6.1 — Snap mode and robustness fixes (after Opus design review)
- Review verdict: a shaped-rest-pose redesign is unsound here because bust size and jiggle come from bones (PHYSICS_SCALER_Pecho.*, DEF_PechoBase.*), not blend shapes, and garments use different bone/shape sets. Not built. Recommended next stage if needed: managed linear blend skinning in the current pose to replace BakeMesh, validated against BakeMesh (p99.9 < 0.3 mm).
- New `Mode = Snap`: Hide plus edge snapping. A covered vertex of a visible boundary triangle slides along its edge to where coverage ends (6 bisection steps), converted to rest space through the per-vertex skin matrix. Vertex count, weights and blend shapes stay untouched, so the 0.5.0 corruption causes (rewritten bone weights, Vector2 UVs, interpolated tangents, unwelded new vertices, topology change each scan) do not apply. Exempt feet/hands/head vertices are never snapped.
- Hide mode now reuses its replacement mesh instead of instantiating a new one on each change; Release destroys the replacement only after sharedMesh was restored. Plugin version is a single constant (`Plugin.Version`) used by BepInPlugin, the log and reports.
- Not verified in game; Snap is per-pose like Push, and texture stretches slightly along snapped edges.

## 0.6.2 — self-diagnosing log
- One `Outfit:` log line per outfit change (item id, kind, layer, visible, coverage flags) and each `Coverage` line now names the outer renderers, so problems can be read from LogOutput.log without saving a report by hand. Not verified in game.

## 0.7.0 — Inflate mode
- Report evidence: the game already fits garments (hoodie SMA_BreastSize_110/125 blended ~32/68%, others follow PHYSICS_SCALER_Pecho bones); what remains is imperfect fit at large sizes, i.e. garment vertices sinking below the skin. New `Mode = Inflate`: the body skin (Skin_Body/Pezones submeshes) is the fixed reference and every worn garment vertex that lies below it (within `PushMaxCm`, default 3 cm scaled) is lifted along the skin outward normal to 1.5 mm above the surface. Nothing is deleted, no layer rules or item lists, the body is never modified, body jewellery/accessories are skipped. Garment vertices whose normal disagrees with the skin normal are left alone. Offsets go through the same per-vertex skin matrix path as Push; rest vertices are cached per state.
- Five managed checks added. Not verified in game. Risks: per-pose snapshot like Push; rewriting large garments (hoodie 118k vertices) through the IL2CPP indexer may cost frame time; garment-vs-garment poke-through is still left to Hide/Snap.

## 0.7.1 — Inflate smoothing and inner-layer references
- Inflate lift now spreads to neighbouring vertices (0.7 falloff per ring, 3 passes, larger value wins so required lift is never reduced) to remove stair steps where lifted and untouched vertices meet (hoodie underbust: only 274 of 117,746 vertices moved, which made a sawtooth).
- References for a garment are now the skin plus every garment worn underneath it by the game layer order (ClothingOrder.Covers); an outer garment is lifted over inner layers instead of hiding them. Jewellery/accessories excluded. Two new managed checks. Not verified in game.

## 0.7.2 — tight hosiery and larger bust window
- Stockings sat 1.5 mm above the skin and looked loose. Garments on layer <= 1 (hosiery, underwear) now use a 0.7 mm margin and are also pulled in to it when they hover within 1.5 cm above the skin (scaled). Other garments use a 3 mm margin because the LayeredLitTessellation skin shader displaces the surface a few millimetres beyond the mesh, which showed as skin through thin cloth on the bust.
- `PushMaxCm` default 3 -> 5 so deeper bust sinks are caught; existing configs keep their value (cfg edited by hand). Two managed checks added. Not verified in game.

## 0.7.3 — seam welding, jewellery hidden, skin-only reference
- Blue dress close-up showed a thin slit along a cup seam: UV/hard-edge seams duplicate vertices, and each copy found its own lift. Inflate now welds co-located vertices (0.05 mm grid) so every copy gets the largest lift of its group, before and after each smoothing pass. New managed check.
- A barbell piercing showed through the dress. Inflate mode must not lift a dress by centimetres for jewellery, so body jewellery (underwear accessories) is still masked with the Hide algorithm while everything else inflates (`Request` got a per-call `mode`).
- The 0.7.1 inner-garment references made the hoodie hem band ripple over the skirt (0.7.0 and 0.5.13 shots show it straight); the skin is the only reference again. Stockings now hug the leg in the new shots. The sawtooth under the hoodie bust is unchanged by smoothing, so it comes from the hoodie mesh itself. Not verified in game.

## 0.8.0 — Inflate for skin, Hide for garment pairs, Push for jewellery
- Screens after 0.7.3: the dress showed through the hoodie again (no garment-vs-garment handling once inner references were dropped) and a nipple piercing showed through the dress. Inflate mode is now a combined job per garment: the skin lifts it (Inflate), and garments worn over it hide its covered triangles with the Hide algorithm (hem keep, feet exemption) in the same replacement mesh (offsets + kept triangles, applied only when they change). Body jewellery uses Push, so the barbell is squashed under the dress instead of hiding it by triangles or lifting the dress over it.
- Not verified in game; hoodie-over-dress hem edge may show the Hide jag again (hem keep 1 cm).

## 0.9.0 — managed skinning (validated against BakeMesh)
- New `Skinning.cs`: pure managed blend shapes + linear blend skinning with up to 4 influences (renderer quality / QualitySettings.skinWeights), extrapolation beyond the last blend shape frame, vertices without a usable bone follow the renderer matrix. 9 managed checks. `CoverageFitter` gets `[Coverage] Skinning = BakeMesh|Managed|Check` (default Check): Check bakes as before and, at most once per 30 s per renderer, also computes the managed surface and logs `Skinning check ...: error mm mean / p99.9 / max`. Managed uses it directly (no native bake, no sharedMesh swap); it falls back to BakeMesh with a one-time warning.
- Opus review applied: bone matrix formula confirmed; blend shape semantics confirmed (extrapolation fixed); mesh data cached by (id, vertex count, blend shape count). Not applied: the reference path still calls BakeMesh(useScale=false) then renderer.localToWorld, which double-applies scale if a renderer has lossyScale != 1; a scale-shaped Check error would mean the reference is wrong, not the managed path. Normal deltas of blend shapes are ignored. Acceptance to switch to Managed: p99.9 < 0.3 mm and max < 1 mm on body, hoodie, dress, shoes, stockings. Not verified in game.

## 0.9.1 — skinning diagnostics
- First Check results (0.9.0): body 16,656 verts p99.9 0.01 mm, stockings/shoes/bracelet/piercing <= 0.02 mm (exact). Mismatch on G-string (p99.9 2.3 mm, max 2.5), Slit Minidress (p99.9 4.2-4.5, max up to 10) and AeroSportsHoodie (p99.9 3.5, max 5.1) with mean ~0.1 mm, so a small fraction of vertices differ. Candidates: vertices with more than 4 bone influences (legacy boneWeights keeps 4) or blend shape semantics (G-string has a shape at weight 157).
- 0.9.1 logs per mesh how many vertices have more than 4 influences (GetBonesPerVertex) and, per Check, how many vertices are off by > 1 mm and how many of those lie on vertices moved by an active blend shape. No behaviour change.

## 0.9.2 — blend shape probe
- 0.9.1 logs: vertices with more than 4 influences exist on hoodie (29,765, max 8), dress (1,647), G-string (421) and even body (207), yet the body matches BakeMesh to 0.02 mm, so influence truncation is not the cause. Every vertex off by > 1 mm (G-string 26, dress 124, hoodie ~4,870) lies on a vertex moved by an active blend shape.
- 0.9.2 adds a one-off `Shape probe` per mismatching renderer: managed surface with each active shape left out in turn, and with weights clamped to 0..100, each compared with BakeMesh, plus shape name, weight, frame count, first frame weight and delta count. Behaviour unchanged.

## 0.9.3 — full bone influences in managed skinning
- 0.9.2 probe: leaving out any single blend shape never removes the G-string/dress mismatch (BODY_Fat p even helps), weights clamped to 0..100 change nothing, and BODY_Fat p touches 4,499 of 4,514 dress vertices, so the earlier blend-shape correlation was meaningless. Remaining hypothesis: Unity skins with all influences (QualitySettings.skinWeights = Unlimited) while the managed path used the legacy 4-influence boneWeights.
- 0.9.3 reads GetBonesPerVertex/GetAllBoneWeights into a variable-length layout (Skinning.MeshData.Start) and applies the renderer quality or QualitySettings.skinWeights (1, 2, 4 or 255). The probe logs the quality values and the error for 4 influences vs all influences. One new managed check. Not verified in game.

## 0.9.4 — Managed skinning is the default
- 0.9.3 Check results: body, G-string, dress, stockings, bracelet, piercing and shoes match BakeMesh to <= 0.015 mm (p99.9) / 0.015 mm (max); the 0.9.0-0.9.2 mismatch was the 4-influence limit (Unlimited skin weights). The hoodie was not worn in that session.
- Default `Skinning = Managed`: no native bake and no sharedMesh swap per capture. Each renderer is compared with BakeMesh once on first use; if p99.9 exceeds 1 mm it is added to a bad list, a warning is logged and BakeMesh is used for it from then on. `Check` and `BakeMesh` remain available. Not yet verified for the hoodie in game.
