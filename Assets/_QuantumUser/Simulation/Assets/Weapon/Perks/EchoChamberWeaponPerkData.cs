namespace Quantum
{
    using Photon.Deterministic;

    // Queues a repeat of each of the first 3 shots of every magazine (see
    // WeaponSystem.EnqueueEcho/TickPendingEchoes) - EchoDelay is shared with Infinite Echo (whichever
    // equipped perk asks for the longer delay wins, so combining them can't make echoes fire faster
    // than either alone intends).
    public unsafe class EchoChamberWeaponPerkData : WeaponPerkData
    {
        public FP Delay = FP._0_50;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponEchoState>(owner, out var echo);
            echo->HasEchoChamber = true;
            echo->EchoDelay = FPMath.Max(echo->EchoDelay, Delay);
        }

        protected override object[] DescriptionArgs => new object[] { Delay.AsFloat };
    }
}
