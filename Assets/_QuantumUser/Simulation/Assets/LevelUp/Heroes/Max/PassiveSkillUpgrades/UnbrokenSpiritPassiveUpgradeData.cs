namespace Quantum
{
    // Vendetta Upgrade - Shield damage from the current attacker also counts toward the Vendetta
    // mark, not just Health damage. MaxVendettaSystem.OnShieldDamageApplied already gates on this
    // tag's presence (see Vendetta.qtn's own comment on ShieldDamageCountsForRevenge) - this is
    // the only thing that ever grants it.
    public unsafe partial class UnbrokenSpiritPassiveUpgradeData : PassiveUpgradeData
    {
        public override void Apply(Frame f, EntityRef entity) => f.AddOrGet<ShieldDamageCountsForRevenge>(entity, out _);
    }
}
