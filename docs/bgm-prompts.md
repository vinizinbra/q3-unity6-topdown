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

| State | `SoundData` asset | Clips folder | Wired via |
| --- | --- | --- | --- |
| Breathing | `Assets/_Project/Audio/Music/BGMBreath.asset` | `Assets/_Project/Audio/Music/Music/BreathMusic/` | `MusicDirector.breathingMusic` |
| Survival | `Assets/_Project/Audio/Music/BGMSurvival.asset` | `Assets/_Project/Audio/Music/Music/SurvivalMusic/` | `MusicDirector.survivalMusic` |
| Boss | `Assets/_Project/Audio/Music/BGMBoss.asset` | `Assets/_Project/Audio/Music/Music/BossMusic/` (create when the first clip lands) | `MusicDirector.bossMusic` |
| In-match Lobby (`GameState.Lobby`) | reuses `BGMBreath.asset` | - | `MusicDirector.lobbyMusic` |
| Main Menu | `Assets/_Project/Audio/Music/BGMMenu.asset` | `Assets/_Project/Audio/Music/Music/MenuMusic/` (create when the first clip lands) | `MenuMusicPlayer.music` (in `MenuScene.unity`) |

Every one of these is a `SoundData` asset with a `variants` list - drop a new clip in the folder and
add it to the matching asset's list to put it in rotation.

**Two different "lobby" tracks exist and they are not the same thing.** `MusicDirector.lobbyMusic`
is for `GameState.Lobby`, the pre-match hub *inside* the gameplay scene, before anyone has walked out
of the `LobbyStart` chunk - it currently reuses `BGMBreath.asset`. The **Main Menu** row above is the
actual title/matchmaking screen (`MenuScene.unity`), which loads *before* any Quantum session exists,
so it can't be driven by `MusicDirector` (that class reads `Global.CurrentState`, which only exists
once a match is running). It has its own trivial player instead: `MenuMusicPlayer`
(`Assets/_Project/Scripts/Audio/MenuMusicPlayer.cs`), a `MonoBehaviour` that just calls
`AudioManager.PlayMusic()` on `Start`. Because `AudioManager`'s singleton survives the scene load into
gameplay and `PlayMusic` always crossfades out whatever was playing, the handoff from menu music to
whatever `MusicDirector` picks first (`GameState.Lobby` → `lobbyMusic`) happens for free with no
extra code on either side.

`MenuMusicPlayer` is not placed in `MenuScene.unity` yet - add it to a root object there (e.g.
`GameManager`) and assign `BGMMenu.asset` to its `Music` field.

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

The boss encounter (`GameState.Boss` - see [boss-encounter.md](boss-encounter.md)). The arena is
sealed, there is no more kiting the fight elsewhere, and the pressure should read as sustained
danger rather than the rolling waves of Survival. `MusicDirector.bossMusic` falls back to
`survivalMusic` until a track is authored here, so a boss fight is never silent in the meantime.

Target: ~108-116 BPM, driving and tense, no climax-and-release - it holds until the fight ends.

### Prompt

> Instrumental **post-apocalyptic western showdown** at around **108–116 BPM**, tense, driving and dangerous.
>
> The music should feel like **the moment the fight can no longer be avoided** - the arena has sealed shut, something massive and scavenger-built is bearing down, and the only way out is through it.
>
> Led by the same **clean twangy electric guitar** as the rest of the score, but pushed harder: faster tremolo picking, urgent repeated phrases, sharp bends used as stabs rather than long expressive holds. Occasional light overdrive on accents only - never a wall of distortion.
>
> **Bass becomes present and driving** - a steady low pulse under the guitar, propulsive rather than heavy.
>
> Percussion is more active than Breathing or Survival but still handmade and metallic: driving boot-stomp kick, scrap-yard hits, mechanical clatter and clangs standing in for a real drum kit.
>
> Build and sustain tension through **repetition and tightening rhythm**, not by stacking on more instruments or a rising orchestral swell.
>
> The arrangement should hold its intensity for the whole track rather than building to one climax and releasing - this has to loop under a fight of unknown length without ever resolving.
>
> Raw, close, dry recording. Warm analog character, not polished or cinematic.
>
> The western identity should still come from **twang, bends, tremolo and desert-blues phrasing** - not from becoming a different genre under pressure.
>
> Imagine:
> the arena sealing shut behind you,
> dust kicked up by something enormous moving closer,
> no cover left to retreat to,
> the ranger's last stand.
>
> Mood: tense, dangerous, relentless, weathered, no false victory swell.
>
> **Western first. Danger second. No cinematic bombast.**
>
> No vocals.
> No metal.
> No orchestral bombast.
> No choir.
> No synths or EDM.
> No full rock band or power chords.
> No triumphant/heroic melody.
> No climax-and-release structure - it must hold, not resolve.
> No cheerful country or bluegrass.
> No repetitive short loop that gets tiring under a long fight.

### Current tracks

None yet - `Assets/_Project/Audio/Music/BGMBoss.asset` has an empty `variants` list. Generate from
the prompt above, drop the clips in a new `Assets/_Project/Audio/Music/Music/BossMusic/` folder, and
add them to that asset.

## Lobby / Menu

The main menu / matchmaking screen (`MenuScene.unity`), playing before any match exists - see "Where
the tracks live" above for why this is a separate mechanism (`MenuMusicPlayer`) from
`MusicDirector`. This is the player's first impression of the game's identity, so it should read as
an inviting establishing shot rather than the *resting-between-fights* mood of Breathing.

Target: ~70-78 BPM, warm and unhurried, welcoming rather than lonely.

### Prompt

> Instrumental **post-apocalyptic western title theme** at around **70–78 BPM**, warm, unhurried and inviting.
>
> The music should feel like **arriving at the edge of the wasteland for the first time** - a wide, sunlit ruin waiting to be explored, full of possibility rather than danger.
>
> Led by **clean twangy electric guitar** with slow expressive bends, tremolo and a simple, memorable, hummable motif - more melodically forward than Breathing's sparse phrasing, since this has to represent the whole game on a title screen.
>
> **Warm, present bass** carrying a gentle, steady pulse underneath - more supportive than Breathing's near-absent bass, without becoming a full band groove.
>
> Percussion stays light and handmade: soft kick, brushed or dry snare taps, hand percussion, boot stomps - present enough to give the theme a settled pulse, not enough to feel like combat is starting.
>
> Let the motif **repeat and develop gently** across the track rather than staying static - a title screen loop is heard many times, so small variation in phrasing and register keeps it from wearing thin.
>
> Raw, close, dry recording. Warm analog character. Handmade scavenger atmosphere, same instrumentation family as every other state.
>
> Imagine:
> sunrise over a rusted horizon,
> a road stretching out into the ruins,
> the ranger checking their gear before heading out,
> the game's world opening up.
>
> Mood: warm, inviting, settled, a little adventurous - the calm before a story starts, not the calm between fights.
>
> **Western first. Welcome second. Apocalypse as subtle background texture.**
>
> No vocals.
> No full rock band.
> No heavy drums.
> No distorted power chords.
> No big bass drops.
> No crescendo or dramatic climax.
> No orchestral western.
> No cheerful country or bluegrass.
> No EDM or synths.
> No identical short loop that gets grating after many menu visits.

### Current tracks

None yet - `Assets/_Project/Audio/Music/BGMMenu.asset` has an empty `variants` list. Generate from
the prompt above, drop the clips in a new `Assets/_Project/Audio/Music/Music/MenuMusic/` folder, add
them to that asset, then attach `MenuMusicPlayer` to a root object in `MenuScene.unity` and assign
the asset to its `Music` field (see "Where the tracks live" above).
