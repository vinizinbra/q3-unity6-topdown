namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Overdrive rank 4 - every effect here is gated on Rage being genuinely maxed
    // (RageOverdriveUtility.IsAtMaxRage), not just Overdrive being active:
    //  - Rank 1: toggles CharacterStats.BurnOnHitStacks on/off exactly at the max-Rage threshold -
    //    driven entirely by RageOverdriveUtility.TryAdvanceStack/ResetStacks via
    //    MaxAscensionUtility.OnEnteredMaxRage/RevertIgnition, NOT by this class's own Execute.
    //  - Rank 2: while at max Rage, drops a Burning Ground patch every BurningGroundSpacing units
    //    travelled - the OnGoing half below, distance-paced the same way SpawnEntitySkillAction.Spacing
    //    already is, reimplemented here directly since the max-Rage gate is Max-specific.
    //  - Rank 3 ("Inferno"): a radial Burn pulse (MaxAscensionUtility.ApplyRadialBurn) the FIRST time
    //    max Rage is reached each Overdrive activation - also driven from OnEnteredMaxRage, latched by
    //    IgnitionUpgrade.InfernoTriggeredThisActivation, reset by this class's own Begin below.
    // Fires on every Berserk Begin, same "refresh fresh off the live rank each cast" idiom Brute's
    // MomentumSkillAction already established.
    public unsafe partial class IgnitionSkillAction : SkillActionData
    {
        public byte[] BurnOnHitStacks = { 1, 1, 1 };
        public bool[] HasBurningGround = { false, true, true };
        public bool[] HasInferno = { false, false, true };
        public FP[] InfernoRadius = { FP._0, FP._0, 4 };
        public FP[] InfernoBurnDuration = { FP._0, FP._0, 4 };
        public FP[] InfernoBurnIntensity = { FP._0, FP._0, 5 };

        [Header("Burning Ground (rank 2+)")]
        public AssetRef<EntityPrototype> BurningGroundPrototype;
        public FP BurningGroundDuration = 3;
        public FP BurningGroundSpacing = 2;

        public IgnitionSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<IgnitionUpgrade>(filter.Entity, out var ignition);
                ignition->BurnOnHitStacks = BurnOnHitStacks[index];
                ignition->HasBurningGround = HasBurningGround[index];
                ignition->HasInferno = HasInferno[index];
                ignition->InfernoRadius = InfernoRadius[index];
                ignition->InfernoBurnDuration = InfernoBurnDuration[index];
                ignition->InfernoBurnIntensity = InfernoBurnIntensity[index];
                ignition->InfernoTriggeredThisActivation = false;

                f.AddOrGet<CanApplyBurn>(filter.Entity, out _);
                return;
            }

            // OnGoing - Burning Ground, rank 2+, only while genuinely at max Rage (paced by
            // IsDueThisTick below regardless of rank/Rage state, so those are checked here instead).
            if (HasBurningGround[index] == false || RageOverdriveUtility.IsAtMaxRage(f, filter.Entity) == false)
                return;

            SpawnedEntitySpawner.Spawn(f, filter.Entity, BurningGroundPrototype, BurningGroundDuration,
                filter.Transform3D->Position, DamageSource.Skill);
        }

        // Same distance-accumulation spacing idiom SpawnEntitySkillAction.IsDueThisTick already
        // established, reimplemented here directly since it has to co-exist with this class's own
        // Begin phase and max-Rage/HasBurningGround gates instead of just Spacing alone.
        protected override bool IsDueThisTick(Frame f, SkillSlot* slot)
        {
            FP travelled = slot->TravelledDistance;
            FP step = slot->LastStepDistance;

            return FPMath.FloorToInt(travelled / BurningGroundSpacing) > FPMath.FloorToInt((travelled - step) / BurningGroundSpacing);
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
