namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Bodyguard) - allies near the dash destination recover a fraction of their max
    // Shield. Same "restore % of Max, clamped" shape Lux's RepairNearbyMachinesSkillAction/
    // PortableCoverSkillAction already use for machines - here applied to nearby players instead.
    // Includes Brute himself if he ends the dash within his own Radius of himself (trivially true) -
    // a self-heal on top of the ally heal, not excluded as a bug.
    public unsafe partial class BodyguardSkillAction : SkillActionData
    {
        public FP Radius = 6;
        public FP ShieldRestoreFraction = FP._0_25;

        public BodyguardSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        protected override object[] DescriptionArgs => new object[] { ShieldRestoreFraction * 100 };

        public override FP EffectRadius => Radius;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            FPVector3 position = filter.Transform3D->Position;
            var allies = EnemyMovementUtility.FindPlayersInRadius(f, position, Radius);

            for (int i = 0; i < allies.Count; i++)
            {
                ShieldUtility.ApplyShield(f, allies[i].Entity, filter.Entity, ShieldRestoreFraction);
            }
        }
    }
}
