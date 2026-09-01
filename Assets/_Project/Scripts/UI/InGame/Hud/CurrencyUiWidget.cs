using Photon.Deterministic;
using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD readout for one of the run-wide/per-player currencies (see CurrencyType). One reusable
// widget parameterized by `currency` rather than one near-duplicate class per currency - drop an
// instance per currency in the scene, each with its own text. The icon is resolved once at Start
// via SpriteManager.GetSprite(currency.ToString()) - a shared name-keyed sprite table
// (SpriteConfigCurrency) instead of each instance needing its own sprite hand-dragged in, same
// lookup FlyingCurrencyManager/PurchasableCardUi now use for the same currencies.
//
// Experience stays a single shared Frame.Global total, shown with no presence check needed (same
// as before). Coin/RiftShard are now PER-PLAYER wallets (CharacterStats.Coins/RiftShards, see
// docs/breathing-poi.md) - this widget self-binds to the local player's own entity for those two,
// same MyLocalPlayer.Instance.BindToSlot pattern SkillCooldownUiWidget/ShieldUiWidget already use,
// so a Coin/RiftShard instance shows THIS local player's own balance, not a shared party total.
public class CurrencyUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private CurrencyType currency;
    [SerializeField] private TMP_Text valueText;
    [SerializeField, Tooltip("Optional - sprite resolved from SpriteManager at Start via currency.ToString(). Leave unassigned if this instance's icon is baked into the prefab art instead.")]
    private Image icon;

    [Header("Punch on value change")]
    [SerializeField, Tooltip("Punched whenever the displayed total changes - defaults to valueText's own transform if left unassigned.")]
    private Transform punchTarget;
    [SerializeField] private Vector3 punchStrength = new Vector3(0.25f, 0.25f, 0f);
    [SerializeField] private float punchDuration = 0.3f;
    [SerializeField] private float punchFrequency = 12f;

    [SerializeField, Tooltip("Coin/RiftShard only (Experience ignores this and stays shared) - which local player slot this instance shows. On: binds itself to that slot automatically. Off: stays unbound until something else calls Initialize (e.g. a future party HUD).")]
    private bool autoBindLocalSlot = true;
    [SerializeField, Tooltip("Local slot index to bind to when autoBindLocalSlot is on - 0 for player 1, 1 for a second local (couch co-op) player.")]
    private int localSlotIndex;

    private EntityRef _entityRef;
    private FP? _lastTotal;
    private Tween _punchTween;
    private Vector3 _restScale;

    private void Start()
    {
        _restScale = (punchTarget != null ? punchTarget : (valueText != null ? valueText.transform : transform)).localScale;

        if (icon != null)
            icon.sprite = SpriteManager.GetSprite(currency.ToString());

        if (autoBindLocalSlot && currency != CurrencyType.Experience)
            MyLocalPlayer.Instance.BindToSlot(localSlotIndex, Initialize);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    // Called externally (e.g. a future party HUD) so an externally-driven instance never fights
    // its own default self-binding - same convention SkillCooldownUiWidget.DisableAutoBind uses.
    public void DisableAutoBind()
    {
        autoBindLocalSlot = false;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        if (currency != CurrencyType.Experience && _entityRef == EntityRef.None)
            return; // not bound to a local player entity yet

        if (TryResolveTotal(frame, currency, _entityRef, out FP total) == false)
            return;

        if (valueText != null)
            valueText.text = Mathf.FloorToInt(total.AsFloat).ToString();

        // Skip the very first read (_lastTotal starts unset) so loading into a run with an
        // already-nonzero total doesn't punch on frame one.
        if (_lastTotal.HasValue && total != _lastTotal.Value)
            PlayPunch();

        _lastTotal = total;
    }

    private static unsafe bool TryResolveTotal(Frame frame, CurrencyType currency, EntityRef entity, out FP total)
    {
        switch (currency)
        {
            case CurrencyType.Experience:
                total = frame.Global->TotalExperience;
                return true;

            case CurrencyType.Coin:
                if (frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var coinStats) == false)
                {
                    total = FP._0;
                    return false;
                }

                total = coinStats->Coins;
                return true;

            case CurrencyType.RiftShard:
                if (frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var shardStats) == false)
                {
                    total = FP._0;
                    return false;
                }

                total = shardStats->RiftShards;
                return true;

            default:
                total = FP._0;
                return false;
        }
    }

    private void PlayPunch()
    {
        Transform target = punchTarget != null ? punchTarget : (valueText != null ? valueText.transform : transform);

        // Reset to the authored rest scale before punching again - Tween.PunchScale punches
        // relative to whatever scale is CURRENT when it's called, so without this a punch that
        // lands before the previous one finished decaying compounds on top of it instead of
        // punching from rest, growing unbounded when several pickups land in quick succession
        // (see JuicyEffects.PlayPunchScale for the same reset-before-punch idiom).
        _punchTween.Stop();
        target.localScale = _restScale;
        _punchTween = Tween.PunchScale(target, punchStrength, punchDuration, punchFrequency, useUnscaledTime: true);
    }
}
