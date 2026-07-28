# Level-Up Upgrades

On a level-up (`Frame.Global.Level` increasing, see `docs/experience-drops.md`), the simulation
pauses and opens an upgrade-choice screen: every currently-connected player independently rolls 3
options and picks one, either by confirming or by the 30s decision timer running out (in which case
an unconfirmed player gets a random one of their own rolled options). Once everyone has resolved,
gameplay resumes. This is the "later piece of work" `docs/experience-drops.md` flagged - nothing
consumed a level-up before this.

Upgrades come from four pools (`LevelUpPoolKind`): **Weapon Perk** and **Global Upgrade** are pooled
globally (one shared config for every player); **Skill Upgrade** and **Passive Upgrade** are
per-hero instead - which skill/passive upgrades make sense depends on which hero is rolling, so they
live directly on that hero's own `CharacterData` asset, not a separate shared pool asset. Global
Upgrade and Passive Upgrade have no gameplay effect designed yet - both are plumbing-only stubs (see
"Current status" below).

All four kinds share one asset base, `UpgradeData` (`Icon`/`DisplayName`/`Rarity`/abstract
`GetDescription()`) - `WeaponPerkData`, `SkillActionData`, `GlobalUpgradeData` and
`PassiveUpgradeData` all derive from it. This is what lets `LevelUpOption` carry a single
`AssetRef<UpgradeData>` instead of one field per kind, lets every candidate be weighted the same way
(by its own `Rarity`, via `LevelUpConfig.GetWeight`) regardless of which kind it is, and lets the
card-building code (`GameplayUiController.BuildCardData`) resolve any option generically with no
switch at all.

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
       -> collect candidates from this entity's own CharacterData.DashSkillUpgrades/
          HeroSkillUpgrades (excluding any upgrade already granted on that slot - see "Why exclude
          already-granted skill upgrades" below) and CharacterData.PassiveUpgrades
       -> every candidate, regardless of kind, is weighted identically: AddCandidate resolves it
          generically as UpgradeData and looks up LevelUpConfig.GetWeight(data.Rarity) - independent
          of WeaponPerkPoolData's own (differently-tuned) Common/Uncommon/Rare/Epic/Legendary
          weights, which stay reserved for the original drop-roll mechanic (WeaponGenerator)
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
       GlobalUpgrade  -> GlobalUpgradeUtility.Grant (stub - just logs, no effect yet)
       PassiveUpgrade -> PassiveUpgradeUtility.Grant (stub - just logs, no effect yet)
     then f.Remove<LevelUpChoice>(entity)
  -> Global.LevelUpScreenOpen = false, LevelUpTimeRemaining = 0, f.SystemEnable<GameplaySystemGroup>()
```

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

`SkillSystem.AddUpgrade` already rejects a duplicate upgrade on the same slot (logs an error, returns
`false`) - that's existing behavior, not new. Without filtering at roll time, a later level-up could
re-offer a card that would silently fail to grant anything if picked. `CollectPerHeroCandidates`
checks each `DashSkillUpgrades`/`HeroSkillUpgrades` candidate against that slot's own
`SkillSlot.Upgrades` (same lookup `AddUpgrade` itself does) before adding it as a candidate.
`PassiveUpgrade` has no equivalent check - see "Current status" below.

## Files

**Simulation (`Assets/_QuantumUser/Simulation/QTN/`)**
- `LevelUp.qtn` - **new**: `enum LevelUpPoolKind` (`None`/`SkillUpgrade`/`WeaponPerk`/
  `GlobalUpgrade`/`PassiveUpgrade`), `struct LevelUpOption` (`Kind` + a single
  `AssetRef<UpgradeData> Upgrade` + `SkillSlotId SkillUpgradeSlot` - one shared field works for every
  kind precisely because `UpgradeData` is a common base every concrete upgrade type derives from;
  `Kind` says which grant path to reinterpret `Upgrade`'s raw `Id` into, see `LevelUpUtility.GrantOption`),
  `component LevelUpChoice` (`array<LevelUpOption>[3] Options`, `Byte OptionCount`,
  `Boolean Confirmed`, `Byte SelectedIndex`).
- `Experience.qtn` - **edited**, two new global fields: `Boolean LevelUpScreenOpen`,
  `FP LevelUpTimeRemaining`.

**Data (`Assets/_QuantumUser/Simulation/Assets/`)**
- `UpgradeRarity.cs` - **new**: `enum UpgradeRarity : Byte` (`Common`/`Uncommon`/`Rare`/`Epic`/
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
  its own `CommonWeight`/`UncommonWeight`/`RareWeight`/`EpicWeight`/`LegendaryWeight` +
  `GetWeight(UpgradeRarity)` - the ONE weight table every level-up candidate uses regardless of kind
  (deliberately separate from `WeaponPerkPoolData`'s own weights - level-up pacing may want different
  tuning than raw drop rolls).
- `LevelUp/GlobalUpgradeData.cs` - **new**: minimal stub asset (`Description` field, no `Apply()`) -
  `Icon`/`DisplayName`/`Rarity` come from `UpgradeData`. No separate pool wrapper type - see
  `LevelUpConfig.GlobalUpgrades` above.
- `LevelUp/PassiveUpgradeData.cs` - **new**: same minimal stub shape as `GlobalUpgradeData` -
  referenced directly from `CharacterData.PassiveUpgrades` (per-hero, see below).
- `Character/CharacterData.cs` - **edited**, one new field: `List<AssetRef<PassiveUpgradeData>>
  PassiveUpgrades`, alongside the existing `DashSkillUpgrades`/`HeroSkillUpgrades` (same per-hero
  list shape).

**Systems (`Assets/_QuantumUser/Simulation/Systems/`)**
- `LevelUpUtility.cs` - **new**: `BeginLevelUpScreen`, `RollOptionsFor`/`CollectGlobalCandidates`/
  `CollectPerHeroCandidates` (the weighted-draw-without-replacement rolling logic),
  `ConfirmSelection`, `AutoConfirm`, `Resolve`, `GrantOption`. Static utility, mirrors
  `ExperienceUtility`'s shape.
- `LevelUpSystem.cs` - **new**: always-on driver described above.
- `GameplaySystemGroup.cs` - **new**: trivial named `SystemGroup` subclass, purely so
  `SystemDisable<T>()`/`SystemEnable<T>()` have an unambiguous type to key off.
- `GlobalUpgradeUtility.cs` / `PassiveUpgradeUtility.cs` - **new**: one-method stubs, each just logs
  `"... grant path not implemented yet"`.
- `SkillSystem.cs` - **edited**: `ResolveSlot` gained a `public static` overload taking
  `CharacterSkills*` directly (the existing one took `ref Filter`) - gives `LevelUpUtility.GrantOption`
  a supported entry point without touching `SkillSystem`'s own filtered `Update`.
- `ExperienceUtility.cs` - **edited**: `Grant` captures `Level` before its while-loop, calls
  `LevelUpUtility.BeginLevelUpScreen(f)` once afterward if it increased.

**Commands (`Assets/_QuantumUser/Simulation/Commands/`)**
- `SelectLevelUpUpgradeCommand.cs` - **new**: `Byte OptionIndex` only. The actual `Kind`/asset/slot is
  read back off the sender's own already-rolled `LevelUpChoice` (found via `PlayerLink`), so a client
  structurally cannot request an option it was never offered or touch another player's pick.

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
  independent of `UpgradeWindow`.
- `Assets/_Project/Scripts/UI/Window/UpgradeWindow.cs` - **new**, plain `UiWindow` (not a
  `QuantumGlobalMonoBehaviour` itself - matches `WaitingWindow`'s shape). Just orchestrates a fixed
  `UpgradeCardWidget[]` (one per `LevelUpChoice.Options` slot) plus a countdown readout;
  `onCardClicked` bubbles a card index up to whoever wired it.
- `Assets/_Project/Scripts/UI/Window/GameplayUiController.cs` - **edited**: `QUpdate` now polls
  `Frame.Global.LevelUpScreenOpen` each frame (same polling idiom `ExpBarUiWidget` already uses -
  diffed against a cached previous value to detect the open/close edge, since the View is never
  rolled back) to show/hide `UpgradeWindow` via the existing `WindowManager`, reads the local
  player's own `LevelUpChoice` (`MyLocalPlayer.Instance.EntityRef`) and builds each card's
  `UpgradeCardWidget.CardData` with ONE generic path (`frame.FindAsset(option.Upgrade)` resolved as
  `UpgradeData` - no switch on `Kind` needed for display), mapping `Rarity` to a card color locally
  (`RarityColor`), and forwards card clicks as a `SelectLevelUpUpgradeCommand`.

## Current status / known simplifications

Code compiles and `LevelUpSystem` is registered, but **no level-up screen can actually open yet** -
the following need Editor authoring, none of it done yet:

1. **`LevelUpConfig.asset`** - no instance exists, and even once created, isn't assigned to
   `RuntimeConfig`. Until it is, `LevelUpUtility.BeginLevelUpScreen` bails on its very first guard
   every time - a level-up still happens (`Frame.Global.Level` still increments), it just never
   pauses anything or shows a screen.
2. **Every pool is empty** - `WeaponPerkPool`/`GlobalUpgrades` have no entries authored on
   `LevelUpConfig` yet, and no hero's `CharacterData.DashSkillUpgrades`/`HeroSkillUpgrades`/
   `PassiveUpgrades` lists have been extended for this specific screen (though `DashSkillUpgrades`/
   `HeroSkillUpgrades` already existed and may already carry entries from before this feature - those
   get picked up automatically, no extra wiring needed). Even with `LevelUpConfig` assigned, a
   level-up with every pool empty still just logs and skips (no screen, no pause) - see
   `BeginLevelUpScreen`'s `anyRolled` check.
3. **`UpgradeWindow` scene/prefab wiring** - no `UpgradeWindow` GameObject exists under any
   `WindowManager` in `QuantumGameScene` yet, and `GameplayUiController`'s new `upgradeWindow` field
   is unassigned. The `ShowWindow<GameplayWindow>()` "close" transition in
   `GameplayUiController.UpdateUpgradeScreen` is a best guess at "back to normal HUD" - `GameplayWindow`
   holds the per-player HUD widget parent and looks like the right target, but nothing in code shows
   it explicitly today (it may just be the scene's default-active window), so verify this in-Editor
   before relying on it.
4. **Manual end-to-end test recipe** (once the above exists): author one throwaway
   `WeaponPerkPoolData` wrapping one existing perk (e.g. `DamageMultiplierWeaponPerkData`), one
   `LevelUpConfig` pointing at it, assign both to `RuntimeConfig`. Force a level-up (temporarily
   shrink `ExperienceConfig.RequiredExperience`'s first keyframe, or grant a large `TotalExperience`
   via a debug hook) and confirm every client's `UpgradeWindow` opens together, gameplay visibly
   freezes, a card click locks only that client's own pick, the screen closes the instant everyone's
   confirmed (not waiting for the timer), an intentionally-unconfirmed client auto-picks at 0s, a
   mid-screen disconnect doesn't block the rest, and a player joining mid-screen spawns normally
   without a card.

Beyond the missing assets/wiring:
- **Global Upgrade and Passive Upgrade have no gameplay effect** - both `GlobalUpgradeUtility.Grant`
  and `PassiveUpgradeUtility.Grant` just log; `GlobalUpgradeData`/`PassiveUpgradeData` only carry
  display metadata. This is deliberate scope for this pass - only the plumbing (roll, offer, confirm,
  grant-path dispatch) needed to exist end-to-end, not the mechanics themselves.
- **Multiple levels from one `Grant` call collapse into one screen** - if a single big exp grant
  crosses more than one level threshold in the same `while` loop, the player still only sees
  `ChoiceCount` (3) options total, not `3 × levelsGained`. Chosen deliberately over queuing multiple
  sequential screens, which would be a confusing wait for a co-op-wide pause.
- **`PassiveUpgrade` candidates aren't deduplicated against past picks** - unlike `SkillUpgrade`
  (checked against `SkillSlot.Upgrades`), there's no "already granted" list for passives yet (the
  grant path itself is a stub), so the same passive upgrade could be re-offered on a later level-up.
  Revisit once a real passive-upgrade mechanic (and its own granted-tracking) exists.
- **Debug perk/skill triggers are inert while a screen is open** - `WeaponPerkDebugTrigger`/
  `SkillUpgradeDebugTrigger` still send their commands, but `WeaponSystem`/`SkillSystem` are paused
  (inside `GameplaySystemGroup`) so the commands are silently dropped that tick - resumes working the
  instant the screen closes.
