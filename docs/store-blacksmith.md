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
  Choose-Weapon level-up (distinct weapons, uniform draw without replacement), and each one's perk
  count comes from the EXACT same `LevelUpUtility.RollWeaponOption` formula a Choose-Weapon pick
  uses (promoted `internal` for this reuse) - driven by the TRIGGERING player's own
  `RuntimePlayer.Talents.WeaponLevel` (`StoreUtility.ResolveWeaponLevelTalent`, resolved via that
  entity's own `PlayerLink` -> `f.GetPlayerData`), the same persistent meta-progression stat that
  seeds `CharacterStats.WeaponTalentLevel` at spawn - deliberately NOT the live in-run
  `CharacterStats.WeaponTalentLevel` (which keeps climbing over a run), so Store offers reflect
  account-level weapon mastery, not this run's luck so far. Since `StoreInventory` is rolled ONCE,
  shared across every player, whoever opens the Store first each Break is whose talent sets the
  quality for everyone until the next restock - an accepted consequence of the shared-inventory
  design, not per-player. `Price = WeaponOfferBasePrice + WeaponOfferPricePerPerk * RolledPerkCount`.
- Food/utility offers are a weighted draw (`WeightedDrawUtility`, new generic helper) from
  `StoreConfig.FoodPool`, each priced at its own `FoodOfferData.Price`.
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

- Rolls **per-player** (`BlacksmithInteraction.PerkChoices`), since eligibility depends on the
  buyer's own currently-equipped weapon - unlike Store's shared inventory.
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
  `StoreFoodOffer`/`StoreInteraction`/`StorePurchases`/`StorePurchaseEntry`.
- `Assets/_QuantumUser/Simulation/QTN/Poi/Blacksmith.qtn` - `Blacksmith`/`BlacksmithInteraction`.
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
  `CloseStoreCommand.cs`/`SelectBlacksmithPerkCommand.cs`/`CancelBlacksmithCommand.cs`.
- `Assets/_QuantumUser/Editor/StoreBlacksmithContentGenerator.cs` (`Tools/RiftRaiders/Generate
  Store & Blacksmith Content`) - authors the 4 `FoodOfferData` instances, `FoodOfferPoolData`,
  `StoreConfig`, `BlacksmithConfig`. Deliberately does NOT touch `WeaponPool`/`PerkPool` (no safe way
  to locate the right assets), `RuntimeConfig`, hand-placed `EntityPrototype`s, or UI prefab wiring -
  same scope-limit every other generator in this codebase follows.

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
`TempMoveSpeedRemaining`/`Multiplier`), and `SystemSetup.User.cs`/`CommandSetup.User.cs` register
`StoreSystem`/`BlacksmithSystem` and the 5 new commands. Not yet run/authored in the Editor:

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
