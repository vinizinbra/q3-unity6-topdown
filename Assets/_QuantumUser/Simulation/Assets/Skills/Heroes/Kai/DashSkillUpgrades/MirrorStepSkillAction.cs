namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Dash Ascension (Mirror Step, line 1/3) - see docs/kai-ascensions.md. Repurposes the old
    // (single-pick) ReflectProjectilesSkillAction - while dashing, any enemy-owned Projectile within
    // Radius of Kai's current position is sent back at its owner (Velocity reversed for the visual
    // snap, then Target + MovementOverride hand it off to HomingProjectileMovementData to curve in on
    // wherever that owner actually is by the time it arrives) and re-owned by him, so it now damages
    // enemies instead of players - same "flip Owner so damage resolves off the new owner's stats"
    // idiom DamageUtility.ApplyDamage already relies on everywhere. Runs every tick
    // of the dash (OnGoing) rather than a single swept-box test at Begin/End - a projectile keeps
    // moving throughout the dash, so a per-tick proximity check catches one that only entered range
    // mid-dash, which a before/after sweep would miss. Rank 2 widens the radius and, on top of it,
    // starts instantly executing weak shooters; rank 3 "Evasive Reflex" extends the execute tier and
    // additionally refunds Vortex cooldown per successful reflection, capped per Dash (see
    // MirrorStepCooldownAccumulator, reset every Dash Begin).
    //
    // Excluded against Elite/Boss-owned projectiles (reflecting a boss's own attack back at it would
    // trivialize the fight) - same EnemyTier gate idiom used elsewhere in this roster (Pixie's Heavy
    // Payload, Kai's own Void Shards search).
    public unsafe partial class MirrorStepSkillAction : SkillActionData
    {
        public FP[] Radius = { FP._3, FP.FromString("4.50"), FP.FromString("4.50") };

        // A reflected bolt no longer inherits the enemy shot's own Damage - it deals a fixed amount
        // off Kai's own Vortex Skill Damage baseline (KaiAscensionUtility.ResolveVortexSkillDamage),
        // same "DamagePercent[index] * Resolve<Hero>SkillDamage" idiom every other damage-dealing
        // Ascension in this roster uses, so a Kai build invested in Skill Damage actually benefits
        // from reflecting instead of just replaying whatever the enemy's own shot happened to hit for.
        public FP[] DamagePercent = { FP.FromString("0.40"), FP.FromString("0.60"), FP._1 };

        // Multiplies the reflected bolt's refreshed Lifetime and travel-distance cap (see
        // RefreshFlightBudget) - both "reset the budget so a late-flight bullet doesn't expire the
        // instant it reverses" and "reflected shots reach further than the original" in one number.
        public FP ReflectedRangeMultiplier = FP._1_50;

        // Swapped onto the reflected bolt's MovementOverride in place of a straight velocity flip -
        // same "per-shot override, preferred every tick by ProjectileSystem.Update over the asset's
        // own Movement" mechanism Pixie's Rocket Conversion (Grenade Launcher Mastery R3) already uses
        // to swap flight behavior, just applied to an already-live projectile instead of at spawn. A
        // pure reversal only connects if the shooter happens to still be standing on the return line,
        // which in a real fight it usually isn't by the time the bolt gets there - undermining the
        // very instant-kill payoff rank 2/3 add. Retargeting at the shooter's live position instead
        // (via Target, re-read every tick by HomingProjectileMovementData) makes the reflect actually
        // land.
        [Tooltip("Homing movement asset a reflected bolt switches to, retargeted at its original owner - see Assets/_QuantumUser/Resources/Weapons/Projectile/MirrorStepReflectedHoming.asset.")]
        public AssetRef<ProjectileMovementData> ReflectedMovement;

        // Rank 3 "Evasive Reflex" only (0 at ranks 1-2, which leaves MirrorStepCooldownAccumulator
        // ungranted so no refund ever fires).
        public FP[] CooldownReductionPerReflect = { FP._0, FP._0, FP._0_50 };
        public FP MaxCooldownReductionPerDash = FP._2;

        public MirrorStepSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing;
        }

        protected override object[] DescriptionArgs => new object[] { Radius[0] };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            if (firedPhase == SkillActionPhase.Begin)
            {
                if (rank >= 3)
                {
                    f.AddOrGet<MirrorStepCooldownAccumulator>(filter.Entity, out var accumulator);
                    accumulator->AppliedThisDash = FP._0;
                    accumulator->MaxPerDash = MaxCooldownReductionPerDash;
                    accumulator->PerReflect = CooldownReductionPerReflect[index];
                }

                return;
            }

            // Skill Area - see StatUtility.GetAreaMultiplier.
            FP radius = Radius[index] * StatUtility.GetAreaMultiplier(f, filter.Entity);
            FP damagePercent = DamagePercent[index];
            FPVector3 position = filter.Transform3D->Position;
            var projectiles = f.Filter<Projectile, Transform3D>();

            while (projectiles.Next(out EntityRef projectileEntity, out Projectile projectile, out Transform3D projectileTransform))
            {
                // TryResolveOwnerTier only succeeds for an Enemy-owned projectile, so this alone also
                // covers the old separate "is this Enemy-owned" check - and naturally skips
                // re-reflecting an already-reflected shot on a later tick of the same dash, since its
                // Owner is no longer an Enemy by then.
                if (TryResolveOwnerTier(f, projectile.Owner, out EnemyTier ownerTier) == false || ownerTier >= EnemyTier.Elite)
                    continue;

                if ((projectileTransform.Position - position).SqrMagnitude > radius * radius)
                    continue;

                if (f.Unsafe.TryGetPointer<Projectile>(projectileEntity, out var live) == false)
                    continue;

                // Captured before Owner is overwritten below - both the instant-kill tier check
                // above and Target here need the ENEMY that fired this, not Kai.
                EntityRef originalOwner = projectile.Owner;

                // The instant reversal is the visual "snap" that reads as a reflection; Target +
                // MovementOverride then take over from the next tick onward, curving the bolt in on
                // wherever the shooter actually is instead of leaving it committed to a fixed return
                // line the shooter has probably already stepped off of.
                live->Velocity = -live->Velocity;
                live->Target = originalOwner;
                live->MovementOverride = ReflectedMovement;
                live->Owner = filter.Entity;
                live->Source = DamageSource.Skill;
                live->Damage = IsInstantKillTier(index, ownerTier)
                    ? ExecuteDamage
                    : damagePercent * KaiAscensionUtility.ResolveVortexSkillDamage(f, filter.Entity);

                RefreshFlightBudget(f, live);

                f.Events.ProjectileReflected(filter.Entity, projectileTransform.Position);

                TryRefundCooldown(f, filter.Entity);

                Log.Debug($"[Skill] {filter.Entity} reflected {projectileEntity} back the way it came");
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }

        private static void TryRefundCooldown(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<MirrorStepCooldownAccumulator>(entity, out var accumulator) == false)
                return;

            FP remaining = accumulator->MaxPerDash - accumulator->AppliedThisDash;

            if (remaining <= FP._0)
                return;

            FP amount = FPMath.Min(accumulator->PerReflect, remaining);

            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return;

            SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, amount);
            accumulator->AppliedThisDash += amount;
        }

        // Tier of the enemy this projectile belongs to - resolved once and reused both to exclude
        // Elite/Boss-owned shots (reflecting a boss's own attack back at it would trivialize the
        // fight - same EnemyTier gate idiom used elsewhere in this roster, e.g. Kai's own Void Shards
        // search, Brute's old Iron Shoulder tier gate) and to decide instant-kill eligibility below.
        private static bool TryResolveOwnerTier(Frame f, EntityRef owner, out EnemyTier tier)
        {
            tier = EnemyTier.Filler;

            if (f.Unsafe.TryGetPointer<Enemy>(owner, out var enemy) == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            tier = data.Tier;
            return true;
        }

        // Rank 2 executes Normal-tier-and-below shooters outright; rank 3 extends that up through
        // Specialist (Heavy stays un-executable at every rank, same as it staying reflectable but
        // never instant-killed). Gated on the ORIGINAL owner's tier - resolved above, before Owner
        // flips to Kai - which is safe to decide up front now that the bolt is actually homing back
        // onto that same owner (see Target/MovementOverride above) rather than just hoping a straight
        // reversal happens to connect with whatever fired it.
        private static bool IsInstantKillTier(int index, EnemyTier ownerTier)
        {
            return index switch
            {
                1 => ownerTier <= EnemyTier.Normal,
                2 => ownerTier <= EnemyTier.Specialist,
                _ => false,
            };
        }

        // Flat sentinel rather than reading the target's own Health - simpler, and it still routes
        // through DamageUtility.ApplyDamage's normal death branch (events/drops/OnEntityKilled)
        // completely unmodified, since to that call this is just an unusually large Damage value like
        // any other lethal hit.
        private static readonly FP ExecuteDamage = FP.FromString("999999");

        // Refreshes the reflected bolt's remaining flight budget. RemainingLifetime/TraveledDistance
        // were left wherever the enemy's own shot happened to be when it got caught - reflecting one
        // late in its own flight (near its own Lifetime, or its MaxTravelDistance/ProjectileDataAsset.
        // MaxDistance cap) used to make it expire almost immediately after reversing course. Reset to
        // a fresh budget, scaled up by ReflectedRangeMultiplier so the reflection reliably makes it
        // back across the radius it was just caught at (and then some).
        private void RefreshFlightBudget(Frame f, Projectile* live)
        {
            ProjectileDataAsset projectileData = f.FindAsset(live->ProjectileData);
            if (projectileData == null)
                return;

            live->RemainingLifetime = projectileData.Lifetime * ReflectedRangeMultiplier;
            live->TraveledDistance = FP._0;

            FP baseMaxDistance = live->MaxTravelDistance > FP._0 ? live->MaxTravelDistance : projectileData.MaxDistance;
            if (baseMaxDistance > FP._0)
                live->MaxTravelDistance = baseMaxDistance * ReflectedRangeMultiplier;
        }
    }
}
