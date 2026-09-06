# Project notes

Unity + Photon Quantum (deterministic ECS), 2D co-op top-down roguelite shooter.

**This file is a map, not a manual.** Every major system has its own design doc under `docs/`
holding the full design, file map, current status, and known simplifications. **Read the relevant
doc before touching that system** — the docs are the source of truth; this index only tells you
which one to open.

## Working rules

- **Quantum `.qtn` codegen gotcha.** Any time a `.qtn` file changes, Quantum's DSL codegen must run
  before C# referencing the new component/global fields will compile. The open Editor does this
  automatically. For headless/CI runs there's a chicken-and-egg trap (new C# + new `.qtn`-derived
  types in the same pass) and a real risk running a second headless Unity against a project with a
  live Editor open — check for `Temp/UnityLockfile` / a running `Unity` process first. Full writeup:
  the "Quantum codegen gotcha" section in `docs/survival-director.md`.
- Most features below compile only *after* codegen picks up their new/changed `.qtn` files, and many
  are code-complete but **not yet authored in the Editor or verified in-Editor** — each doc's own
  "Current status" / "Editor authoring needed" section is authoritative on what's still outstanding.

## Core loop & run flow

- **Survival Director** — continuous-spawn combat pacing (progression/director/spawner/lifecycle) deciding when, where, and what to spawn. → `docs/survival-director.md`
- **Run Curves & Co-op Scaling** — time-based difficulty curves + player-count scaling for enemy HP/damage, DirectorBudget, and XP requirement (`BalanceConfig`). → `docs/run-curves-coop-scaling.md`
- **Game State** — `Global.CurrentState` match-flow state machine (`GameState` enum) replacing ad hoc phase booleans; thin `SetState` + `GameStateChanged` event. → `docs/game-state.md`
- **Breathing Phase & Run-Phase State Machine** — repeating Breathing Breaks as `SurvivalConfig.Phases[]` entries; independent SurvivalTime/PhaseTimer clocks; skip-vote; encounter-clear hold. → `docs/run-phase.md`
- **Boss Encounter** — SurvivalConfig Boss phase teleports/seals the arena, spawns the boss, hard-pauses over a reveal card + camera cutaway; dedicated Boss HUD. → `docs/boss-encounter.md`

## Progression & economy

- **Experience Drops** — enemies drop ExpOrb pickups crediting one shared co-op run total/level via `ExperienceUtility`/`ExpOrbSystem`. → `docs/experience-drops.md`
- **Level-Up Upgrades** — level-up pauses sim, rolls 3 cards per player from 5 pools; category sequencing, Choose Weapon, Reroll, ranked Ascensions. → `docs/level-up-upgrades.md`
- **Chests** — chest entity reusing the level-up pipeline, forced to a fixed category set per instance. → `docs/chests.md`
- **Global Upgrades** — the 22-of-26 stacking hero-wide stat pool + the Coin currency economy (Coin/Rift Shard per-player wallets). → `docs/global-upgrades.md`
- **Rift Mutations** — rare non-stackable run-wide picks, rebuilt around the Accessory. → `docs/rift-mutations.md`
- **Weapon Perks** — ~35 roguelite weapon modifiers baked into `Weapon` at equip; ramp pool, on-kill/crit signals, post-impact procs. → `docs/weapon-perks.md`
- **Talents (meta-progression) + Lobby Start** — permanent out-of-match unlocks on `RuntimePlayer.Talents`; `ChunkSpawnConfig` talent-gated spawns; run starts when a player leaves the LobbyStart chunk. → `docs/talents.md`

## Breathing-only POIs

- **Healing Shrine, Cursed Rift & Context Interaction** — two Breathing-only POIs on a shared availability/usage/Base-Skill-redirect layer; per-player input lock, `PoiUsagePolicy` incl. Cooldown. → `docs/breathing-poi.md`
- **Choice Window Refactor** — `UpgradeWindow`→`ChooseWindow` generalization reused by Cursed Rift/Store/Blacksmith (one shared instance); per-player Coin/Rift-Shard wallets. → `docs/choice-window-refactor.md`
- **Store & Blacksmith** — two Breathing-only Coin POIs reusing ChooseWindow + weapon/perk/currency systems; shared SurvivalTime weapon-offer curve; per-Break Blacksmith roll cache. → `docs/store-blacksmith.md`
- **Traversal Challenge** — timed co-op gap-crossing puzzle; global spawn/timer pause via a standalone counter (not GameState); permanent platforms; shared HUD banner. → `docs/traversal-challenge.md`

## Combat & defense systems

- **Recoverable Accessory Guard** — per-hero durability accessory blocks a hit and pops off to be recovered; charge-only Shield covers what it can afford, Merchant repairs. → `docs/accessory-guard.md`
- **Hold-to-Revive (Alive → Downed → KO)** — player life-state machine + teammate-hold/self-revive/auto-revive-on-secure; KO is a dead end; enemies drop incapacitated targets. → `docs/revive.md`
- **Elemental Reactions** — Fire/Ice/Rock/Lightning baseline statuses (Burn/Chill/Intimidate/Shock); Burn+Chill/Burn+Shock/Chill+Shock fire Thermal Shock/Overload/Shatter, non-consuming, cooldown-gated. → `docs/elemental-reactions.md`

## Enemies

- **Enemy Burrow / Invulnerable Relocation** — reusable `BurrowDeliveryData` dives an enemy underground: invulnerable, untargetable, relocates near its target, resurfaces. → `docs/enemy-burrow.md`
- **Mortar Elite / Random-Scatter Barrage** — `MortarBarrageDeliveryData` lobs many arc shells (some aimed, some scattered) with a generic per-shell ground-warning telegraph. → `docs/mortar-elite.md`
- **Explode-On-Destroy / Mini Bomb** — generic `ExplodeOnDestroy` component detonates an entity on timed expiry or damage-death; also enables decoy traps. **Do not redirect Pixie's Cluster Charge onto Mini Bomb again without being asked.** → `docs/explode-on-destroy.md`

## Heroes — Ascensions

- **Hero Ascension Balance Pass (2026-08-20)** — all 6 heroes normalized to 9×3; shared generic primitives (WallSlam, aura-DR slot, AreaAllyBudget, DelayedBlast, etc.), deviations, skill-area audit. Read this first for cross-cutting hero architecture. → `docs/hero-ascension-balance-pass.md`
- **Pixie** — Cluster Bomb / Direct Hit / Birthday Cake / Pocket Bombs / Unstable Mixture / Unstable Targeting / Explosive Rounds / Backblast / Hot Fuse; `ForceMarkOnDetonate`, Chain Reaction base. → `docs/pixie-ascensions.md`
- **Brute** — Juggernaut/Protector/Dash lines; `CheckActions` bug fix, Groundbreaker, charge-only + temporary Shield, Bodyguard Free Hit Guard. → `docs/brute-ascensions.md`
- **Max** — Overdrive/Passive/Dash; Rage-as-boolean, Adrenaline deleted, `MaxOverdriveReactionSystem` ordered before `MaxVendettaSystem`. → `docs/max-ascensions.md`
- **Kai** — Vortex/Passive/Dash; `CheckActions` bug, `EnemyActionUtility.TryInterrupt`, `ApplyBound`, Vortex Skill Damage now dealt. → `docs/kai-ascensions.md`
- **Zara** — Resonance fully removed for Flow State passive; `OnHostileHitConnected`, Totem/Portable Speaker, `AlternatingArea.EffectivenessMultiplier`. → `docs/zara-ascensions.md`
- **Lux** — Engineer/Sentry/Scrap loop; lifetime-as-Health, Covering Fire Free Hit Guard, MK II weapon swap. → `docs/lux-ascensions.md`

## UI / View / presentation

- **Hero Info Popup (Tab-hold)** — `HeroInfoPopupWidget` shows a full "what I'm running" readout by composing existing widgets. → `docs/hero-info-popup.md`
- **Minimap** — node-based minimap baked into one painted `Texture2D`: per-chunk fills, level outline, POI icons, player/enemy markers. → `docs/minimap.md`
- **Environment Details** — View-only hand-placed ground/wall detail slots; runtime only picks whether/which themed sprite shows, deterministically seeded. → `docs/environment-details.md`
- **Loading / Generating Level Screen** — menu-side `LoadingWindow` covers the whole match start (connect→generate→enter), then fades and hands off to `InMatchWindow`. → `docs/loading-screen.md`

## Tooling & testing

- **Local-testing Bots** — `RuntimePlayer.IsBot`→`BotBrain`, sim-synthesized input; follow/void-avoidance/leash AI; bots consume no local slot (`GetLocalSlotIndex` = -1). → `docs/bots.md`

## Reference docs

- **Game Design Document** — high-level design. → `docs/gdd.md`
- **Party / matchmaking report** — party/matchmaking flow analysis. → `docs/party-matchmaking-report.md`
- **BGM prompts** — background-music generation prompts. → `docs/bgm-prompts.md`
