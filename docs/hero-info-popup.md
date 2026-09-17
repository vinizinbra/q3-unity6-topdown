# Hero Info Popup (Tab-hold)

The Tab-hold overlay, previously `UpgradePopupWidget` (a pure upgrade-history list), is now
`HeroInfoPopupWidget`: a full "everything I'm currently running" readout for one player. Renamed in
place (same `.cs.meta` GUID, so the existing scene component and every wired field survived the
rename untouched).

## Layout

Three stacked sections, top to bottom:

1. **`HeroInfoWidget`** (new) - head icon, health and shield readouts, one row each for the hero's
   **Base Skill** and **Passive Skill** (icon + name + description), and three live equipped-weapon
   stat readouts, **Current Damage**, **Current Elemental**, and **Current Critical** (see below).
2. **`CurrentWeaponUiWidget`** (existing, reused as-is) - the equipped weapon plus one row per
   granted perk.
3. **The upgrade history lists** (unchanged) - the 4 `LevelUpPoolKind`-split vertical scroll lists
   that were the whole widget before: hero (SkillUpgrade + PassiveUpgrade), global, rift, rift mark.

`HeroInfoPopupWidget` itself only owns the Tab toggle, the entity binding it forwards down to
sections 1 and 2, and section 3's own pooled rebuild. Weapon perks deliberately never appear in
section 3 - `LevelUpUtility.RecordHistory` early-outs on `WeaponPerk`/`ChooseWeapon` because they're
already visible on the weapon itself, which is exactly what section 2 shows.

## Composition, not reimplementation

`HeroInfoWidget` reads nothing itself that an existing widget already reads - same
compose-and-forward-`Initialize` shape as `PartyHudWidget`:

| Piece | Reused widget | Notes |
| --- | --- | --- |
| Head icon | `PlayerPortraitUiWidget` | Snapshots the bound entity's live `BlobAnimationView.Head` sprite - no per-hero portrait art authored twice. |
| Health text | `HealthUiWidget` | Its `Slider` is optional; assign only `healthText` for a plain readout. |
| Shield text | `ShieldUiWidget` | Same - `Slider` optional, `shieldText` alone is a valid setup. |
| Base Skill row | `UpgradeWidget` | Already exactly an icon + name + description + optional level row. Passed level `0`, so the level badge stays hidden. |
| Passive Skill row | `UpgradeWidget` | Same row type as above. |

**Base Skill** resolves off `CharacterSkills.HeroSkill.Skill` (the live slot), not
`CharacterData.HeroSkill`, so a Hero Skill swap/upgrade mid-run is reflected. **Passive** resolves
off `CharacterStats.CharacterData` -> `CharacterData.Passive`. Both rows only re-render when the
resolved `AssetRef` actually changes; health/shield poll every frame via their own widgets.

The head icon is a snapshot off the live `CharView`, which can legitimately not exist yet at bind
time, so `HeroInfoPopupWidget` calls `HeroInfoWidget.Refresh()` every time the popup is opened
rather than relying on the one-shot `Initialize`. Everything under the popup's `root` only ticks
while the popup is actually shown - `QuantumGlobalMonoBehaviour.Update` doesn't run on an inactive
GameObject.

## Current Damage / Current Elemental / Current Critical

`HeroInfoWidget` also reads the bound entity's equipped `Weapon`/`WeaponDataAsset` directly (its own
`RefreshWeaponStats`, called every `QUpdate` alongside `UpdateBaseSkill`/`UpdatePassive`) to show
three live-computed stats - independent of `CurrentWeaponUiWidget`'s own weapon-icon/perk-row
readout in section 2, which neither reads nor is read by these:

- **Current Damage** - `weaponData.Damage * weapon.DamageMultiplier` (perks/base traits already
  baked into `DamageMultiplier` at Equip) times the new `DamageUtility.ResolveBaselineDamageMultiplier`
  - `CharacterStats.DamageMultiplier`/`WeaponDamageMultiplier`, level scaling, live Rift Mutation/
  temp-buff bonuses, and Hero Mastery's flat Weapon Family + Element bonus (Pistol Mastery, Fire
  Mastery, etc. - the new `HeroMasteryUtility.ResolveBaselineDamageMultiplier`). Deliberately a
  **baseline** number: every target-conditional term real combat also applies (range falloff, Point
  Blank/Vendetta/Deadeye/Infernal Rage specials, Stun/Bound/RevengeMark/Cremation bonuses, crit) is
  skipped, since the popup has no target to evaluate them against. Both new `DamageUtility`/
  `HeroMasteryUtility` methods are public specifically so this widget reads the same math real
  damage resolution uses instead of a hand-duplicated copy - see `ResolveDamageReduction`/
  `DamageReductionUiWidget`'s own precedent for that pattern.
- **Current Elemental** - mirrors `StatusEffectUtility.ApplyElementBaseline`'s own per-element
  formula, fed the same baseline hit damage above: Burn tick damage (Fire, via
  `ComputeDotDamagePerTickWithFloor`) or Chill buildup as a % of the Freeze threshold per hit (Ice).
  Hidden for a Neutral weapon; Lightning/Shock has no build-scaled magnitude in the sim (it's a
  setup state, not a DoT) so it shows a static "Shock on hit" label instead of a number.
  **Known gap, not fixed here:** `ApplyElementBaseline` is fed the *pre-CharacterStats-multiplier*
  hit damage in the real sim (see `WeaponSystem`'s hit call sites) - so Fire/Element Mastery's damage
  bonus does NOT currently move Burn tick damage in actual gameplay, only direct hit damage. This
  widget mirrors that real behavior rather than silently "fixing" it, so the two new stats never
  contradict each other, but it means Fire Mastery visibly raises Current Damage without raising
  Current Elemental - a real balance inconsistency worth its own pass.
- Both stats also skip magazine-position/ramp perk bonuses (Execution/Final/Escalating Rounds,
  Relentless Fire, Phantom Strike) - those vary shot-to-shot rather than being a stable build number.
- **Current Critical** - `"x{multiplier} <color=#FD3971>|</color> {chance}%"` (the standard #FD3971
  inline-highlight color, see `feedback_description_highlight_color`) via the new
  `DamageUtility.ResolveBaselineCritical`: `CharacterStats.CriticalChance/CriticalDamageMultiplier`
  plus the equipped weapon's own `CriticalChance`/`CriticalDamageBonus`. Skips Max's Hot Target
  (bonus Critical Chance vs a Burning target) since that's target-conditional, same reasoning as
  Current Damage.

## `PassiveData` gained Icon/DisplayName

`PassiveData` had a `Description` but no icon or player-facing name, so the Passive Skill row had
nothing to show. Added via a new `PassiveData.View.cs` partial, mirroring `SkillData`/
`SkillData.View.cs` one-for-one - on the shared abstract base, not per hero passive. `PassiveData`
itself is now `partial`.

Name fallback (empty `DisplayName` -> beautified asset file name) lives in `HeroInfoWidget`, not on
the asset, because `StringUtility` is Assembly-CSharp and `PassiveData` is in the Simulation
assembly. Same convention `CurrentWeaponUiWidget`/`GameplayUiController.BuildWeaponCardData` already
use for an unauthored `WeaponDataAsset.DisplayName`.

## Current status

Code compiles; no `.qtn` change, so no codegen dependency. The rename cost the scene nothing - the
existing `root`/`upgradeWidgetPrefab`/`heroContent`/`globalContent`/`riftContent` assignments on
`QuantumGameScene.unity`'s Canvas are all still wired.

### Editor authoring needed

The scene component currently sits on the HUD `Canvas` GameObject with `root` pointing at
`AcquiredUpgrades` (three `Scroll View`s: hero, global, rift). Everything new is unassigned:

1. Build the `HeroInfoWidget` hierarchy under `AcquiredUpgrades` (above the scroll views) and assign
   it to `HeroInfoPopupWidget.heroInfoWidget`. Consider renaming `AcquiredUpgrades` - it's now the
   whole popup, not just the upgrade lists.
2. On that `HeroInfoWidget`, wire `portraitWidget` (a `PlayerPortraitUiWidget` + `Image`),
   `healthWidget`/`shieldWidget` (`HealthUiWidget`/`ShieldUiWidget`, text-only is fine), and
   `baseSkillWidget`/`passiveSkillWidget` (two `UpgradeWidget` rows).
3. Add a `CurrentWeaponUiWidget` instance under the popup and assign it to
   `HeroInfoPopupWidget.currentWeaponWidget`. Its `autoBindLocalPlayerOne` is force-disabled by the
   popup in `Awake` - the popup pushes the binding instead, same as `PartyHudWidget` does. Also wire
   `HeroInfoWidget`'s own new `currentDamageText`/`currentElementalText`/`currentCriticalText` (all
   optional - left unassigned just skips that stat) - see "Current Damage / Current Elemental /
   Current Critical" above.
4. Author `Icon`/`DisplayName` on all 6 `PassiveData` assets (Max's Vendetta, Brute's Protector,
   Lux's Scrap Collector, Pixie's Chain Reaction, Kai's Void Field, Zara's Resonance) - until then
   the Passive row shows no icon and falls back to the beautified asset name.
5. Still outstanding from before this pass: `riftMarkContent` has never been assigned a scroll
   Content (see `docs/rift-mutations.md`). `RebuildList` early-outs on a null `content`, so nothing
   breaks - Rift Mark Mutation picks just silently don't render until it's wired.

Not yet manually verified end-to-end in-Editor.
