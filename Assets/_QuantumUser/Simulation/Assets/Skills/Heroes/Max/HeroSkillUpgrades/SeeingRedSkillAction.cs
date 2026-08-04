namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Hero Skill Upgrade for Berserk/Overdrive - activating Overdrive (Begin only) releases a
    // short-range shockwave, damaging and igniting every enemy caught in it. One-shot with nothing
    // to track across the activation, unlike RageOverdriveSkillAction/VendettaRushSkillAction/
    // UncontrolledFurySkillAction - so no component of its own. Reuses AreaQueryUtility (capped
    // radius query) and StatusEffectUtility.ApplyBurn, same building blocks Wildfire/Burning
    // Vengeance already share via FireMasterySpreadUtility. VFX handled generically via BeginFx (see
    // SkillActionData/SkillActionFxView) - EffectRadius lets a BeginFx step scale to Radius without a
    // bespoke event.
    public unsafe partial class SeeingRedSkillAction : SkillActionData
    {
        public FP Radius = 4;
        public FP Damage = 20;
        public FP BurnDuration = 3;
        public FP BurnIntensity = FP._0_10;
        public int MaxTargets = 8;

        // {0} = Radius, {1} = Damage - e.g. "Activating Overdrive releases a shockwave in a {0}m
        // radius, dealing {1} damage and igniting nearby enemies."
        protected override object[] DescriptionArgs => new object[] { Radius, Damage };

        public override FP EffectRadius => Radius;

        public SeeingRedSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            List<EntityRef> targets = AreaQueryUtility.FindEnemiesInRadius(f, filter.Transform3D->Position, Radius, filter.Entity, MaxTargets);

            if (targets.Count == 0)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            for (int i = 0; i < targets.Count; i++)
            {
                EntityRef target = targets[i];

                DamageUtility.ApplyDamage(f, target, Damage, filter.Entity, DamageSource.Skill);

                // The direct hit above can kill a Filler/Normal-tier enemy outright (destroyed
                // immediately, see DamageUtility.ApplyDamage) - guard against igniting a
                // now-nonexistent entity.
                if (config != null && f.Exists(target) == true)
                {
                    StatusEffectUtility.ApplyBurn(f, target, BurnDuration, BurnIntensity, filter.Entity, DamageSource.Skill, config.TickInterval);
                }
            }

            Log.Debug($"[Skill] {filter.Entity}'s Seeing Red shockwave hit {targets.Count} enemies");
        }
    }
}
