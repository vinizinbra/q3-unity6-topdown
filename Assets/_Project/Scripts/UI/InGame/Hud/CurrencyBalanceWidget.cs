using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "YOUR COINS  4,820" readout for a Choose window that spends a currency (Store, Blacksmith, and the
// future Black Market, which pays in Rift Shards). Pure view: ChooseWindow.SetCurrencyBalance pushes
// the currency + amount every tick, so one instance serves any currency - the icon comes from
// SpriteManager (same name-keyed lookup CurrencyUiWidget/PurchasableCardUi use) and the label from
// labelFormat, so adding a currency is one entry in LabelFor, not a new widget.
public class CurrencyBalanceWidget : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField, Tooltip("Small caption, e.g. \"YOUR COINS\". Optional.")]
    private TMP_Text labelText;
    [SerializeField] private TMP_Text amountText;

    [Header("Punch on change")]
    [SerializeField] private Vector3 punchStrength = new Vector3(0.2f, 0.2f, 0f);
    [SerializeField] private float punchDuration = 0.3f;
    [SerializeField] private float punchFrequency = 12f;

    private CurrencyType? _currency;
    private int _amount = int.MinValue;
    private Tween _punchTween;
    private Vector3 _restScale;
    private bool _restCaptured;

    public void Setup(CurrencyType currency, int amount)
    {
        if (gameObject.activeSelf == false)
            gameObject.SetActive(true);

        if (_currency != currency)
        {
            _currency = currency;
            _amount = int.MinValue;

            // Keeps the authored sprite (the Coin art) if SpriteManager has nothing for this currency.
            Sprite sprite = SpriteManager.Instance != null ? SpriteManager.GetSprite(currency.ToString()) : null;
            if (icon != null && sprite != null)
                icon.sprite = sprite;

            if (labelText != null)
                labelText.text = LabelFor(currency);
        }

        if (amount == _amount)
            return;

        // First value after (re)opening just appears; only a change while open punches.
        bool changed = _amount != int.MinValue;
        _amount = amount;

        if (amountText != null)
            amountText.text = amount.ToString("N0");

        if (changed)
            PlayPunch();
    }

    public void Hide()
    {
        _punchTween.Stop();
        _currency = null;
        _amount = int.MinValue;

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    private static string LabelFor(CurrencyType currency)
    {
        return currency switch
        {
            CurrencyType.Coin => "YOUR COINS",
            CurrencyType.RiftShard => "YOUR RIFT SHARDS",
            CurrencyType.Scrap => "YOUR SCRAP",
            _ => "YOUR " + currency.ToString().ToUpperInvariant(),
        };
    }

    private void PlayPunch()
    {
        if (amountText == null)
            return;

        Transform target = amountText.transform;

        if (_restCaptured == false)
        {
            _restScale = target.localScale;
            _restCaptured = true;
        }

        // Back to rest first so rapid changes punch from rest instead of compounding.
        _punchTween.Stop();
        target.localScale = _restScale;
        _punchTween = Tween.PunchScale(target, punchStrength, punchDuration, punchFrequency, useUnscaledTime: true);
    }

    private void OnDisable()
    {
        _punchTween.Stop();

        if (_restCaptured && amountText != null)
            amountText.transform.localScale = _restScale;
    }
}
