# Frost Patriarch (Patriarca da Geada) — Arctic boss

A Rift-corrupted polar bear: low, wide quadruped, cyan ice plates on the back, the anatomical right front leg
(viewer left) swollen with magenta Rift crystals. Rears up only for the Icebreaker slam. In-game name
**Patriarca da Geada**; every file uses the English id **FrostPatriarch**. Replaces the old skeleton "Rei da Geada"
concept (not used).

Everything is built by **Tools ▸ RiftRaiders ▸ Boss ▸ Build Frost Patriarch (Arctic)**
(`Assets/_Project/Editor/Boss/FrostPatriarchBossBuilder.cs`) — **edit the builder, not the generated assets**. It is
idempotent: re-running keeps every Unity guid and Quantum id (verified: 72 files identical across a rerun) and
re-applies all tuning. Templates are only read/copied (W1 boss prefab/asset, StickyPuddle hazard, standard telegraph
prefabs, shared GenericDamage/Knockback effects, Epic Toon FX effects).

## Files

| What | Path |
| --- | --- |
| Builder | `Assets/_Project/Editor/Boss/FrostPatriarchBossBuilder.cs` |
| Production atlases (18 poses) | `Assets/_Project/Art/Sprites/Enemy/Arctic/FrostPatriarch/FrostPatriarch_SheetA.png`, `_SheetB.png` |
| Atlas prep script + specs (not imported) | `.../FrostPatriarch/Source~/prep_frost_patriarch_sheets.py`, `specs.json` |
| Body material (copy of W1 pink-crystal glow) | `.../FrostPatriarch/FrostPatriarch_PinkCrystalGlow.mat` |
| Quantum assets | `Assets/_QuantumUser/Resources/Enemy/Arctic/Boss/FrostPatriarch/` |
| Boss prefab / prototype | `Assets/_QuantumUser/Entities/Enemies/Arctic-FrostPatriarchPrefab.prefab` |
| Ice-trail piece prefab / prototype | `Assets/_QuantumUser/Entities/Enemies/Arctic-FrostPatriarchIceTrail.prefab` |
| Owned effect variants | `Assets/_Project/Prefabs/Effects/FrostPatriarch/` |
| Source sheets (untouched) | `Assets/0_Refs/ArcticBoss/PatriarcaDaGeada/PatriarcaDaGeada-v1-SheetA.png` + `PatriarcaDaGeada-v2-SheetB-Jump.png` (v2: centre pose = the leap jump; v1 sheet B no longer used) |

Boss asset guid `061F8A80C1230124` (= `441223574106341668` for the JumpToBoss cheat).

## Sprites

The source sheets store a dark backdrop/glow as colour under **zero alpha** plus ~50k stray low-alpha specks between
cells, and several poses overlap their neighbours' cells. `prep_frost_patriarch_sheets.py` isolates each pose from its
connected components (≥128 alpha bodies + nearby fragments + a 2px antialias ring), drops everything else, and packs one
pose per **non-overlapping 640×440 cell** (3×3 per atlas) at the source's single scale. Nothing is redrawn or rescaled.

Pivot = the silhouette's mass-centre X on the feet line, so the torso stays put between poses (clean left/right flip)
and the reared Icebreaker windup simply stands taller. The view fits the **Idle** sprite to the collider once
(`EnemyView.ResolveFitScale`) and every pose keeps that scale — the extended paw/crystals never grow the collider
(`Stats.Radius` 1.6 = trunk). Art faces right (the rig mirrors for left, from `Aim.Angle`).

| Pose | Sprite | Used by |
| --- | --- | --- |
| A1 | `FrostPatriarch_Idle` | rest sprite |
| A2/A3 | `_WalkA` / `_WalkB` | `EnemyViewRig.walkSprites` (walk cycle while running) |
| A4/A5/A6 | `_SwipePrep` / `_SwipeRelease` / `_SwipeRecover` | Corrupted Swipe anticipation / begin / end |
| A7/A8/A9 | `_IcebreakerPrep` / `_IcebreakerImpact` / `_IcebreakerRecover` | Icebreaker anticipation / begin (held through the eruptions) / end |
| B1/B2/B3 | `_BreathPrep` / `_BreathActive` / `_BreathRecover` | Polar Breath anticipation / begin (whole channel) / end |
| B4/B5/B6 | `_LeapPrep` / `_LeapJump` / `_LeapLand` | Glacial Leap anticipation (coiled) / stage Air (the v2 jump pose) / stage Wave = touchdown (forepaws planted) |
| B7 | `_StaggerHit` | kneel stage Hit, charge stage WallStun |
| B8 | `_KneelHold` | kneel stage Kneel (exposure), defeat (`EnemyViewRig.dyingSprite`) |
| B9 | `_KneelRise` | kneel stage Rise |

Recovery poses are the actions' `EndStep` with `Duration` = `DownTime`, so they hold for the whole punish window
(a step sprite is only reverted by its own Duration, never by the phase change).

## Kit (all numbers in the builder)

Skill slots: 0 = BasicAction (none), **1 Swipe, 2 Icebreaker (line), 3 Breath, 4 Glacial Leap, 5 Kneel** (forced only),
**6 Icebreaker (fan)**, **7 Jump Slam**. `GlobalActionSlots {0,2,3,6}` (swipe/breath/leap/jump slam always); phase 1's pool = `{1}` (straight
line), phase 2's pool = `{5}` (three-line fan). Icebreaker/Breath/Leap share `SelectionWeight` 4 (rotate on cooldowns); the
jump slam (engage 5.5) has `MeleeWeight` 5, so whenever the target is in reach it wins. The swipe (8m cone, engage 8.5,
cooldown 5s) is back to weight 1 **for now** — a filler that rarely comes up (at weight 5 it chained after every leap). The
leap is a gap closer with almost no recovery (`WaveSpeed` 14 → the wave stage holds the bear ~0.5s, `DownTime` 0.2), so
a landing chains straight into Leap → Slam while the wave's Chill slows the target. `FallBackWhenTopActionBlocked = true`, `DeferForcedActionsWhileActive = true`.
HP: W1 boss baseline × `HealthMultiplier` 1.6 (party/difficulty scaling unchanged). MoveSpeed 5.6.

| Action | Delivery | Windup / aim lock | Damage | Recovery | Cooldown |
| --- | --- | --- | --- | --- | --- |
| Patada Corrompida (Corrupted Swipe) | `GroundAreaDeliveryData` 90° cone, r 6 (= the W1 boss punch reach), floor-gated | 0.75s, locks at 75% | 30 + small knockback, no cold | 0.8s | 2s |
| Quebra-Gelo (Icebreaker) | `EruptionLineDeliveryData` | 0.95s, **no windup telegraph** - the per-eruption warnings are the telegraph | slam 28 (r 2.2) + up to 12 × 24 (r 1.4, 2.7m apart from 3m, 0.6s then +0.12s each - a fast wave) - the line runs until a wall (the sealed arena edge, raised blocks), a gap or another floor | 1.2s | 6.5s |
| Sopro Polar (Polar Breath) | `BreathConeDeliveryData` 60° cone, **r 11, 1.5s** | 0.9s, locks at end; the cone shows only during the windup | 7 per 0.25s + `ChillEffectData` 1.0 buildup/tick | 1.0s | 8s |
| Quebra-Gelo — phase 2 | same delivery, `LineCount` 3, 30° apart, `RadiusGrowthPerStep` 0.05 | 0.95s, no windup telegraph | slam + 4 waves of 3 (×1 / ×1.15 / ×1.3 / ×1.45 radius) | 1.2s | 6.5s |
| Salto Glacial (Glacial Leap) | `LeapWaveDeliveryData` (leap + slowing wave), height 10 (as the W1 boss leap), 0.85s in the air | 0.65s, landing circle (r 4), locks at 90%, pulled back onto valid floor | landing 36 (r 4) + strong knockback, ground crack (W1 `GroundDecal`), then a frost wave to 7m: capped `ChillEffectData` (buildup 4/5 → ~32% slow with the usual Chill feedback, never Freezes by itself) | 0.9s | 5.5s |
| Pisão Glacial (Jump Slam) | `SequenceDeliveryData` (boss prefab has `EnemySequenceState`): step 1 `JumpSlamHit` — `GroundAreaDeliveryData` circle around the bear, r 5, floor-gated; step 2 `FrostRings` — `EruptionLineDeliveryData` with 12 lines 30° apart (= concentric rings), no paw slam | 0.8s, circle under the bear; the windup is a hop in place (`Jump` anim, B5 pose) landing on the hit | 28 + medium knockback, flat ice shockwave + crack; then 4 rings of the Icebreaker's ice pyramids marching out (6 / 8.5 / 11 / 13.5m, 18 each, radius ×1 → ×1.3, 0.6s then +0.3s each, **one ring telegraphed at a time**). Ring 1 is nearly closed, the outer ones open lanes: step IN towards the bear (the slam is over) or into a lane | 0.8s | 3.5s |
| Kneel (stagger break) | `AirborneKneelDeliveryData` | — | — | Hit 0.4 → Kneel 3.5s (Rupture ×1.5) → Rise 0.7 | — |

Phase 2 (≤ 50%): `RecoveryMultiplier` 1.4, `MoveSpeedMultiplier` 1.2, `AnticipationMultiplier` 1.1. Windups/warnings untouched, same kit, no summons.
Stagger thresholds: cloned from the W1 boss profile, `OnBreakForcedAction` = Kneel.

### Telegraphs (all the standard red)

- Swipe: `ConeTelegraph90`, real radius/angle.
- Icebreaker / fan / frost rings: no windup telegraph. Every eruption point gets its own owner-bound
  `ProjectileLandingWarning` (fades by itself if the boss is staggered — `GroundWarningTelegraphManager.CanOwnerStillDetonate`),
  **one wave at a time**: `EruptionLineDeliveryData.WarningLead` (rings = `Interval` 0.3s: one ring at a time; the line rolls out like a wave at 0.12s with a 0.36s lead: ~3 points warned ahead of the front) — the first wave is
  warned at the slam for the whole `FirstDelay`, each later wave appears as the previous one erupts (all at once read as
  a mess). `WarningLead` 0 keeps the old all-at-once behaviour. `EruptionRumbleFxDirector` also resolves the delivery
  through a running `SequenceDeliveryData` step (the frost rings).
- Breath: `ConeTelegraph60` during the windup, then `BreathConeFxDirector` keeps the same cone decal + the breath effect
  on the live `Aim.Angle` for the whole channel (the visual turns exactly as the hit area does).
- Leap: **no windup telegraph**. `LeapWaveDeliveryData.WarnLandingInAir`: at takeoff the locked, floor-validated landing
  spot gets an owner-bound `ProjectileLandingWarning` (the standard red circle, radius = the landing hit, for exactly
  `JumpDuration`) — the bear commits first, then the floor shows where it comes down.

## New code (generic, opt-in)

Simulation:
- `QTN/Enemy/BossFrost.qtn` — `IceChargeState` (per-pass hit list, travel, wall pause) and the marker
  `DissipateWithOwner`.
- `EruptionLineDeliveryData` — slam + committed eruption line(s): `LineCount` lines `LineSpreadDegrees` apart, each
  wave `RadiusGrowthPerStep` bigger (defaults = one straight line, no growth). Points are laid out along the direction locked at the
  end of the windup; the line stops at the first point behind a wall/ledge, over a void or on a floor more than
  `MaxHeightDifference` from the boss's own. Nothing follows the player (unlike `GroundBarrageDeliveryData`).
- `BreathConeDeliveryData` (+ `.View.cs`) — channelled cone; turns toward the target at ≤ `TurnRateDegrees` (no snap);
  floor-gated, line-of-sight (walls/ledges block); frozen players are skipped (`SkipFrozenTargets`). Damage element is
  Neutral — the cold comes only from the authored `ChillEffectData`.
- `IceChargeDeliveryData` (+ `.View.cs` stage steps) — straight committed run; one hit per player per pass
  (floor-gated); stops at ledges/voids/steps (> `MaxStepHeight`); a wall (arena gates included) = stopped for
  `WallPause` with Rupture (works on Boss tier, unlike the self-stun of `ChargeDeliveryData`).
- `ChillEffectData` — fixed Chill per application into the one existing Ice meter (`StatusEffectUtility.ApplyIce`).
  **Heroes had no anti-permafreeze**: `ApplyFreeze` only grants `FreezeRecoveryRemaining` to targets with an enemy tier
  resistance. When this effect freezes a hero it grants that window itself (`PlayerFreezeRecovery`, 3s) — the next
  Freeze from any source can't land until it ends. No shared config changed.
- `BossHazardUtility.CancelOwnedHazards` also removes the boss's `DissipateWithOwner` hazards (the ice trail);
  `BossDefeatUtility.TryBegin` now calls it too (death cleanup for every boss's owned hazards).
- All three new deliveries return `AllowsForcedPreemption = true`: a stagger break cancels breath / charge / eruptions
  at once (`EnemySystem.TryPreemptActive` → `OnInterrupted`), nothing fires on cancel.

**Glacial Charge → Glacial Leap (2026-10-29):** the charge (and its ice trail) was replaced by a leap onto the target
with a slowing landing wave — `LeapWaveDeliveryData` (extends `LeapDeliveryData`: same locked/validated landing and arc,
then stage Wave: a floor-gated ring expanding at `WaveSpeed` to `WaveRadius`, each player swept once, `WaveEffects`
applied; the ring outline uses `RingWaveVisualManager` via the `WaveVisual` RingSlam asset). The builder deletes the
retired charge/trail assets. `IceChargeDeliveryData`, `IceChargeState` and `DissipateWithOwner` stay as generic, unused
building blocks (the trail cleanup in `CancelOwnedHazards` is still wired).

View:
- `BreathConeFxDirector` (self-installed) — breath effect at `MouthOffset` + red cone decal while a breath is Active.
- `EnemyViewRig.walkSprites` / `walkFramesPerStride` + `EnemyBlobAnimationView.UpdateWalkFrame` — opt-in whole-sprite
  walk cycle paced by the run stride; never overrides an attack step's sprite. Empty on every other rig.

Effects (owned copies, URP-safe `Mobile/Particles` materials swapped in for the pack's `Particles/Standard Unlit`):
`FrostPatriarch_SwipeSlash` (SwordSlashThinRed → magenta), `_IceSlam` (ToonBodySlam → snow/cyan), `_IceEruption`
(MagicPillarBlastBlue → ice), `_Breath` (FlamethrowerToonyBlue → frost), `_IceSplash` / `_IceSparkle` (sticky-puddle
layers → frost). No Rift crystal sparks on any attack (removed: they read as noise). Take-off reuses the W1 jump puff.

## Feedback (juice) per attack

Everything is view-side and sits on top of the simulation's real events/phases; tuned for the **Arctic** floor (ice
effects are saturated cyan/blue and snow is blue-grey shaded — white-on-white vanishes on snow).

| Moment | Feedback |
| --- | --- |
| Swipe release | the W1 boss punch flash in ice blue (`_PunchFlash`, aligned to the aim) + magenta claw slash (`_SwipeSlash`), cold impact burst inside the cone (`_IceSlam`) — all three sized from their measured particle reach to span the 8m hit cone (≈0.2–8m), not past it, shake 1.4 |
| Icebreaker windup | reared pose + Inflate, frost mist drawn in around the paws (`_FrostInhale`), ice chips hopping at its feet (`_EruptionRumble`), light shake |
| Icebreaker slam | Slam anim, `_IceSlam` at the slam radius (dust translucent so it never whites out the boss), shake 2.5 |
| Eruption warnings | red circles + `_EruptionRumble` on every point for exactly its countdown (`EruptionRumbleFxDirector`, stops if the boss is staggered) |
| Eruption | **3D ice spike** (`IceEruptionSpikeView`): a crown of the boss arena wall's pyramid spikes (`PrototypePyramid.obj`) in ice — `FrostPatriarch_IceWall.mat`, an owned copy of `RiftWall.mat` (same rim shader) tinted cyan; two hand-laid cluster variants (`ClusterA/B` in the builder: tall centre spike, smaller ones leaning outward) — punches out of the floor with an overshoot inside a cold flash, snow burst, holds ~0.6s, shatters (shard burst from the Epic Toon ice shard sheet) and sinks; camera shake per eruption; lingering frost mist |
| Breath windup | frost mist pulled into the mouth (`_FrostInhale`), Inflate |
| Breath channel | starts at the drawn jaws (`MouthOnSprite` through the rendered rig - flip/tilt/fit scale included, not a 3D offset), saturated blue stream (`_Breath`, reshaped by `FitBreathToCone`: every layer's cone opened to the 60° half-angle and speed × lifetime set to the range minus the mouth offset, so the spray fills exactly the hit wedge) + the red cone decal, both on the live aim (`BreathConeFxDirector`), small shake; heroes show the existing Chill (blue tint, slow particles) and Freeze (frozen material, frost mist, shatter on thaw) feedback from `StatusEffectsManager`/`HitFeedback` |
| Leap windup | B4 coiled crouch, ice chips at the paws (`_RumbleOnce`), red landing circle on the target |
| Leap | take-off puff, B5 stretched through the air (stage Air) |
| Landing | stage Wave at touchdown: B6 forepaws planted (Slam anim), a flat ice shockwave scaled to the 7m slow wave (`_IceWave`: the ToonBodySlam family Brute's Groundbreaker landing uses, dust nearly off so it never hazes the arena) + `_IceSlam` at the landing hit's size, ground crack, shake 3 |
| Elite smoke | the W1 boss's pinkish-red `VFX3D_Smoke` on the rig (76% up the body, scaled to it) |
| Stagger / kneel | B7 shake + crystals, B8 drop with a cold impact and shake, B9 rise |

`IceEruptionSpikeView.PreviewAt(time)` renders any moment of the spike animation for look-dev (see the render under
`Recordings/FrostPatriarch/spike/`).

## Integration

- `BossDataAsset.DebugPrototype` = the boss prototype; the asset's **Spawn Near Local Player** / **Jump To This Boss**
  buttons work. The cheat `JumpToBoss` with AssetId `441223574106341668` jumps to it.
- **No Arctic world yet**: only `Assets/_Project/Data/WorldTheme/ArticFieldsTheme.asset` exists — no `WorldDefinition`
  or `SurvivalConfig` for World 3. The boss is reachable only through the debug route; hooking it into an Arctic
  SurvivalConfig's Boss phase is pending (nothing of W1/W2 was changed).

## Testing

Debug flow (any level scene with a boss arena, e.g. `GrasslandOutpostGameScene`): enter Play, cheat `JumpToBoss`
(AssetId above), `ToggleGodMode`, then **switch the biome to Arctic** — cheat menu ▸ World ▸ Biome ▸ `ArticFields`
(`EnvironmentManager.Instance.Load(ArticFieldsTheme)`, view-only: re-skins every chunk with the Arctic tileset/sky/water)
so it is judged on the floor it belongs to. `BecomeBot` lets the bot fight it; `ToggleDamageBoost` reaches the stagger
thresholds and phase 2 quickly.

Verified in Play (offline, one bot hero):
- Every action fires and rotates; poses/recoveries show as mapped (impact pose held through the eruptions after a fix).
- Facing follows the simulation aim on every sample (art faces right).
- Chill builds per breath tick (2 → 4), Freeze lands, the hero then gets `FreezeRecoveryRemaining` = freeze + 3s, and a
  later Freeze only lands after it expired — no chained freeze.
- (Charge, before it was replaced: one hit per pass, wall slam → stopped with Rupture, ice trail spawned/expired.)
- Leap: lands on the hero's spot, the wave expands to 7m, the hero gets the 2s slow; elite smoke on the rig.
- No effect left behind: live boss-effect instances return to 0 after each attack and after the boss dies.
- Stagger during the **charge** (state removed, trail 8 → 0 at once) and during the **breath** (forced break: channel
  stops, breath effect stops emitting) and during the Icebreaker windup → B7/B8 kneel with Rupture → B9 rise → normal.
- Phase 2 reached; boss death → defeat sequence.
- Builder rerun: 72 guids/ids identical.

Pending / not verified:
- A stagger landing **mid eruption line** (same preemption path as the breath; warnings are owner-bound) — not observed.
- Breath line of sight against real walls/ledges and the charge stopping at a ledge — code paths only, not staged.
- Co-op (multiple heroes: per-pass hit list holds 4, breath/eruptions are per player) and **online** — untested.
- Retry/teardown mid-fight beyond the normal defeat sequence.
- No regression play-test of Dono do Céu / Riff Riot after the shared changes (all opt-in or cleanup-only).

## Known limitations / placeholders

- **No audio** (no roar, slam, breath or charge sounds).
- Trail visuals are the sticky puddle's particle layers re-tinted (placeholder frost look).
- During the kneel the boss rests ~0.2m above its normal standing height (shared `AirborneKneelDeliveryData.ResolveLanding`
  re-snaps to ground + collider radius); gravity settles it after the rise.
- The breath can turn the sprite's facing mid-channel when its aim crosses straight up/down screen (facing follows aim).
- Mouth offset / breath effect scale are eyeballed (`BreathConeDeliveryData.MouthOffset` (0, −0.7, 1.9), scale 1).

**Orientation note:** the pack `ToonBodySlam` is authored standing up. `_IceSlam`/`_IceWave` have a 270° X root
rotation like Brute's `RR_ToonBodySlam` (used as-is by AreaHit blasts), and step particles pass the same rotation
(`Flat(...)` in the builder) because the step spawner ignores the prefab's rotation.
