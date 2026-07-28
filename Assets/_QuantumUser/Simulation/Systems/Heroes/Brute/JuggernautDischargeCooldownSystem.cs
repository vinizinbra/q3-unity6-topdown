namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks down JuggernautDischargeCooldown.Remaining for every entity a Juggernaut discharge marked
    // (see JuggernautSkillData.Discharge), removing the component once it expires. Deliberately its
    // own system rather than folded into EnemySystem - EnemySystem is a generic, hero-agnostic AI
    // shell ("Adding a new delivery type is a new EnemyDeliveryData subclass - zero changes here"), and this
    // needs to keep counting down regardless of whether Brutus is still nearby or even still using
    // the skill, which a hero-specific hook inside EnemySystem couldn't cleanly guarantee anyway.
    [Preserve]
    public unsafe class JuggernautDischargeCooldownSystem : SystemMainThreadFilter<JuggernautDischargeCooldownSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            filter.Cooldown->Remaining -= f.DeltaTime;

            if (filter.Cooldown->Remaining <= FP._0)
            {
                f.Remove<JuggernautDischargeCooldown>(filter.Entity);
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public JuggernautDischargeCooldown* Cooldown;
        }
    }
}
