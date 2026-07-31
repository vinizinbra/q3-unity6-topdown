namespace Quantum
{
    using Photon.Deterministic;

    // Shared Apply for any Global Upgrade that just multiplies one CharacterStats field in place -
    // every concrete subtype only names which field. Mirrors WeaponPerkData's individual Apply
    // overrides, but those differ in formula per stat (FireRate divides, Damage multiplies); every
    // entry here is the identical "multiply this field, floor at 0" shape, so one base avoids a
    // dozen copies of the same three lines.
    public abstract unsafe class CharacterStatMultiplierUpgradeData : GlobalUpgradeData
    {
        public FP Multiplier = FP._1;

        protected abstract FP* GetStat(CharacterStats* stats);

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            FP* stat = GetStat(stats);
            *stat = FPMath.Max(FP._0, *stat * Multiplier);
        }

        // Every subtype's Description template references just the one magnitude (e.g. "+{0}%
        // Weapon Damage", "-{0}% Dash Cooldown") - the sign/wording lives in the template itself,
        // this only ever supplies the unsigned percent so it can't drift from a retuned Multiplier.
        protected override object[] DescriptionArgs => new object[] { FPMath.RoundToInt(FPMath.Abs(Multiplier - FP._1) * 100) };
    }
}
