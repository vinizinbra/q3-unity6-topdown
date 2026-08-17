using System;

// Opt-in purchase affordance shared by UpgradeCardWidget/WeaponCardWidget - Store's weapon/food
// cards and Blacksmith's perk cards all reuse this instead of each card widget growing its own
// separate price/afford/sold-out fields. See docs/store-blacksmith.md's "reuse existing UI
// primitives" requirement.
[Serializable]
public struct PurchasableCardState
{
    // False by default - the entire opt-in gate. Every existing non-purchase caller (Level-Up,
    // Choose-Weapon, Cursed Rift) never sets this, so nothing about their rendering changes at all.
    public bool ShowPurchaseUi;

    public float Price;

    // Which currency this price is in - resolved to a sprite via SpriteManager.GetSprite(Currency.
    // ToString()) at render time (see PurchasableCardUi.Apply), same "look up by name in a shared
    // SpriteConfigSO" idiom the rest of the UI is consolidating onto instead of each widget carrying
    // its own duplicate sprite array/switch.
    public CurrencyType Currency;

    // Read live off the buyer's own CharacterStats.Coins every refresh, never cached - a second
    // player's purchase (or this player's own previous purchase) can change this from one tick to
    // the next.
    public bool CanAfford;

    public bool IsSoldOut;
}
