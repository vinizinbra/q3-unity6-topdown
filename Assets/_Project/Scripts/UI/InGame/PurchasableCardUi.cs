using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared static helper applying a PurchasableCardState onto a card's own purchase-affordance UI
// row - one small function instead of duplicating this logic inside both UpgradeCardWidget.Setup
// and WeaponCardWidget.Setup. Structs can't share a base class, so PurchasableCardState rides as a
// plain field on each CardData instead of via inheritance - this is the "shared behavior" half of
// that split.
public static class PurchasableCardUi
{
    // One button, always active/selectable - a purchase card (ShowPurchaseUi) just layers a
    // price/currency/sold-out row (purchaseRoot) on top of it, it doesn't swap in a second Button.
    // The label itself already reads "BUY" for a purchase card via CardData.ButtonLabel (see
    // UpgradeCardWidget/WeaponCardWidget.Setup) - this only owns the purchase row's own visuals.
    public static void Apply(PurchasableCardState state, GameObject purchaseRoot, TMP_Text priceText,
        Image currencyIcon, GameObject soldOutOverlay, ref bool interactable)
    {
        if (purchaseRoot != null)
            purchaseRoot.SetActive(state.ShowPurchaseUi);

        if (state.ShowPurchaseUi == false)
            return;

        if (soldOutOverlay != null)
            soldOutOverlay.SetActive(state.IsSoldOut);

        if (priceText != null)
            priceText.text = state.Price.ToString("0");

        if (currencyIcon != null)
            currencyIcon.sprite = SpriteManager.GetSprite(state.Currency.ToString());

        // Disabled, NOT hidden - an unaffordable or sold-out offer stays visible so co-op players
        // can see what's on offer even if they personally can't (or already did) buy it right now.
        interactable = interactable && state.CanAfford && state.IsSoldOut == false;
    }
}
