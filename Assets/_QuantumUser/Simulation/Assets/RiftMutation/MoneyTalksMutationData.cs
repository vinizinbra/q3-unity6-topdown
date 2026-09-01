namespace Quantum
{
    using Photon.Deterministic;

    // Your wallet is your weapon: outgoing damage scales with the Coins you are carrying RIGHT NOW,
    // which turns every Store purchase into a genuine tradeoff - buying a weapon upgrade or an
    // Accessory repair also costs you damage until you earn the balance back.
    //
    // That live coupling is the whole mutation, and it is why this bakes a RULE onto CharacterStats
    // rather than a number: the bonus is resolved per hit by CoinUtility.ResolveDamageBonus (called
    // from DamageUtility.ResolveOutgoingDamage), so it tracks the balance continuously. A one-shot
    // bake at pick time would freeze it at whatever the player happened to be holding that instant,
    // which is the opposite of the intent.
    //
    // Scales ALL damage rather than a Weapon/Skill slice, so it composes with every build.
    public unsafe class MoneyTalksMutationData : RiftMutationData
    {
        public FP DamagePerHundredCoins = FP._0;
        public FP MaxDamageBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            // Take-the-stronger rather than additive, so a second source of the same rule can't
            // quietly stack into a far larger cap than either intended.
            stats->CoinDamagePerHundred = FPMath.Max(stats->CoinDamagePerHundred, DamagePerHundredCoins);
            stats->CoinDamageMaxBonus = FPMath.Max(stats->CoinDamageMaxBonus, MaxDamageBonus);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            DamagePerHundredCoins.AsFloat * 100f,
            CoinUtility.CoinsPerDamageStep,
            MaxDamageBonus.AsFloat * 100f
        };
    }
}
