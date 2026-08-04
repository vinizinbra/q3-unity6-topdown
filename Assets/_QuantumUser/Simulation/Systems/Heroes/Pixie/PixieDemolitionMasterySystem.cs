namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Pixie's Demolition Mastery traits that react to signals rather than the per-target proximity
    // hook (see DemolitionMasteryUtility for Direct Hit/Concussive Force instead) - Volatile Payload
    // (a crit that's also an explosion applies Burn) and Mini Ordnance (a chance to drop a Mini Bomb
    // off any qualifying explosion). One system for the whole trait pool, same shape
    // MaxFireMasteryReactionSystem already uses for Max's own traits. Unfiltered - no Filter query,
    // entities resolved directly off each signal's own payload. See Heroes/Pixie/
    // DemolitionMastery.qtn.
    [Preserve]
    public unsafe class PixieDemolitionMasterySystem : SystemMainThread,
        ISignalOnExplosionCriticalHit, ISignalOnAreaExplosionDetonated
    {
        public override void Update(Frame f)
        {
        }

        // Volatile Payload - only ever fires for a crit already flagged isExplosion:true (see
        // DamageUtility.ApplyDamage/Combat.qtn's own comment on this signal), so there's no
        // "was this an explosion" check needed here - the signal itself already means that.
        public void OnExplosionCriticalHit(Frame f, EntityRef target, EntityRef owner, FP damage, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<VolatilePayloadUpgrade>(owner, out var payload) == false)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            StatusEffectUtility.ApplyBurn(f, target, payload->BurnDuration, payload->BurnIntensity, owner, source, config.TickInterval);
        }

        // Mini Ordnance - never fires for a Mini Bomb's own detonation (see
        // OnAreaExplosionDetonated's own comment in Combat.qtn), so there is no depth check needed
        // here either - "cannot generate additional Cluster Charges" is enforced by this signal
        // simply never firing from ExplodeOnDestroyUtility.TryDetonate, not by a runtime guard.
        public void OnAreaExplosionDetonated(Frame f, EntityRef owner, FPVector3 center, FP radius, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<MiniOrdnanceUpgrade>(owner, out var ordnance) == false)
                return;

            if (ordnance->MiniBombPrototype.IsValid == false || ordnance->Explosion.IsValid == false)
                return;

            if (DamageUtility.RollChance(f, ordnance->Chance) == false)
                return;

            EntityRef bomb = SpawnedEntitySpawner.Spawn(f, owner, ordnance->MiniBombPrototype, ordnance->Fuse, center, source);

            if (bomb == EntityRef.None)
                return;

            f.AddOrGet<ExplodeOnDestroy>(bomb, out var explode);
            explode->Damage = ordnance->Damage;
            explode->Explosion = ordnance->Explosion;

            Log.Debug($"[Skill] {owner}'s Mini Ordnance dropped a Mini Bomb at {center}");
        }
    }
}
