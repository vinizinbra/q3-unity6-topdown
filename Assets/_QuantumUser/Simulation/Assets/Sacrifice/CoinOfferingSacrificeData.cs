namespace Quantum
{
    using Photon.Deterministic;

    // Spends a percentage of the sacrificing player's OWN Coin wallet (per-player since the
    // currency conversion - see docs/breathing-poi.md/CharacterStats.Coins) - only that player is
    // affected, not the whole party.
    public unsafe class CoinOfferingSacrificeData : SacrificeDefinition
    {
        public FP CoinPercent = FP._0_50;

        public override bool IsEligible(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true && stats->Coins > FP._0;
        }

        public override void ApplyCost(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            CoinUtility.TrySpend(f, entity, stats->Coins * CoinPercent);
        }

        public override string BuildValuePreview(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return string.Empty;

            FP after = stats->Coins - (stats->Coins * CoinPercent);

            return $"COINS\n{stats->Coins.AsFloat:0} -> {after.AsFloat:0}";
        }
    }
}
