namespace Quantum
{
    using Photon.Deterministic;

    // Fires a single projectile at the player's locked target, the same way WeaponSystem's
    // Projectile fire type does - see ProjectileAimUtility. A skill's own gun: no ammo/reload, since
    // SkillSlot's stacks/Cooldown already gate reuse. Doesn't resolve on the casting tick - the
    // slot stays Active (blocking a stacked refire) until the fired projectile explodes or expires,
    // so End()/End-phase actions represent the shot actually landing, not just leaving the muzzle.
    public unsafe partial class ProjectileSkillData : SkillData
    {
        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;

        // Seeds the projectile's Damage; DamageSource.Skill makes CharacterStats.SkillDamageMultiplier
        // apply on top - see DamageUtility.ResolveOutgoingDamage. What it hits for beyond that (and
        // whether it knocks back) is the projectile's own ProjectileHitData.Effects, not this.
        public FP Damage = 10;

        public ProjectileSpawnAnchor SpawnAnchor = ProjectileSpawnAnchor.OnSelf;
        public FPVector3 SpawnOffset;

        // Only matters for SpawnAnchor.OnTarget: ResolveSpawnOrigin needs a real point to anchor
        // onto, and a free-aimed cast (no locked filter.Aim->Target) has none - see
        // ProjectileSpawnAnchor's own doc comment. Same "caster position + aim direction * distance"
        // synthetic-destination construction DashSkillData uses for its own TargetPosition, so an
        // OnTarget spawn still lands somewhere sensible down the aim ray instead of collapsing onto
        // the caster (same as OnSelf).
        public FP Range = 10;

        public override bool Begin(Frame f, ref SkillSystem.Filter filter, Input* input, SkillSlot* slot)
        {
            FPVector3 casterPosition = filter.Transform3D->Position;
            FP aimAngle = filter.Aim->Angle;
            FPVector3 flatDirection = FPQuaternion.Euler(0, aimAngle, 0) * FPVector3.Forward;
            bool aimAtCenter = ProjectileAimUtility.ResolveAimsAtCenter(f, ProjectileData);
            FPVector3 fallbackTarget = casterPosition + flatDirection * Range;
            FPVector3 spawnPosition = ProjectileSpawner.ResolveSpawnOrigin(casterPosition, fallbackTarget, aimAngle, SpawnAnchor, SpawnOffset);

            // From the real spawn point, not the caster's own position - they can differ (see
            // SpawnOffset), and a lob needs the correct elevation to compute a believable arc.
            FPVector3 aimDirection = ProjectileAimUtility.ResolveAimDirection(f, filter.Aim->Target, spawnPosition, flatDirection, aimAtCenter);

            bool fired = Fire(f, ref filter, slot, casterPosition, aimAngle, spawnPosition, aimDirection, aimAtCenter);

            return fired == false; // nothing fired -> finish now; otherwise wait for it, see Tick()
        }

        public override bool Tick(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            return slot->ProjectilePending == false;
        }

        private bool Fire(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, FPVector3 casterPosition,
            FP aimAngle, FPVector3 spawnPosition, FPVector3 aimDirection, bool aimAtCenter)
        {
            ProjectileDataAsset projectileData = f.FindAsset(ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            // A locked target is solved toward as a point rather than a direction - a lob needs the
            // real distance to land on the target instead of a fixed TargetDistance down the aim ray.
            ProjectileLaunch launch = ProjectileAimUtility.TryGetAimPoint(f, filter.Aim->Target, aimAtCenter, out FPVector3 aimPoint)
                ? movement.GetLaunchToTarget(f, ProjectileSpawner.ResolveSpawnOrigin(casterPosition, aimPoint, aimAngle, SpawnAnchor, SpawnOffset), aimPoint, filter.Aim->Target)
                : movement.GetLaunch(f, spawnPosition, aimDirection);

            if (launch.IsValid == false)
            {
                Log.Error($"[Skill] {filter.Entity} resolved no valid launch - nothing fired");
                return false;
            }

            SkillSlotId sourceSlot = slot == &filter.CharacterSkills->DashSkill ? SkillSlotId.DashSkill : SkillSlotId.HeroSkill;
            FP damage = Damage * ResolveDamageMultiplier(f, filter.Entity);
            damage *= ApplyBombCharge(f, filter.Entity, sourceSlot, movement, ref launch);
            ProjectileSpawner.Spawn(f, filter.Entity, ProjectileData, launch, damage, DamageSource.Skill, sourceSlot, filter.Aim->Target);
            slot->ProjectilePending = true;

            Log.Debug($"[Skill] {filter.Entity} fired a projectile skill from {spawnPosition} with velocity {launch.Velocity}");

            return true;
        }

        // ProjectileDamageUpgrade (see Heroes/Pixie/IncreaseProjectileDamageSkillAction) - read here
        // rather than in the shared ProjectileSpawner, so it only multiplies this skill's own throw,
        // not every projectile the owner happens to fire while it's granted.
        private static FP ResolveDamageMultiplier(Frame f, EntityRef owner)
        {
            return f.Unsafe.TryGetPointer<ProjectileDamageUpgrade>(owner, out var upgrade) == true ? upgrade->Multiplier : FP._1;
        }

        // PixieBombCharge (see that component - shared by Hot Fuse and Blast Jump) - only ever meant
        // to empower Bunny Bomb specifically (Pixie's HeroSkill), not any other projectile-type
        // skill, so gated on sourceSlot rather than just component presence. Applies this throw's
        // damage multiplier (returned) and projectile speed (scaled into `launch` in place); the
        // combined radius multiplier is applied later, at this bomb's own detonation - see
        // AreaHitData.Detonate and PixieBombCharge.qtn's own comment for why the split.
        private static FP ApplyBombCharge(Frame f, EntityRef owner, SkillSlotId sourceSlot,
            ProjectileMovementData movement, ref ProjectileLaunch launch)
        {
            if (sourceSlot != SkillSlotId.HeroSkill)
                return FP._1;

            if (f.Unsafe.TryGetPointer<PixieBombCharge>(owner, out var charge) == false)
                return FP._1;

            if (charge->InstantDetonate == true)
            {
                f.AddOrGet<InstantDetonate>(owner, out _);
            }

            // Blast Jump - delegated to the movement rather than multiplying launch.Velocity here,
            // because "faster" is not the same operation for every movement type. This used to scale
            // the whole vector inline, which on Bunny Bomb's arc scaled the vertical launch speed too
            // and therefore multiplied its RANGE by 1.25^2 - a ~56% overshoot on a lob whose entire
            // range is ~2.5 units and whose only aiming control is where the player points. See
            // ProjectileMovementData.ApplySpeedMultiplier / ThrownProjectileMovementData's override.
            FP speedMultiplier = PixieAscensionUtility.Neutral(charge->BlastJumpProjectileSpeedMultiplier);

            movement.ApplySpeedMultiplier(ref launch, speedMultiplier);

            return PixieAscensionUtility.Neutral(charge->HotFuseDamageMultiplier);
        }
    }
}
