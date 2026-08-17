# Level-Up Upgrades

On a level-up (`Frame.Global.Level` increasing, see `docs/experience-drops.md`), the simulation
pauses and opens an upgrade-choice screen: every currently-connected player independently rolls 3
options and picks one, either by confirming or by the 30s decision timer running out (in which case
an unconfirmed player gets a random one of their own rolled options). Once everyone has resolved,
gameplay resumes. This is the "later piece of work" `docs/experience-drops.md` flagged - nothing
consumed a level-up before this.

Upgrades come from five pools (`LevelUpPoolKind`): **Weapon Perk** and **Global Upgrade** are pooled
globally (one shared config for every player); **Skill Upgrade** and **Passive Upgrade** are
per-hero instead - which skill/passive upgrades make sense depends on which hero is rolling, so they
live directly on that hero's own `CharacterData` asset, not a separate shared pool asset. **Choose
Weapon** is the newest - see "Category sequencing / Choose Weapon" below, it doesn't fit this
shared-`UpgradeData` shape at all. Global Upgrade and Passive Upgrade have no gameplay effect
designed yet - both are plumbing-only stubs (see "Current status" below).

By default every level-up still mixes all four `UpgradeData`-shaped pools together (Choose Weapon is
never part of this mix - see below). `LevelUpConfig.LevelSequence` can instead lock a given level to
exactly ONE of 5 player-facing categories (`LevelUpCategory` - HeroSkill/GlobalUpgrade/RiftMutation/
WeaponPerk/ChooseWeapon), so e.g. level 1/2/4 could roll Hero Skill only while level 3 rolls Global
Upgrade only. `LevelUpCategory.HeroSkill` merges `LevelUpPoolKind.SkillUpgrade` + `PassiveUpgrade` -
both are already collected together by `CollectPerHeroCandidates`, so this is purely a
category-selection grouping, not a new grant path. An empty `LevelSequence` (the default) reproduces
today's original mixed-all-pools roll exactly, so an unedited `LevelUpConfig.asset` needs zero
re-authoring to keep working. A `Chest` entity (see `docs/chests.md`) reuses this same category
concept, but locked once in the Editor per chest instead of per level.

All four kinds share one asset base, `UpgradeData` (`Icon`/`DisplayName`/abstract
`GetDescription()`) - `WeaponPerkData`, `SkillActionData`, `GlobalUpgradeData` and
`PassiveUpgradeData` all derive from it. This is what lets `LevelUpOption` carry a single
`AssetRef<UpgradeData>` instead of one field per kind, and lets the card-building code
(`GameplayUiController.BuildCardData`) resolve any option generically with no switch at all.
**As of 2026-08-14, `Rarity` is no longer part of the shared base** - only `WeaponPerkData` and
`RiftMutationData` still declare their own `Rarity` field and weight their level-up rolls by it
(`LevelUpConfig.GetWeight`); `SkillActionData`/`GlobalUpgradeData`/`PassiveUpgradeData` draw at a
flat `LevelUpConfig.CommonWeight` instead (`LevelUpUtility.ResolveWeight`), and their cards show no
rarity badge at all (`UpgradeCardWidget` hides it when `CardData.RarityIndex < 0`).

## Runtime flow

```
ExperienceUtility.Grant - right after its own while-loop finishes incrementing Level
  -> if Level increased at all this call (regardless of how many levels the loop covered):
       LevelUpUtility.BeginLevelUpScreen(f)

LevelUpUtility.BeginLevelUpScreen
  -> RuntimeConfig.LevelUpConfig not assigned -> skip (logged)
  -> for every entity with a PlayerLink: RollOptionsFor(f, entity, config)
       -> collect candidates from WeaponPerkPool.Perks (global - only the eligible-perks list is
          read, not WeaponPerkPoolData's own weight fields, see below) and GlobalUpgrades (global,
          ships empty)
       -> collect candidates from this entity's own CharacterData.DashSkillUpgrades and, for
          HeroSkill, straight from HeroSkill's own Actions (any entry with Activated == false -
          excluding any upgrade already granted on that slot - see "Why exclude already-granted
          skill upgrades" below) and CharacterData.PassiveUpgrades
       -> AddCandidate resolves the weight via ResolveWeight: WeaponPerk/RiftMutation candidates look
          up LevelUpConfig.GetWeight(data.Rarity) - independent of WeaponPerkPoolData's own
          (differently-tuned) Common/Rare/Epic/Legendary weights, which stay reserved for the
          original drop-roll mechanic (WeaponGenerator) - every other kind (SkillUpgrade/
          GlobalUpgrade/PassiveUpgrade) draws at a flat LevelUpConfig.CommonWeight instead, since
          those kinds have no Rarity of their own (see "Current status")
       -> weighted draw without replacement (same pattern as WeaponGenerator.DrawPerks: draw via
          f.RNG->Next(0, totalWeight), subtract the drawn candidate's weight, remove it, repeat) up
          to ChoiceCount times, stopping early if the combined pool runs dry
       -> 0 drawn -> no LevelUpChoice added for this entity; >0 -> f.AddOrGet<LevelUpChoice>,
          fill Options[0..drawn), OptionCount = drawn
  -> nobody got anything (every pool empty for every player) -> skip, no pause (logged)
  -> otherwise: Global.LevelUpScreenOpen = true, Global.LevelUpTimeRemaining =
     LevelUpConfig.DecisionTimeSeconds, f.SystemDisable<GameplaySystemGroup>()

LevelUpSystem.Update (always-on, every tick, outside GameplaySystemGroup)
  -> returns immediately while Global.LevelUpScreenOpen is false
  -> reads a SelectLevelUpUpgradeCommand for each PlayerLink entity, if present:
       LevelUpUtility.ConfirmSelection(f, entity, choice, command.OptionIndex)
  -> Global.LevelUpTimeRemaining -= f.DeltaTime
  -> once every entity holding a LevelUpChoice is Confirmed, OR time ran out:
       LevelUpUtility.Resolve(f)
  -> ISignalOnPlayerDisconnected: if the screen is open and that player still holds an unconfirmed
     LevelUpChoice, LevelUpUtility.AutoConfirm(f, entity) immediately - the rest of the group
     shouldn't wait out the full 30s for a pick that will never come

LevelUpUtility.Resolve
  -> for every PlayerLink entity with a LevelUpChoice: AutoConfirm if still unconfirmed (random pick
     among that entity's own OptionCount, never Options.Length - trailing slots past OptionCount are
     unrolled Kind.None), then grant Options[SelectedIndex]:
       WeaponPerk     -> WeaponSystem.AddPerk (same method GrantWeaponPerkCommand already calls)
       SkillUpgrade   -> SkillSystem.ResolveSlot + SkillSystem.AddUpgrade (same as
                         GrantSkillUpgradeCommand)
       GlobalUpgrade  -> GlobalUpgradeUtility.Grant (dispatches to the picked asset's own Apply -
                         same method a GrantGlobalUpgradeCommand debug grant now also calls)
       PassiveUpgrade -> PassiveUpgradeUtility.Grant (dispatches to the picked asset's own Apply -
                         same as GrantPassiveUpgradeCommand)
       ChooseWeapon   -> WeaponChoiceUtility.Grant (bakes option.RolledPerks into Weapon.Perks,
                         WeaponSystem.Equip(option.WeaponData), then CharacterStats.
                         WeaponTalentLevel++ - see "Category sequencing / Choose Weapon" below)
     then f.Remove<LevelUpChoice>(entity)
  -> Global.LevelUpScreenOpen = false, LevelUpTimeRemaining = 0, f.SystemEnable<GameplaySystemGroup>()
```

## Category sequencing / Choose Weapon

`LevelUpConfig.LevelSequence` (`List<LevelUpCategory>`) locks a given level to exactly one of 5
categories, indexed cyclically by `LevelUpUtility.GetCategoryForLevel` as
`sequence[(level - 1) % sequence.Count]` (`level` is 1-based - the level a player is currently
choosing an upgrade FOR). `RollOptionsFor` resolves this once per roll, then:

- **HeroSkill/GlobalUpgrade/RiftMutation/WeaponPerk** all still go through the same weighted-
  candidate-list draw as before (`CollectCandidatesForCategory` now dispatches to exactly one of
  `CollectPerHeroCandidates`/`CollectGlobalUpgradeCandidates`/`CollectRiftMutationCandidates`/
  `CollectWeaponPerkCandidates` instead of always calling all four) - `CollectGlobalCandidates`
  from the original implementation is gone, split into the last two of those.
- **If the configured category rolls dry** (e.g. Hero Skill exhausted for that hero), `RollOptionsFor`
  falls back to the original mixed-all-categories roll for that player only, rather than wasting
  their level-up on an empty screen.
- **ChooseWeapon bypasses the weighted-candidate machinery entirely** (`RollChooseWeaponOptionsFor`)
  - a rolled weapon+perks combo has no single `Rarity` to weight against an ordinary `UpgradeData`
    pick, so it's also never included in the mixed-all-categories roll above (an unedited
    `LevelUpConfig.asset`, empty `LevelSequence`, behaves exactly as it always has). It draws
    `min(ChoiceCount, WeaponChoicePool.Weapons.Count)` **distinct** weapons uniformly (no per-weapon
    weight - `WeaponDataAsset` carries no `Rarity`), then for each rolls a perk count via
    `RollWeaponOption`: slot `i` (0-based, up to `MaxRolledPerks`) independently succeeds with
    probability `clamp01((WeaponTalentLevel - i) * ChancePerLevelPerSlot)` (`DamageUtility.
    RollChance`), successes counted = that weapon's perk count, then
    `WeaponGenerator.DrawDistinctPerks` (shared with `WeaponGenerator.Roll`'s own equip-time draw)
    picks that many perks from `WeaponPerkPool`. At the shipped defaults
    (`ChancePerLevelPerSlot = 0.2`, `MaxRolledPerks = 3`): `WeaponTalentLevel` 1 -> 20% chance of a
    1st perk, 0% for a 2nd; level 2 -> 40%/20%; level 5 -> 100%/80%/60%. `WeaponTalentLevel` is a new
    persistent `Byte` on `CharacterStats`, incremented by `WeaponChoiceUtility.Grant` each time this
    entity actually picks a ChooseWeapon option - independent of overall character level.
- A `LevelUpOption` with `Kind == ChooseWeapon` carries `WeaponData`/`RolledPerks[5]`/
  `RolledPerkCount` instead of `Upgrade` - rendered by a dedicated `WeaponCardWidget`, never
  `UpgradeCardWidget` (see "View / presentation" below). `RecordHistory` excludes it from
  `UpgradeHistory`, same as `WeaponPerk` (a whole re-equipped weapon is even more visible on its own
  than a single perk).
- **"Keep Current" decline (2026-08-07) - a separate button, not a 4th/replacement card.**
  `RollChooseWeaponOptionsFor` always rolls the full `slots` (up to 3) distinct real weapons -
  identical to before this feature, nothing reserved. Declining is a dedicated
  `UpgradeWindow.keepCurrentButton`, shown only on a Choose-Weapon screen
  (`RefreshWeaponChoice`, hidden during `Refresh`), sending a new zero-payload
  `KeepCurrentWeaponCommand` (mirrors `RerollLevelUpOptionsCommand`'s shape). `LevelUpSystem.
  ProcessKeepCurrentCommands` -> `LevelUpUtility.ConfirmKeepCurrent` sets a new `LevelUpChoice.
  KeptCurrent` flag (not on `LevelUpOption` - it isn't tied to any of the 3 rolled slots) alongside
  `Confirmed`. `Resolve` checks `KeptCurrent` before calling `GrantOption` at all, so keeping the
  current weapon is a genuine no-op - nothing re-equipped, `WeaponTalentLevel` untouched. A 30s
  timeout/disconnect (`AutoConfirm`) never sets `KeptCurrent` - it only ever picks a random one of
  the 3 rolled options, same as any other category, so "keep current" is only ever reachable by an
  explicit button click, never a fallback.
- A `Chest` entity (see `docs/chests.md`) reuses this whole roll/grant pipeline via
  `LevelUpUtility.BeginChestScreen(f, player, forcedCategory)` - same `OpenUpgradeScreen` plumbing as
  a real level-up, just for one recipient and with the category forced rather than sequence-driven.

## Reroll (2026-08-07)

A player can redraw their own current `LevelUpChoice.Options` in place, spending one charge from a
new persistent per-character stat, `CharacterStats.RerollQuantity`. **Not a Global Upgrade** -
sourced the same way as `WeaponTalentLevel`/Choose-Weapon's own meta-progression seed: a pre-run
talent (`RuntimePlayer.Talents.RerollQuantity`, its own `PlayerPrefInt` in `MatchMakingConfig`, key
`"reroll_quantity"`) copied 1:1 into `CharacterStats.RerollQuantity` once at spawn
(`PlayerSpawnUtility.Spawn`). Starts at 0 for a fresh save, same as `WeaponLevel`. Deliberately kept
out of `TalentUtility.ApplyPerPlayerTalents`'s `Player*Level` block (`docs/talents.md`) - those are
all 0-5 percent-scaled multipliers, and a "percent bonus" of a flat charge count doesn't mean
anything the way it does for e.g. `MoveSpeedMultiplier`. See `docs/talents.md`'s own `RuntimePlayer`
fields section for the full meta-progression side; this section only covers the in-match spend.

Flow, mirroring `SelectLevelUpUpgradeCommand`'s shape end to end:

1. **UI**: a reroll button on `UpgradeWindow` (shared by both card families - `cards`/`weaponCards` -
   since a reroll redraws whichever is currently showing), showing the player's own live
   `RerollQuantity` and disabled at 0 charges or once `confirmedIndex` is set (`UpdateRerollButton`,
   called every tick from `GameplayUiController.UpdateUpgradeScreen` alongside `Refresh`/
   `RefreshWeaponChoice`). Click raises `onRerollClicked`, forwarded into a new zero-payload
   `RerollLevelUpOptionsCommand` (`GameplayUiController.OnRerollClicked`) - unlike a card click, this
   does NOT mark the local slot done, since the player still has to pick (or reroll again) afterward.
2. **Simulation**: `LevelUpSystem.ProcessRerollCommands` (mirrors `ProcessSelectCommands`'s own
   PlayerLink lookup - a client can only ever reroll its own `LevelUpChoice`) calls
   `LevelUpUtility.RerollOptionsFor`, which no-ops if already `Confirmed` or `RerollQuantity <= 0`,
   otherwise calls the existing private `RollOptionsFor` again with the exact same inputs the
   original roll used - `level` is recomputed fresh (`f.Global->Level + 1`, unchanged since the
   screen is still paused) and `forcedCategory` is `choice->Category` when `FromChest` (a Chest's
   category is genuinely forced and must be reused exactly) or `null` otherwise (a plain level-up
   re-derives the same category deterministically from `LevelSequence` given the same `level`, so
   nothing needs to be stored for that case). `RollOptionsFor` already resets
   `Confirmed`/`SelectedIndex` and overwrites `Options` in place via `f.AddOrGet` finding the
   existing component - a reroll needs no separate "clear" step, and dispatches to
   `RollChooseWeaponOptionsFor` internally exactly like the original roll did, so it works
   identically for a Choose-Weapon screen - all 3 slots redraw as real weapons. `keepCurrentButton`
   itself is untouched by a reroll (it isn't one of the rolled `Options`, see "Keep Current" above)
   and stays available throughout.
3. Only the charge itself gates frequency - no per-screen reroll limit beyond however many charges
   the player has banked, and no cooldown between rerolls within the same screen.

## Why pause via `GameplaySystemGroup`, not a hand-rolled flag

Quantum's `SystemBase`/`SystemGroup` already support enabling/disabling a whole subtree at runtime
(`Frame.SystemDisable<T>()`/`SystemEnable<T>()` - `OnSchedule` skips scheduling any disabled system's
entire hierarchy each tick). `SystemSetup.User.cs` wraps every per-tick gameplay system
(`CombatDirectorSystem` through `DestroyAfterTimeSystem`, same relative order as before, just nested
one level deeper) in a single `new GameplaySystemGroup(...)`, so `LevelUpUtility` only ever has to
toggle one thing rather than checking an `IsPaused` flag inside ~20 individual systems.

Three systems stay outside the group, always ticking regardless of pause state:
- **`LevelGenerationSystem`** - was already outside (world/match setup, not per-entity).
- **`PlayerInitSystem`** - a player joining mid-screen must still be able to spawn.
- **`CharacterSystem`** - moved outside specifically for this feature. Signal dispatch
  (`ISignalOnEntityPrototypeMaterialized`, which is what seeds a fresh character's
  `CharacterStats`/`Health`/`Shield`) is gated by system-enabled state exactly like `Update` is - if
  `CharacterSystem` had stayed inside the disabled group, a player spawning while a screen is open
  would never get seeded, permanently stuck at 0 max health for the rest of the run. `SkillSystem`
  has no equivalent one-shot risk (its lazy stack-init just runs a few seconds late once unpaused),
  so it's safe to stay inside.
- **`LevelUpSystem`** (new) - can't live inside the group it's the one disabling/enabling.

`ExpOrbSystem` deliberately stays *inside* the group: it's the only caller of
`ExperienceUtility.Grant`, and `Grant` is what triggers a new screen - keeping it paused for the
whole duration of an already-open screen is what makes re-entrant level-ups structurally impossible,
not just guarded against.

## Why per-player choice state lives on the character entity, not `Frame.Global`

`LevelUpChoice` is a component added to a player's own character entity (found via the existing
`PlayerLink` component), not a `Frame.Global` array indexed by `PlayerRef`. `SessionConfig.PlayerCount`
isn't configured yet (see `Assets/_QuantumUser/Resources/Quantum/SessionConfig.asset`), so there's no
real fixed ceiling to size a Global array against without guessing. Every other per-player runtime
value in this codebase (`CharacterSkills`, `Weapon`) already lives on the character entity the same
way - the component's mere presence on an entity IS "this player still has an unresolved pick",
removed the moment `Resolve` grants it.

## Why exclude already-granted skill upgrades from a re-roll

`SkillSystem.AddUpgrade` already rejects a duplicate grant of a non-ranked (`MaxRank == 1`) upgrade on
the same slot (logs an error, returns `false`) - that's existing behavior, not new; a ranked action
instead treats the re-grant as a valid rank-up (see "Ranked Ascensions" above). Without filtering at
roll time, a later level-up could re-offer a card that would silently fail to grant anything (or
over-rank past `MaxRank`) if picked. `CollectPerHeroCandidates` checks each `DashSkillUpgrades`
candidate, and `AddHeroSkillUpgradeCandidates` checks each `HeroSkill.Actions` candidate, via
`AlreadyGranted` - plain slot presence for a non-ranked action, `SkillUpgradeUtility.IsCappedOut` for
a ranked one.
`PassiveUpgrade` checks each `CharacterData.PassiveUpgrades` candidate against `UpgradeHistory`
(`PassiveUpgradeUtility.IsAlreadyPicked`) the same way - a rank comparison against `MaxRank` now,
still a pure boolean "already picked" for every non-ranked passive (`MaxRank == 1`, unchanged
behavior), unlike GlobalUpgrade's uncapped-by-default opt-in stacking. Reuses the same display
ledger both grant paths already populate rather than a dedicated Picks component (see
`UpgradeHistory`'s own comment in `LevelUp.qtn` for the shared-budget tradeoff that's judged
acceptable here, unlike Global Upgrade/Rift Mutation which each get their own). `WeaponPerk`
similarly excludes any perk already sitting in one of this entity's own `Weapon.Perks` slots
(`CollectWeaponPerkCandidates`/`AlreadyEquipped`).

## Ranked Ascensions (multi-rank Passive Upgrade / Skill Upgrade lines)

A "Hero Ascension" (the `LevelUpCategory.HeroSkill` merge of `PassiveUpgrade` + `SkillUpgrade`) can now
define more than one rank/level - added for Pixie's Ascension rework (`docs/pixie-ascensions.md`,
2026-08-09), but built generic from the start since every other hero's Ascension pool is expected to
go through the same treatment. Both pools already funneled pick bookkeeping through the same
component, `UpgradeHistory.Entries[].Count` (`LevelUp.qtn`), which `RecordHistory` above already
increments correctly on every repeat pick for any kind - it just wasn't read back as a count before,
only as boolean presence. That's the entire foundation; no new component was needed anywhere.

- **Shared primitives**: `UpgradeHistoryUtility.GetCount(f, entity, kind, upgrade)`
  (`Systems/Progression/`) is the one place that reads `UpgradeHistory` as a count. `IRankedUpgrade`
  (`Assets/_QuantumUser/Simulation/Assets/`) is a tiny marker interface (`byte MaxRank`,
  `string GetDescription(int rank)`) implemented by both `PassiveUpgradeData` and `SkillActionData`,
  so generic tooling (the card UI below) doesn't need to know which concrete kind it's looking at.
- **`PassiveUpgradeData`**: `MaxRank` (byte, default 1 = classic single-pick, zero change for every
  non-ranked passive across every hero). `Apply(Frame, EntityRef)` is now `virtual` (was `abstract`)
  with an empty body; ranked ascensions instead override `Apply(Frame, EntityRef, int rank)` (`rank`
  is 1-based, the rank being granted by THIS pick) - `PassiveUpgradeUtility.Grant`/`GetRank`/
  `IsAlreadyPicked` are thin wrappers over `UpgradeHistoryUtility.GetCount` now. Every override should
  **SET** its component's fields to that rank's total tuned values, not add to whatever the previous
  rank left - every rank's numbers are cumulative totals (e.g. "+60% damage" at rank 2 is the total,
  not +30% stacked on rank 1's own +30%).
- **`SkillActionData`**: same `MaxRank`/rank-aware overload pattern, but does NOT change
  `SkillSlot.Upgrades`' shape at all - a ranked action still occupies exactly one slot entry
  regardless of rank; `Execute` instead gained a `selfRef`-carrying overload
  (`Execute(..., AssetRef<SkillActionData> selfRef)`, defaulting to forwarding to the original
  parameterless-rank `Execute`) so an implementation can look up its own live rank via
  `SkillUpgradeUtility.GetRank(f, entity, selfRef)` and branch internally - "only available at rank
  3" is just an `if` inside `Execute`, re-evaluated fresh every activation. `SkillSystem.AddUpgrade`
  treats a re-grant of an already-present `MaxRank > 1` action as a valid rank-up (returns `true`,
  bumping `UpgradeHistory.Count` via the normal `GrantOption` → `RecordHistory` path) without
  inserting a second slot entry, which would double-`Execute` per phase per tick.
  `LevelUpUtility.AlreadyGranted` (used by both `AddSkillUpgradeCandidates`/
  `AddHeroSkillUpgradeCandidates`) checks `SkillUpgradeUtility.IsCappedOut` instead of plain slot
  presence once `MaxRank > 1`.
- **Never offering rank 2 before rank 1, or past `MaxRank`**: falls out for free from both pools
  reusing the exact same `IsAlreadyPicked`/`AlreadyGranted` exclusion check that already ran before
  ranking existed - rank only ever increments by 1 per grant, so there's no path to skip one.
- **Card UI**: `UpgradeCardWidget.CardData`'s existing `CurrentStacks`/`MaxStacks` readout (previously
  only populated for a capped `GlobalUpgradeData`) is now populated generically in
  `GameplayUiController.BuildCardData` for any `IRankedUpgrade` with `MaxRank > 1`, and the
  description shown is `ranked.GetDescription(currentRank + 1)` - the next rank's numbers, not the
  current ones. No new widget/prefab work.
- **`RankDescriptions` (data, not code)**: `GetDescription(int rank)`'s default implementation on both
  `PassiveUpgradeData`/`SkillActionData` reads a `string[] RankDescriptions` field (rank 1 = index 0,
  `[TextArea]`-authored, editable per-rank directly in the Inspector) instead of requiring every
  ranked class to hand-write its own override that string-interpolates the tuned numbers. Falls back
  to the plain `Description` field if empty/unauthored/out of range - a non-ranked upgrade
  (`MaxRank == 1`) is unaffected. A ranked class can still override `GetDescription(int rank)` directly
  if it genuinely needs live-computed text instead of authored data, but every current Pixie/Brute
  Ascension line just authors `RankDescriptions` in its own Editor generator now (see
  `PixieAscensionAssetGenerator.cs`/`BruteAscensionAssetGenerator.cs`) - no hand-written override
  remains anywhere in the roster. This was a deliberate tradeoff: authored text can't auto-stay in
  sync with a balance-pass number change the way a computed string could, but it's editable by a
  writer without touching C#, which is what the ranked pool's original text-generation shape didn't
  allow. Also fixed a real bug this surfaced: `UpgradePopupWidget.cs` (the Tab-hold ascension history
  popup) called the plain, rank-unaware `GetDescription()` with no `IRankedUpgrade` check, so every
  ranked `SkillActionData` Ascension (which never had `Description` authored, relying entirely on the
  rank override) showed up there with a blank description - fixed to resolve the same
  `ranked.GetDescription(count)` way `GameplayUiController`/the debug menu already did.
- **`IsEligible` (generic prerequisite gate, added for Max's Ascension refactor)**: both
  `PassiveUpgradeData`/`SkillActionData` gained `public virtual bool IsEligible(Frame f, EntityRef
  entity) => true;`, checked by `LevelUpUtility`'s candidate-collection methods (`CollectPerHeroCandidates`'s PassiveUpgrades loop, `AddSkillUpgradeCandidates`, `AddHeroSkillUpgradeCandidates`)
  alongside the existing rank/already-picked filters. Default `true` means every pre-existing upgrade
  across every hero is offered exactly as before - a concrete override lets an Ascension require some
  other upgrade/tag first without a hero-specific branch anywhere in `LevelUpUtility` itself. First
  consumer: Max's Flashpoint checks `f.Has<CanApplyBurn>(entity)` so it doesn't draft until a real
  Burn source (Ignition/Burning Vengeance/Vendetta Strike rank 1) has actually been picked - see
  `docs/max-ascensions.md`'s "Burn Ascension Eligibility" note. Reusable by any future hero's own
  prerequisite-gated pick the same way.
- **Debug grant paths** pick up ranking for free (same `Grant`/`AddUpgrade` calls) but bypass the cap
  check exactly like they already did for single-pick upgrades - pre-existing limitation, not new.
- **Why not a bigger unification**: `GlobalUpgradeData.MaxPicks`/`GlobalUpgradePicks` (indefinite
  generic-stat stacking) is untouched - a different, already-working pool this rework doesn't touch.
  `MaxRank` is intentionally duplicated as a small field + a couple of virtual methods on both
  `PassiveUpgradeData` and `SkillActionData` rather than hoisted onto the shared `UpgradeData` base,
  since `WeaponPerkData`/`GlobalUpgradeData` have no need for it.

**This is the pattern the next hero's Ascension rework should reuse, not rediscover** - see
`docs/pixie-ascensions.md`'s per-line breakdown for worked examples of both a ranked `PassiveUpgradeData`
(e.g. Unstable Mixture) and a ranked `SkillActionData` (e.g. Backblast/Hot Fuse).

## Files

**Simulation (`Assets/_QuantumUser/Simulation/QTN/`)**
- `LevelUp.qtn` - **new**: `enum LevelUpPoolKind` (`None`/`SkillUpgrade`/`WeaponPerk`/
  `GlobalUpgrade`/`PassiveUpgrade`/`RiftMutation`/`ChooseWeapon` - the grant-path axis), `enum
  LevelUpCategory` (`HeroSkill`/`GlobalUpgrade`/`RiftMutation`/`WeaponPerk`/`ChooseWeapon` - the
  distinct player-facing category axis used by `LevelUpConfig.LevelSequence`/`Chest.Kind`, see
  "Category sequencing / Choose Weapon" above), `struct LevelUpOption` (`Kind` + a single
  `AssetRef<UpgradeData> Upgrade` + `SkillSlotId SkillUpgradeSlot` - one shared field works for every
  `UpgradeData`-derived kind precisely because `UpgradeData` is a common base every concrete upgrade
  type derives from; `Kind` says which grant path to reinterpret `Upgrade`'s raw `Id` into, see
  `LevelUpUtility.GrantOption` - plus, valid only when `Kind == ChooseWeapon`:
  `AssetRef<WeaponDataAsset> WeaponData`, `array<AssetRef<WeaponPerkData>>[5] RolledPerks`,
  `Byte RolledPerkCount`), `component LevelUpChoice` (`array<LevelUpOption>[3] Options`,
  `Byte OptionCount`, `Boolean Confirmed`, `Byte SelectedIndex`, plus **new** `Boolean KeptCurrent`
  - see "Keep Current" above).
- `Experience.qtn` - **edited**, two new global fields: `Boolean LevelUpScreenOpen`,
  `FP LevelUpTimeRemaining`.
- `CharacterStats.qtn` - **edited**, one new field: `Byte WeaponTalentLevel` (see "Category
  sequencing / Choose Weapon" above).
- `Chest.qtn` / `Events.qtn`'s `ChestOpened` - see `docs/chests.md`.

**Data (`Assets/_QuantumUser/Simulation/Assets/`)**
- `UpgradeRarity.cs` - **new**: `enum UpgradeRarity : Byte` (`Common`/`Rare`/`Epic`/
  `Legendary`) - generalized from what used to be `WeaponPerkData`-only `WeaponPerkRarity`.
- `UpgradeData.cs` - **new**: abstract base (`AssetObject`) with `Sprite Icon`, `string DisplayName`,
  `UpgradeRarity Rarity`, and an abstract `string GetDescription()` (a method, not a field, since
  `SkillActionData`'s real description needs to be computed - see below - not just read off a plain
  string). `WeaponPerkData`, `SkillActionData`, `GlobalUpgradeData` and `PassiveUpgradeData` all
  derive from this now instead of `AssetObject` directly.
- `Weapon/Perks/WeaponPerkData.cs`/`.View.cs` - **edited**: derives `UpgradeData` instead of
  `AssetObject`; dropped its own `Rarity`/`Icon`/`DisplayName` (inherited now); `GetDescription()`
  just returns its existing `Description` field.
- `Weapon/Perks/WeaponPerkPoolData.cs` - **edited**: `GetWeight` retyped `WeaponPerkRarity` ->
  `UpgradeRarity` (same weights, same purpose - the *original* drop-roll mechanic, see
  `WeaponGenerator`; level-up rolling uses `LevelUpConfig`'s own separate weight table instead, see
  below).
- `Skills/SkillActionData.cs` - **edited**: derives `UpgradeData`; `GetDescription()` returns
  `GetFormattedDescription()` (its existing live-templated text), so a skill upgrade's card
  description stays in sync with its own tuned numbers rather than needing a second static field.
- `LevelUp/LevelUpConfig.cs` - **edited**: `FP DecisionTimeSeconds` (30), `int ChoiceCount` (3),
  `AssetRef<WeaponPerkPoolData> WeaponPerkPool` (reuses the existing type - only its `Perks` list is
  read for level-up purposes), `List<AssetRef<GlobalUpgradeData>> GlobalUpgrades` (ships empty), plus
  its own `CommonWeight`/`RareWeight`/`EpicWeight`/`LegendaryWeight` +
  `GetWeight(UpgradeRarity)` - the ONE weight table every level-up candidate uses regardless of kind
  (deliberately separate from `WeaponPerkPoolData`'s own weights - level-up pacing may want different
  tuning than raw drop rolls). **Further edited**: `List<LevelUpCategory> LevelSequence` (empty by
  default), `AssetRef<WeaponChoicePoolData> WeaponChoicePool`, `FP ChancePerLevelPerSlot` (0.2),
  `int MaxRolledPerks` (3) - see "Category sequencing / Choose Weapon" above.
- `Weapon/WeaponChoicePoolData.cs` - **new**: `List<AssetRef<WeaponDataAsset>> Weapons` - mirrors
  `WeaponPerkPoolData`'s shape, no weight table (a `WeaponDataAsset` has no `Rarity` of its own, so
  every listed weapon is equally likely to be drawn).
- `Weapon/WeaponDataAsset.cs` - **edited**: one new display-only field, `string DisplayName` - not
  made to derive `UpgradeData` (a rolled weapon has no single `Rarity`, that lives on each
  individually-rolled perk instead). Read by `GameplayUiController.BuildWeaponCardData` for a
  Choose-Weapon card's header. No separate `Icon` field - `WeaponDataAsset.View.cs`'s new
  `GetIcon()` reuses the sprite already authored on `ViewPrefab`'s own root `SpriteRenderer` (every
  weapon already has one for its in-world visual), so a Choose-Weapon card needs zero additional
  per-weapon icon authoring.
- `Systems/WeaponSystem.cs` - **edited**: `Equip` fires a new `Events.qtn` event, `WeaponEquipped
  (EntityRef Owner, AssetRef<WeaponDataAsset> WeaponData)`, unconditionally at the end of every
  call - a single generic View hook covering every equip path (initial spawn AND a later
  Choose-Weapon pick) rather than one bespoke call site per path.
- `View/Entities/Weapon/WeaponViewController.cs` - **edited**: subscribes to `WeaponEquipped`
  (filtered to its own `_entityRef`) and calls its own `SpawnWeaponView` again on a live re-equip -
  previously that method only ever ran once, at `Initialize`. Reconnect was already safe before
  this change and stays that way: `Initialize` does its own cold read of the CURRENT
  `Weapon.WeaponData` the moment the view is (re)instantiated (see `CustomQuantumEntityViewComponent`'s
  `OnEntityInstantiated` -> `Initialize` chain), independent of how many times `Equip` fired in the
  past - the event subscription only covers an ALREADY-connected client watching a later pick happen
  live.
- `Systems/WeaponGenerator.cs` - **edited**: its private weighted-draw-without-replacement `DrawPerks`
  body is now the public `DrawDistinctPerks(Frame, AssetRef<WeaponPerkPoolData>, int count,
  FixedArray<AssetRef<WeaponPerkData>> perks)`, shared by `WeaponGenerator.Roll` (an equipped
  weapon's own perk roster) and `LevelUpUtility.RollWeaponOption` (a not-yet-equipped Choose-Weapon
  candidate's rolled perks) - same shape, different destination buffer.
- `Systems/WeaponChoiceUtility.cs` - **new**: `Grant(Frame, EntityRef, LevelUpOption)` - the
  `ChooseWeapon` grant path (see `LevelUpUtility.GrantOption`), following the same one-method-
  dispatcher convention as `GlobalUpgradeUtility`/`RiftMutationUtility`. Bakes `option.RolledPerks`
  into `Weapon.Perks` BEFORE calling `WeaponSystem.Equip(option.WeaponData)` (`Equip`'s own
  `ApplyPerks` reads `Weapon.Perks`, same ordering `WeaponGenerator.Roll` already relies on), then
  increments `CharacterStats.WeaponTalentLevel`.
- `LevelUp/GlobalUpgradeData.cs` - **new**: minimal stub asset (`Description` field, no `Apply()`) -
  `Icon`/`DisplayName`/`Rarity` come from `UpgradeData`. No separate pool wrapper type - see
  `LevelUpConfig.GlobalUpgrades` above.
- `LevelUp/PassiveUpgradeData.cs` - **new**: same minimal stub shape as `GlobalUpgradeData` -
  referenced directly from `CharacterData.PassiveUpgrades` (per-hero, see below).
- `LevelUp.qtn` - **edited**, new `component UpgradeHistory` (`array<UpgradeHistoryEntry>[32]
  Entries`) + `struct UpgradeHistoryEntry` (`Kind`, `AssetRef<UpgradeData> Upgrade`, `Byte Count`) -
  a flat "everything this entity has ever picked" ledger covering Skill Upgrade/Global Upgrade/
  Passive Upgrade/Rift Mutation, purely for display (see "Upgrade history / party HUD icons"
  below). Independent of each covered kind's own gameplay-facing tracking (`SkillSlot.Upgrades`,
  `GlobalUpgradePicks`, `RiftMutationPicks`) for Skill/Global/Rift - **Passive Upgrade is the one
  exception**: it has no dedicated Picks component of its own and reads this same display ledger
  back for its "already granted" gameplay check (`PassiveUpgradeUtility.IsAlreadyPicked`, filtered
  to `Kind == PassiveUpgrade`) instead, on the judgment call that Passive Upgrade's small per-hero
  catalog won't realistically exhaust the shared 32-slot budget across one run - see this
  component's own comment in `LevelUp.qtn`.
- `LevelUpUtility.cs` - **edited**: `GrantOption` now calls a new `public static RecordHistory` at the
  top (before the per-kind switch), find-or-add-slot into `UpgradeHistory` keyed by `AssetRef<UpgradeData>`
  equality alone (a repeat pick of the same asset bumps `Count` rather than adding a duplicate
  entry) - same idiom as `GlobalUpgradeUtility.RecordPick`. `RecordHistory` early-returns on
  `Kind == WeaponPerk`/`ChooseWeapon` - deliberately excluded, since a weapon's own perks/identity are
  already visible on the weapon itself and roll too often to be worth a HUD icon. `RecordHistory` is
  `public` (not `private`) specifically so the four debug-grant systems below can call it too - see
  the "Debug tooling" section for why that matters.
- `Character/CharacterData.cs` - **edited**, one new field: `List<AssetRef<PassiveUpgradeData>>
  PassiveUpgrades`, alongside the existing `DashSkillUpgrades` (same per-hero list shape). Later
  removed the equivalent `HeroSkillUpgrades` list entirely - `AddHeroSkillUpgradeCandidates` now
  pulls that pool straight from `HeroSkill`'s own `Actions` instead (see "Runtime flow" above).

**Systems (`Assets/_QuantumUser/Simulation/Systems/`)**
- `LevelUpUtility.cs` - **new**: `BeginLevelUpScreen`, `RollOptionsFor`/`CollectGlobalCandidates`/
  `CollectPerHeroCandidates` (the weighted-draw-without-replacement rolling logic),
  `ConfirmSelection`, `AutoConfirm`, `Resolve`, `GrantOption`. Static utility, mirrors
  `ExperienceUtility`'s shape.
- `LevelUpSystem.cs` - **new**: always-on driver described above.
- `GameplaySystemGroup.cs` - **new**: trivial named `SystemGroup` subclass, purely so
  `SystemDisable<T>()`/`SystemEnable<T>()` have an unambiguous type to key off.
- `GlobalUpgradeUtility.cs` / `PassiveUpgradeUtility.cs` - one-method dispatchers: resolve the picked
  asset and call its own `Apply(Frame, EntityRef)` - real grant paths, not stubs (this doc used to
  describe them as logging-only placeholders; that's stale).
- `GlobalUpgradeSystem.cs` - **new**: per-tick processor for `GrantGlobalUpgradeCommand`, mirrors
  `PassiveUpgradeSystem` - debug-only today (see "Debug tooling" below), the real level-up path grants
  through `LevelUpUtility.GrantOption` directly. Also calls `LevelUpUtility.RecordHistory` itself right
  after the grant, since it bypasses `GrantOption` entirely and would otherwise never touch
  `UpgradeHistory` - same pattern in `PassiveUpgradeSystem`/`RiftMutationSystem`/
  `SkillSystem.ProcessGrantUpgradeCommand`.
- `SkillSystem.cs` - **edited**: `ResolveSlot` gained a `public static` overload taking
  `CharacterSkills*` directly (the existing one took `ref Filter`) - gives `LevelUpUtility.GrantOption`
  a supported entry point without touching `SkillSystem`'s own filtered `Update`. Also gained
  `RemoveUpgrade`/`ClearUpgrades` (debug-only, see below) alongside the original `AddUpgrade`.
- `ExperienceUtility.cs` - **edited**: `Grant` captures `Level` before its while-loop, calls
  `LevelUpUtility.BeginLevelUpScreen(f)` once afterward if it increased.

**Commands (`Assets/_QuantumUser/Simulation/Commands/`)**
- `SelectLevelUpUpgradeCommand.cs` - **new**: `Byte OptionIndex` only. The actual `Kind`/asset/slot is
  read back off the sender's own already-rolled `LevelUpChoice` (found via `PlayerLink`), so a client
  structurally cannot request an option it was never offered or touch another player's pick.
- `GrantGlobalUpgradeCommand.cs` - **new**: `AssetRef<GlobalUpgradeData> Upgrade`, debug-only (see
  below) - mirrors `GrantPassiveUpgradeCommand`.
- `RemoveSkillUpgradeCommand.cs` / `ClearSkillUpgradesCommand.cs` - **new**: debug-only counterparts
  to `GrantSkillUpgradeCommand` - remove one upgrade / clear an entire slot via
  `SkillSystem.RemoveUpgrade`/`ClearUpgrades`. The only kind where this actually reverts the effect -
  see the "no revert path" bullet below.

**Debug tooling (`Assets/_QuantumUser/Simulation/Assets/**/*Debug.cs` + `View/Managers/*DebugTrigger.cs`)**

Each of the four upgrade kinds gets a "Grant To Local Player" button directly on its own asset's
Inspector, via Quantum's `[EditorButton(..., EditorButtonVisibility.PlayMode)]` (not NaughtyAttributes'
`[Button]` - Simulation code can't see that asmdef, and it'd collide with Quantum's own `Button` input
struct anyway). Since Simulation can't call `QuantumRunner`/`SendCommand` directly, the button just
raises a static `Action` on a `<Kind>DataDebug` class; a matching `<Kind>DebugTrigger`
`QuantumGlobalMonoBehaviour` (`View/Managers/`) subscribes and sends the real command for the local
player. `SkillActionData` additionally gets "Remove From Local Player"/"Clear All From Local Player"
buttons per slot (Dash/Hero) that actually work (see the no-revert-path bullet below); the other three
kinds get the same two buttons for interface consistency, but they only log a warning instead of
reverting anything.

| Kind | Debug event class | Trigger (View/Managers/) | Command(s) |
|---|---|---|---|
| `WeaponPerkData` | `WeaponPerkDataDebug` | `WeaponPerkDebugTrigger` | `GrantWeaponPerkCommand` |
| `SkillActionData` | `SkillActionDataDebug` | `SkillUpgradeDebugTrigger` | `GrantSkillUpgradeCommand`, `RemoveSkillUpgradeCommand`, `ClearSkillUpgradesCommand` |
| `PassiveUpgradeData` | `PassiveUpgradeDataDebug` | `PassiveUpgradeDebugTrigger` | `GrantPassiveUpgradeCommand` |
| `GlobalUpgradeData` | `GlobalUpgradeDataDebug` | `GlobalUpgradeDebugTrigger` | `GrantGlobalUpgradeCommand` |

Additionally, `Assets/_Project/Scripts/UI/Window/DebugUpgradeMenuWindow.cs` +
`DebugUpgradeButtonWidget.cs`/`DebugUpgradeSectionLabelWidget.cs`/`DebugUpgradeCategoryPanelWidget.cs`
and `Assets/_QuantumUser/View/Managers/DebugUpgradeMenuTrigger.cs` give an in-game (not Editor-only)
alternative that lists every upgrade currently reachable by the local player's own hero, opened/closed
by `toggleButton` (starts closed - `panelRoot`, everything except `toggleButton` itself, is hidden at
`Awake`; content still builds normally while closed, since `DebugUpgradeMenuTrigger.Rebuild` has no
dependency on visibility, so it's already populated the first time the panel opens) and, once open,
across 3 scrollview tabs - Hero/Global/Weapon Perk, one visible at a time, switched by `heroTabButton`/
`globalTabButton`/`weaponPerkTabButton` on `DebugUpgradeMenuWindow` (Hero is the default open tab).
There's only one hand-authored scrollview prefab (`panelPrefab`, a `DebugUpgradeCategoryPanelWidget` -
owns its own `Content` transform the same way `DebugUpgradeButtonWidget` owns its row) -
`DebugUpgradeMenuWindow.Awake` instantiates it 3 times into `panelsParent` rather than needing 3
hand-duplicated scrollview hierarchies in the scene, exposing each instance's content via
`HeroContent`/`GlobalContent`/`WeaponPerkContent`. Built automatically as soon as the local player is
set up (`MyLocalPlayer.AddOnLocalPlayerSetup`, same idiom the couch-coop HUD widgets use).
`HeroContent` is shared by all 3 per-hero pools, each under its own section header (`AddLabel`, spawns
a `DebugUpgradeSectionLabelWidget` - "Dash"/"Hero Skill"/"Passive"); `GlobalContent`/`WeaponPerkContent`
are one category each, no label needed. Each row (`DebugUpgradeButtonWidget`) shows
name/category/icon/description (`UpgradeData.Icon`/`GetDescription()`, same fields `UpgradeCardWidget`
reads for the real level-up cards), a checkmark shown while already granted, an optional
`stackRoot`/`stackText` current/max readout (same "MaxStacks == 0 hides it" convention as
`UpgradeCardWidget.CardData` - see "View / presentation" above), and one state-driven action button,
not a separate Activate/Deactivate pair: green "Add" (sends the same command the table above lists)
when not yet granted, red "Remove" (`RemoveSkillUpgradeCommand`) when granted AND revertible - Skill
Upgrades (Dash/Hero) only. For the 3 kinds with no revert path (Weapon Perk/Passive/Global), once
granted there's nothing left the button can do, so it's hidden entirely (the checkmark becomes the
only "already added" signal) rather than shown disabled. Granted state is real for Skill Upgrades
(checked against that slot's own `CharacterSkills.DashSkill/HeroSkill.Upgrades`), Weapon Perk (checked
against `Weapon.Perks`), and a capped Global Upgrade (`MaxPicks > 0` - "granted" means fully maxed out,
via the same `GlobalUpgradeUtility.GetPickCount` the real level-up screen's card reads; a
partially-stacked one still shows its "Add" button, just with the current/max readout alongside it) -
Passive and an uncapped Global Upgrade have no granted-tracking at all, so those rows always start
ungranted (green "Add", no checkmark, no stack readout) at build time. On click, the row doesn't wait
on a round trip back through the sim to learn its own new state - it fires its command, deactivates
its own button, and flips its own checkmark locally (it lives on its own row's prefab instance, not
shared state), same one-shot shape the existing per-asset Inspector buttons already have. The stack
readout itself IS a round-trip value (read fresh from `GlobalUpgradeUtility.GetPickCount` each
`Rebuild`), so it only updates the next time the menu rebuilds, not instantly on click like the
checkmark does.

It enumerates `CharacterData.DashSkillUpgrades`/`PassiveUpgrades` plus (for Hero Skill) EVERY entry in
`HeroSkill`'s own `Actions` for whichever hero the local player's `CharacterStats.CharacterData`
resolves to - deliberately not filtered by `Activated` the way `LevelUpUtility.
AddHeroSkillUpgradeCandidates` filters the real level-up pool (an `Activated == true` entry is already
running for everyone, so a real screen has nothing left to offer there); this debug menu shows every
entry regardless, for full visibility/control while testing, even though granting an already-Activated
one has no visible effect. Plus
`LevelUpConfig.WeaponPerkPool.Perks`/`GlobalUpgrades` globally - the same five pools
`LevelUpUtility.RollOptionsFor` draws from. No new Commands or `.qtn` - reuses every grant/remove path
above directly rather than going through the `<Kind>DataDebug` static-event indirection (that
indirection exists only so a Simulation-side Inspector button can reach the View; this menu already
lives in the View layer). Like every other debug trigger, it targets local slot 0 only.

**Needs Editor authoring before it shows anything**, same as everything else in this doc: a
`DebugUpgradeButtonWidget` prefab (name/category/description text + icon Image + checkmark GameObject
+ one action button with its own background Image + label text + optional `stackRoot`/`stackText` for
the current/max readout, same optional-and-unwired status as `UpgradeCardWidget`'s own
`stackRoot`/`stackText` - see "Current status" below), a `DebugUpgradeSectionLabelWidget`
prefab for `AddLabel`, ONE `DebugUpgradeCategoryPanelWidget` scrollview prefab (its `content` field
pointing at its own ScrollRect content) wired to `DebugUpgradeMenuWindow`'s `panelPrefab` +
`panelsParent`, 3 tab `Button`s wired to `heroTabButton`/`globalTabButton`/`weaponPerkTabButton`, a
`panelRoot` GameObject wrapping everything above (toggled open/closed), a `toggleButton` placed
*outside* `panelRoot` so it stays clickable while the panel is closed, plus a `DebugUpgradeMenuTrigger`
in the scene (alongside the `DEBUGGER` GameObject the other 4 triggers live on) with its `menu` field
pointing at that window - none of this exists in `QuantumGameScene.unity` yet. All 3 prefab fields (`panelPrefab`/`buttonPrefab`/`labelPrefab`) are
expected to be live template objects placed directly in the scene rather than Project-window `.prefab`
assets - `DebugUpgradeMenuWindow.Awake` hides each template right after cloning from it (an active
template would otherwise render as one extra stray copy alongside its real clones), and every clone
(the 3 panels, plus every row/label `AddButton`/`AddLabel` spawns later) force-activates itself right
after spawning, since `Instantiate()` would otherwise copy the now-hidden template's inactive state
onto it.

**Edited existing files:**
- `Default/RuntimeConfig.User.cs` - `AssetRef<LevelUpConfig>`.
- `Default/SystemSetup.User.cs` - `CharacterSystem` moved out of the flat list into the always-on
  section (see "Why pause via GameplaySystemGroup" above); added `LevelUpSystem`; everything from
  `CombatDirectorSystem` to `DestroyAfterTimeSystem` now nested inside `new GameplaySystemGroup(...)`
  with every prior ordering comment preserved verbatim.

## View / presentation

- `Assets/_Project/Scripts/UI/Window/UpgradeCardWidget.cs` - **new**, one card = one
  `MonoBehaviour` (icon/rarity-frame/name/description/button), fully Quantum-agnostic - takes a
  plain `CardData` struct via `Setup()` and raises `onClicked`. Reusable/prefab-friendly on its own,
  independent of `UpgradeWindow`. **Edited**: `CardData` also carries `CurrentStacks`/`MaxStacks` -
  `MaxStacks` is 0 for every option except a capped `GlobalUpgradeData` (`MaxPicks > 0`, e.g. Dash
  Charge/Hero Skill Charge), which `Setup` reads as "hide the stack readout"; otherwise it shows
  `CurrentStacks/MaxStacks` (e.g. "2/3") via an optional `stackRoot`/`stackText`, so a player can see
  how close a capped pick is to maxing out before choosing it.
- `Assets/_Project/Scripts/UI/Window/WeaponCardWidget.cs` - **new**, same one-card-one-
  `MonoBehaviour`/Quantum-agnostic shape as `UpgradeCardWidget`, but for a `ChooseWeapon` option -
  `CardData` carries `WeaponIcon`/`WeaponName` plus a `PerkRowData[]` (length ==
  `RolledPerkCount`), each rendered by a small `WeaponCardPerkRowWidget` sub-row
  (`Assets/_Project/Scripts/UI/Window/WeaponCardPerkRowWidget.cs`, icon + name optionally tinted by
  rarity). A dedicated widget rather than a reinterpreted `UpgradeCardWidget.CardData` - confirmed
  with the user - since a rolled weapon has no single description/`Rarity`, it has a name/icon plus a
  variable-length list of individually-rarity'd rolled perks.
- `Assets/_Project/Scripts/UI/Window/UpgradeWindow.cs` - **new**, plain `UiWindow` (not a
  `QuantumGlobalMonoBehaviour` itself - matches `WaitingWindow`'s shape). Just orchestrates a fixed
  `UpgradeCardWidget[]` (one per `LevelUpChoice.Options` slot) plus a countdown readout;
  `onCardClicked` bubbles a card index up to whoever wired it. **Edited**: also owns a parallel
  `WeaponCardWidget[]` (same clone-from-template shape) and a second entry point,
  `RefreshWeaponChoice`, alongside the original `Refresh` - only one of the two card families is ever
  active at a time (`SetCardFamilyActive`), since a `LevelUpChoice` is always homogeneous (every
  rolled option is `ChooseWeapon`, or none are).
- `Assets/_Project/Scripts/UI/Window/GameplayUiController.cs` - **edited**: `QUpdate` now polls
  `Frame.Global.LevelUpScreenOpen` each frame (same polling idiom `ExpBarUiWidget` already uses -
  diffed against a cached previous value to detect the open/close edge, since the View is never
  rolled back) to show/hide `UpgradeWindow` via the existing `WindowManager`, reads the local
  player's own `LevelUpChoice` (`MyLocalPlayer.Instance.EntityRef`) and builds each card's
  `UpgradeCardWidget.CardData` with ONE generic path (`frame.FindAsset(option.Upgrade)` resolved as
  `UpgradeData` - no switch on `Kind` needed for display), mapping `Rarity` to a card color locally
  (`RarityColor`), and forwards card clicks as a `SelectLevelUpUpgradeCommand`. **Edited**:
  `BuildCardData` now also takes the rolling entity and, only for `Kind == GlobalUpgrade`, re-resolves
  `option.Upgrade` as `AssetRef<GlobalUpgradeData>` (same raw-Id reinterpret `LevelUpUtility.GrantOption`
  uses) to read `MaxPicks`/`GlobalUpgradeUtility.GetPickCount` for the card's stack readout - every
  other kind leaves `CardData.MaxStacks` at 0. **Further edited**: `UpdateUpgradeScreen` checks
  `choice->Options[0].Kind == ChooseWeapon` once per player slot (never mixed within one screen) and
  routes to a new `BuildWeaponCardData`/`upgradeWindows[i].RefreshWeaponChoice` path instead -
  `BuildCardData`/`KindText` are unchanged and never see a `ChooseWeapon` option.

## Upgrade history / party HUD icons

`PartyHudWidget` (see `docs/hud-couch-coop.md` if present) gains a fifth "hero resource gauge"
sibling alongside Adrenaline/Remix/Scrap/Juggernaut Stack Damage:

- `Assets/_Project/Scripts/UI/Hud/PartyHistoryUpgradeContainer.cs` - **new**, same shape as
  `ScrapUiWidget` (`QuantumGlobalMonoBehaviour`, `autoBindLocalPlayerOne`, `Initialize(EntityRef)`/
  `DisableAutoBind()` called externally by `PartyHudWidget`, self-hides its own `root` rather than
  assuming every entity has one) - no shared base class exists among these sibling widgets, so this
  one doesn't introduce one either. Shows Skill Upgrade/Global Upgrade/Passive Upgrade/Rift
  Mutation picks only - `UpgradeHistory` itself never contains a Weapon Perk entry (see
  `LevelUpUtility.RecordHistory` above), so there's no filtering to do here. Reads `UpgradeHistory`
  off the bound entity every `QUpdate`,
  but only rebuilds its `grid` (`Destroy` every child, `Instantiate` one `PartyHistoryUpgradeWidget`
  per valid `Entries` slot) when a cheap folded signature (`Upgrade.GetHashCode()`/`Count` per entry)
  changes - Destroy+Instantiate-per-icon every frame would be wasted work for state that only
  changes on a level-up. `iconPrefab` is a live template, NOT a child of `grid` (same "hidden at
  Start, cloned on rebuild" convention as `DebugUpgradeMenuWindow`'s row prefabs).
- `Assets/_Project/Scripts/UI/Hud/PartyHistoryUpgradeWidget.cs` - **new**, one icon = one
  `MonoBehaviour` (`Image` + optional `TMP_Text`), fully Quantum-agnostic - `Setup(Sprite, int)`
  only shows the count label once `count > 1`, so a single pick reads as a bare icon and a repeat
  pick (e.g. an uncapped Global Upgrade taken 3 times) reads as one icon with "3" on it rather than
  3 duplicate icons.
- `PartyHudWidget.cs` - **edited**: new `upgradeHistoryContainer` field, wired into
  `PopulateChildren`/`DisableChildAutoBind`/`Initialize` exactly like every other sibling gauge.

**Needs Editor authoring before it shows anything**, same as everything else in this doc: a
`PartyHistoryUpgradeWidget` prefab (icon `Image` + optional count `TMP_Text`) and a `grid` container
(typically a `GridLayoutGroup`) added under each `PartyHudWidget` instance in the scene, with
`iconPrefab`/`grid`/`root` wired in the Inspector - none of this exists in `QuantumGameScene.unity`
yet.

## Current status / known simplifications

**Update: all four points below are resolved.** `Assets/_QuantumUser/Resources/LevelUpConfig.asset`
exists and is assigned to `RuntimeConfig` in `QuantumGameScene.unity` (confirmed by GUID match). Its
`WeaponPerkPool` is wired to the populated `WeaponPerkPoolData` (see `docs/weapon-perks.md`), and its
`GlobalUpgrades` list carries 21 entries. Every hero's `CharacterData` has `DashSkillUpgrades` and
`PassiveUpgrades` populated. `GameplayUiController` now takes an `UpgradeWindow[] upgradeWindows`
(one per local player slot, for couch co-op - see `docs/hud-couch-coop.md` if present) instead of a
single window, and the scene has one wired (`upgradeWindows[0]`).

1. ~~`LevelUpConfig.asset`~~ - done.
2. ~~`GlobalUpgrades` empty~~ - done, 21 entries.
3. ~~`UpgradeWindow` scene/prefab wiring~~ - done; `GameplayUiController.upgradeWindows[0]` assigned
   in `QuantumGameScene`.
5. **Reroll (2026-08-07) - code compiles, not yet Editor-authored.** No asset authoring needed (it's
   a code-level `RuntimePlayer`/`CharacterStats` field, not a pickable asset) - but
   `UpgradeWindow`'s new `rerollButton`/`rerollChargesText` `SerializeField`s are unassigned on the
   scene prefab, so the button won't appear/do anything until a Button + TMP_Text are added under
   the prefab hierarchy and wired in the Inspector. Also, nothing in this codebase currently *writes*
   to the new `"reroll_quantity"` PlayerPref (same pre-existing gap `WeaponTalentLevelPref`/
   `TalentsPref` already have - see `docs/talents.md`'s own "Editor authoring needed" section), so
   every player starts every match with 0 reroll charges until some settings/meta-progression screen
   writes to it. See "Reroll" above.
6. **Keep Current (2026-08-07) - code compiles, not yet Editor-authored.** Same gap as `rerollButton`
   above - `UpgradeWindow.keepCurrentButton` is unassigned on the scene prefab, no Button exists
   under the hierarchy for it yet. Unlike Reroll, no PlayerPref/meta-progression gap here - Keep
   Current has no cost or charge, it's always available on a Choose-Weapon screen once the button
   itself is wired.
4. **Manual end-to-end test still not confirmed run** - the recipe below hasn't been verified
   in-Editor yet as far as this doc knows: force a level-up (temporarily shrink
   `ExperienceConfig.RequiredExperience`'s first keyframe, or grant a large `TotalExperience` via a
   debug hook) and confirm every client's `UpgradeWindow` opens together, gameplay visibly freezes, a
   card click locks only that client's own pick, the screen closes the instant everyone's confirmed
   (not waiting for the timer), an intentionally-unconfirmed client auto-picks at 0s, a mid-screen
   disconnect doesn't block the rest, and a player joining mid-screen spawns normally without a card.

**`Rarity` removed from Skill Upgrade / Global Upgrade / Passive Upgrade (2026-08-14)** - per explicit
user request, only `WeaponPerkData`/`RiftMutationData` still have a `Rarity` field now (their own,
not shared - it moved off `UpgradeData` onto each of those two classes directly).
`SkillActionData`/`GlobalUpgradeData`/`PassiveUpgradeData` no longer have any rarity concept at all:
`LevelUpUtility.ResolveWeight` draws them at a flat `LevelUpConfig.CommonWeight`, and their level-up
cards show no rarity frame/label (`UpgradeCardWidget.Setup` hides the badge entirely when
`CardData.RarityIndex < 0`, which `GameplayUiController.BuildCardData` now sends for any kind that
isn't `WeaponPerkData`/`RiftMutationData`). Every Global Upgrade `.asset` and every hero's ranked
Skill/Passive Ascension `.asset` still has a stale `Rarity:` line in its on-disk YAML from before this
change - harmless (Unity silently drops an unknown key on next save) but will linger until each asset
is re-saved, e.g. by re-running its generator (`Generate Global Upgrade Assets` /
`Brute|Kai|Max|Pixie|Zara Generate Ascension Assets` / `LuxScrapAssetGenerator`).

**`CharacterData.HeroSkillUpgrades` was removed** - the Hero Skill slice of the Skill Upgrade pool no
longer has its own authored list at all. `LevelUpUtility.AddHeroSkillUpgradeCandidates` now pulls it
straight from `HeroSkill`'s own `Actions` list instead: any `SkillActionData` authored there with
`Activated == false` is a candidate (see `SkillActionData.Activated` and `SkillSystem.InvokeActions`'
`isUpgrade` bypass - granting it via `AddUpgrade` ignores `Activated` and turns it on for just that
player, while it stays inert as a baseline action for everyone else). No more parallel list to keep
in sync with the skill asset it upgrades. Authoring status per hero not audited here - check each
`HeroSkill` asset's own `Actions` for `Activated == false` entries directly.

**Category sequencing / Choose Weapon (newest addition) - code compiles, mostly authored**:
- `LevelUpConfig.LevelSequence` ships empty (legacy mixed-all-categories behavior, unchanged) -
  needs actually populating to use per-level category locking at all.
- `LevelUpConfig.WeaponChoicePool` **is now assigned** to a real `WeaponChoicePoolData.asset` (8
  weapons listed) - `RollChooseWeaponOptionsFor` has something to draw from.
- Every existing `WeaponDataAsset`'s `DisplayName` is still unset - a Choose-Weapon card shows a
  blank name until authored per weapon. Its icon is no longer a separate authoring step - `GetIcon()`
  (`WeaponDataAsset.View.cs`) reuses the sprite already on `ViewPrefab`'s own `SpriteRenderer`, which
  every weapon asset already has.
- `ChancePerLevelPerSlot`/`MaxRolledPerks` ship at reasonable starting defaults (0.2, 3 - see
  "Category sequencing / Choose Weapon" above for the worked example), not a tuned/playtested value.
- `WeaponCardWidget`/`WeaponCardPerkRowWidget` prefabs and `UpgradeWindow`'s new `weaponCardPrefab`/
  `weaponCardCount` fields need Editor authoring/wiring on every `UpgradeWindow` instance in the
  scene, same "needs Editor authoring before it shows anything" gap as `UpgradeCardWidget` itself.
- `LevelUpCategory.ChooseWeapon` has no placed Chest instance yet either - see `docs/chests.md`'s own
  authoring checklist for the "Weapon Chest" gap specifically.
- **`WeaponTalentLevel` meta-progression is now split across two layers**: `RuntimePlayer.Talents.WeaponLevel`
  (`Assets/_QuantumUser/Simulation/Default/RuntimePlayer.User.cs`) is the durable, outside-this-match
  value - `MatchMakingConfig.StartRunner` reads it from a local `PlayerPrefInt("weapon_talent_level")`
  right before `AddPlayer`, and `PlayerSpawnUtility.Spawn` copies it onto the freshly-created
  entity's `CharacterStats.WeaponTalentLevel` once at spawn (after `CharacterSystem`'s own zero-seed,
  since `PlayerLink.Player` - and therefore which `RuntimePlayer` an entity even belongs to - isn't
  set until `Spawn` itself, too late for `CharacterSystem`'s materialization signal to resolve it
  directly). `CharacterStats.WeaponTalentLevel` then keeps incrementing live for the rest of that
  match exactly as before. **Nothing writes the final value back out to `PlayerPrefInt` when a match
  ends** - today a player's meta-progression never actually advances past whatever
  `"weapon_talent_level"` was already saved; wiring that save-back (and whatever UI/system actually
  raises this value between matches) is a separate follow-up, out of scope here.
- Manual end-to-end test not yet run: a `LevelSequence` with a `ChooseWeapon` slot should roll 3
  distinct weapons with sane perk counts across a few `WeaponTalentLevel`s, render via
  `WeaponCardWidget` (not `UpgradeCardWidget`), and picking one should re-equip the weapon with that
  many perks baked in and increment `CharacterStats.WeaponTalentLevel`; a category configured to one
  with zero eligible candidates for the test hero should fall back to a mixed roll instead of an
  empty screen. See also `docs/chests.md` for the Chest-side half of this test.

Beyond the missing assets/wiring:
- **Both Passive Upgrade and Global Upgrade grant a real `Apply(Frame, EntityRef)`** now (dispatched
  via `PassiveUpgradeUtility.Grant`/`GlobalUpgradeUtility.Grant` - see `docs/global-upgrades.md` for
  Global Upgrade's own roster). `UpgradeHistory` (see "Upgrade history / party HUD icons" above) also
  records every pick across all 5 kinds; `GlobalUpgrade` still reads its own dedicated
  `GlobalUpgradePicks` back for candidate filtering, but `PassiveUpgrade` now reads `UpgradeHistory`
  itself (see the dedup bullet below) rather than getting its own Picks component.
- **Multiple levels from one `Grant` call collapse into one screen** - if a single big exp grant
  crosses more than one level threshold in the same `while` loop, the player still only sees
  `ChoiceCount` (3) options total, not `3 × levelsGained`. Chosen deliberately over queuing multiple
  sequential screens, which would be a confusing wait for a co-op-wide pause.
- **`PassiveUpgrade`/`GlobalUpgrade`/`WeaponPerk` candidates are now deduplicated against past picks**,
  same as `SkillUpgrade`/`RiftMutation` always were - `PassiveUpgrade` via `UpgradeHistory`
  (single-pick, filtered to `Kind == PassiveUpgrade`; judged safe to share that ledger's 32-slot
  budget rather than get its own dedicated Picks component - see `UpgradeHistory`'s own comment in
  `LevelUp.qtn`), `GlobalUpgrade` via `GlobalUpgradePicks`/`MaxPicks` (opt-in stacking, most upgrades
  leave `MaxPicks` at 0 and stack indefinitely), `WeaponPerk` via a direct check against the entity's
  own live `Weapon.Perks` (no separate ledger needed - the weapon component already IS the "what
  does this entity currently have" source of truth).
- **No revert path for Weapon Perk / Passive Upgrade / Global Upgrade** - each bakes its effect
  directly into a live component field at grant time (`WeaponPerkData`/`PassiveUpgradeData`/
  `GlobalUpgradeData`'s own `Apply`), with no per-grant ledger to undo from and, in several cases, a
  lossy transform (multiply-then-clamp, or additive-then-partially-consumed) that isn't even
  mathematically invertible from current state alone. Their "Remove"/"Clear All" debug buttons (see
  below) only log this instead of pretending to revert - restart play mode to actually reset a
  player. `SkillActionData` upgrades are the one kind that's cheaply reversible (a slot re-reads
  `SkillSlot.Upgrades` fresh every activation instead of baking it), so those two buttons work for
  real - see `SkillSystem.RemoveUpgrade`/`ClearUpgrades`. Note this revert path does NOT touch
  `UpgradeHistory` - `RemoveSkillUpgradeCommand`/`ClearSkillUpgradesCommand` only ever call
  `SkillSystem.RemoveUpgrade`/`ClearUpgrades`, never `LevelUpUtility.RecordHistory` - so a
  debug-removed skill upgrade stays visible in the party HUD icon row until the entity is destroyed.
  Real (non-debug) play never removes an upgrade once granted, so this is debug-tooling-only. (The
  four debug *grant* commands - `GrantGlobalUpgradeCommand`/`GrantPassiveUpgradeCommand`/
  `GrantRiftMutationCommand`/`GrantSkillUpgradeCommand` - used to have the mirror-image gap on the add
  side, silently skipping `RecordHistory` since they bypass `LevelUpUtility.GrantOption` entirely; each
  now calls `RecordHistory` itself right after its own grant, so debug-granted upgrades do show up in
  the party HUD icon row.)
- **`UpgradeCardWidget`'s and `DebugUpgradeButtonWidget`'s new `stackRoot`/`stackText` fields are
  optional and unwired on their existing prefabs** - both are allowed to stay `null` (`Setup` no-ops
  on either) on each widget, so nothing breaks today, but no card or debug row actually shows a stack
  count until someone adds the readout (a small text + its container) to the respective prefab and
  assigns those two fields in the Inspector.
- **Debug perk/skill/passive/global triggers are inert while a screen is open** -
  `WeaponPerkDebugTrigger`/`SkillUpgradeDebugTrigger`/`PassiveUpgradeDebugTrigger`/
  `GlobalUpgradeDebugTrigger` still send their commands, but `WeaponSystem`/`SkillSystem`/
  `PassiveUpgradeSystem`/`GlobalUpgradeSystem` are paused (inside `GameplaySystemGroup`) so the
  commands are silently dropped that tick - resumes working the instant the screen closes.
