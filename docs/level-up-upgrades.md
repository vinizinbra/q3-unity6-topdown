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
       -> collect candidates from this entity's own CharacterData.DashSkillUpgrades and, for
          HeroSkill, straight from HeroSkill's own Actions (any entry with Activated == false -
          excluding any upgrade already granted on that slot - see "Why exclude already-granted
          skill upgrades" below) and CharacterData.PassiveUpgrades
       -> every candidate, regardless of kind, is weighted identically: AddCandidate resolves it
          generically as UpgradeData and looks up LevelUpConfig.GetWeight(data.Rarity) - independent
          of WeaponPerkPoolData's own (differently-tuned) Common/Rare/Epic/Legendary
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
       GlobalUpgrade  -> GlobalUpgradeUtility.Grant (dispatches to the picked asset's own Apply -
                         same method a GrantGlobalUpgradeCommand debug grant now also calls)
       PassiveUpgrade -> PassiveUpgradeUtility.Grant (dispatches to the picked asset's own Apply -
                         same as GrantPassiveUpgradeCommand)
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
checks each `DashSkillUpgrades` candidate, and `AddHeroSkillUpgradeCandidates` checks each
`HeroSkill.Actions` candidate, against that slot's own `SkillSlot.Upgrades` (same lookup `AddUpgrade`
itself does) before adding it as a candidate.
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
  tuning than raw drop rolls).
- `LevelUp/GlobalUpgradeData.cs` - **new**: minimal stub asset (`Description` field, no `Apply()`) -
  `Icon`/`DisplayName`/`Rarity` come from `UpgradeData`. No separate pool wrapper type - see
  `LevelUpConfig.GlobalUpgrades` above.
- `LevelUp/PassiveUpgradeData.cs` - **new**: same minimal stub shape as `GlobalUpgradeData` -
  referenced directly from `CharacterData.PassiveUpgrades` (per-hero, see below).
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
  through `LevelUpUtility.GrantOption` directly.
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
  (`RarityColor`), and forwards card clicks as a `SelectLevelUpUpgradeCommand`. **Edited**:
  `BuildCardData` now also takes the rolling entity and, only for `Kind == GlobalUpgrade`, re-resolves
  `option.Upgrade` as `AssetRef<GlobalUpgradeData>` (same raw-Id reinterpret `LevelUpUtility.GrantOption`
  uses) to read `MaxPicks`/`GlobalUpgradeUtility.GetPickCount` for the card's stack readout - every
  other kind leaves `CardData.MaxStacks` at 0.

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
4. **Manual end-to-end test still not confirmed run** - the recipe below hasn't been verified
   in-Editor yet as far as this doc knows: force a level-up (temporarily shrink
   `ExperienceConfig.RequiredExperience`'s first keyframe, or grant a large `TotalExperience` via a
   debug hook) and confirm every client's `UpgradeWindow` opens together, gameplay visibly freezes, a
   card click locks only that client's own pick, the screen closes the instant everyone's confirmed
   (not waiting for the timer), an intentionally-unconfirmed client auto-picks at 0s, a mid-screen
   disconnect doesn't block the rest, and a player joining mid-screen spawns normally without a card.

**`CharacterData.HeroSkillUpgrades` was removed** - the Hero Skill slice of the Skill Upgrade pool no
longer has its own authored list at all. `LevelUpUtility.AddHeroSkillUpgradeCandidates` now pulls it
straight from `HeroSkill`'s own `Actions` list instead: any `SkillActionData` authored there with
`Activated == false` is a candidate (see `SkillActionData.Activated` and `SkillSystem.InvokeActions`'
`isUpgrade` bypass - granting it via `AddUpgrade` ignores `Activated` and turns it on for just that
player, while it stays inert as a baseline action for everyone else). No more parallel list to keep
in sync with the skill asset it upgrades. Authoring status per hero not audited here - check each
`HeroSkill` asset's own `Actions` for `Activated == false` entries directly.

Beyond the missing assets/wiring:
- **Both Passive Upgrade and Global Upgrade grant a real `Apply(Frame, EntityRef)`** now (dispatched
  via `PassiveUpgradeUtility.Grant`/`GlobalUpgradeUtility.Grant` - see `docs/global-upgrades.md` for
  Global Upgrade's own roster), but neither kind tracks *which* upgrades an entity currently holds -
  see the dedup bullet below.
- **Multiple levels from one `Grant` call collapse into one screen** - if a single big exp grant
  crosses more than one level threshold in the same `while` loop, the player still only sees
  `ChoiceCount` (3) options total, not `3 × levelsGained`. Chosen deliberately over queuing multiple
  sequential screens, which would be a confusing wait for a co-op-wide pause.
- **`PassiveUpgrade`/`GlobalUpgrade` candidates aren't deduplicated against past picks** - unlike
  `SkillUpgrade` (checked against `SkillSlot.Upgrades`), there's no "already granted" list for either
  kind, so the same upgrade could be re-offered on a later level-up. Revisit once a real
  granted-tracking ledger exists for both.
- **No revert path for Weapon Perk / Passive Upgrade / Global Upgrade** - each bakes its effect
  directly into a live component field at grant time (`WeaponPerkData`/`PassiveUpgradeData`/
  `GlobalUpgradeData`'s own `Apply`), with no per-grant ledger to undo from and, in several cases, a
  lossy transform (multiply-then-clamp, or additive-then-partially-consumed) that isn't even
  mathematically invertible from current state alone. Their "Remove"/"Clear All" debug buttons (see
  below) only log this instead of pretending to revert - restart play mode to actually reset a
  player. `SkillActionData` upgrades are the one kind that's cheaply reversible (a slot re-reads
  `SkillSlot.Upgrades` fresh every activation instead of baking it), so those two buttons work for
  real - see `SkillSystem.RemoveUpgrade`/`ClearUpgrades`.
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
