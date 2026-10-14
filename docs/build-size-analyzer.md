# Build Size Analyzer

Editor window that turns the last player build into a **size Fix Plan**: one recommended action per asset,
ranked by bytes saved, each with its reason, the alternatives, who references the asset and why it is in the
build at all. **Tools ▸ RiftRaiders ▸ Build ▸ Analyze Last Build.**

## What it does

1. Loads `Library/LastBuild.buildreport` (`BuildReport.GetLatestReport()`, with a copy-into-Assets fallback),
   sums every object Unity wrote into the archives per source asset, and buckets assets into categories.
   Atlas pages appear in the report as built-in textures (`sactx-0-4096x8192-Crunch-GameplaySprites-<hash>`);
   they are parsed into per-atlas **build pages**, which gives the real cost per sprite pixel of each atlas
   (≈0.08–0.1 B/px with Crunch) instead of a format guess.
2. Saves a per-platform size snapshot (`Library/BuildAnalyzer/<Platform>-last|prev.json`) so every asset and
   the total show a **Δ vs the previous analyzed build** — the way to confirm a fix actually shrank the build.
3. **Texture reference index** (`TextureReferenceIndex`): reads the YAML of every shipped asset plus the Build
   Settings scenes. A reference to `fileID: 2800000` is the Texture2D itself (materials, RawImages, TMP sprite
   assets, Texture fields) — that is what makes Unity write a texture into the build even when it is atlased.
   Any other fileID is one of its Sprites. Sprite references also vote **UI vs Gameplay**: SpriteRenderer /
   ParticleSystem / SpriteMask → gameplay, an Image's `m_Sprite` → UI, custom/data fields → the Sprite
   Optimizer's field-name rule (`SpriteUsageScanner.GuessFromFieldName`).
4. Plans every shipped texture (below), then audits atlases, audio and byte-identical duplicates.
5. Flags anything **edited after the build** (file or `.meta` newer than the report file, or packed by an atlas
   that was) with ⟳ — its numbers are the old build's. A banner also says when a newer report exists.

## Texture decision tree (`TextureAudit.Plan`)

| Situation | Plan |
|---|---|
| Only its Sprites shipped (pixels came from an atlas page) | nothing — shown as "✓ from atlas" |
| Atlased, Texture2D still shipped, texture/atlas edited after the build | `PendingRebuild` ⟳ — "rebuild to confirm" |
| Atlased, Texture2D shipped, used as sprite **and** texture | `DirectTextureReference` — **Copy for material(s)** (keeps atlas batching) |
| Atlased, no sprite of it used (YAML + usage scan), only used as a Texture2D | `DirectTextureReference` — **Remove from atlas** (the atlas copy batches nothing) |
| Atlased, sprites used but the texture still ships (no YAML texture ref) | `DirectTextureReference` — something loads the Texture2D (code / binary); select / "Why is this in the build?". Never removed from the atlas |
| Sprite, not atlased, only used as sprites, fits | `AtlasCandidate` — **→ UI / Gameplay atlas**. No POT/×4: the atlas page already is |
| Anything else that ships as its own texture | standalone checks ↓ |

**Fits an atlas** = Sprite type, no Texture2D references, under `Assets/` and not a demo/reference folder,
largest sprite rect ≤ ¼ of the atlas Max Size, total sprite area ≤ ⅛ of a page. Target atlas = UI or
Gameplay by context: Sprite Optimizer usage scan (if run, toolbar **Scan sprite usage**) → reference votes →
texture path → referencer paths.

**Standalone checks** (savings use the platform's compressed bpp: DXT5/DXT1 on WebGL/Standalone, ASTC 6×6 on mobile):
- `NotBlockCompressible` — not a multiple of 4 *and* it shipped at ≥2 B/px (or is set uncompressed): DXT/ETC2/ASTC
  work on 4×4 blocks, so Unity silently falls back to RGBA32. This was the 6 MB World2DuneLeviathanUI / icon-sheet case.
  - **Sprites → Pad to ×4** (`PadToMultipleOf4`, recommended): rewrites the PNG/JPG adding ≤3 transparent px.
    No rescale: sheets are padded right/top (Unity sprite rects are bottom-left based, so every slice keeps
    pointing at the same pixels); single sprites are padded around the center with a Custom pivot pinned to
    the same art pixel; 9-slice borders grow by the padding.
  - Sprites → **Make POT (keeps slices)** (`MakePowerOfTwoKeepingSlices`, alternative): **uniform** scale so the
    larger side lands on its closest POT, transparent padding up to POT per axis, every slice rect and border
    scaled by the same factor, **Pixels Per Unit × the same factor** → identical shapes and world size. Bigger than
    padding; only when something truly needs POT.
  - **Material / non-sprite textures → POT (NPOT → ToNearest)** (`SetNpotToNearest`): importer setting only,
    non-destructive; UVs stay 0–1 so materials look the same.
- `Uncompressed` — set uncompressed / ≥3.5 B/px with ×4 dims → Set Compressed.
- `NpotMaterialTexture` — note (no size impact): NPOT material texture; mipmaps/Repeat on NPOT break on WebGL 1.
- `MipmapsOnSprite`, `NoAlphaButAlphaFormat`, `ReadWrite` (note).
- Quality **tradeoffs** (counted apart, toggle "Tradeoffs"): `CrunchCandidate` (DXT/ETC, ≥256 KB, not crunched
  → Crunch quality 50, ≈½ size) and `OversizedTexture` (>2048, or a sprite >1024 px and ≥512 KB → Max Size to the
  next lower POT; sprites keep their on-screen size).

Platform-setting fixes write the platform override when the texture has one for the report's platform, else the defaults.

## Fix Plan tab

Default tab. Rows = findings with savings (notes hidden unless the "Notes" group is picked), sorted by bytes.
Columns: badge (✓ fixed / ⟳ changed since build), saves (purple italic = tradeoff), asset, current size,
recommended action, one-line reason. **Apply** runs the recommended option, **▾** the others. Clicking a row
expands the full reason, every option with its tooltip, referencers, and **Why is this in the build?**
(`ShipReasonIndex`: reverse dependency graph of the shipped assets, built on first use, walked up to Build
Settings scenes / Resources folders / Preloaded Assets — e.g. why `Assets/Test/...` tileset textures ship).
Group filter (Atlas, Compression, Tradeoffs, Direct references, Atlas hygiene, Audio, Duplicates, Notes).
A summary bar shows **what applying every shown fix would do**: packed assets before → after and a rough build
size (`BuildAnalysis.CombinedSavings` applies the estimates of one asset one after the other on what's left, so
pad ×4 + Crunch isn't counted as 125%; `EstimatedBuildSizeAfter` scales the packed saving by this build's
output/packed ratio). **Apply all shown** applies the recommended option of every shown row that saves size and
has a safe automatic fix (mixed kinds; rows needing a choice — keep in which atlas, select referencers,
duplicates — are skipped; tradeoffs only while the Tradeoffs toggle is on). Confirm dialog lists counts and
savings per action; file rewrites / atlas edits run first, then all importer-only changes in one
`StartAssetEditing` batch. The top bar shows the same for all safe fixes.

**Atlas batching is never traded for size**: a texture whose sprites are used is never offered "Remove from
atlas". When it is also used as a Texture2D (material, RawImage…), the recommendation is **Copy for
material(s)** (`TextureAudit.CopyForTextureUsers`): a Default-type `<name>_Tex` copy next to the original, every
non-scene Texture2D reference repointed to it (scene references are logged for a manual fix), sprites stay in
the atlas. No size win (the double cost is the price of batching), but the copy can be shrunk on its own.

Other tabs: **Overview** (build + Δ, plan totals per group, biggest wins, categories, atlases with build pages,
largest assets), **Assets** (Δ column, sort by Δ, expansion shows the plan and "why in build"), **Textures**
(dims in red when not ×4 and shipped as a texture, Use = Sprite·UI / Sprite·Gameplay / Material / both, plan +
Apply), **Atlases** (build pages and cost per pixel, ⟳ when edited after the build), **Audio** (unchanged).

## File map — `Assets/_Project/Editor/BuildAnalyzer/`

| File | Role |
|---|---|
| `BuildReportLoader.cs` | Finds/loads the report; report file time (UTC); platform-name mapping for texture/audio settings. |
| `BuildAnalysis.cs` | Data model (`AssetEntry`, `TextureInfo`, `AtlasReport` + `AtlasBuildPage`, `Finding` with `Short`/`IsTradeoff`/`ChangedSinceBuild`/`Group`, `FixOption.Resolves`), atlas-page parsing, `Run` orchestration, changed-since-build check, `Fmt`. |
| `TextureAudit.cs` | Decision tree + all texture fixes (add to atlas via `AtlasActions`, pad ×4, POT keeping slices, NPOT ToNearest, compress, crunch, max size, mipmaps, alpha). |
| `TextureReferenceIndex.cs` | YAML scan: Texture2D vs Sprite referencers per shipped texture + UI/Gameplay votes. |
| `ShipReasonIndex.cs` | "Why is this in the build?" chains. |
| `BuildSnapshot.cs` | Per-platform size snapshots for Δ. |
| `SpriteUsageContextScan.cs` | Runs the Sprite Optimizer usage scan and stores texture → UI/Gameplay/Both. |
| `AtlasAudit.cs` | Atlas reports (+ build pages) + hygiene findings; `AtlasActions` (add/remove/dedupe via `m_EditorData.packables`). |
| `AudioAudit.cs`, `DuplicateContentAudit.cs` | Unchanged audits (see git history of this doc for their rules). |
| `ImageResampler.cs` | Premultiplied-alpha resampler (also used by `ResizeTextureToPowerOfTwoContextMenu`). |
| `PagedListDrawer.cs` | Generic IMGUI pager. |
| `BuildSizeAnalyzerWindow.cs` | The window. Static cache of the last analysis (lost on domain reload → Analyze again). |

Shared code: `AddTextureToAtlasContextMenu` (atlas paths + add), `SpriteOptimizer` (`SpriteUsageScanner`,
`SpriteAtlasAnalysis`, `GuessFromFieldName`), `AudioImportOptimizer`, `LogHelper` (tag `BuildAnalyzer`).

## WebGL audio overrides (Unity 6.3 traps)

- `AudioImporter.SetOverrideSampleSettings("WebGL" | BuildTargetGroup.WebGL, …)` returns **false and stores nothing**
  for every settings combination, so the old "Apply optimizer tier" never stuck and the same rows came back every
  build. `AudioAudit.WriteSerializedOverride` writes the `m_PlatformSettingOverrides` entry the way the Inspector does
  when the API refuses, and the fix throws (row stays open) unless the stored override reads back as asked.
- WebGL pins the sample rate to **Override / 44100**; writing anything else to the sample-rate fields resets the whole
  override (quality back to 100 %, load type to DecompressOnLoad). WebGL targets keep 44100, and `AudioHighSampleRate`
  is not reported on WebGL (it isn't a lever there).
- WebGL only plays **AAC**: every tier targets AAC there (ShortSfx = DecompressOnLoad AAC), never ADPCM/Vorbis.

## Known simplifications

- Sizes are the serialized sizes from the report, not the compressed download (Brotli/LZ4). Rankings hold.
- Savings are estimates and can overlap (a texture can be both padded and crunched).
- The YAML reference index can't see binary-serialized assets or code loads (`Resources.Load`) — those show as
  "Use: Sprite? (code)" and their texture-shipping reason as "no YAML reference".
- Context votes are heuristics; **Scan sprite usage** gives the real answer (≈5 s, same scan as the Sprite Optimizer).
- Changed-since-build compares file times with the report file time; a reimport that only touches `Library/`
  isn't seen.
- Fixes update the cached analysis in place; sizes only change on the next build (⟳ / ✓ mark that).
- `ShipReasonIndex` follows direct asset dependencies; references created by code or Always Included Shaders show "no chain".

## Current status

Rewritten 2026-10-14 (size-focused Fix Plan). Compiles (checked offline against the Editor assembly); the
decision tree was dry-run against the 2026-10-14 10:17 WebGL report. **Needs an in-Editor pass**: Analyze,
Scan sprite usage, one Pad ×4 on a sprite sheet (check slices in the Sprite Editor), one Make POT (keeps slices)
on a copy, one batch "→ atlas", rebuild and check the Δ column.
