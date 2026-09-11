# Build Size Analyzer

Editor window that explains what made the last player build big and which import/atlas mistakes are
inflating it. **Tools ▸ RiftRaiders ▸ Build ▸ Analyze Last Build.**

## What it does

1. Loads `Library/LastBuild.buildreport` (`BuildReport.GetLatestReport()`, with a copy-into-Assets
   fallback), sums every object Unity wrote into the archives per source asset, and buckets assets into
   categories (Texture, SpriteAtlas, Audio, Mesh, Animation, Material, Shader, Font, Prefab,
   ScriptableObject, Scene, Script, Video, Text, BuiltIn, Other).
2. Scans every `SpriteAtlas` in the project and records which textures each packs.
3. Audits textures, atlases, audio and byte-identical duplicate files, producing **findings** with a
   severity, an estimated saving, and (where safe) one-click **fix** buttons.
4. Shows it all in six tabs — Overview, Assets, Textures, Atlases, Audio, Issues — every list
   paginated (25/50/100/250 rows) and every filtered list cached until a filter changes.

## File map — `Assets/_Project/Editor/BuildAnalyzer/`

| File | Role |
|---|---|
| `BuildReportLoader.cs` | Finds/loads the report; platform-name mapping for texture (`iPhone`, `Standalone`…) and audio (`NamedBuildTarget`) import settings. |
| `BuildAnalysis.cs` | Data model (`AssetEntry`, `TextureInfo`, `AudioInfo`, `AtlasReport`, `Finding`, `FixOption`), category classification, `BuildAnalysis.Run` orchestration, `Fmt` size formatting. |
| `TextureAudit.cs` | Per-texture checks + fixes (disable mipmaps, set Compressed, Alpha Source → None). `BytesPerPixelFor` format estimate. |
| `AtlasAudit.cs` | Atlas reports + findings; `AtlasActions` (add to atlas, remove from atlas, dedupe entries) — rewrites `m_EditorData.packables` through a `SerializedObject` so duplicated references are handled exactly. |
| `AudioAudit.cs` | Clip checks against `AudioImportOptimizer` tiers; fix = `AudioImportOptimizer.ApplyTo` on that clip. |
| `DuplicateContentAudit.cs` | MD5 over same-length Texture/Audio/Mesh/Video sources shipped in the build. |
| `PagedListDrawer.cs` | Generic IMGUI pager (draws only the current page; resets when the list instance changes). |
| `BuildSizeAnalyzerWindow.cs` | The window. Static cache of the last analysis (lost on domain reload → click Analyze again). |

Shared code it depends on: `AddTextureToAtlasContextMenu.AddTexturesToAtlas` (`Assets/_Project/Editor/`),
`AudioImportOptimizer.Tier/Classify/ApplyTo` (`Assets/_Project/Scripts/Audio/Editor/`), `LogHelper` (tag `BuildAnalyzer`).

## Findings and thresholds

**Textures** (every texture the build shipped, import settings read for the report's platform)
- `Uncompressed` — compression Uncompressed / explicit uncompressed format, or ≥3.5 B/px measured from the
  build. Fix: Set Compressed (only when the setting itself is Uncompressed).
- `Npot` — non-power-of-two and NPOT scale None. Warning if ≥2 B/px (compression fell back), else Info.
  Skipped for sprites that only ship through an atlas (the atlas page is POT). Fix **Make POT**
  (`TextureAudit.MakePowerOfTwo`, also a per-row button) **rewrites the PNG/JPG source file**:
  (1) resample each axis to its *closest* power of two (`ImageResampler`: premultiplied alpha, box
  filter down / linear up), (2) pad with transparent pixels to a square. Sprite sheets are anchored
  bottom-left and their rects/borders are scaled by the same factors through `ISpriteEditorDataProvider`;
  single sprites are centered in the square. Non-image sources (PSD/TGA) get importer
  `npotScale = ToNearest` instead when not sprites, and are refused when sprites. Scaling changes the
  rendered size of a sprite unless Pixels Per Unit is adjusted — the log line states the factors.
  Revert with git if unwanted.
- `MipmapsOnSprite` — mipmaps on Sprite/GUI textures (≈ +33%). Fix: Disable mipmaps.
- `ReadWrite` — Info, memory only, no fix.
- `NoAlphaButAlphaFormat` — source has no alpha but format is DXT5/BC7/ETC2_RGBA8/PVRTC_RGBA/RGBA32… Fix: Alpha Source → None.
- `LargeTexture` — largest side > 2048.
- `SourceShippedBesideAtlas` — texture is an atlas packable *and* its Texture2D object was written into
  the build (something references the texture directly).
- `NotInAtlas` — Sprite-type, ≤1024 px, under `Assets/_Project`, in no atlas. Suggests UI when the path
  contains `/UI/`, `/Icons/`, `UpgradeIcons`, `/GUI`, `/HUD/`; otherwise Gameplay. **Both** "→ UI Atlas"
  and "→ Game Atlas" buttons are offered here and on every non-atlased row of the Textures tab.

**Atlases** (all `t:SpriteAtlas`, not only the two known ones)
- `DuplicateAtlasEntries` — raw packable entries > distinct objects (the two project atlases had 540→50
  and 313→54). Fix: Remove duplicate entries.
- `InMultipleAtlases` — Error; savings ≈ w×h×bpp per extra atlas. Fix: Keep in `<atlas>` (removes from
  the others). The Atlases tab also has per-row "Keep here" / "Remove".
- `SuspiciousAtlasPackable` — packable from `Assets/0_Refs`, `Assets/_Delete`, `/Demo/`, `/Examples/`, `/Samples/`. Fix: Remove.
- `NonSpritePackable` — Texture Type isn't Sprite (atlas ignores it) or it isn't a Texture2D. Fix: Remove.
- `AtlasNotInBuild`, `AtlasUncompressed`, `AtlasPoorlyFilled` (<50% of packed page pixels are sprite pixels; needs the atlas packed in the Editor).

**Audio** (every clip the build shipped)
- `AudioPcm` (Error), `AudioQualityHigh` (Vorbis/AAC > 70%), `AudioWrongTier` (load type/format ≠ the
  optimizer tier for its length: ≥10s or `/Music/` → Streaming Vorbis; ≤2s → DecompressOnLoad ADPCM;
  else CompressedInMemory Vorbis), `AudioStereoSfx`, `AudioHighSampleRate` (>22050 Hz preserved, not music),
  `AudioOutsideProjectFolder` (not under `Assets/_Project/Audio` → not covered by the optimizer; use the
  existing "Move Used Clips Into Project Audio" tool). Fix for in-scope clips: Apply optimizer tier.
- `AudioStereoMusic` (Warning) — a `/Music/` clip shipping two channels; savings ≈ half its packed size
  (`AudioInfo.MonoSavings`, exact for PCM/ADPCM, approximate for lossy codecs). Fix: **Force To Mono**
  (only that flag; load type/codec/quality untouched). `AudioStereoSfx` offers the same fix. The Audio
  tab shows a "Mono saves" column, a per-row **Force mono** button, a "Force mono on all shown" batch
  button (confirm dialog, one `StartAssetEditing` batch) and "Stereo music" / "Music" filters.
- **Platform overrides.** Settings are read from the clip's override for the report's platform when one
  exists (`ContainsSampleSettingsOverride`/`GetOverrideSampleSettings`), else the defaults. The tier
  target is platform-aware (`AudioAudit.TargetFor`): **WebGL has no Streaming load type and no Vorbis**,
  so its music/medium target is CompressedInMemory + AAC; Vorbis/AAC/MP3 count as interchangeable lossy
  codecs. "Apply tier" writes the **platform override** (`SetOverrideSampleSettings`) when the clip has
  one or the platform is WebGL, and only *caps* quality (never raises a hand-tuned low value); otherwise
  it defers to `AudioImportOptimizer.ApplyTo` on the defaults. Rows with an override show `*` after the
  load type.

**Generic**
- `DuplicateContent` — byte-identical files under different paths, both shipped. Fix: Select all copies (deleting is a manual decision).
- `MultiArchive` — the same asset written into ≥2 archives (per-scene duplication), ≥64 KB.

"Fix all shown" on the Issues tab applies the first fix option to every unresolved finding of a single
kind (only for the safe kinds: atlas dedupe, mipmaps, compression, alpha source, the audio tiers) inside
one `StartAssetEditing` batch after a confirm dialog.

## Known simplifications

- Sizes are the **uncompressed serialized sizes** from the report's packed-asset list, not the on-disk
  size after LZ4/LZMA. Proportions and rankings hold; absolute numbers are an upper bound.
- Savings are estimates and overlap (e.g. a texture can be both uncompressed and mipmapped); the
  "recoverable" total is an upper bound.
- "Should be in atlas" is a path heuristic, not a usage scan (Sprite Atlas Scanner does the scene scan).
- Texture/audio settings are read for the platform in the report; if the same clip has overrides for
  another platform they are not shown.
- Atlas fill ratio needs the atlas packed in the Editor (Sprite Atlas inspector ▸ Pack Preview); it is
  unknown otherwise.
- Fixes update the cached analysis in place (rows/findings), but packed sizes only change on the next build.

## Current status

Code complete; compiles in the open Editor. Needs an in-Editor pass on a real report (see the
verification list in the plan: category totals vs `summary.totalSize`, the two-atlas overlap list,
pagination at 25 rows, one audio Fix, one atlas dedupe).
