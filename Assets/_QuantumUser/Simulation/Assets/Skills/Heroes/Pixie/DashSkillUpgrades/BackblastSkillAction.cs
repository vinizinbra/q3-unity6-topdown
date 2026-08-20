namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Dash Ascension - drops a bomb where the dash fires from (Begin, every rank) and, from
    // rank 2 onward, where it lands too (End) - not an instant blast. Each bomb sits for a short Fuse
    // then detonates via the shared ExplodeOnDestroy/AreaOwner/DestroyAfterTime primitive (see
    // ExplodeOnDestroy.qtn), same pattern Pocket Bombs already uses for its own Mini Bomb drop.
    // Damage is DamagePercent of Bunny Bomb's own base damage (see PixieAscensionUtility.
    // ResolveBunnyBombDamage), not a fixed value.
    //
    // TriggersSpawnUpgrades is always set true on the spawned bomb - these detonations go through
    // AreaHitData.Detonate's full, unabridged path (ExplodeOnDestroyUtility.TryDetonate's cascade-
    // eligible branch), exactly like a real Bunny Bomb throw, so they're full qualifying Pixie
    // explosions: OnAreaExplosionDetonated fires (letting Pocket Bombs react), Direct Hit's proximity
    // bonus applies, and normal Chain Reaction marking already works via the ordinary isExplosion
    // gate. At rank 3, ForceMarkOnDetonate is additionally granted onto the bomb itself, guaranteeing
    // every enemy it hits marks for Chain Reaction regardless of tier/chance - see that component's
    // own comment for why this is scoped to the bomb entity, not the owner.
    //
    // IsPlantedThrow is deliberately left FALSE, so a Backblast bomb never spawns Cluster Bomb
    // bomblets. That is a design decision, not an oversight: Cluster Bomb belongs to the Hero Skill
    // pool and is balanced against Bunny Bomb's cooldown. Backblast fires off the dash - the cheapest,
    // most-frequent button in the kit - and drops TWO bombs per dash from rank 2, so clustering off it
    // would have meant up to 10 detonations per dash and made "never cast the Hero Skill, just dash"
    // the optimal line. Everything else above is kept precisely because it deepens Backblast's
    // identity without multiplying its output. See docs/pixie-ascensions.md.
    //
    // Reads live rank fresh every activation via selfRef, so a rank-up mid-run takes effect on the
    // very next dash.
    public unsafe partial class BackblastSkillAction : SkillActionData
    {
        // Needs Editor authoring - see docs/pixie-ascensions.md. A minimal stationary EntityPrototype
        // (Transform3D only, same shape Pocket Bombs' own MiniBombPrototype needs) and an AreaHitData
        // asset for the blast itself (radius/effects) - DashBomb.prefab/its own AreaHitData are the
        // existing reference prototype this can point at directly.
        [ExpandableAsset] public AssetRef<EntityPrototype> BombPrototype;
        [ExpandableAsset] public AssetRef<AreaHitData> Explosion;

        public FP Fuse = FP._1;
        public FP[] DamagePercent = { FP.FromString("0.50"), FP.FromString("0.50"), FP.FromString("0.75") };

        public BackblastSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }


        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));

            // Rank 1 only drops a bomb at the dash's start (Begin) - the End bomb is a rank 2+
            // ability, so this phase should no-op below rank 2.
            if (firedPhase == SkillActionPhase.End && rank < 2)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;
            FP damage = DamagePercent[index] * PixieAscensionUtility.ResolveBunnyBombDamage(f, filter.Entity);

            // Begin fires before the dash moves the entity, so slot->StartPosition and the entity's
            // live position are still the same point - only End actually needs to distinguish them,
            // once the dash has relocated the entity to slot->TargetPosition.
            FPVector3 position = firedPhase == SkillActionPhase.End ? filter.Transform3D->Position : slot->StartPosition;

            EntityRef bomb = SpawnedEntitySpawner.Spawn(f, filter.Entity, BombPrototype, Fuse, position, DamageSource.Skill);

            if (bomb == EntityRef.None)
                return;

            f.AddOrGet<ExplodeOnDestroy>(bomb, out var explode);
            explode->Damage = damage;
            explode->Explosion = Explosion;
            explode->TriggersSpawnUpgrades = true;

            if (rank >= 3)
            {
                f.AddOrGet<ForceMarkOnDetonate>(bomb, out _);
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
