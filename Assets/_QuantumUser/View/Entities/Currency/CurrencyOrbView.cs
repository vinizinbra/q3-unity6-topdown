using Photon.Deterministic;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Generic "3 visual tiers by value" presentation for ANY currency orb (Coin/RiftShard/Exp all
    // share the one CurrencyOrb component - see CurrencyOrb.qtn) - reads CurrencyOrb.Value once at
    // spawn and shows exactly one of lowValueVisual/midValueVisual/highValueVisual, so a big drop
    // reads as visually bigger than a small one. Value never changes after spawn (set once from
    // EnemyTierStatsConfig, see CoinUtility/RiftShardUtility/ExperienceUtility), so this resolves
    // once in Initialize rather than polling every tick like PoiView's State does.
    public class CurrencyOrbView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Shown while Value < mediumValueThreshold.")]
        private GameObject lowValueVisual;

        [SerializeField, Tooltip("Shown while mediumValueThreshold <= Value < highValueThreshold.")]
        private GameObject midValueVisual;

        [SerializeField, Tooltip("Shown while Value >= highValueThreshold.")]
        private GameObject highValueVisual;

        [SerializeField, Tooltip("Below this, lowValueVisual shows. Tune per-prefab - Coin/RiftShard/Exp values scale very differently.")]
        private FP mediumValueThreshold = 5;

        [SerializeField, Tooltip("At or above this, highValueVisual shows.")]
        private FP highValueThreshold = 10;

        public override unsafe void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            if (game.Frames.Verified.Unsafe.TryGetPointer<CurrencyOrb>(_entityRef, out var currencyOrb) == false)
                return;

            FP value = currencyOrb->Value;

            bool isMid = value >= mediumValueThreshold;
            bool isHigh = value >= highValueThreshold;

            SetShown(lowValueVisual, isMid == false);
            SetShown(midValueVisual, isMid && isHigh == false);
            SetShown(highValueVisual, isHigh);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        private static void SetShown(GameObject go, bool shown)
        {
            if (go != null)
                go.SetActive(shown);
        }
    }
}
