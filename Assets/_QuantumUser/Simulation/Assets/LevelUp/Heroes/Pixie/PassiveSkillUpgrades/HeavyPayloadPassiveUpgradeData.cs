namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - Specialist and stronger enemies create much larger death-explosions, and
    // the base passive's own Filler/Normal-only mark gate (MaxAffectedTier) is raised to also cover
    // Specialist - see MarkExplosiveDeath.qtn's own comments and DamageUtility.
    // TryMarkExplodeOnDeath/TryExplodeOnDeath.
    public unsafe partial class HeavyPayloadPassiveUpgradeData : PassiveUpgradeData
    {
        public FP ExplosionMultiplier = FP._2;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(entity, out var mark) == false)
                return;

            mark->MaxAffectedTier = (byte)EnemyTier.Specialist;
            mark->HeavyPayloadMultiplier = ExplosionMultiplier;
        }
    }
}
