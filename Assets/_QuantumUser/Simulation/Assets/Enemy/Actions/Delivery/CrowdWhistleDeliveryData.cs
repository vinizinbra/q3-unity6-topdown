namespace Quantum
{
    // The whistle cone: a plain GroundAreaDeliveryData cone (authored ConeShaped, Origin Self, no damage effect) whose
    // action Effects carry the slow + CrowdMarkEffectData - this subclass only adds the "one big mechanic at a time"
    // gate: never whistles while its tornadoes are out or a barrage is already running.
    public unsafe class CrowdWhistleDeliveryData : GroundAreaDeliveryData
    {
        public bool BlockWhileMajorMechanicActive = true;

        public override bool CanBegin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (BlockWhileMajorMechanicActive == true && BossHazardUtility.IsMajorMechanicActive(f, filter.Entity) == true)
                return false;

            return base.CanBegin(f, ref filter, data, action, target);
        }
    }
}
