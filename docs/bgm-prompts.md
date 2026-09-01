# BGM Prompts

Reference doc for the **generative-music prompts** used to produce this project's background
music. One section per run state, each holding the exact prompt text used, so a track can be
regenerated or extended later without re-deriving the style from scratch.

This is a **content/authoring doc, not a system doc** - nothing here is read by code. The playback
side is plain `SoundData` assets (see "Where the tracks live" below).

## How to use

- Keep each prompt **verbatim** as it was actually used. If a prompt is revised, replace it and note
  what changed and why - a half-remembered variant is worse than none.
- Generate several takes per prompt and keep the ones that fit; each state's `SoundData` holds a
  list of `variants` and picks between them, so a state wants a small pool of tracks, not one.
- Prompt text is written for a text-to-music model, so it is deliberately **explicit about what NOT
  to do**. The negative list is doing as much work as the positive description - a model left to its
  own devices reliably adds a drum kit, a crescendo and a heroic melody to anything western-flavoured.
- Name the resulting file descriptively (`Sunset Iron Trail.mp3`, `Rustline Ambush.mp3`) rather than
  by state + number - the variant pool is easier to curate when the names are distinguishable.

## Where the tracks live

| State | `SoundData` asset | Clips folder |
| --- | --- | --- |
| Breathing | `Assets/_Project/Audio/Music/Breath.asset` | `Assets/_Project/Audio/Music/Music/BreathMusic/` |
| Survival | `Assets/_Project/Audio/Music/SurvivalBGM.asset` | `Assets/_Project/Audio/Music/Music/SurvivalMusic/` |

Both are `SoundData` assets with a `variants` list - drop a new clip in the folder and add it to the
matching asset's list to put it in rotation.

## Shared direction

Every state shares one identity: **weathered post-apocalyptic western**. Dusty, handmade, analog,
scavenger-built. The western character comes from *twang, bends, tremolo, slide and desert-blues
phrasing* - never from stereotypical cowboy instrumentation (no harmonica-and-banjo shorthand, no
orchestral western, no cheerful country). The apocalypse is background texture - rust, distant
machinery, wind - not the subject. No vocals in any state.

What changes between states is **density and pressure**, not genre.

---

## Breathing

The between-assault break (`GameState.Breathing` - see [run-phase.md](run-phase.md)). The player is
resting, spending, deciding. Nothing is chasing them, but the next fight is coming.

Target: ~80-86 BPM, sparse, no climax.

### Prompt

> Instrumental **lonely post-apocalyptic western** at around **80–86 BPM**, sparse, dusty and intimate.
>
> The music should feel like **one old, weathered ranger sitting alone in a vast ruined desert**, resting between battles while distant machinery creaks in the wasteland.
>
> The track should be led almost entirely by **clean twangy electric guitar** with slow expressive bends, tremolo, sustained notes, dry acoustic plucks and occasional subtle slide guitar.
>
> Use **very sparse warm bass** only when needed.
>
> Percussion should be minimal. Mostly no full drum kit. Use occasional soft kick, brushed or dry snare taps, hand percussion, boot stomps, mechanical clicks or distant metallic sounds purely as texture.
>
> Leave **large amounts of empty space between notes**.
>
> The guitar should feel human, imperfect and restrained, using short lonely phrases and subtle blues-western call-and-response.
>
> Do not build toward a full-band climax.
>
> Do not gradually add more and more instruments.
>
> Keep the arrangement sparse from beginning to end, with only small changes in guitar phrasing, register, bass presence and environmental texture.
>
> Use one simple recognizable western motif, but reinterpret it gently rather than repeating the exact same riff.
>
> Avoid identical 4-bar or 8-bar loops. Let phrases breathe and develop naturally.
>
> Raw, close, dry recording. Warm analog character. Handmade scavenger atmosphere.
>
> The western identity should come from **twang, silence, tremolo, bends, slide guitar and desert-blues phrasing**, not stereotypical cowboy instrumentation.
>
> Imagine:
> an empty wasteland,
> late afternoon sun,
> rusted machinery,
> dust moving across the road,
> an old ranger sitting alone,
> his weapon beside him,
> knowing another fight is coming.
>
> Mood: lonely, dusty, weathered, calm, restrained, mysterious, slightly dangerous.
>
> **Western first. Loneliness second. Apocalypse as subtle background texture.**
>
> No vocals.
> No full rock band.
> No heavy drums.
> No distorted power chords.
> No big bass.
> No crescendo.
> No heroic melody.
> No orchestral western.
> No cheerful country.
> No bluegrass.
> No EDM.
> No dramatic climax.
> No repetitive short loop.

### Current tracks

`BreathMusic/`: Dusty Sun Engine, Dusty Water Run, Rust On The Range, Rust Yard Refuge,
Scrap Bar Standoff, Sunset Iron Trail, Sunset Scrap Range, Sunset Scrap Ridge, Tin Can Horizon.

---

## Survival

The continuous-combat phase. Tracks exist (`SurvivalMusic/`: Dust Devil Run, Dust Highway Rattle,
Dust Run Shootout, Rustline Ambush) but **the prompt used to generate them was not recorded** - paste
it here when it is next used, so the pool can be extended consistently.

## Boss

No dedicated boss BGM yet. See [game-state.md](game-state.md) and `CLAUDE.md`'s "Boss Phase Trigger"
section for where that state transition happens.

## Lobby / Menu

No dedicated prompt yet.
