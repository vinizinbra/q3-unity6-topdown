# Choice Window Refactor

Generalizes the existing Level-Up/Weapon-Upgrade/Chest 3-card upgrade window
(`docs/level-up-upgrades.md`) so Cursed Rift's own offer screen (`docs/breathing-poi.md`) reuses
the exact same card layout, navigation, rarity presentation, animations, and **window instance** -
not a copy of it. Read `docs/level-up-upgrades.md` first if you haven't; this doc only covers what
changed and why.

## History: this went through three designs

The first pass gave Cursed Rift its own, separate `UpgradeWindow` instance per local slot
(`GameplayUiController.poiChoiceWindows[]`), living outside `WindowManager`'s own Canvas subtree,
plus a bespoke 2-button "CONFIRM SACRIFICE? [CONFIRM] [BACK]" sub-panel bolted onto the window
class. Explicitly rejected by the user once built: a second, hand-glued-together window is exactly
the kind of "two near-identical things that must be kept in sync by hand" this codebase has hit
(and fixed) more than once before (see Zara's `PortableSpeaker.asset` drift bug, Pixie's dual-
generator drift - CLAUDE.md's own Ascension write-ups). The confirm sub-panel was also new UI
nothing else in the game was shaped for. Both are gone now - see "What was removed" below.

**Second design**: `UpgradeWindow` was renamed to **`ChooseWindow`** (GUID-preserving rename, same
class, same Inspector-assigned values) and Cursed Rift reused the literal SAME per-slot instance
Level-Up already uses (`GameplayUiController.choiceWindows[]`, one array now, not two) - not a
second copy. Cursed Rift's own flow was two back-to-back uses of this one window: open it showing
Sacrifice cards, click one (which committed immediately - no separate confirm step), it refreshed
in place showing Mutation cards, click one, done. Every screen this window shows - Level-Up,
Weapon-Upgrade, Chest, Cursed Rift Sacrifice, Cursed Rift Mutation - was "one click on a card = one
irreversible pick," the same idiom throughout; Cursed Rift's confirm-panel design was the odd one
out, not the norm.

**Current design (2026-09-11)**: collapsed the two-screen Sacrifice→Mutation flow into a single
card, per the user's own request - two 3-card screens was more friction than the choice was worth,
and the cost lived on a screen the player saw and clicked away from before ever seeing the reward
it paid for. Now `CursedRiftUtility.TryBeginInteraction` rolls exactly one sacrifice AND one
mutation together up front (see `docs/breathing-poi.md`), and `RefreshCursedRiftWindow` shows them
as ONE card: the mutation reward's own Icon/DisplayName/Description/Rarity (via the existing
`BuildCardData`, unchanged), with the sacrifice's own cost folded directly onto that same card via
`ValuePreview` (see "`UpgradeCardWidget.CardData`" below) and its `ButtonLabel` set to "SACRIFICE".
One click on that one card commits both the cost and the reward at once - same idiom as before,
just no longer split across two screens. `allowReroll` stays `false` (a blind roll on both halves,
same as before) and `secondaryButton`/"CANCEL" stays wired exactly as it was, now always
meaningful (nothing is applied until that click, so there's no longer a stage past which Cancel
becomes a no-op).

## The tradeoff this accepts

`WindowManager.ShowWindow<T>()` (`Assets/_Project/Scripts/UI/Common/WindowManager.cs`) hides
**every** registered `UiWindow` not of type `T` and shows **every** registered window of type `T`.
A normal Level Up correctly relies on this - the whole party pauses together
(`SystemDisable<GameplaySystemGroup>` + a `Time.timeScale` ramp), so hiding the HUD and showing
every local slot's `ChooseWindow` at once makes sense. Cursed Rift must not pause anything and must
not touch `Time.timeScale` - `GameplayUiController.UpdateCursedRiftWindow` shows/hides
`choiceWindows[i]` directly per slot instead, with no `WindowManager`/timescale involvement.

Because both flows now drive the SAME window instance, and `choiceWindows[]` still lives under
`WindowManager`'s own Canvas (same as before - it has to, to keep working the way Level-Up already
does), a real Level-Up for a **different** player can visually pre-empt a player's own in-progress
Cursed Rift screen: `WindowManager.ShowWindow<T>()`'s blanket sweep hides every `ChooseWindow`
instance, this interacting player's included, the instant anyone else's Level-Up opens or closes.
**This is an accepted tradeoff, confirmed with the user, not a bug.** Nothing about the
pre-empted player's own `CursedRiftInteraction` is touched by it (that component lives entirely in
the simulation, independent of any View-side window state) - `UpdateCursedRiftWindow` re-shows
their screen (replaying its intro animation - a minor, acceptable visual hiccup) the moment
`Global.LevelUpScreenOpen` goes back to false, picking up exactly where they left off. Their own
movement/weapon/skill stays locked throughout regardless (`CursedRiftUtility.IsInputLocked` keys
off the interaction component's presence, not window visibility) - and in practice, a real Level-Up
already disables `GameplaySystemGroup` for everyone, including `CursedRiftSystem`, so this player's
own commands would stop processing for that same window anyway; the window pre-emption isn't
introducing a new kind of interruption, just extending one that already existed.

## `ChooseWindow` - what's generic vs. what was removed

Additions that stayed, still reproducing the exact original Level-Up visuals when unused so every
existing call site is unaffected:

- `subtitleText` (optional `TMP_Text`) + `Refresh`/`RefreshWeaponChoice`'s `string subtitle = null`
  param - `null` leaves it untouched (only Cursed Rift ever passes a real value, e.g. "SACRIFICE
  FOR A MUTATION").
- **`secondaryButton`** (optional `Button`, `[FormerlySerializedAs("keepCurrentButton")]`) + ONE
  event `onSecondaryButtonClicked` - reused for two mutually-exclusive "decline this screen"
  actions that can never both apply at once (`RefreshWeaponChoice` vs. `Refresh`'s `allowCancel`
  drive two different card families, only one active at a time): "KEEP CURRENT" on a Choose-Weapon
  screen (`RefreshWeaponChoice` always shows it) or "CANCEL" on Cursed Rift's own screen
  (`Refresh`'s `bool allowCancel = false` param - the one place walking away without paying
  anything needs to be possible). Was originally two separate fields/buttons/events
  (`cancelButton`+`onCancelClicked` alongside the pre-existing `keepCurrentButton`+
  `onKeepCurrentClicked`) - merged into one once the user pointed out Choose-Weapon already had a
  working decline button that Cursed Rift could just reuse instead of building a second one.
  `ChooseWindow` sets the button's label text (resolved once via
  `GetComponentInChildren<TMP_Text>()` in `Awake`, no extra Inspector field needed) each time it's
  shown; `GameplayUiController.OnSecondaryButtonClicked` reads the same live
  `Global.LevelUpScreenOpen` check `OnCardClicked` uses to decide which command a click actually
  means (`KeepCurrentWeaponCommand` vs. `CancelCursedRiftCommand`) - a plain Level-Up never shows
  this button at all, so there's no third case.
- `Refresh`'s `bool allowReroll = true` param - unlike `secondaryButton`/`allowCancel`, this one
  needed a THIRD state: reroll is valid on a plain Level-Up and on Choose-Weapon
  (`RefreshWeaponChoice` always passes it true), but not on Cursed Rift's own screen
  (`RefreshCursedRiftWindow` always passes `allowReroll: false` - `allowCancel` alone can't
  express this, since Cursed Rift's own screen sets that true too). `SetRerollButtonActive`
  toggles `rerollButton`/`rerollChargesText` visibility - the pre-existing
  `UpdateRerollButton(charges, confirmed)` only ever set `.interactable`/`.text`, never visibility,
  so without this the reroll button/charge count would sit there, visible (and possibly still
  clickable) with stale content, throughout a Cursed Rift screen.

**What was removed** (the confirm sub-panel from the first design): `confirmPanel`/`confirmButton`/
`backButton`/`confirmRecapText` fields, `ShowConfirmPanel`/`HideConfirmPanel` methods,
`onConfirmClicked`/`onBackClicked` events - all gone. Clicking the (single, see "Current design"
above) card now applies the sacrifice's cost and grants the mutation reward in the same simulation
call (`CursedRiftUtility.Confirm` - see `docs/breathing-poi.md`), so there was never anything left
for a confirm panel to gate.

## `UpgradeCardWidget.CardData` - additive only, unchanged by this pass

Three fields, all empty-default, still Cursed-Rift-only in practice:

- `string TopLabelOverride` - non-empty replaces the rarity-sprite/label readout verbatim instead
  of the `RarityIndex` lookup. NOT used by Cursed Rift's own card anymore (its card is the
  mutation reward, which DOES have a Rarity - see "Current design" above) - still used by Store's
  food/utility/accessory-service cards (e.g. "FOOD"/"REPAIR").
- `string ValuePreview` - live before→after text, shown in its own row only when non-empty.
  Cursed Rift's card uses this for the rolled sacrifice's own cost (e.g. "COST: Blood
  Offering\nHP 100 -> 80") - see `BuildCursedRiftOfferCardData`. No plain Level-Up/Mutation card
  uses this.
- `string ButtonLabel` - overrides the card's baked button text (e.g. "SACRIFICE"/"PAY" instead of
  "CHOOSE"). Empty leaves the prefab's own authored label.

## `GameplayUiController` - one array, two drivers, precedence by live state

`choiceWindows[]` (renamed from `upgradeWindows[]`, `[FormerlySerializedAs]` preserves the scene's
existing assignment) is read by both `UpdateUpgradeScreen` (Level-Up, unchanged from before - still
gates on `Global.LevelUpScreenOpen`, still ramps `Time.timeScale`, still calls
`windowManager.ShowWindow<ChooseWindow>()`) and `UpdatePoiWindow` (Cursed Rift/Store/Blacksmith,
called right after it every `QUpdate`). Neither method tracks a separate "who owns this slot" flag
- both derive precedence from the same live Quantum state every tick:

- `UpdatePoiWindow` opens with `if (frame.Global->LevelUpScreenOpen == true) return;` - steps
  aside entirely for every slot whenever a real Level-Up is open anywhere, since
  `UpdateUpgradeScreen`'s own `WindowManager` sweep already owns every instance for that duration.
- Show/hide decisions check the window's own **live** `gameObject.activeSelf`, not a
  separately-tracked bool - self-healing regardless of what else touched it (see "the tradeoff"
  above): if `WindowManager` externally hid a slot's window mid-Cursed-Rift, the next tick this
  slot's `CursedRiftInteraction` is still present, `activeSelf` is still false, so it gets shown
  again automatically.

Per slot, per tick, once `UpdatePoiWindow` is actually running for a Cursed Rift slot:

```
no CursedRiftInteraction -> Hide() only if still actually showing (activeSelf == true)
has one                  -> Show() only if not already showing, then RefreshCursedRiftWindow()
                             every tick:

  Refresh("CURSED RIFT", ..., subtitle: "SACRIFICE FOR A MUTATION", allowCancel: true,
          allowReroll: false)
  cardData = [ BuildCursedRiftOfferCardData(...) ] - a single-entry array (Refresh already leaves
  every other cards[] slot at its default/empty state). Built from the EXISTING BuildCardData
  (internal, zero logic change) for the mutation reward's own Icon/DisplayName/Description/Rarity
  - CursedRiftInteraction.Mutation is the exact same LevelUpOption shape one LevelUpChoice.Options
  entry already is - then the rolled Sacrifice's own DisplayName + a LIVE BuildValuePreview(frame,
  entity) call (never cached, so it can't go stale between roll and click) are folded onto that
  same CardData's ValuePreview/ButtonLabel.
```

A card click is routed by `GameplayUiController.OnCardClicked` (the SAME handler wired to
`choiceWindows[i].onCardClicked` regardless of which flow is currently showing) - it reads
`_windowOwner[slotIndex]` and, for Cursed Rift, always sends `ConfirmCursedRiftCommand` (the
single card carries no other meaning - see `docs/breathing-poi.md`). `onRerollClicked`/
`onKeepCurrentClicked` stay wired to their original Level-Up-only handlers unchanged - Cursed Rift
never enables either.

## Currency (per-player Coins/Rift Shards) - a related, explicitly-requested change

Confirmed with the user: Coins and Rift Shards moved from shared `Frame.Global` totals to
**per-player wallets** (`CharacterStats.Coins`/`RiftShards`) so a Cursed Rift Coin/Rift Shard
sacrifice is a meaningful individual choice, not a party-wide tax. A pickup now credits every
connected player the same base amount, each scaled by *their own* gain multiplier
(`CoinUtility.GrantAll`/`RiftShardUtility.GrantAll`, called from `CurrencyOrbSystem`) rather than
crediting a shared pool from whoever physically walked over the orb. `CurrencyUiWidget` now
self-binds to the local player's own entity for Coin/RiftShard (same `MyLocalPlayer.Instance
.BindToSlot` pattern `SkillCooldownUiWidget` already uses) - Experience is untouched, still a
single shared `Frame.Global` total. See `docs/global-upgrades.md`'s "Economy" section for the
currency system itself, `docs/breathing-poi.md` for the Sacrifice system that motivated this.

## What did NOT change

Every existing Level-Up/Weapon-Upgrade/Chest call site passes the same arguments it always did -
card generation, rarity, rerolls, Choose-Weapon/Keep-Current, `Time.timeScale` ramp, `WindowManager`
routing, and the whole-party `GameplaySystemGroup` pause are all unchanged. This refactor still adds
scaffolding Cursed Rift builds on top of, not a rewrite of Level-Up itself - only the INSTANCE
Cursed Rift renders through changed (now shared, was briefly a duplicate).

## Editor authoring needed

1. **`choiceWindows[0]`** (the existing, already-wired Level-Up instance) still needs one new field
   built and assigned: `subtitleText` (a `TMP_Text`) - doesn't exist in the scene yet.
   `secondaryButton` needs NOTHING new - it's the same already-wired `keepCurrentButton` GameObject
   from before (`[FormerlySerializedAs]` carried the existing assignment over), now also used for
   "CANCEL". No second window/Canvas to build anymore.
2. **The card template's `valuePreviewText`/`buttonLabelText`** - also unassigned on the scene's
   `cardPrefab` instance. `buttonLabelText` can just point at the card's own existing baked button
   label text (no new element needed there); `valuePreviewText` needs one new `TMP_Text` row.
3. **`countdownText`** - shared by both flows now (same instance); Cursed Rift's own screen
   doesn't have a per-interaction timer of its own (the Breathing countdown is shown separately,
   always-visible - see `docs/run-phase.md`), so `RefreshCursedRiftWindow` passes `0f` for
   `timeRemaining` - `Mathf.CeilToInt(Mathf.Max(0f, 0f))` renders as "0", worth double-checking in
   Editor whether that should just be left blank/hidden during a Cursed Rift screen instead.
4. **Manual regression test not yet run**: a normal Level Up, a Weapon-Upgrade level, and an
   existing Chest still roll/display/reroll/confirm exactly as before, AND a couch-co-op collision
   (one player mid-Cursed-Rift while another levels up) behaves as documented above (screen
   pre-empted, then resumes) - the riskiest acceptance criteria this refactor touches.
