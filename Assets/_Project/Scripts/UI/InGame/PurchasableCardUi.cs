using System;
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
        Image currencyIcon, GameObject soldOutOverlay, ref bool interactable, PurchaseButtonStyle buyButtonStyle = null)
    {
        if (purchaseRoot != null)
            purchaseRoot.SetActive(state.ShowPurchaseUi);

        // Reset every Setup (not just when purchasable) so a reused card slot never keeps a stale gray.
        buyButtonStyle?.Apply(state.ShowPurchaseUi && (state.CanAfford == false || state.IsSoldOut));

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

// Swaps the Buy button's Image to a gray look while the offer can't be bought (unaffordable or sold
// out), restoring the authored sprite/color otherwise. The originals are captured on first use, so
// whatever is authored on the Image in the Inspector stays the "can buy" look.
[Serializable]
public class PurchaseButtonStyle
{
    [Tooltip("The Buy button's background Image (e.g. the green BuyButton). Leave empty to disable the gray swap.")]
    public Image Image;

    [Tooltip("Optional sprite swapped in while unaffordable/sold out. Empty keeps the authored sprite and only applies UnaffordableColor - note a colored sprite tinted gray still reads as a darker version of that color, so a neutral/white sprite here gives a true gray.")]
    public Sprite UnaffordableSprite;

    public Color UnaffordableColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    private bool _captured;
    private Sprite _defaultSprite;
    private Color _defaultColor;

    public void Apply(bool unavailable)
    {
        if (Image == null)
            return;

        if (_captured == false)
        {
            _captured = true;
            _defaultSprite = Image.sprite;
            _defaultColor = Image.color;
        }

        Image.sprite = unavailable && UnaffordableSprite != null ? UnaffordableSprite : _defaultSprite;
        Image.color = unavailable ? UnaffordableColor : _defaultColor;
    }
}
