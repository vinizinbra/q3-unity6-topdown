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
    // chooseButton/buyButton are mutually exclusive - a purchase card (ShowPurchaseUi) hides the
    // card's normal "CHOOSE" button entirely and shows a dedicated Buy button in its place, rather
    // than just relabeling the same button; every non-purchase card (Level-Up/Choose-Weapon/Cursed
    // Rift) keeps showing chooseButton exactly as before, buyButton never appears. Both fire the
    // SAME onClicked event (wired in UpgradeCardWidget/WeaponCardWidget's own Awake) - the
    // downstream command dispatch (GameplayUiController) doesn't care which literal button was
    // clicked, only which card/slot.
    public static void Apply(PurchasableCardState state, GameObject purchaseRoot, TMP_Text priceText,
        Image currencyIcon, GameObject soldOutOverlay, Button chooseButton, Button buyButton, ref bool interactable)
    {
        if (purchaseRoot != null)
            purchaseRoot.SetActive(state.ShowPurchaseUi);

        if (chooseButton != null)
            chooseButton.gameObject.SetActive(state.ShowPurchaseUi == false);

        if (buyButton != null)
            buyButton.gameObject.SetActive(state.ShowPurchaseUi);

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
