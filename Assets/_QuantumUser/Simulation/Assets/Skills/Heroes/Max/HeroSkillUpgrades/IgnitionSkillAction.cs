namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Overdrive line 4 - every effect here is gated on Rage being genuinely maxed
    // (RageOverdriveUtility.IsAtMaxRage), not just Overdrive being active:
    //  - Rank 1: toggles CharacterStats.BurnOnHitStacks on/off exactly at the max-Rage threshold -
    //    driven by RageOverdriveUtility.EnterMaxRage/ResetStacks via MaxAscensionUtility.
    //    OnEnteredMaxRage/RevertIgnition, NOT by this class's own Execute.
    //  - Rank 2: a Burning enemy KILLED while at max Rage leaves a burning-ground patch where it died
    //    (MaxOverdriveReactionSystem.OnEntityKilled). No OnGoing phase at all any more - this line is
    //    entirely reactive now.
    //  - Rank 3 ("Inferno"): a radial Burn pulse (MaxAscensionUtility.ApplyRadialBurn) the FIRST time
    //    max Rage is reached each Overdrive activation - also driven from OnEnteredMaxRage, latched by
    //    IgnitionUpgrade.InfernoTriggeredThisActivation, reset by this class's own Begin below.
    //
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
        public FP BurningGroundDuration = 4;
        public FP BurningGroundRadius = 2;

        [Tooltip("Damage per tick dealt to anything standing in the patch.")]
        public FP BurningGroundDamage = 4;
        public FP BurningGroundTickInterval = FP._0_50;

        public IgnitionSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<IgnitionUpgrade>(filter.Entity, out var ignition);
            ignition->BurnOnHitStacks = BurnOnHitStacks[index];
            ignition->HasBurningGround = HasBurningGround[index];
            ignition->BurningGroundPrototype = BurningGroundPrototype;
            ignition->BurningGroundDuration = BurningGroundDuration;
            ignition->BurningGroundRadius = BurningGroundRadius;
            ignition->BurningGroundDamage = BurningGroundDamage;
            ignition->BurningGroundTickInterval = BurningGroundTickInterval;
            ignition->HasInferno = HasInferno[index];
            ignition->InfernoRadius = InfernoRadius[index];
            ignition->InfernoBurnDuration = InfernoBurnDuration[index];
            ignition->InfernoBurnIntensity = InfernoBurnIntensity[index];
            ignition->InfernoTriggeredThisActivation = false;

            f.AddOrGet<CanApplyBurn>(filter.Entity, out _);

            // Last Stand rank 1 can start an activation already at max Rage, in which case there is no
            // later threshold crossing to hook - so react to the state we're actually in, right here.
            // OnEnteredMaxRage is idempotent (latches on Applied/InfernoTriggeredThisActivation).
            if (RageOverdriveUtility.IsAtMaxRage(f, filter.Entity) == true)
            {
                MaxAscensionUtility.OnEnteredMaxRage(f, filter.Entity, ignition);
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
