namespace Quantum
{
    // Simulation half of the generic Damage Echo's presentation config - deliberately empty on the
    // sim side (nothing here is gameplay-relevant, see DamageEchoUpgrade.Visual's own comment). The
    // actual particle prefab lives in the companion .View.cs partial (DamageEchoVisualData.View.cs),
    // same split QuantumRoundsWeaponPerkData/.View.cs already uses for its own ImpactEffectPrefab -
    // keeps a Unity-only field (ParticleSystem) out of anything the deterministic simulation touches.
    public unsafe partial class DamageEchoVisualData : AssetObject
    {
    }
}
