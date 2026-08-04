namespace Quantum
{
    using Photon.Deterministic;

    // Queues a repeat of every shot (see WeaponSystem.EnqueueEcho/TickPendingEchoes) - EchoDelay is
    // shared with Echo Chamber (whichever equipped perk asks for the longer delay wins, so combining
    // them can't make echoes fire faster than either alone intends).
    public unsafe class InfiniteEchoWeaponPerkData : WeaponPerkData
    {
        public FP Delay = FP._0_50;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponEchoState>(owner, out var echo);
            echo->HasInfiniteEcho = true;
            echo->EchoDelay = FPMath.Max(echo->EchoDelay, Delay);
        }

        protected override object[] DescriptionArgs => new object[] { Delay.AsFloat };
    }
}
