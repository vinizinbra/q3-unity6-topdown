# Riff Riot (Neon boss)

Compact neon punk - magenta mohawk, cyan shades, purple vest, guitar-axe, speaker stack on his back, Rift
crystals on shoulders/gauntlets/gear. Built on the existing boss framework (`BossDataAsset`/`BossSystem`/
`BossRuntimeState`, `docs/boss-encounter.md`) - same HUD, intro card, stagger, defeat sequence, rewards and
XP/level-up lock as W1/W2. **No Neon `WorldDefinition`/`SurvivalConfig` exists yet**, so he isn't in any world's
timeline; he's fought through the debug paths below. W1/W2 bosses and their assets are untouched.

## Source of truth: the builder
`Tools ▸ RiftRaiders ▸ Boss ▸ Build Riff Riot (Neon)` (`Assets/_Project/Editor/Boss/RiffRiotBossBuilder.cs`)
slices the sprites, builds the view/entity/VFX prefabs and creates/re-tunes every Quantum asset. Idempotent:
re-running keeps every file (so every guid) and re-applies the numbers in the builder - **edit the builder, not
the generated assets** (hand edits are overwritten). Templates (W1 boss, W1 boss punch/leap telegraph, Kneel,
W1 Gunner projectile, W2 Scarab Nest) are only read (`Object.Instantiate` + reset `Identifier`), never modified.

## Files
| What | Where |
|---|---|
| Approved sheet (reference, untouched) | `Assets/0_Refs/NeonBoss/RiffRiotSpriteSheet-v2.png` |
| Cleaned sheet + 9 sprites | `_Project/Art/Sprites/Enemy/Neon/RiffRiot/RiffRiotSheet.png` (+ `RiffRiot_PinkCrystalGlow.mat`, own copy of the W1 boss glow material) |
| Boss + all actions/deliveries/telegraphs | `_QuantumUser/Resources/Enemy/Neon/Boss/RiffRiot/RiffRiot*.asset` |
| Boss entity prefab | `_QuantumUser/Entities/Enemies/Neon-RiffRiotPrefab.prefab` (copy of World1BossPrefab, W1 smoke removed) |
| Speaker view | `_Project/Prefabs/View/EnemyView/Neon/RiffRiotSpeaker.prefab` |
| Sound projectile (speakers) | `_QuantumUser/Entities/Projectile/RiffRiotSoundProjectile.prefab` (pink copy of the basic enemy bullet) |
| Sound-wave VFX | `_Project/Prefabs/Effects/RiffRiot/RiffRiot_SoundWaveCone/Ring.prefab` |
| Speaker spawn / broken FX | `_Project/Scripts/Boss/BossSpeakerStageFxDirector.cs` (self-installed) |
| Guitar SFX clips | `_Project/Audio/Library/RiffRiot/RiffRiot*.wav` (+ generator `Source~/riff_riot_guitar_sfx.py`) |
| Guitar SoundData + cue prefabs | `_Project/Audio/Enemies/RiffRiot*.asset`, `_Project/Prefabs/Effects/RiffRiot/RiffRiot_Sfx_*.prefab` |
| New sim code | `GapRingProjectileDeliveryData`, `SpeakerStageDeliveryData`, `HoldPoseDeliveryData`, `BossSpeakerStageSystem`, `QTN/Enemy/BossSpeakerStage.qtn` |

**Sprites.** The sheet is 3x3 with overlapping cell bounds, so each pose was isolated by connected component,
the low-alpha glow halo removed and the result repacked into `RiffRiotSheet.png` (nothing redrawn). Pivot =
boot baseline, X centred on the vest, so poses swap without the body jumping; the airborne pose pivots on its
tucked boots, so its height comes only from the simulated arc (step `AnimationType` None - no extra visual hop).
Idle / PowerChord / LeapPrep / LeapAir / LeapImpact / LeapRecover / Stagger / Roar / Speaker. The rig's Dying
sprite is the Stagger pose.

## Kit (SkillActions order)
0. **Power Chord** - directional **cone slam** (`GroundAreaDeliveryData`, cone 90 deg, range 6.5, 22 dmg + W1 leap
   damage/knockback effects, floor-height gated 1.2u). 0.85s windup on the fist-up (Roar) pose, aim locks at 60%,
   `ConeTelegraph90` decal = the exact hit area. On hit: strum pose held 1s (recovery) + **neon sound waves**
   (`RiffRiot_SoundWaveCone`): three `wave4` crescents, bulge leading, riding the cone and stopping on its edge -
   additive soft halo (`wave3`) + pink alpha body + near-white core, an additive `glow1` flash at the guitar and
   additive spark streaks. Tuned for Neon City's dark asphalt (the game has no Bloom, so the glow is additive
   layering - on bright grass it washes out). Cooldown 3.5s.
1. **Quebra-Palco** (leap) - `LeapDeliveryData` 0.9s air, 4.5 height, radius 3.2, 24 dmg. Crouch pose + circle mark
   from the start of the windup; the mark tracks then **locks at 55% of the windup** (~1.3s of fixed mark before
   impact; `UpdateTargetDirectionOnAnticipation`, never homes while airborne). `ResolveValidLanding` pulls the
   landing back onto real ground within 1.5u of his floor *before* the lock, so the mark always shows the real
   landing. Landing hit is floor-height gated (1.2u). Air pose -> impact pose -> pulling-the-guitar-out pose for
   the 1.3s recovery. Landing: W1 body slam + small Rift Pink blast + crystal chips + crack decal, shake.
2. **Som no Talo** - instantly drops **two Riot Amp speakers** (`SpeakerStageDeliveryData`) and goes straight back
   to choosing actions (the summon never holds the boss). See below. Cooldown 26s.
3. **Kneel** - forced only: stagger break (W1 thresholds) and the both-speakers-destroyed exposure (3s, x1.5 taken).
4. **Roar** - forced only: phase 2 entry (`HoldPoseDeliveryData` 1.4s, roar pose, pink shout ring + blast).

**Phases.** Same kit, same look. Phase 2 at 50% HP: `EntryAction` = Roar, then `RecoveryMultiplier` 1.4 (also
tightens the speaker cadence) and `MoveSpeedMultiplier` 1.15 - windups (= reaction time) untouched.

## Som no Talo (speakers)
- Spawned at mirrored spots across the boss->target axis (90/60/120/40/140 deg, 7 -> 4u): ground found, floor within
  1u of the boss's, clear straight line from the boss (no wall/cliff between), >= 7u apart, no hero within 2u.
  If no pair qualifies, `CanBegin` fails and he does something else.
- Real enemies (Specialist tier, x1.5 HP, no shield, stationary/anchored, generic Director prototype + own view),
  so all weapons/HUD/death pops work. Tagged `BossSummon` (also on boss-cast `SpawnPackDeliveryData` adds), so
  Yoru's Mirror Step doesn't reflect their rings (no execute / one-dash kill). Boss-summoned: no `EnemyLifecycle`, no XP (Boss state), normal coin/shard
  tier drops like other boss adds.
- `BossSpeakerStageSystem` forces one speaker's `SpeakerPulse` every 2.2s (/ phase RecoveryMultiplier),
  **alternating**; the speaker's own 0.8s windup (pulse + circle telegraph) is the warning. The pulse is
  `GapRingProjectileDeliveryData`: 18 sound projectiles over 260 deg, **100 deg opening** centred on the target
  direction +- a deterministic `f.RNG` jitter of 60 deg, so it moves every emission. Flat at body height, real
  projectiles (walls block them).
- Destroyed speaker = drops out of the rotation. **Both destroyed** -> sequence ends, `BossSpeakerStageBroken`
  (flash/blast/crystals) and the boss is forced into Kneel (deferred if airborne).
- **14s max**: remaining speakers pop (no exposure) and the leap's cooldown is cleared - it's the closer, with its
  normal windup.
- While up: the **leap is blocked** (`BlockedActionsWhileActive`), Power Chord still plays. One pair per boss
  (`BossSpeakerStage` presence gates `CanBegin`). Orphan sweep pops speakers whose boss died/vanished; the defeat
  cinematic's `ClearArena` also clears them.

## Guitar SFX
Distorted guitar, generated offline from two reference takes (a distorted F1 bass-string note = the tone, a dive-bomb
take whose ~700 Hz feedback squeal tops the dive). `Source~/riff_riot_guitar_sfx.py <src> <out>` re-pitches the bass
into power chords (root/fifth/octave, double-tracked), re-amps them (drive + cab curve) and adds pick/sub transients;
`src` needs the two takes as `bass.wav` / `dive.wav`. Rerun it and overwrite the wavs to change a sound.

| Moment | Clip | Step |
|---|---|---|
| Power Chord hit | `RiffRiotPowerChord` - F5, hard pick, ~1s ring | Power Chord `BeginStep` |
| Leap take-off | `RiffRiotDiveBomb` - flutter, then a ~3.5-octave accelerating fall over 1s (the 0.9s air time) | Leap `BeginStep` |
| Leap landing | `RiffRiotLandingChord` - lower D#5 + sub, boom, crash, 1.7s ring (stacks on the body-slam impact) | Leap `EndStep` |
| Speaker pulse | `RiffRiotAmpChug1/2/1/3` - palm-muted stabs, `Sequential` = an F-G#-F-A# riff across the alternating pulses | Speaker Pulse `BeginStep` |

Each SoundData rides a silent `RiffRiot_Sfx_*` cue effect (renderer off, one invisible particle so the pool keeps it
alive long enough for its `ParticleSound` to fire) in the step's `AdditionalParticles` - the builder makes both.
Enemies group, spatial. Verified in Play (all four fire; landing chord starts ~0.93s after the dive).

## Shared framework additions (opt-in, defaults = old behaviour)
- `BossPhaseData.EntryAction` - forced on entering a phase.
- `BossSystem.TryForceAction` + `BossRuntimeState.PendingForcedSlot` - force any SkillAction; waits while the boss
  is Active (airborne/burrowed/kneeling) instead of yanking it out mid-delivery.
- `BossDataAsset.DeferForcedActionsWhileActive` - routes the stagger break through the above (Riff Riot on;
  W1/W2 off = unchanged). `BossDataAsset.DebugPrototype` - the boss's own prototype for debug spawns.
- `LeapDeliveryData.MaxHeightDifference` / `ResolveValidLanding` / `MaxLandingHeightDifference` (0/false = old).
- `EnemyDecisionUtility.TrySelectAction` skips actions a running `BossSpeakerStage` blocks (bosses only).

## Testing
- In Play Mode, `RiffRiot.asset` ▸ **Jump To This Boss** - the real encounter (arena, intro card, HUD, XP lock,
  defeat) with Riff Riot in any world's Boss phase (`Global.DebugBossPrototypeOverride`).
- `RuntimeConfig.DebugBossPrototypeOverride` (match-start fallback) + CheatMenu ▸ Jump to Boss does the same.
- **Spawn Near Local Player** spawns his real prototype (outside the encounter - no arena/XP lock).

## Status
Verified in-Editor (GrasslandOutpost, solo, Lv24 via Jump to Boss): spawn/intro, Power Chord cone + waves, leap
mark/lock/landing/recovery, speaker spawn/alternation/gap ring/expiry/closer leap, speaker destruction, phase 2
roar, a forced Kneel, defeat + cleanup. Not isolated yet: the both-speakers-destroyed exposure on its own (seen
only in a damage-boosted run where a stagger Kneel landed at the same time). Not yet: 4-player feel, online, balance pass (HP/damage are first-pass),
Balance Simulator model for this boss, Catalog/Boss Window art (UiSprite = idle pose).
