using Photon.Deterministic;
using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// HUD readout for one of the run-wide currencies (see CurrencyType). One reusable widget
// parameterized by `currency` rather than one near-duplicate class per currency - drop an instance
// per currency in the scene, each with its own icon/text. The icon itself needs no field/code at
// all - same as ScrapUiWidget's own fixed icon, it's just a static child Image with its sprite
// dragged in directly per-instance (no Coin/RiftShard icon asset exists yet to read at runtime -
// neither CoinConfig nor RiftShardConfig has an Icon field). Always-visible, no presence check
// needed - unlike a per-entity gauge (Scrap, Adrenaline), Frame.Global.TotalCoins/TotalRiftShards/
// TotalExperience exist for the whole run regardless of hero/upgrades. Experience is included here
// too even though ExpBarUiWidget already shows it as a bar - CurrencyType is shared with
// FlyingCurrencyManager, which needs all three values.
public class CurrencyUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private CurrencyType currency;
    [SerializeField] private TMP_Text valueText;

    [Header("Punch on value change")]
    [SerializeField, Tooltip("Punched whenever the displayed total changes - defaults to valueText's own transform if left unassigned.")]
    private Transform punchTarget;
    [SerializeField] private Vector3 punchStrength = new Vector3(0.25f, 0.25f, 0f);
    [SerializeField] private float punchDuration = 0.3f;
    [SerializeField] private float punchFrequency = 12f;

    private FP? _lastTotal;
    private Tween _punchTween;

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;
        FP total = ResolveTotal(frame, currency);

        if (valueText != null)
            valueText.text = Mathf.FloorToInt(total.AsFloat).ToString();

        // Skip the very first read (_lastTotal starts unset) so loading into a run with an
        // already-nonzero total doesn't punch on frame one.
        if (_lastTotal.HasValue && total != _lastTotal.Value)
            PlayPunch();

        _lastTotal = total;
    }

    private static unsafe FP ResolveTotal(Frame frame, CurrencyType currency)
    {
        switch (currency)
        {
            case CurrencyType.Experience: return frame.Global->TotalExperience;
            case CurrencyType.Coin: return frame.Global->TotalCoins;
            case CurrencyType.RiftShard: return frame.Global->TotalRiftShards;
            default: return FP._0;
        }
    }

    private void PlayPunch()
    {
        Transform target = punchTarget != null ? punchTarget : (valueText != null ? valueText.transform : transform);

        _punchTween.Stop();
        _punchTween = Tween.PunchScale(target, punchStrength, punchDuration, punchFrequency, useUnscaledTime: true);
    }
}
