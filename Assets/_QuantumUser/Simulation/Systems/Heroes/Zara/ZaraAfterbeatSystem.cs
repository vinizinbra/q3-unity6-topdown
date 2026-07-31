namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks down ZaraAfterbeat.Remaining (see AfterbeatSkillAction, which sets it) and fires the
    // delayed pulse once it hits 0 - same "countdown component ticked by its own tiny System" shape
    // as ExplodeOnDeathTimerSystem/JuggernautDischargeCooldownSystem.
    [Preserve]
    public unsafe class ZaraAfterbeatSystem : SystemMainThreadFilter<ZaraAfterbeatSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            ZaraAfterbeat* afterbeat = filter.Afterbeat;

            if (afterbeat->Remaining <= FP._0)
                return;

            afterbeat->Remaining -= f.DeltaTime;

            if (afterbeat->Remaining > FP._0)
                return;

            HitEffectUtility.ApplyDamageInRadius(f, afterbeat->Position, afterbeat->Radius, filter.Entity,
                afterbeat->Damage, DamageSource.Skill, DamageTargetMask.Enemies);

            f.Events.ResonancePulseReleased(filter.Entity, afterbeat->Position, afterbeat->Radius);

            // Fired directly rather than through HitEffectUtility.ApplyShockwave - Afterbeat is a pure
            // damage echo with no knockback of its own (see ZaraAfterbeat.qtn), but still wants the
            // same view hookup: ResonanceFxView (filtered to this entity) plays Zara's own tinted pulse
            // particle for it, same as a normal Resonance pulse (see ResonanceUtility.FirePulse), with
            // EffectsManager's generic handler as a fallback if that component/prefab isn't set up.
            f.Events.ShockwaveReleased(filter.Entity, afterbeat->Position, afterbeat->Radius, default);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ZaraAfterbeat* Afterbeat;
        }
    }
}
