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
- **Difficulty Tiers** — Easy/Medium/Hard/Nightmare N (`RuntimeConfig.Difficulty`/`NightmareLevel`) via `DifficultyConfig`, snapshotted into `Global.Difficulty`; multiplies enemy HP/damage, density, elite weight, windup/recovery, enemy projectile speed/lead, move speed on top of the balance stack. Nightmare = Hard × growth^N. → `docs/difficulty.md`
- **Worlds** — `WorldDefinition`s on `Resources/Worlds/WorldCatalog` (Theme + SurvivalConfig + optional `WorldBalanceData` multipliers over the global BalanceConfig); leader picks in the menu, synced via `RuntimeConfig.World`; `EnvironmentManager` loads the theme from it. → `docs/worlds.md`
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
- **Hero Mastery** — two ranked Ascension lines per hero (Weapon Family + Element), drafted through the existing Passive Upgrade pool; rank 3 unlocks a hero-specific special effect. → `docs/hero-mastery.md`
- **Talents (meta-progression) + Lobby Start** — permanent out-of-match unlocks on `RuntimePlayer.Talents`; `ChunkSpawnConfig` talent-gated spawns; run starts when a player leaves the LobbyStart chunk. → `docs/talents.md`

## Breathing-only POIs

- **Healing Shrine, Cursed Rift & Context Interaction** — two Breathing-only POIs on a shared availability/usage/Base-Skill-redirect layer; per-player input lock, `PoiUsagePolicy` incl. Cooldown. → `docs/breathing-poi.md`
- **Choice Window Refactor** — `UpgradeWindow`→`ChooseWindow` generalization reused by Cursed Rift/Store/Blacksmith (one shared instance); per-player Coin/Rift-Shard wallets. → `docs/choice-window-refactor.md`
- **Store & Blacksmith** — two Breathing-only Coin POIs reusing ChooseWindow + weapon/perk/currency systems; shared SurvivalTime weapon-offer curve; per-Break Blacksmith roll cache. → `docs/store-blacksmith.md`
- **Traversal Challenge** — timed co-op gap-crossing puzzle; global spawn/timer pause via a standalone counter (not GameState); permanent platforms; shared HUD banner. → `docs/traversal-challenge.md`
- **Optional Team Challenge** — voluntary whole-team Survival interruption (Kill Rush/Flawless Hunt/Cursed Survival) behind unanimous Ready-up; own `ActiveTeamChallengeCount` pause counter; reuses `BeginChestScreen` for the shared Rift Mutation reward claim. → `docs/optional-team-challenge.md`

## Combat & defense systems

- **Recoverable Accessory Guard** — per-hero durability accessory blocks a hit and pops off to be recovered; charge-only Shield covers what it can afford, Merchant repairs. → `docs/accessory-guard.md`
- **Hold-to-Revive (Alive → Downed → KO)** — player life-state machine + teammate-hold/self-revive/auto-revive-on-secure; KO is a dead end; enemies drop incapacitated targets. → `docs/revive.md`
- **Elemental Reactions** — Fire/Ice/Rock/Lightning baseline statuses (Burn/Chill/Intimidate/Shock); Burn+Chill/Burn+Shock/Chill+Shock fire Thermal Shock/Overload/Shatter, non-consuming, cooldown-gated. → `docs/elemental-reactions.md`

## Enemies

- **Enemy Burrow / Invulnerable Relocation** — reusable `BurrowDeliveryData` dives an enemy underground: invulnerable, untargetable, relocates near its target, resurfaces. → `docs/enemy-burrow.md`
- **Mortar Elite / Random-Scatter Barrage** — `MortarBarrageDeliveryData` lobs many arc shells (some aimed, some scattered) with a generic per-shell ground-warning telegraph. → `docs/mortar-elite.md`
- **Explode-On-Destroy / Mini Bomb** — generic `ExplodeOnDestroy` component detonates an entity on timed expiry or damage-death; also enables decoy traps. **Do not redirect Pixie's Cluster Charge onto Mini Bomb again without being asked.** → `docs/explode-on-destroy.md`

- **Enemy Passives** — reusable `EnemyPassiveData` assets on `EnemyDataAsset.Passives` (recorded on `EnemyPassives`, single entry `EnemyPassiveUtility.Apply`) — groundwork for random Elite passive packs; first one: `GroundTrail` (Fuel Runner oil trail) + shared `StickySlow` ground hazards. → `docs/enemy-passives.md`

- **World 2 Enemy Roster** — 12 W2 enemies (BlackMoles / DesertSecurity / Wildlife), each a guid-independent clone of a W1 base (`W2-*` assets, placeholder view variants); Dune Crusher cone slam, Scarab Nest spawner. → `docs/world2-enemies.md`

## Heroes — Ascensions

- **Hero Ascension Balance Pass (2026-08-20)** — all 6 heroes normalized to 9×3; shared generic primitives (WallSlam, aura-DR slot, AreaAllyBudget, DelayedBlast, etc.), deviations, skill-area audit. Read this first for cross-cutting hero architecture. → `docs/hero-ascension-balance-pass.md`
- **Pixie** — Cluster Bomb / Direct Hit / Birthday Cake / Pocket Bombs / Unstable Mixture / Unstable Targeting / Explosive Rounds / Backblast / Hot Fuse; `ForceMarkOnDetonate`, Chain Reaction base. → `docs/pixie-ascensions.md`
- **Brute** — Juggernaut/Protector/Dash lines; `CheckActions` bug fix, Groundbreaker, charge-only + temporary Shield, Bodyguard Free Hit Guard. → `docs/brute-ascensions.md`
- **Max** — Overdrive/Passive/Dash; Rage-as-boolean, Adrenaline deleted, `MaxOverdriveReactionSystem` ordered before `MaxVendettaSystem`. → `docs/max-ascensions.md`
- **Kai** — Vortex/Passive/Dash; `CheckActions` bug, `EnemyActionUtility.TryInterrupt`, `ApplyBound`, Vortex Skill Damage now dealt. → `docs/kai-ascensions.md`
- **Zara** — Resonance fully removed for Flow State passive; `OnHostileHitConnected`, Totem/Portable Speaker, `AlternatingArea.EffectivenessMultiplier`. → `docs/zara-ascensions.md`
- **Lux** — Engineer/Sentry/Scrap loop; lifetime-as-Health, Covering Fire Free Hit Guard, MK II weapon swap. → `docs/lux-ascensions.md`

## UI / View / presentation

- **Announcer** — shared `AnnouncerManager` slide-in title banner (fade+slide, hold, fade+slide out) for rare whole-team moments (Area Secured, Survival Started, Team Challenge Started/Complete/Failed), replacing three near-duplicate copies of the same animation. → `docs/announcer.md`
- **Hero Info Popup (Tab-hold)** — `HeroInfoPopupWidget` shows a full "what I'm running" readout by composing existing widgets. → `docs/hero-info-popup.md`
- **Minimap** — node-based minimap baked into one painted `Texture2D`: per-chunk fills, level outline, POI icons, player/enemy markers; one surface that expands (click/M/R2) from the panned corner view to a whole-level view. → `docs/minimap.md`
- **In-Match Settings Popup** — Escape-toggled pause-style popup: SFX/Music/Voice sliders (`AudioManager` category multipliers), Disconnect, offline-only Restart (`RestartOfflineMatch` shuts the runner down and restarts the offline session, reloading the scene). → `docs/in-match-settings.md`
- **Menu & in-game UI navigation** — controller/keyboard focus: cascading `FocusScopeWidget` levels (Cancel = back, L1/R1 = sub-tabs), popups that wait for their intro animation, modal menu popups, `CanvasInputGateWidget` (no input to the hidden menu during a match), and the reusable `FocusOutlineWidget` glow + the `Add Focus Glow To Open Scenes` tool. → `docs/menu-navigation.md`
- **Menu Tips** — `TipsData` asset + `TipsWidget` rotating tip bar on the Home tab (shuffle bag, slide/fade/typewriter transition, hover-pause, click-skip); reusable by the LoadingWindow. → `docs/menu-tips.md`
- **Settings screens + Quality** — shared look for the menu and in-match settings popups (`SettingsSliderRow` prefab, row-level focus glow) and the mobile-only Quality slider (`RenderScaleSetting` = URP render scale 100–60 %, persisted). → `docs/settings-ui.md`
- **Menu Settings Popup** — MenuScene TopBar Settings: same SFX/Music/Voice `AudioManager` category sliders as in-match, plus Photon region dropdown (`PhotonRegionSettings`): None = find best and save it as the selection until set back to None. → `docs/menu-settings.md`
- **Loading / Generating Level Screen** — menu-side `LoadingWindow` covers the whole match start (connect→generate→enter), then fades and hands off to `InMatchWindow`. Also the generic transition screen: self-instantiated persistent `LoadingScreen` prefab behind `SceneLoader.Load/Cover` (2s minimum; Game→Menu covers *before* teardown starts and holds until the gameplay scene has unloaded) for Intro→Menu and Game→Menu. → `docs/loading-screen.md`

- **Haptics** — Nice Vibrations behind one `Haptics.Play` entry point + Settings on/off toggle; triggered by `SoundData.haptic` (fires with the sound) or `HapticsDirector` (self-installed: any UI press + local-player Quantum events, tuned in `Resources/HapticsConfig`). Never call Nice Vibrations directly. → `docs/haptics.md`

- **Smart Ping** — one Ping button (Y / middle mouse), `PingSystem` resolves Elite/POI/Team Challenge/ground from context → `PingPlaced` event → HUD chat feed + minimap radar. → `docs/ping.md`

## Tooling & testing

- **Input (Input System + CF2)** — Active Input Handling stays *Both* (Control Freak 2 is legacy-only); gamepads + UI nav through `Quantum.GamepadControls`/`InputSystemUIInputModule`, keyboard/touch through `CF2Input`. → `docs/input.md`
- **Local-testing Bots (co-op autopilot)** — `RuntimePlayer.IsBot` or the Become Bot / Recover From Bot cheats → `BotBrain`, sim-synthesized input; goal-based brain (Revive/Interact/Retreat/Fight/Loot/Follow/Explore) tethered to the human leader, per-hero skill rules, safe Dash, Breathing POI use; bots consume no local slot (`GetLocalSlotIndex` = -1). → `docs/bots.md`
- **Build Size Analyzer** — Editor window over `Library/LastBuild.buildreport`: size by category/asset, texture/atlas/audio/duplicate-file audits with one-click fixes, paginated lists. → `docs/build-size-analyzer.md`
- **Tileset Platform Builder** — turns grid-aligned level cubes into autotiled cartoon platforms (dual-grid Center/Edge/Corner/InnerCorner + weighted variants, merged stretched Centers, Y scaled to cube height) with the shared `ToonTerrain` shader (world-space textures, texture-level outlines, one-directional outline→surface fade, multiply hatching); Blender generators in `Source~/`. Per-cube, collision-merged (across chunks) replacement for `CubeVisualBuilder`, used by all LevelChunk prefabs. → `docs/tileset-builder.md`
- **Sprite Atlas Optimizer** — Tools ▸ RiftRaiders ▸ Optimize (UI/Gameplay/Conflicts tabs): usage scan tags every sprite UI or Gameplay, flags missing/wrong/multi-atlas/shared/split/unused packables, per-context Sync, Duplicate & reassign for sprites used by both, play-mode recorder for code-assigned sprites. → `docs/sprite-atlas-optimizer.md`
- **Projectile View Pooling** — `ProjectileViewUpdater` (scene updater subclass) reuses projectile entity views instead of Instantiate/Destroy per shot; the detached visual returns home (`ParticleGracefulStop` return mode), `ProjectileViewSnapshot` restores the pristine hierarchy, views are held until all pieces are back. Kill switch on the updater. → `docs/projectile-view-pooling.md`
- **Mobile Performance** — on-device profiling workflow (USB/ADB, no Deep Profile, logs off on device, reading the Profiler via `unity cmd eval`), findings, and the prioritized improvement backlog with per-item plans. → `docs/performance.md`
- **Balance Simulator** — Editor window that predicts a run per minute/per hero (level, kills, weapon/skill DPS, weapon level, coins, enemy HP/TTK, Director pressure) from the real config assets, plus `BalanceRunRecorder` to diff against a played run. → `docs/balance-simulator.md`

## Reference docs

- **Game Design Document** — high-level design. → `docs/gdd.md`
- **Party / matchmaking report** — party/matchmaking flow analysis. → `docs/party-matchmaking-report.md`
- **BGM prompts** — background-music generation prompts. → `docs/bgm-prompts.md`
