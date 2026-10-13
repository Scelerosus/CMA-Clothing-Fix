# Progress — 2026-10-11

## Current handoff — 2026-10-12 / 0.4.0

Clothing Fix and matching CMA Scenes technical worktree installed as 0.4.0 while the game was closed. Both DLL hashes match Release; DLL/config backups and hashes are recorded in `.rune/install-0.4.0-20261012-054917.json`. Clipping config retains Enabled/ExperimentalCoverage/Diagnostics=true, pose interval changed from the prior default 5 to 2 seconds; obsolete F6/F7 bindings removed, Menu=Slash. Saved drafts and game saves were preserved. 54 geometry checks, 42 actual wardrobe-code checks with native stubs, 38 catalog/storage/source checks and 29 static Outfit checks pass. Compiled Unity calls: 555 entry points audited against installed game interop, including stripped-method self-check.

Generic masks now use local normals, same-layer region containment and material alpha/UV sampling. Texture reads are bounded and cached; workers operate only on managed arrays. English `/` menu, native cursor/input driver, no manual recalculate hotkeys. Outfit is Browse / Outfit / Saved; staged styles, explicit Dress/Restore, native undress reconciliation, load rollback and retained cleanup ownership. Solid previews and material defaults share the same cache/request values.

User said "Я проверю сам потом"; leave game input and visual validation to them. Latest instruction: finish the work, then put the PC to sleep. Older pending steps/control keys below are historical. Do not claim 0.4.0 visually verified or every material perfect.

User authorized implementation after two clipping examples (body below bust through blue dress, blue dress through green outer garment). Do not ask for design/plan approval again.

Standalone plugin 0.1.0 implemented and installed at C:/Games/Corrupted Modeling Agency 0.66/BepInEx/plugins/CMAClothingFix/CMAClothingFix.dll. Release build succeeds, zero warnings/errors. No in-game verification yet. Git identity is unconfigured; no commit made, do not invent an identity.

IMPORTANT: no active garment rules yet; this is an inventory/rule-engine build, not a confirmed visual fix. Empty rules file is intentional because exact runtime IDs/meshes are unknown.

Diagnostics enabled in installed config. User needs to launch/load game and expose both outfits (Ctrl+F10 forces reports). Next agent should read BepInEx/CMAClothingFix-reports/*.json and BepInEx/LogOutput.log, identify exact garment/body targets, add rules only where covered geometry can be isolated. Preserve visible skirt and exposed skin. If a single submesh spans covered and exposed regions, this engine cannot safely mask part of it; explain evidence before changing geometry strategy.

Ctrl+F11 reloads rules without restart; Ctrl+F12 toggles masks. Main source README documents schema and limitations. DLL build is not evidence clipping was fixed.

## Runtime reports received

Two reports: 20261011-140259-952-482922.json and 20261011-140312-633-482922.json. Both contain the same 7 clothing IDs and the same 36 renderers (same enabled/active state); neither contains the green outer garment. Plugin loaded and report generation worked. IL2CPP warning about private WarnOnce(Exception): mark HideFromIl2Cpp in a future code update.

Confirmed target: manniakk.dresses.slitminidress; Slit Minidress(Clone), 4514 vertices, one submesh, 20472 indices, readable. Upper dress and skirt cannot be separately hidden through submesh selection. Body: CC_Base_Body(Clone), 16656 vertices; submesh 2 Skin_Body has 54354 indices spanning body/limbs. The underbust is not an independently toggleable region. Do NOT hide either whole submesh as a fix.

Current renderer/submesh rule approach cannot safely solve these screenshots. Next geometry approach needs local triangle masking or garment fitting, with actual geometry evidence before enabling edits. Still need a report wearing the green outer garment over the dress (Ctrl+F10; no restart necessary). No active masking rules were added.

## 0.2.0 implemented and installed

New reports include sma.clothing.aerosportshoodie (AeroSportsHoodie(Clone), 117746 vertices, 6 submeshes) and spruceless.clothing.clubdress (Dress(Clone), 19916 vertices, 2 submeshes spanning full height; transparent materials). User explicitly emphasized protruding breast/skin under dress; preserve that objective.

Implemented experimental local coverage masks in CoverageFitter/CoverageGeometry. Built-in pairs: Skin_Body submesh 2 under Slit Minidress; Slit Minidress and Club Dress under Aero Sports Hoodie. Exact IDs/vertex signatures required. No body masking under transparent Club Dress. Cover materials must have _SurfaceType=0 and no alpha cutoff. Pure managed BVH/ray tests on worker; native BakeMesh capture/apply on main thread. Masked triangles require all corners+center+4 footprint samples covered within +5.5/-3.5 cm. 85% removal limit. Pose sampled once after 2 second outfit stabilization; Ctrl+F11 recalculates, Ctrl+F12 restores. Explicit manual rules override automatic coverage; invalid rule file disables both.

Release build zero warnings/errors. 10 pure geometry checks passed including protruding skin, range, garment edge, dense BVH and holes. Installed DLL hash 30C7CB7204A3E0C196050CB052E4B53AF8028B013B6F0209CA66E277352E335F matches build. Previous 0.1.0 DLL preserved as CMAClothingFix.dll.v0.1.0.bak. Config Enabled=true, Diagnostics=true; new ExperimentalCoverage defaults true on next startup. No game process found during installation.

NEXT: user launches game and checks both clipping cases. Read LogOutput.log Coverage messages and any new reports. Do not claim visual success before runtime evidence. Check for mask holes at neckline/hem and body changes in different poses; may require Ctrl+F11. Current algorithm is experimental geometry coverage, not cloth collision physics.

## Universal Modder runtime check (2026-10-11)

Applied Ponytail and Universal Modder on user's request. Config confirms all three options true. UM CLI runs through `python -m um` in installed plugin directory; GPU capture setup completed with checksum-verified FFmpeg. User explicitly chose to drive wardrobe themselves; do not send input. Capture without --scale works (Pillow absent); Auto HDR changes colours.

Fresh game run confirms both masks without code changes: body under Slit at report 20261011-181622-882-482922 (3603/18118), Club Dress under hoodie at 181653-260 (12570/17460). Front screenshot of Slit shows intact dress, no underbust skin visible. Later captures show hoodie disabled (report 181742-798), therefore no visual verification of hoodie pair yet. User was asked to compare original low-angle view with Ctrl+F12; response pending. Source/installed DLL remain 0.2.0. MODLOG.md and README now reflect verified evidence and remaining limitations.

User answered the pending comparison: "Да, кожа исчезает". Underbust skin clipping is visually verified by user with fix on/off. README and MODLOG updated. Remaining: visual confirmation of dress under visible hoodie and different poses; do not claim these have been verified.

## 0.3.0 / Outfit refactoring (2026-10-11)
General native-layer coverage and combined surfaces replace garment-specific IDs. / menu, F6 recompute, F7 toggle; periodic pose refresh and one target capture per scan. 13 geometry checks pass; clean Release. Installed with timestamped backup and verified hash while game closed. Config Enabled/ExperimentalCoverage true. CMA Scenes 0.3.40 installed alongside: native undress synchronization, glasses, staged materials/color and labeled dress geometry; 34 catalog checks + 10 removal/sync checks pass. New runtime validation pending. User manages game input; do not drive wardrobe without authorization.
