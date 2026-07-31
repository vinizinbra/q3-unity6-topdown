namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Healing Step) - heal nearby allies at the dash destination. Direct
    // HealUtility.ApplyHeal call, same shape Brute's Bodyguard/Lux's dash actions already use for
    // their own "restore % at destination" effects.
    public unsafe partial class HealingStepSkillAction : SkillActionData
    {
        public FP Radius = 5;
        public FP HealPercent = FP._0_10;

        public HealingStepSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        protected override object[] DescriptionArgs => new object[] { HealPercent * 100 };

        public override FP EffectRadius => Radius;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            FPVector3 position = filter.Transform3D->Position;
            var allies = EnemyMovementUtility.FindPlayersInRadius(f, position, Radius);

            for (int i = 0; i < allies.Count; i++)
            {
                HealUtility.ApplyHeal(f, allies[i].Entity, filter.Entity, HealPercent);
            }
        }
    }
}
