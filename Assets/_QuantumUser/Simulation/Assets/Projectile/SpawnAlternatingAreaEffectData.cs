namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // SpawnEntityEffectData that also configures the spawned entity's AlternatingArea/AreaDamage
    // (see AlternatingAreaSystem) - so a pulsing structure's actual heal/damage numbers live with
    // the throw that places it (part of the skill's own asset chain: Hero Skill -> ProjectileData ->
    // Hit -> Effects -> this), not scattered onto a separate prototype you'd otherwise have to go
    // dig up. The prototype itself only needs to carry what's physically its own - PhysicsCollider3D
    // (the pulse shape) plus bare AlternatingArea/AreaDamage components - every value on them gets
    // overwritten here at spawn, so whatever's authored there is inert placeholder.
    public unsafe class SpawnAlternatingAreaEffectData : SpawnEntityEffectData
    {
        public FP TickInterval = 1;

        public DamageTargetMask HealTargetMask = DamageTargetMask.Players;
        public List<AssetRef<HitEffectData>> HealEffects = new();

        public FP DamageAmount = 10;
        public DamageTargetMask DamageMask = DamageTargetMask.Enemies;
        public List<AssetRef<HitEffectData>> DamageEffects = new();

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            EntityRef spawned = SpawnedEntitySpawner.Spawn(f, context.Owner, Prototype, Duration, context.Position, context.Source, context.Element);
            Configure(f, context.Owner, spawned);
        }

        private void Configure(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<AreaDamage>(spawned, out var area) == true)
            {
                area->TickInterval = ResolveTickInterval(f, owner);
            }

            if (f.Unsafe.TryGetPointer<AlternatingArea>(spawned, out var alternating) == false)
                return;

            alternating->HealTargetMask = HealTargetMask;
            CopyEffects(HealEffects, alternating->HealEffects);

            alternating->DamageAmount = ResolveDamageAmount(f, owner);
            alternating->DamageMask = DamageMask;
            CopyEffects(DamageEffects, alternating->DamageEffects);

            ApplyPoisonUpgrade(f, owner, alternating);
            ApplyHasteUpgrade(f, owner, alternating);
            ApplyKnockbackUpgrade(f, owner, alternating);
        }

        // TickRateUpgrade (see Heroes/Zara/IncreaseWavesTickRateSkillAction) - shrinks the interval
        // rather than the caller supplying a pre-divided value, same reasoning
        // StatUtility.GetFireCooldown divides instead of multiplying (a rate expresses "how often",
        // not "how long").
        private FP ResolveTickInterval(Frame f, EntityRef owner)
        {
            FP bonus = f.Unsafe.TryGetPointer<TickRateUpgrade>(owner, out var upgrade) == true ? upgrade->RateBonus : FP._0;
            return TickInterval / (FP._1 + bonus);
        }

        // IncreaseDamageUpgrade (see Heroes/Zara/IncreaseDamageSkillAction) - boosts the amount
        // rather than the caller supplying a pre-boosted value, same shape as ResolveTickInterval.
        private FP ResolveDamageAmount(Frame f, EntityRef owner)
        {
            FP bonus = f.Unsafe.TryGetPointer<IncreaseDamageUpgrade>(owner, out var upgrade) == true ? upgrade->DamageBonus : FP._0;
            return DamageAmount * (FP._1 + bonus);
        }

        // PoisonDamageWavesUpgrade (see Heroes/Zara/PoisonDamageWavesSkillAction) - baked into this
        // specific speaker's own DamageEffects once, here at spawn, rather than checked live every
        // pulse. The upgrade's Begin/End only brackets the throw itself, which ends (End revokes it)
        // the instant this speaker is created - long before the speaker's own later pulses would
        // ever see it live. Reading it here, while the throw is still Active, is the only point
        // guaranteed to see it; AlternatingAreaSystem's own per-pulse CopyEffects then carries it
        // forward automatically for the rest of this speaker's lifetime.
        private static void ApplyPoisonUpgrade(Frame f, EntityRef owner, AlternatingArea* alternating)
        {
            if (f.Unsafe.TryGetPointer<PoisonDamageWavesUpgrade>(owner, out var upgrade) == false
                || upgrade->PoisonEffect.IsValid == false)
                return;

            for (int i = 0; i < alternating->DamageEffects.Length; i++)
            {
                if (alternating->DamageEffects[i].IsValid == true)
                    continue;

                alternating->DamageEffects[i] = upgrade->PoisonEffect;

                Log.Debug($"[Skill] {owner}'s PoisonDamageWavesUpgrade baked into the spawned speaker's DamageEffects slot {i}");
                return;
            }

            Log.Error($"[Skill] {owner}'s PoisonDamageWavesUpgrade couldn't fit - the spawned speaker's DamageEffects already fills all 4 slots");
        }

        // HasteOnHealUpgrade (see Heroes/Zara/HasteOnHealSkillAction) - same bake-once-at-spawn
        // shape as ApplyPoisonUpgrade above, just appending into HealEffects instead of DamageEffects.
        private static void ApplyHasteUpgrade(Frame f, EntityRef owner, AlternatingArea* alternating)
        {
            if (f.Unsafe.TryGetPointer<HasteOnHealUpgrade>(owner, out var upgrade) == false
                || upgrade->HasteEffect.IsValid == false)
                return;

            for (int i = 0; i < alternating->HealEffects.Length; i++)
            {
                if (alternating->HealEffects[i].IsValid == true)
                    continue;

                alternating->HealEffects[i] = upgrade->HasteEffect;

                Log.Debug($"[Skill] {owner}'s HasteOnHealUpgrade baked into the spawned speaker's HealEffects slot {i}");
                return;
            }

            Log.Error($"[Skill] {owner}'s HasteOnHealUpgrade couldn't fit - the spawned speaker's HealEffects already fills all 4 slots");
        }

        // KnockbackOnDamageUpgrade (see Heroes/Zara/KnockbackOnDamageSkillAction) - same
        // bake-once-at-spawn shape as ApplyPoisonUpgrade above, into DamageEffects as well - every
        // damage pulse should knock back, not just some of them, so there's nothing conditional to
        // check live the way StunEveryWavesUpgrade needs.
        private static void ApplyKnockbackUpgrade(Frame f, EntityRef owner, AlternatingArea* alternating)
        {
            if (f.Unsafe.TryGetPointer<KnockbackOnDamageUpgrade>(owner, out var upgrade) == false
                || upgrade->KnockbackEffect.IsValid == false)
                return;

            for (int i = 0; i < alternating->DamageEffects.Length; i++)
            {
                if (alternating->DamageEffects[i].IsValid == true)
                    continue;

                alternating->DamageEffects[i] = upgrade->KnockbackEffect;

                Log.Debug($"[Skill] {owner}'s KnockbackOnDamageUpgrade baked into the spawned speaker's DamageEffects slot {i}");
                return;
            }

            Log.Error($"[Skill] {owner}'s KnockbackOnDamageUpgrade couldn't fit - the spawned speaker's DamageEffects already fills all 4 slots");
        }

        // destination is a FixedArray "handle" into the entity's own component memory (same as
        // AlternatingAreaSystem.CopyEffects) - indexing it writes through to the real data even
        // though it's passed by value, so no ref is needed.
        private static void CopyEffects(List<AssetRef<HitEffectData>> source, FixedArray<AssetRef<HitEffectData>> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = i < source.Count ? source[i] : default;
            }
        }
    }
}
