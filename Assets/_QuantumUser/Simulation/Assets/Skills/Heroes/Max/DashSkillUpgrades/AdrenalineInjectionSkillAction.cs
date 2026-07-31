namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Adrenaline Injection) - dashing grants Adrenaline; dashing through enemies
    // grants additional stacks. The "through enemies" half is checked once at the dash's end
    // position (not a literal swept-path test along the whole route) - a simplification, same
    // shape Kai's SlowArea/Reflect already accept elsewhere in this roster, chosen to avoid a
    // point-to-segment distance check for a bonus that's already generous either way.
    public unsafe partial class AdrenalineInjectionSkillAction : SkillActionData
    {
        public byte StacksOnDash = 2;
        public byte StacksPerEnemyHit = 1;
        public FP HitRadius = 2;

        public AdrenalineInjectionSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        protected override object[] DescriptionArgs => new object[] { StacksOnDash, StacksPerEnemyHit };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                AddStacks(f, filter.Entity, StacksOnDash);
                return;
            }

            FPVector3 position = filter.Transform3D->Position;
            var enemies = f.Filter<Enemy, Transform3D>();

            while (enemies.Next(out EntityRef enemyEntity, out Enemy _, out Transform3D enemyTransform))
            {
                if ((enemyTransform.Position - position).SqrMagnitude > HitRadius * HitRadius)
                    continue;

                AddStacks(f, filter.Entity, StacksPerEnemyHit);
            }
        }

        private static void AddStacks(Frame f, EntityRef entity, byte amount)
        {
            if (amount == 0 || f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return;

            int sum = adrenaline->Stacks + amount;
            adrenaline->Stacks = (byte)(sum > adrenaline->MaxStacks ? adrenaline->MaxStacks : sum);
            adrenaline->TimeSinceLastGain = FP._0;
        }
    }
}
