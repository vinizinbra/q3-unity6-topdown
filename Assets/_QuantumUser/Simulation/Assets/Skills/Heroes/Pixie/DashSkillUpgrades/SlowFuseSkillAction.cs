namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Dash Ascension (Slow Fuse) - every OnGoing tick of the dash, sweeps an overlap sphere around
    // Pixie's current position and marks every enemy caught with the same ExplodeOnDeath tag
    // DamageUtility.TryMarkExplodeOnDeath grants (see ExplodeOnDeath.qtn/ExplodeOnDeathTimerSystem),
    // but at this action's own Duration instead of the shared ExplodeOnDeathConfig's - a long fuse
    // that detonates on death regardless of what else hits the enemy in the meantime. Independent of
    // MarkExplosiveDeath/Chain Reaction entirely - a dash never routes through
    // DamageUtility.ApplyDamage, so this marks directly rather than needing RequiresExplosion
    // satisfied.
    public unsafe partial class SlowFuseSkillAction : SkillActionData
    {
        public FP Radius = FP._1_50;
        public FP Duration = 999;

        public SlowFuseSkillAction()
        {
            Phase = SkillActionPhase.OnGoing;
            Interval = 0;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            Shape3D sphere = Shape3D.CreateSphere(Radius);
            var hits = f.Physics3D.OverlapShape(filter.Transform3D->Position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                f.AddOrGet<ExplodeOnDeath>(target, out var explode);
                explode->Remaining = Duration;
            }
        }
    }
}
