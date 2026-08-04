namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Dash Ascension (Vendetta Strike) - every OnGoing tick of the dash, sweeps an overlap sphere
    // around the caster's current position and seeds/refreshes a RevengeMark on every enemy caught,
    // same component Vendetta's own passive (MaxVendettaSystem) creates reactively the first time an
    // enemy lands a qualifying hit on the holder - this is just a second, proactive way to grant the
    // same mark instead of waiting to get hit. Mirrors MaxVendettaSystem.TryAccumulate's own
    // MarkedBy-switch/refresh rule (a different holder's old stored damage is discarded, duration
    // resets to the holder's own RevengeConfig.MarkDuration) but never touches StoredDamage upward -
    // a dash-applied mark starts (or stays) at whatever it already had, since this action deals no
    // damage of its own to bank. No-op for anyone without RevengeConfig (hasn't picked up Vendetta),
    // same guard TryAccumulate uses. Enemy sweep shape copied from Pixie's SlowFuseSkillAction.
    public unsafe partial class VendettaStrikeSkillAction : SkillActionData
    {
        public FP Radius = FP._1_50;

        public VendettaStrikeSkillAction()
        {
            Phase = SkillActionPhase.OnGoing;
            Interval = 0;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (f.Unsafe.TryGetPointer<RevengeConfig>(filter.Entity, out var config) == false)
                return;

            Shape3D sphere = Shape3D.CreateSphere(Radius);
            var hits = f.Physics3D.OverlapShape(filter.Transform3D->Position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false || f.Has<Invulnerable>(target) == true)
                    continue;

                f.AddOrGet<RevengeMark>(target, out var mark);

                if (mark->MarkedBy != filter.Entity)
                {
                    mark->MarkedBy = filter.Entity;
                    mark->StoredDamage = FP._0;
                }

                mark->RemainingDuration = config->MarkDuration;
            }
        }
    }
}
