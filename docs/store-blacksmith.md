# Store & Blacksmith

Two Breathing-only POIs on top of the existing generic POI framework (`Poi.qtn`/`ContextInteraction.qtn`,
see `docs/breathing-poi.md`) - a **Store** (buy weapons and food/utility items with Coins) and a
**Blacksmith** (pay Coins to add a new Weapon Perk to your currently-equipped weapon). Both reuse
the same `ChooseWindow`/`UpgradeCardWidget`/`WeaponCardWidget` UI Level-Up/Cursed Rift already use -
no new parallel UI classes - and both reuse the existing weapon-generation/weapon-perk/currency
systems rather than duplicating them.

## Design

### Store

- Rolls a **shared** `StoreInventory` (up to `StoreConfig.MaxWeaponOfferSlots` weapons +
  `StoreConfig.FoodOfferCount` food/utility offers) once per Breathing Break, lazily the first time
  any player opens it that Break (`StoreUtility.EnsureInventoryRolled`) - deterministic across
  clients (`f.RNG`), same guarantee every other shared roll in this codebase relies on.
- Weapon offers are rolled the same way `LevelUpUtility.RollChooseWeaponOptionsFor` rolls a
  Choose-Weapon level-up (distinct weapons, uniform draw without replacement). As of 2026-08-29, a
  Store offer's own quality is driven by 2 **deliberately independent** axes (previously 3 - see
  "Weapon Offer Scaling" below for what changed): `Global.SurvivalTime` sets **Weapon Level** and
  **starting perk COUNT**, shared with a Choose-Weapon level-up/Chest pick so both draw from the
  exact same random configuration; the TRIGGERING player's own `RuntimePlayer.Talents.WeaponLevel`
  (`StoreUtility.ResolveWeaponLevelTalent`, resolved via that entity's own `PlayerLink` ->
  `f.GetPlayerData` - unchanged, still the persistent meta-progression stat that seeds
  `CharacterStats.WeaponTalentLevel` at spawn, deliberately NOT the live in-run one, which is pure
  bookkeeping now) sets starting perk **RARITY**. `ShopWeaponOfferCount` (unchanged, see below) sets
  how many offers are shown - a separate, 3rd axis. Since `StoreInventory` is rolled ONCE, shared
  across every player, whoever opens the Store first each Break is whose talent sets the perk rarity
  for everyone until the next restock - an accepted consequence of the shared-inventory design, not
  per-player. `Price = WeaponOfferBasePrice + WeaponOfferPricePerPerk * RolledPerkCount` (unchanged -
  Weapon Level doesn't factor into price).

#### Weapon Offer Scaling (Weapon Level / starting perk count) - `LevelUpConfig.WeaponOfferCurve`

**Reworked 2026-08-29** - previously Store scaled Weapon Level/starting perk count off
`Global.BreathingIndex` via its own `StoreConfig.BreakWeaponConfig[]`/`ResolveBreakWeaponConfig`,
while a Choose-Weapon level-up/Chest pick scaled its own starting perk count off the persistent
`CharacterStats.WeaponTalentLevel` via `LevelUpUtility.RollWeaponOption`'s
`clamp01((weaponTalentLevel - slot) * ChancePerLevelPerSlot)` formula - two independently-tuned
mechanisms producing the same kind of value. Both are gone, replaced by one shared,
`Global.SurvivalTime`-driven curve on `LevelUpConfig` (not `StoreConfig` - it has to live somewhere
both Store and Choose-Weapon/Chest can reach, and `LevelUpConfig` is already the "weapon choose"
config both paths reference):

- **`LevelUpConfig.WeaponOfferCurve[]`** (`WeaponOfferTimeAnchor` rows: `Minute`/`WeaponLevel`/
  `StartingPerkRolls`) - one row per anchor minute of `Global.SurvivalTime`, mirroring
  `BalanceConfig.RunCurveAnchor`/`Evaluate`'s own shape (linear interpolation between the two
  bracketing anchors, clamped flat outside the authored range) rather than folded into the shared
  `BalanceConfig` asset itself, since `WeaponLevel`/`StartingPerkRolls` are direct authored values,
  not multipliers applied to a baseline the way `EnemyHp`/`EnemyDmg`/`DirectorBudget` are.
  `LevelUpConfig.ResolveWeaponOfferLevel(survivalSeconds)` lerps `WeaponLevel` (rounded to the
  nearest `byte`); `LevelUpConfig.RollWeaponOfferPerkCount(f, survivalSeconds)` lerps each
  `StartingPerkRolls` slot independently (a slot past either bracketing anchor's own authored array
  length reads as 0 - "not yet unlocked") then rolls each as an INDEPENDENT Bernoulli chance
  (`DamageUtility.RollChance`) - the number of successes is the rolled perk count. Both methods are
  called from `StoreUtility.RollWeaponOffers` AND `LevelUpUtility.RollWeaponOption` - the single
  shared roll behind "weapons in the Store and weapons from a Chest/Choose-Weapon level-up use the
  same random configuration."
- **Weapon Level application**: this is the SAME `Weapon.Level`/`WeaponSystem.AddLevel` (+5%,
  compounding, `LevelUpConfig.WeaponLevelDamageBonusPerLevel` - a new field, distinct from
  `StoreConfig.WeaponLevelUpDamageBonusPerLevel`, which stays its own separate value for the
  unrelated guaranteed "Increase Weapon Level" purchase below) the guaranteed offer already uses - no
  new damage formula. Since `WeaponChoiceUtility.Grant` always calls `WeaponSystem.Equip`, which
  resets a freshly-equipped `Weapon` back to Level 0 (`WeaponSystem.SeedStats`), the rolled starting
  Level can't be baked into the option/weapon beforehand - it's carried as a new
  `LevelUpOption.RolledWeaponLevel` field (Choose-Weapon/Chest) or copied from `StoreWeaponOffer.
  WeaponLevel` into that same field at purchase time (Store), and `Grant` now takes an explicit
  `weaponLevelDamageBonusPerLevel` parameter and applies `AddLevel` that many times right after
  `Equip`, in BOTH paths - previously only Store ever produced a nonzero Level at all (`StoreUtility.
  ApplyBreakWeaponLevel`, now deleted); a Choose-Weapon/Chest pick was always Level 0.
- **Starting perk rarity**: `StoreConfig.TalentRarityTuning[]` (`WeaponTalentRarityTuning` rows,
  `StoreConfig.ResolveTalentRarityTuning(weaponTalentLevel)` - indexed by the SAME account-level
  `RuntimePlayer.Talents.WeaponLevel` `ResolveWeaponLevelTalent` already resolves) mirrors
  `BlacksmithConfig`'s own `BlacksmithBreakTuning`/`ResolveBreakTuning` shape one-for-one (Common/
  Rare/Epic/Legendary weights + `GetWeight`, same clamp-to-last-row convention) - deliberately a
  parallel struct, not a shared/renamed one, since it's indexed by a different axis (Weapon Talent
  Level, not Breathing Break). `StoreUtility.RollStorePerks` draws from it via `WeightedDrawUtility`
  (weighted, WITHOUT replacement) over the same `LevelUpConfig.WeaponPerkPool` a Choose-Weapon pick
  already draws from - one candidate per distinct perk asset, so a freshly-rolled weapon can never
  receive the same perk twice (no `AlreadyEquipped` exclusion needed, unlike Blacksmith - this is
  always a brand new, perk-less weapon).
- Food/utility offers are a weighted draw (`WeightedDrawUtility`, new generic helper) from
  `StoreConfig.FoodPool`, each priced at its own `FoodOfferData.Price`.
- A third, **guaranteed** card - "Increase Weapon Level" - is always present alongside the 2 rolled
  food offers (appended at a fixed index in the food-card family, `GameplayUiController.
  StoreWeaponLevelUpCardIndex`), unlike everything else in `StoreInventory`, nothing about it is
  random or shared - it's resolved live per-player off their own currently-equipped `Weapon`. This
  is a NEW, third "weapon level" concept, deliberately distinct from the other two already in the
  codebase: `RuntimePlayer.Talents.WeaponLevel` (permanent meta-progression, seeds the run's
  starting value, never written to mid-match) and `CharacterStats.WeaponTalentLevel` (live in-run,
  drives how many perks a *future* weapon pick rolls - see "Level-Up Upgrades" in the root
  `CLAUDE.md`). This one, `Weapon.Level`, only affects the ONE `Weapon` instance currently equipped
  - buying it calls `WeaponSystem.AddLevel`, which compounds `+5%` (`StoreConfig.
  WeaponLevelUpDamageBonusPerLevel`) into `Weapon.DamageMultiplier`, the exact same "compound in
  place" idiom `DamageMultiplierWeaponPerkData.Apply` already uses for a rolled perk - so it
  composes identically with every other damage-multiplier source. `Level` itself is pure
  bookkeeping (price scaling, display), reset to 0 on every re-equip alongside `DamageMultiplier`
  (`WeaponSystem.SeedStats`) - a new weapon drop/pick starts back at Level 0. Price = `StoreConfig.
  WeaponLevelUpBasePrice + WeaponLevelUpPricePerLevel * Weapon.Level` (read live, gets pricier the
  more you've already bought). Purchasable once per player per Break (`StorePurchases.
  WeaponLevelUpPurchasedAtBreathingIndexPlusOne`, stored offset by 1 so a fresh-zero default can't
  false-collide with a real `BreathingIndex 0`, same bug class `StoreInventory.
  RolledAtBreathingIndex`'s own `-1` sentinel exists to avoid) - resets automatically next Break,
  same "per Break" cadence every other Store/Blacksmith limit already uses.
- **Per-player, per-offer purchase tracking** is the one genuinely new mechanism this feature
  needed - the existing `PoiUsage` component is one bit per whole POI per player, not enough to
  track "did I buy offer N" independently. `StorePurchases` (find-and-overwrite-in-place by
  `(Store, OfferIndex, IsWeaponOffer)`, compared against the inventory's own live
  `RolledAtBreathingIndex`) fills that gap - buying resets to "available" automatically the next
  time that same offer slot gets rerolled with something new.
- **How many of the shared weapon offers a player can actually buy from** is a separate,
  per-player concept: `CharacterStats.ShopWeaponOfferCount` (seeded once at spawn from
  `RuntimePlayer.Talents.ShopWeaponOfferCount`, a meta-progression talent - see `docs/talents.md`).
  Confirmed with the user: rank 0 -> 1 offer, rank 1 -> 2, rank 2 -> 3
  (`StoreUtility.ResolveWeaponOfferCount` = `Clamp(ShopWeaponOfferCount + 1, 1, MaxWeaponOfferSlots)`,
  also capped by however many the shared inventory actually rolled).
- No `PoiUsagePolicy` on `Store` itself (unlike Healing Shrine/Cursed Rift/Blacksmith) - the POI is
  unconditionally re-browsable while Available; `PoiActivationSystem` resolves its `PoiActivation`
  against `PoiUsagePolicy.Reusable` for this reason.
- Buying a weapon reuses `WeaponChoiceUtility.Grant` **unchanged** - `StoreUtility.BuyWeapon`
  constructs a throwaway `LevelUpOption` from the purchased offer's 3 relevant fields
  (`WeaponData`/`RolledPerks`/`RolledPerkCount`) and calls `Grant` directly, so a Store purchase also
  bumps `CharacterStats.WeaponTalentLevel` exactly like a Choose-Weapon level-up pick does (accepted
  consequence, not a bug).
- Buying food calls the purchased `FoodOfferData.Apply` immediately - no food inventory, nothing
  persisted (see MVP scope below).

### Blacksmith

- Rolls **per-player**, since eligibility depends on the buyer's own currently-equipped weapon -
  unlike Store's shared inventory. **2026-09-01 fix:** the roll is cached for the rest of the
  current Breathing Break in a new `BlacksmithOffer` component (`RolledAtBreathingIndex` +
  `PerkChoices`/`PerkChoiceCount`), separate from the transient `BlacksmithInteraction` ("is the
  window open") component - mirrors `StoreInventory.RolledAtBreathingIndex`/
  `StoreUtility.EnsureInventoryRolled` one-for-one (see `BlacksmithUtility.EnsureOfferRolled`).
  Previously `BlacksmithInteraction` was the ONLY place the roll lived, so removing it on Cancel
  (or any other close) threw the roll away - reopening, including a plain walk-away-and-come-back
  with no explicit Cancel intent, silently rerolled every time, since Blacksmith has no neutral
  "just close" affordance the way Store's `CloseStoreCommand` does (its "CANCEL" button is the
  only exit). Now a fresh roll only happens on the FIRST visit each Break; Cancel/reopen within
  the same Break reads the cached `BlacksmithOffer` back out unchanged.
- Excludes any perk already on the buyer's weapon via `LevelUpUtility.AlreadyEquipped` (promoted
  `internal` for this reuse) - confirmed with the user: Blacksmith never offers an already-owned
  perk, there is no rank-upgrade mechanic.
- Weighted by the **current Breathing Break's own rarity tuning**
  (`BlacksmithConfig.ResolveBreakTuning(Global.BreathingIndex)` - a `BlacksmithBreakTuning[]` array,
  same "last authored row holds forever past the authored range" convention `SurvivalConfig.Phases[]`
  uses) rather than `WeaponPerkPoolData`'s own flat Common/Rare/Epic/Legendary weights - the whole
  point of Blacksmith is getting rarer as a run progresses. 4 example rows authored:
  Break1 85/15/0/0, Break2 70/28/2/0, Break3 50/45/5/0, Break4 30/60/10/0.
- **Costs Coins** (confirmed with the user), priced per the SPECIFIC perk's own Rarity rather than
  one flat price for every offer (`BlacksmithConfig.ResolvePerkPrice(perkData.Rarity)` -
  `CommonPerkPrice`/`RarePerkPrice`/`EpicPerkPrice`/`LegendaryPerkPrice`, resolved live off
  `WeaponPerkData.Rarity` at both display and spend time, never baked into `BlacksmithInteraction`
  since Rarity is a static asset field), spent via `CoinUtility.TrySpend` before
  `WeaponSystem.AddPerk` (reused unchanged); a failed grant (perk slots filled between roll and
  pick) refunds rather than silently eating the Coins.
- Reuses the existing generic `PoiUsagePolicy.OncePerPlayerPerBreak` mechanism directly (unlike
  Store) - exactly one successful pick per player per Break, no bespoke tracking needed.
- A picked perk's card never shows SOLD OUT - the moment a pick lands the whole interaction/window
  closes (`BlacksmithInteraction` removed), so a still-rendered card is never "sold out," only gone.
- Cancel is free (no `PoiUsage` marked) - Blacksmith has no payment step before the pick itself, so
  walking away costs nothing, unlike Cursed Rift's Sacrifice stage (which commits cost on pick, not
  confirm).

### Shared UI generalization

- `PurchasableCardState` (new struct: `ShowPurchaseUi`/`Price`/`Currency`/`CanAfford`/
  `IsSoldOut`) + `PurchasableCardUi.Apply` (one static helper) - both `UpgradeCardWidget.CardData`
  and `WeaponCardWidget.CardData` gained a `Purchase` field and 5 new optional serialized fields
  (purchase row + SOLD OUT overlay + a dedicated `buyButton`). `ShowPurchaseUi` defaults `false`, so
  every existing Level-Up/Weapon-Upgrade/Chest/Cursed-Rift call site is completely unaffected. A
  purchase card hides the widget's normal "CHOOSE" `button` entirely and shows `buyButton` in its
  place instead of just relabeling the same button - both fire the identical `onClicked` event, so
  `ChooseWindow`/`GameplayUiController`'s dispatch needed no changes. An unaffordable or sold-out
  offer stays visible with `buyButton` disabled, never hidden - co-op players can always see what's
  on offer. The currency icon itself is resolved at render time via the pre-existing (but
  previously unwired) `SpriteManager.GetSprite(Currency.ToString())` - a shared name-keyed
  `SpriteConfigCurrency` asset - instead of each card widget carrying its own duplicate sprite
  array; `CurrencyUiWidget`'s HUD icon and `FlyingCurrencyManager`'s pickup-flight sprite were both
  switched onto the same lookup at the same time, replacing their own previously-duplicated
  hand-dragged-sprite/hardcoded-per-currency-field approaches.
- `ChooseWindow.SetCardFamilyActive` was split from one `showWeaponCards` bool into independent
  `showCards`/`showWeaponCards` - every existing screen still only shows one family at a time
  (zero behavior change there), but Store's new `RefreshStore` shows both at once, food/utility row
  first, weapon row second (confirmed layout order with the user).
  `ChooseWindow.onCardClicked` (from `cards[]`) and the new `onWeaponCardClicked` (from
  `weaponCards[]`) are now genuinely separate events for this reason - Store maps clicks from each
  family to a different command (`BuyStoreFoodCommand` vs `BuyStoreWeaponCommand`).
- `GameplayUiController`'s old binary `Global.LevelUpScreenOpen ? LevelUp : CursedRift` dispatch is
  now a real per-slot `ChoiceWindowOwner` resolution (`None/LevelUp/CursedRift/Store/Blacksmith`,
  `ResolveOwner` - a plain sequential presence check, safe because `PoiInteractionLockUtility`
  already guarantees a player can never hold more than one POI interaction at once), cached once per
  tick (`UpdateWindowOwners`, run before both `UpdateUpgradeScreen` and the renamed
  `UpdatePoiWindow`) so every click handler can read it without a fresh Quantum lookup.
  `secondaryButton` (already reused for "KEEP CURRENT"/"CANCEL") gained two more meanings - "CLOSE"
  on Store, "CANCEL" on Blacksmith.

## Simulation-side input lock generalization

`PoiInteractionLockUtility.IsInputLocked` (new) replaces what used to be
`CursedRiftUtility.IsInputLocked`'s own single check - now an OR across
`CursedRiftInteraction`/`StoreInteraction`/`BlacksmithInteraction`. Read by
`PlayerMovementProcessor.BeforeMove`, `WeaponSystem.Update`, `SkillSystem.Update`, and
`ContextInteractionSystem`'s own Busy check - the same 4 call sites `CursedRiftUtility.IsInputLocked`
used to serve alone. A player mid-Store/Blacksmith is locked exactly the same way a Cursed Rift
Choice Window already locks input - movement/weapon/Dash/Hero-Skill gated, `GameplaySystemGroup`/
`Time.timeScale` untouched, everyone else keeps playing normally.

## File map

- `Assets/_QuantumUser/Simulation/QTN/Poi/Store.qtn` - `Store`/`StoreInventory`/`StoreWeaponOffer`/
  `StoreFoodOffer`/`StoreInteraction`/`StorePurchases`/`StorePurchaseEntry` (+ `StorePurchases.
  WeaponLevelUpPurchasedAtBreathingIndexPlusOne`, and `StoreWeaponOffer.WeaponLevel` - the offer's
  own resolved starting Weapon Level, `LevelUpConfig.ResolveWeaponOfferLevel` as of 2026-08-29 - see
  "Weapon Offer Scaling" above).
- `Assets/_QuantumUser/Simulation/QTN/Weapon.qtn` - `Weapon.Level` (the guaranteed offer's own
  target - see Design above). `Assets/_QuantumUser/Simulation/Systems/Weapon/WeaponSystem.cs` -
  `AddLevel`, `SeedStats` resets `Level = 0` alongside `DamageMultiplier`.
- `Assets/_QuantumUser/Simulation/Assets/Config/StoreConfig.cs` - `WeaponTalentRarityTuning`/
  `TalentRarityTuning[]`/`ResolveTalentRarityTuning` (Weapon Talent Level -> starting perk rarity) -
  see "Weapon Offer Scaling" above. As of 2026-08-29, Weapon Level/starting perk count are no longer
  here (`StoreBreakWeaponConfig`/`BreakWeaponConfig[]`/`ResolveBreakWeaponConfig` deleted) - see
  `Assets/_QuantumUser/Simulation/Assets/LevelUp/LevelUpConfig.cs` (`WeaponOfferTimeAnchor`/
  `WeaponOfferCurve[]`/`ResolveWeaponOfferLevel`/`RollWeaponOfferPerkCount`/
  `WeaponLevelDamageBonusPerLevel`) and `Assets/_QuantumUser/Simulation/QTN/LevelUp.qtn`
  (`LevelUpOption.RolledWeaponLevel`, new field). `StoreUtility.cs` - `RollWeaponOffers` rewritten to
  call the shared `LevelUpConfig` methods (`RollStorePerkCount`/`ApplyBreakWeaponLevel` deleted),
  `RollStorePerks` unchanged (perk RARITY stays Store-only).
- `Assets/_QuantumUser/Simulation/QTN/Poi/Blacksmith.qtn` - `Blacksmith`/`BlacksmithInteraction`/
  `BlacksmithOffer` (new 2026-09-01, the per-Break roll cache - see "Blacksmith" section above).
- `Assets/_QuantumUser/Simulation/QTN/Poi/ContextInteraction.qtn` - `InteractableKind` gained
  `Store`/`Blacksmith` (append-only).
- `Assets/_QuantumUser/Simulation/QTN/Chunk.qtn` - `ChunkType` gained `Blacksmith` (append-only;
  Store reuses the pre-existing `Merchant` value/`MarketChunk.prefab` scaffolding, no new ChunkType).
- `Assets/_QuantumUser/Simulation/QTN/CharacterStats.qtn` - `ShopWeaponOfferCount` field.
- `Assets/_QuantumUser/Simulation/QTN/StatusEffects.qtn` - `TempMoveSpeedRemaining`/
  `TempMoveSpeedMultiplier` (backs the Energy Drink food offer; the temp-damage food offer reuses
  the pre-existing `TemporaryWeaponDamageRemaining`/`Amount` unchanged, see `docs/max-ascensions.md`).
- `Assets/_QuantumUser/Simulation/Systems/Poi/StoreUtility.cs` + `StoreSystem.cs`,
  `BlacksmithUtility.cs` + `BlacksmithSystem.cs` - mirror `CursedRiftUtility`/`CursedRiftSystem`'s
  shape.
- `Assets/_QuantumUser/Simulation/Systems/Poi/PoiInteractionLockUtility.cs` (new),
  `WeightedDrawUtility.cs` (new, generic weighted-draw-without-replacement helper used by both
  Blacksmith's perk draw and Store's food draw - deliberately NOT used to unify
  `LevelUpUtility.DrawWeighted`/`CursedRiftUtility.RollSacrificeOptions`, each already has its own
  proven implementation).
- `Assets/_QuantumUser/Simulation/Assets/Config/StoreConfig.cs`, `BlacksmithConfig.cs`
  (+ `BlacksmithBreakTuning`).
- `Assets/_QuantumUser/Simulation/Assets/Store/FoodOfferData.cs` (abstract base, mirrors
  `SacrificeDefinition`) + `HealFoodOfferData.cs`/`RestoreShieldFoodOfferData.cs`/
  `TempMoveSpeedFoodOfferData.cs`/`TempDamageFoodOfferData.cs` + `FoodOfferPoolData.cs`.
  `RestoreShieldFoodOfferData`/`HealFoodOfferData` call `ShieldUtility.ApplyShield`/
  `HealUtility.ApplyHeal` unchanged.
- `Assets/_Project/Scripts/UI/InGame/PurchasableCardState.cs` + `PurchasableCardUi.cs` (new,
  view-layer only).
- `Assets/_Project/Scripts/UI/InGame/UpgradeCardWidget.cs`/`WeaponCardWidget.cs` - `Purchase` field
  + purchase-row serialized fields, `Setup` calls `PurchasableCardUi.Apply`.
- `Assets/_Project/Scripts/UI/InGame/ChooseWindow.cs` - `SetCardFamilyActive` split,
  `onWeaponCardClicked` added, `RefreshStore` added.
- `Assets/_Project/Scripts/UI/InGame/GameplayUiController.cs` - `ChoiceWindowOwner`/`ResolveOwner`/
  `UpdateWindowOwners`, `UpdateCursedRiftWindow` generalized to `UpdatePoiWindow`,
  `RefreshStoreWindow`/`RefreshBlacksmithWindow`/`BuildFoodOfferCardData`/`BuildStoreWeaponCardData`/
  `BuildPerkOfferCardData` added, `BuildWeaponCardData` refactored to take raw fields
  (`AssetRef<WeaponDataAsset>`/`FixedArray<AssetRef<WeaponPerkData>>`/count) instead of a whole
  `LevelUpOption` so Store's own weapon-offer builder reuses it unchanged.
- `Assets/_QuantumUser/Simulation/Systems/Progression/LevelUpUtility.cs` - `RollWeaponOption`/
  `AlreadyEquipped` promoted `private` -> `internal` for the two reuses above (zero behavior change
  otherwise).
- `Assets/_QuantumUser/Simulation/Systems/Director/RunPhaseUtility.cs` -
  `CloseStoreInteractionsOnBreathingEnd`/`CloseBlacksmithInteractionsOnBreathingEnd` (unconditional
  sweeps, called from `CombatDirectorSystem.ApplyPhaseGameState` alongside the existing Cursed Rift
  sweep - simpler than that one since neither POI ever has a "paid, reward still pending" multi-tick
  window, every purchase/pick is one atomic command).
- `Assets/_QuantumUser/Simulation/Default/RuntimeConfig.User.cs` - `StoreConfig`/`BlacksmithConfig`
  fields. `RuntimePlayer.User.cs` - `PlayerTalents.ShopWeaponOfferCount`.
  `Assets/_Project/Scripts/MatchMakingConfig.cs` - `shop_weapon_offer_count` PlayerPrefInt, same
  shape as `weapon_talent_level`/`reroll_quantity`.
  `Assets/_QuantumUser/Simulation/Systems/Player/PlayerSpawnUtility.cs` - seeds
  `CharacterStats.ShopWeaponOfferCount` from the talent at spawn.
- `Assets/_QuantumUser/Simulation/Commands/BuyStoreWeaponCommand.cs`/`BuyStoreFoodCommand.cs`/
  `BuyStoreWeaponLevelCommand.cs`/`CloseStoreCommand.cs`/`SelectBlacksmithPerkCommand.cs`/
  `CancelBlacksmithCommand.cs`.
- `Assets/_QuantumUser/Editor/StoreBlacksmithContentGenerator.cs` (`Tools/RiftRaiders/Generate
  Store & Blacksmith Content`) - authors the 4 `FoodOfferData` instances, `FoodOfferPoolData`,
  `StoreConfig`, `BlacksmithConfig`, and `TalentRarityTuning` (the same decisive-placeholder defaults
  documented above). As of 2026-08-29 no longer authors `BreakWeaponConfig` (deleted) -
  `LevelUpConfig.WeaponOfferCurve`/`WeaponLevelDamageBonusPerLevel` are hand-authored via the
  Inspector instead, same convention every other `LevelUpConfig` field already follows (see
  `LevelUpConfig.cs`'s own class-level defaults). Deliberately does NOT touch `WeaponPool`/`PerkPool`
  (no safe way to locate the right assets), `RuntimeConfig`, hand-placed `EntityPrototype`s, or UI
  prefab wiring - same scope-limit every other generator in this codebase follows.

## Edge cases

- Two players opening the Store simultaneously - non-issue by construction: `StoreInventory` rolls
  lazily once per Break (idempotent re-check), purchase state is fully independent per player
  (`StorePurchases` on each player's own entity).
- Two players at the same Blacksmith simultaneously - each gets their own independently-rolled
  `BlacksmithInteraction` (their own weapon decides eligibility) - no interaction between them.
- Weapon has 0 free perk slots, or 0 eligible perks left, at Blacksmith -
  `ResolveInteractionState` returns `NotNeeded` (reused, not new) - the world prompt shows its
  authored description, press fires `EventContextInteractionRejected` -> toast instead of casting
  Hero Skill, same path Healing Shrine's own full-HP case already established.
- Store/Blacksmith open when a Breathing Break ends - both get an unconditional removal sweep;
  input unlocks the instant the component is removed.
- A real Level-Up for a different player still visually pre-empts this player's own Store/Blacksmith
  window - generalizes for free, `UpdatePoiWindow`'s live-`activeSelf` self-heal (already proven for
  Cursed Rift) covers Store/Blacksmith identically.

## Current status

Short version: the code compiles once codegen picks up every new/changed `.qtn` file (`Store.qtn`,
`Blacksmith.qtn`, `ContextInteraction.qtn`'s new `InteractableKind` values, `Chunk.qtn`'s new
`ChunkType` value, `CharacterStats.qtn`'s `ShopWeaponOfferCount`, `StatusEffects.qtn`'s
`TempMoveSpeedRemaining`/`Multiplier`, `Weapon.qtn`'s `Level` field +
`Store.qtn`'s `StorePurchases.WeaponLevelUpPurchasedAtBreathingIndexPlusOne` (2026-08-18),
`Store.qtn`'s `StoreWeaponOffer.WeaponLevel` (2026-08-19), and, as of 2026-08-29,
`LevelUp.qtn`'s `LevelUpOption.RolledWeaponLevel`), and `SystemSetup.User.cs`/
`CommandSetup.User.cs` register `StoreSystem`/`BlacksmithSystem` and the 6 new commands
(`BuyStoreWeaponLevelCommand` added alongside the original 5). The guaranteed "Increase Weapon
Level" card reuses the exact same food-card UI slot/prefab everything else in this doc's own
"Editor authoring needed" list already covers - no new UI gap introduced, it needs the identical
purchase-row wiring (item 5 below) before it displays anything. As of 2026-08-29, a Store weapon
offer's own Weapon Level/starting perk count are rolled off the shared `LevelUpConfig.
WeaponOfferCurve` (see "Weapon Offer Scaling" above) - no additional Editor authoring needed beyond
what this list already covers, since `LevelUpConfig`'s new fields ship with the same decisive-placeholder
defaults every other config in this doc already has. Not yet run/authored in the Editor:

1. `Tools/RiftRaiders/Generate Store & Blacksmith Content` (authors the plain-data assets) - not yet run.
2. `StoreConfig.WeaponPool`/`BlacksmithConfig.PerkPool` need hand-assigning (point at the same pools
   `LevelUpConfig` already uses, or curated ones) - the generator deliberately leaves these unassigned.
3. `RuntimeConfig.StoreConfig`/`BlacksmithConfig` need assigning on `QuantumMenuConfig.asset`.
4. No `Store`/`Blacksmith` `EntityPrototype` exists yet - `Store` needs hand-placing on
   `MarketChunk.prefab` (`ChunkType.Merchant`, already exists, currently unwired) + a
   `StoreChunkSpawnConfig.asset`; `Blacksmith` needs a new `BlacksmithChunk.prefab`
   (`ChunkType.Blacksmith`) + its own `ChunkSpawnConfig` - both then registered in
   `TestChunkLevel.asset`'s own `ChunkPool`.
5. No purchase-row UI exists on either card prefab yet (`purchaseRoot`/`priceText`/`currencyIcon`/
   `soldOutOverlay`/`buyButton`) - needs building and wiring on `UpgradeCardWidget`'s and
   `WeaponCardWidget`'s own prefabs; `buyButton` should sit wherever the purchase row's own "BUY"
   affordance goes (separate from the card's existing `button`, which purchase cards now hide
   entirely rather than relabel). Also: no `SpriteManager` instance exists in either scene yet
   (`SpriteConfigCurrency.
   asset` at `Assets/_Project/Data/SpriteConfig/` is authored with real Coin/RiftShard/Experience
   sprites, entry names now fixed to match `CurrencyType.ToString()` exactly, but nothing resolves
   it until a `SpriteManager` GameObject with that config assigned is placed in the scene) -
   `CurrencyUiWidget`'s new `icon` field, `PurchasableCardUi`, and `FlyingCurrencyManager` will all
   silently resolve null sprites (logged warnings) until that's done.
6. `ChooseWindow`'s food row (`cards[]`) and weapon row (`weaponCards[]`) still occupy the same
   overlapping rect, toggled mutually exclusive - needs splitting into two visible sections
   (food/utility above, weapons below) for Store's own screen to read correctly; a "CLOSE" label
   variant for `secondaryButton` also needs adding.
7. `MinimapWidget.chunkTypeSprites[]` needs a Blacksmith sprite appended.
8. No `Icon` sprites authored for the 4 `FoodOfferData` assets.

Not yet manually verified end-to-end in-Editor, solo or co-op.
