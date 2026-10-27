# Hero Info Popup (Tab-hold)

Shown while Tab (keyboard) or the `OpenHeroInfo` Input Manager button (`joystick button 6`) is held.
On mobile, Android Back (`KeyCode.Escape`) *toggles* it instead - an Android pad's Select (MOGA Pro 2)
arrives as Back with down+up in the same frame, so it can never read as held.

The Tab-hold overlay, previously `UpgradePopupWidget` (a pure upgrade-history list), is now
`HeroInfoPopupWidget`: a full "everything I'm currently running" readout for one player. Renamed in
place (same `.cs.meta` GUID, so the existing scene component and every wired field survived the
rename untouched).

## Layout (2026-10-26 readability pass)

Laid out in the `Canvas.prefab` on a fixed grid - 36px outer margin, 20px gutter - with the same pink
(#FD3971) uppercase section headers everywhere:

1. **Hero panel** (top-left, 540x392) - portrait, uppercase name, Health and Shield side by side (the
   Shield readout hides itself for a hero with no shield, `HeroInfoWidget.UpdateShieldRow`, instead of
   leaving `ShieldUiWidget`'s placeholder text), a divider, then `HeroBuildStatsWidget`'s
   OFFENSE / DEFENSE & UTILITY columns. Base/Passive Skill rows are retired from this popup.
2. **Weapon panel** (top-right, 539x392) - `CurrentWeaponUiWidget`: icon, name (+level), a tags line
   (`weaponTagsText`: family | weight | element, element tinted; Medium/Neutral omitted), a divider and
   the 5 perk rows as compact one-line-or-two "**Name** - effect" rows that always fit the panel, with a
   "No perks yet" label (`noPerksLabel`) when empty. `separatePerkTitles` exists for roomier layouts but
   is off here.
3. **Run Upgrades** (bottom, full width) - title pinned above the scroll viewport (not a list child), and
   the history cards in a 2-column `GridLayoutGroup` (514x98 cells), so roughly twice as many are visible
   before scrolling; "No upgrades yet" (`emptyUpgradesLabel`) when nothing has been picked, including
   before the entity even has an `UpgradeHistory`.

Mockups: `HeroBuildStatsWidget`'s Build Preview rows are `HideFlags.DontSave`, so they can never be
written into the prefab/scene (a prefab instance can't delete inherited children, so a saved mockup used to
stack under every rebuild). Nothing layout-related lives as a scene-instance override.

`HeroInfoPopupWidget` itself only owns the Tab toggle, the entity binding it forwards down to
sections 1 and 2, and section 3's own pooled rebuild. Weapon perks deliberately never appear in
section 3 - `LevelUpUtility.RecordHistory` early-outs on `WeaponPerk`/`ChooseWeapon` because they're
already visible on the weapon itself, which is exactly what section 2 shows.

## Build Stats (HeroBuildStatsWidget)

`HeroArea` is now: icon + name + health/shield on top, then `BuildStats` below with two titled columns,
**Offense** (Damage, Crit Chance, Crit Damage, Area) and **Defense & Utility** (Max Health, Move Speed,
Skill Cooldown, Dash Cooldown). Rows are built in code; each shows value + colored delta (green better /
red worse, cooldowns invert). Damage, Crit Chance/Damage, Max Health and Move Speed always show; Area and
the two cooldowns only once the build moves them off baseline. Damage shows the TOTAL baseline hit (the same number as the floating damage text, tinted green/red vs base) with the weapon's authored damage as a gray "(base N)" - never a "+N", which reads additive (both rounded UP, like the floating damage text and the health readout). Skill/Dash Cooldown show the real change to the cooldown (`1 / CooldownMultiplier` - the stat is a rate that `StatUtility.GetSkillCooldown` divides by, so a 1.15 pick is -13%, not +15%). Every other row is a % delta vs a neutral 1.0 multiplier
(include the hero's own baseline). Wired to `HeroInfoPopupWidget.buildStatsWidget`. Attack Speed, Reload,
Skill Damage, Dmg Reduction, Life Steal, Pickup, XP/Coin Gain and Luck were deliberately left off.

**`CharacterStats.ProjectileSpeedMultiplier` was removed from the game** (field in `CharacterStats.qtn`,
`CharacterData`, `CharacterSystem` seeding, `StatUtility.GetProjectileSpeedMultiplier`, and its term in
`ProjectileSpawner.Spawn`). Nothing wrote it after the global upgrade was deleted and every hero authored
1.0, so gameplay is unchanged. Weapon, enemy/boss, Pixie Blast Jump and Yoru Event Horizon projectile-speed
multipliers are different fields and remain.

Base Skill / Passive rows: the `Passive`/`BaseSkill` objects are off and `HeroInfoWidget.baseSkillWidget`/
`passiveSkillWidget` are unassigned on the popup's instance - `UpdateBaseSkill`/`UpdatePassive` re-activate
their object every frame, so disabling the GameObject alone does nothing. The old Damage/Crit text lines
on `HeroInfoWidget` are also unassigned (superseded by Offense).

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
   Capy's Scrap Collector, Pixie's Chain Reaction, Yoru's Void Field, Zara's Resonance) - until then
   the Passive row shows no icon and falls back to the beautified asset name.
5. Still outstanding from before this pass: `riftMarkContent` has never been assigned a scroll
   Content (see `docs/rift-mutations.md`). `RebuildList` early-outs on a null `content`, so nothing
   breaks - Rift Mark Mutation picks just silently don't render until it's wired.

Not yet manually verified end-to-end in-Editor.
