# Sprite Atlas Optimizer

Editor window that keeps **UI sprites and Gameplay sprites in separate atlases** so Canvas batches and
world-sprite batches never pull each other's atlas pages. **Tools ▸ RiftRaiders ▸ Optimize ▸ UI Sprites /
Gameplay Sprites / Sprite Atlas Conflicts**: one window, tabs **UI · Gameplay · Conflicts · Settings**.

## Why (batching rules this enforces)

- **uGUI** merges consecutive Graphics (in hierarchy/depth order) into one batch when they share **material +
  texture**. A sprite packed in an atlas uses the atlas *page* texture, so every UI sprite on the same page batches.
  A UI Image whose sprite lives in the gameplay atlas (or in no atlas) uses a different texture and splits the batch
  — as does anything drawn between two batchable elements that overlaps them (TMP text uses its font atlas material;
  that ordering problem is **plan 2**, not handled here).
- **SpriteRenderers** batch the same way (same material + texture), sorted by layer/order.
- **A sprite can only live in one atlas.** If the same sprite is packed in two atlases, Unity binds it to one of them
  (with a warning) — memory for the other copy is wasted and one side draws from the wrong page. If one sprite is drawn
  by both UI and gameplay, one side always samples the other side's atlas → **duplicate it** for one side.
- Sprites on **different pages** of the same atlas are different textures and don't batch — keep atlases to one page.

## Pipeline

1. **Scan** (`SpriteUsageScanner`, ~4s on this project): every Sprite reference in enabled Build Settings scenes
   (opened additively, read-only, then closed), all prefabs, ScriptableObjects and `.anim` sprite clips outside
   `ExcludedUsageFolders` — plus any prefab/SO/clip/material a scanned scene or prefab depends on, even inside an
   excluded folder. Only assets whose direct dependencies include a Sprite texture are loaded. Only the place a value
   is **defined** is recorded (a prefab-instance value that isn't overridden belongs to its source prefab), so each
   usage can be rewritten in exactly one place. Materials / RawImages / TMP sprite assets that reference a *packed*
   texture directly are recorded as raw users.
2. **Context per usage**: Renderer/ParticleSystem → Gameplay; any other component on a **RectTransform** → UI; else
   Gameplay. Custom MonoBehaviour fields and ScriptableObject fields get a rule key `DeclaringType.field`
   (`UpgradeData.Icon`, `CharacterData.PawnSprite`) — data fields are guessed by name (icon/ui/portrait/… → UI,
   projectile/pawn/body/… → Gameplay, else **Unknown**). Rules can be overridden in Settings; a per-texture
   override (row popup in the UI/Gameplay lists) forces UI/Gameplay/Ignore for a whole texture.
3. **Runtime recorder** (`RuntimeSpriteRecorder`, toolbar toggle "● Record play mode"): while on, samples live
   Image/Selectable/SpriteRenderer/SpriteMask/sprite-sheet ParticleSystems once a second in Play mode and stores
   `sprite → context` in `Library/SpriteOptimizer/RuntimeSightings.json` (per machine). Rescan to include it. This is
   the ground truth for sprites assigned from code; it can't be auto-reassigned.
4. **Analysis** (`SpriteAtlasAnalysis`, instant — rerun after any atlas/settings change) reads every atlas fresh and
   emits issues with fixes. Only atlases bound to **UI** or **Gameplay** are managed; **Ignore**/unbound atlases
   (e.g. `IntroAtlas`) are left alone, but still count for "in several atlases".

## Scene filter

Toolbar popup: **All scenes / each scanned scene / Not in any scene (code / Resources)**. The scan records, per
scene, its recursive dependencies (`ScanResult.ScenesByAsset`); `ScenesOf(usage)` is the scene itself for scene
usages, every scene that depends on the prefab/data/clip otherwise, and the recorded scene for runtime sightings.
The filter narrows the issues (an issue passes when any of its references is in the scene; atlas-level issues
always show), the tab counts, the texture rows and their reference lists, **Sync** and **Duplicate all shared**
(both act only on what's shown). Every non-scene reference row also shows "· in MenuScene, …".

On this project: Splash 3 refs, Intro 22, MenuScene 312 (10 shared sprites), GrasslandOutpost 470 (11 shared),
no scene 616 (9 shared). The "no scene" bucket is mostly Quantum assets reached through the Quantum asset DB
(not a scene dependency) — record a play session to attribute those to a scene.

## Issues

| Kind | Tab | Fix (★ = included in Sync) |
|---|---|---|
| Not in atlas — sprite used by C is in no C atlas | C | ★ add texture to the C atlas it's already in, else C primary (per-sprite if the sheet is already packed per sprite) |
| Wrong atlas — used only by C, packed in the other context's atlas | C | ★ move |
| In several atlases | Conflicts | Keep in X (★ when X matches the only context using it) |
| UI + Gameplay — one sprite used by both | Conflicts | **Duplicate → UI / → Gameplay** (★ label = suggested side: the one not already owning the atlas copy) |
| Split sheet — texture has UI-only and Gameplay-only sprites | Conflicts | ★ pack per sprite (whole-texture entry removed; each sprite into its context's atlas). Blocked while the sheet has shared sprites |
| Unused, packed — no sprite of it is used anywhere scanned | C | remove (only in Sync with "also remove unused"). Warns for `Resources/` |
| Raw texture use — packed texture also referenced by a material/RawImage (ships twice) | C | info only |
| TMP sprite sheet packed (TMP never samples the atlas) | C | remove |
| Not a Sprite (Texture Type ≠ Sprite, atlas ignores it) | C | ★ remove |
| Unclassified field — data field the scanner can't place | Conflicts | set rule UI / Gameplay / Ignore |
| Duplicate / missing packable entries | C | ★ dedupe / drop |
| Multiple pages (estimate: Σ(sprite rect + padding)² / 0.85·max²) | C | info; "Pack Preview" on the atlas card gives the real page count (internal `GetPreviewTextures` via reflection) |
| Atlas has no context / no primary atlas for a context | Conflicts / C | bind (Settings) |

## Duplicate & reassign (`SpriteDuplicator`)

For a shared sprite and a target side T:
- **Single-sprite texture** → `AssetDatabase.CopyAsset` to `<T duplicate folder>/<Name>_UI|_Game.<ext>`.
- **Sprite from a PNG/JPG sheet** → only its pixels are extracted (source file decoded with `LoadImage`, rect from
  `ISpriteEditorDataProvider`) into `<Sheet>__<Sprite>_UI.png`, Single mode, same pivot, 9-slice border,
  compression/platform overrides, and PPU = `width / sprite.bounds.x` so world size is unchanged even if the sheet
  was downscaled on import.
- **Other sheet formats** → the sheet is copied once and only the needed sprites are packed.
- The copy goes into T's primary atlas; every T-context usage (prefab via `SavePrefabAsset`, scene opened
  additively + saved, ScriptableObject, `.anim` keyframes) whose property still holds the original is repointed.
  Runtime-only usages are listed in the Console for manual change. Names are deterministic, so re-running reuses
  the copy. After duplicating, a rescan runs; remaining placement (e.g. the original still packed in T's atlas) is
  handled by Sync.

Default folders: `Assets/_Project/Art/Sprites/_AtlasSplit/UI` and `…/Gameplay`.

## File map — `Assets/_Project/Editor/SpriteOptimizer/`

| File | Role |
|---|---|
| `SpriteOptimizerSettings.cs` (+ `.asset`) | `SpriteContext`, atlas bindings (context + primary), excluded folders, scan-all-scenes, duplicate folders, field rules, texture overrides. Created on first open; binds atlases by name (ui/menu/hud → UI, game/world → Gameplay). |
| `SpriteOptimizerModel.cs` | `SpriteKey`, `SpriteUsage`, `SpriteInfo`, `TextureInfo`, `AtlasInfo`, `AtlasMembership`, `Issue`/`IssueFix`, utils (index paths, rule-key cleanup). |
| `SpriteUsageScanner.cs` | The scan (see Pipeline). |
| `SpriteAtlasAnalysis.cs` | Context resolution, issues + fixes, rules table, per-context texture rows. |
| `AtlasEditBatch.cs` | `AtlasEditBatch` (queued add/remove/dedupe applied in one pass per atlas through `m_EditorData.packables`, handles Sprite packables) + `AtlasReader` (memberships, folder packables, max size for active platform, pack preview). |
| `SpriteDuplicator.cs` | Duplicate & reassign. |
| `RuntimeSpriteRecorder.cs` | Play-mode sampler. |
| `SpriteOptimizerWindow.cs` | The window. Scan cached statically (lost on domain reload → Rescan). Reuses `BuildAnalyzer/PagedListDrawer`. |

Supersedes `SpriteAtlasScannerWindow` (Tools ▸ Art, open-scenes only) and complements the Build Size Analyzer's
path-heuristic `NotInAtlas` finding with a real usage scan.

## Known simplifications

- Context is structural (RectTransform vs Renderer). A custom component on a non-UI object that feeds a sprite into
  UI is classified Gameplay until a field rule or the runtime recorder says otherwise.
- Sprites loaded by path (`Resources`, Addressables) are invisible to the static scan → record a play session before
  removing "unused" textures.
- "Not in atlas" packs the **whole** texture (matches current practice); unused sprites of that sheet come along.
- The page estimate ignores tight packing/rotation; use Pack Preview.
- Scene reassign saves the scene it touched (asks to save modified open scenes first).

## Current status (2026-09-28)

Compiles; scan + analysis verified in the live Editor on this project (1424 refs, 140 prefabs, 4 scenes,
183 data assets, ~4s). First results: 16 UI+Gameplay shared sprites (hero Geometric heads, RR_Coins/Rift/Scraps,
Shadow, HandCircle…), 4 split sheets, 105 UI / 24 Gameplay textures not in their atlas (mostly Layer Lab UI),
`minimapCharacters` in UISprites but used by gameplay, 8 non-Sprite ETFX textures + raw-material-only textures
packed in GameplaySprites, EmojiOne (TMP) in UISprites, UISprites ≈ 2 pages at 4096. `IntroAtlas` bound to Ignore.
**Not yet exercised in-Editor:** Sync, Duplicate & reassign, Pack Preview, the runtime recorder.

**Plan 2 (later):** TMP/UI draw-order optimization (text vs image interleaving that breaks batches).
