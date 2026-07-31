namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Afterbeat) - after a short delay, creates a damaging pulse at the dash's
    // starting position. Stores the countdown directly on Zara's own entity (see ZaraAfterbeat.qtn/
    // ZaraAfterbeatSystem) rather than spawning a marker entity - no EntityPrototype authoring
    // needed, unlike the other heroes' remaining pending dash ascensions.
    public unsafe partial class AfterbeatSkillAction : SkillActionData
    {
        public FP Delay = FP._1;
        public FP Damage = 20;
        public FP Radius = 4;

        public AfterbeatSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        protected override object[] DescriptionArgs => new object[] { Delay, Damage };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<ZaraAfterbeat>(filter.Entity, out var afterbeat);
            afterbeat->Remaining = Delay;
            afterbeat->Position = slot->StartPosition;
            afterbeat->Damage = Damage;
            afterbeat->Radius = Radius;
        }
    }
}
