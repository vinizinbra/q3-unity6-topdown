namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Pixie's Pocket Bombs Ascension - reacts to a signal rather than the per-target proximity hook
    // (see DemolitionMasteryUtility for Direct Hit instead): a chance to drop a Mini Bomb off any
    // qualifying explosion. Unfiltered - no Filter query, the owner is resolved directly off the
    // signal's own payload. See Heroes/Pixie/DemolitionMastery.qtn.
    [Preserve]
    public unsafe class PixieDemolitionMasterySystem : SystemMainThread, ISignalOnAreaExplosionDetonated
    {
        public override void Update(Frame f)
        {
        }

        // Pocket Bombs - never fires for a Mini Bomb's own detonation (see
        // OnAreaExplosionDetonated's own comment in Combat.qtn), so there is no depth check needed
        // here either - "cannot generate additional Pocket Bombs" is enforced by this signal simply
        // never firing from ExplodeOnDestroyUtility.TryDetonate, not by a runtime guard.
        public void OnAreaExplosionDetonated(Frame f, EntityRef owner, FPVector3 center, FP radius, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<PocketBombsUpgrade>(owner, out var ordnance) == false)
                return;

            if (ordnance->MiniBombPrototype.IsValid == false || ordnance->Explosion.IsValid == false)
                return;

            if (DamageUtility.RollChance(f, ordnance->Chance) == false)
                return;

            EntityRef bomb = SpawnedEntitySpawner.Spawn(f, owner, ordnance->MiniBombPrototype, ordnance->Fuse, center, source);

            if (bomb == EntityRef.None)
                return;

            f.AddOrGet<ExplodeOnDestroy>(bomb, out var explode);
            explode->Damage = ordnance->DamagePercent * PixieAscensionUtility.ResolveBunnyBombDamage(f, owner);
            explode->Explosion = ordnance->Explosion;

            Log.Debug($"[Skill] {owner}'s Pocket Bombs dropped a Mini Bomb at {center}");
        }
    }
}
