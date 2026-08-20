namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Dash Ascension (new) - Pixie's dash-into-Bunny-Bomb synergy line, sitting alongside
    // Backblast (dash is the weapon) and Hot Fuse (dash empowers damage) as the "dash is
    // repositioning AND tempo" option.
    //
    //  - Rank 1: for a short window after dashing, the next Bunny Bomb flies faster and blasts wider
    //    (shares PixieBombCharge with Hot Fuse - see that component for why each line owns its own
    //    fields rather than compounding into shared ones).
    //  - Rank 2: dashing also shaves time off Bunny Bomb's remaining cooldown, so repositioning and
    //    re-arming become the same action.
    //  - Rank 3: dashing through or near one of her own ALREADY-PLANTED bombs detonates it on the
    //    spot for bonus damage - turning a planted bomb into a mine she chooses the timing of.
    //
    // Rank 3 finds a planted bomb by the exact shape TryPlant leaves behind (ExplodeOnDestroy +
    // AreaOwner pointing at this Pixie, no Projectile any more - see ProjectileSystem.TryPlant), so it
    // can never trigger a still-flying bomb, another player's bomb, or an unrelated hazard. Detonation
    // reuses the ordinary destroy path (ExplodeOnDestroyUtility.TryDetonate + f.Destroy) rather than a
    // parallel blast, so the bomb behaves exactly as it would have on its own fuse - Birthday Cake's
    // radius/damage bonus, Cluster Bomb's bomblets, Pocket Bombs' signal and all.
    public unsafe partial class BlastJumpSkillAction : SkillActionData
    {
        [Header("Rank 1 - empowered next bomb")]
        public FP Window = 2;
        public FP[] ProjectileSpeedMultiplier = { FP._1_25, FP._1_25, FP._1_25 };
        public FP[] RadiusMultiplier = { FP._1_25, FP._1_25, FP._1_25 };

        [Header("Rank 2 - cooldown refund")]
        public FP[] CooldownReduction = { FP._0, FP._1, FP._1 };

        [Header("Rank 3 - dash-detonate a planted bomb")]
        [Tooltip("How close the dash has to pass to one of her own planted bombs to set it off.")]
        public FP TriggerRadius = 3;
        public FP DetonationDamageBonus = FP._0_50;

        public BlastJumpSkillAction()
        {
            // Begin arms the charge/refund; OnGoing is what actually sweeps for planted bombs, since a
            // dash covers ground over several ticks and a single before/after test would miss a bomb
            // passed through mid-dash. Interval 0 = every tick.
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing;
            Interval = 0;
        }

        public override FP EffectRadius => TriggerRadius;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<PixieBombCharge>(filter.Entity, out var charge);
                PixieAscensionUtility.ExtendBombChargeWindow(charge, Window);
                charge->BlastJumpProjectileSpeedMultiplier = ProjectileSpeedMultiplier[index];
                charge->BlastJumpRadiusMultiplier = RadiusMultiplier[index];

                if (CooldownReduction[index] > FP._0 && f.Unsafe.TryGetPointer<CharacterSkills>(filter.Entity, out var skills) == true)
                {
                    SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, CooldownReduction[index]);
                }

                return;
            }

            if (rank < 3)
                return;

            // Skill Area widens how close the dash has to pass - see StatUtility.GetAreaMultiplier.
            FP triggerRadius = TriggerRadius * StatUtility.GetAreaMultiplier(f, filter.Entity);

            TryDetonateNearbyPlantedBombs(f, filter.Entity, filter.Transform3D->Position, triggerRadius);
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }

        // Filters over ExplodeOnDestroy rather than an OverlapShape query: a planted bomb has no
        // guarantee of a physics collider on a layer this sweep would find, and the live count of
        // these entities is tiny (Pixie can only have one bomb in flight/planted at a time, plus a
        // handful of dropped Mini Bombs).
        private void TryDetonateNearbyPlantedBombs(Frame f, EntityRef owner, FPVector3 position, FP triggerRadius)
        {
            FP sqrRadius = triggerRadius * triggerRadius;
            var bombs = f.Filter<ExplodeOnDestroy, AreaOwner, Transform3D>();

            while (bombs.Next(out EntityRef bomb, out ExplodeOnDestroy explode, out AreaOwner areaOwner, out Transform3D transform))
            {
                if (areaOwner.Owner != owner)
                    continue;

                // IsPlantedThrow is what distinguishes a planted Bunny Bomb (a delayed continuation of
                // a real throw - see ProjectileSystem.TryPlant) from a fire-and-forget drop like a
                // Pocket Bombs Mini Bomb or a Backblast bomb, which this line deliberately leaves
                // alone.
                //
                // This used to read TriggersSpawnUpgrades, which quietly stopped meaning that once
                // Backblast started setting it true to make its own drops count as genuine explosions -
                // so Blast Jump had been detonating Backblast bombs despite this comment saying it
                // didn't. IsPlantedThrow is set ONLY by TryPlant, so it can't drift that way again.
                if (explode.IsPlantedThrow == false)
                    continue;

                if (f.Has<Projectile>(bomb) == true)
                    continue; // still in flight, not planted yet

                if ((transform.Position - position).SqrMagnitude > sqrRadius)
                    continue;

                if (f.Unsafe.TryGetPointer<ExplodeOnDestroy>(bomb, out var live) == true)
                {
                    live->Damage *= FP._1 + DetonationDamageBonus;
                }

                ExplodeOnDestroyUtility.TryDetonate(f, bomb);
                f.Destroy(bomb);

                Log.Debug($"[Skill] {owner}'s Blast Jump detonated planted bomb {bomb} on dash contact");

                // Only one Bunny Bomb can be in flight/planted per caster at a time (see
                // ProjectileSkillData.Tick's own ProjectilePending gate), so there is never a second
                // match to find - returning here also avoids continuing to iterate a filter whose
                // backing set was just mutated.
                return;
            }
        }
    }
}
