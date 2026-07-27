namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine;

    public enum SpawnAlignment
    {
        // Whatever the prototype was authored with.
        Prototype = 0,

        // The caster's aim.
        Facing = 1,

        // Along the path the skill covered.
        Path = 2
    }

    // Where a spawn's position is measured from. Deliberately not named Self/Target - Target already
    // means the locked aim entity elsewhere (Aim.Target, ProjectileSpawnAnchor); this is a different
    // axis entirely.
    public enum SkillSpawnAnchor
    {
        // The caster's own live position (filter.Transform3D->Position) - right for a Begin/OnGoing
        // spawn, and for an End spawn on a skill that moves the caster there itself (DashSkillData).
        Caster = 0,

        // slot->TargetPosition - the skill's resolved destination, written by whichever side actually
        // knows it: the caster at Begin for a skill that decides its own destination up front
        // (DashSkillData), or later by whatever finishes the skill for one that doesn't (ProjectileSystem
        // writes the real impact/expiry point when the firing projectile terminates - see
        // ProjectileSkillData). Right for an End spawn on a skill where the caster's live position has
        // since diverged from where the skill actually landed.
        SkillDestination = 1
    }

    // Drops a prefab into the world: a decoy at Begin, a fire trail segment every Spacing of travel
    // while OnGoing, a blast at End. What the spawned thing then does is the prototype's own business - a
    // prefab carrying an AreaDamage hurts whoever stands in it, one carrying a Decoy pulls enemies,
    // one with neither just sits there - so decoy vs fire vs explosion is a different prefab here,
    // not a different class.
    //
    // This is also all an area hit needs: an explosion is an AreaDamage whose Duration is short
    // enough to tick once, and "hit everything the dash swept through" is that same blast with
    // FitToPath. Neither wants an action of its own - the difference is a Duration and a prefab.
    public unsafe partial class SpawnEntitySkillAction : SkillActionData
    {
        public AssetRef<EntityPrototype> Prototype;

        // How long the spawn lives, stretched by the caster's SkillDurationMultiplier since a skill
        // spawned it - see SpawnedEntitySpawner. Short enough to cover a single tick makes the spawn
        // an instant blast rather than a lingering area.
        public FP Duration = 3;

        public SpawnAlignment Alignment = SpawnAlignment.Facing;

        // See SkillSpawnAnchor. Matters most for an End-phase spawn on a skill that doesn't move the
        // caster (ProjectileSkillData) - Caster would spawn wherever the caster has wandered to by
        // the time the shot lands, not where it actually hit.
        public SkillSpawnAnchor Anchor = SkillSpawnAnchor.Caster;

        public FPVector3 Offset;

        // Resizes the prototype's authored collider, so one prefab covers a big and a small version.
        // Quantum's Transform3D is position and rotation only - it has no scale - so this is the
        // whole of what scaling means in the simulation. What the spawn *looks* like is the view's
        // own business and does not follow this.
        //
        // Ignored by FitToPath, which builds its shape from the path and Width/Height instead.
        public FPVector3 Scale = FPVector3.One;

        // OnGoing only: spawn one entity per Spacing units the caster travels - the distance analog
        // of the base's time Interval, so a dash drops a cube every Spacing of path rather than every
        // Spacing of seconds. Measured off the real ground covered (SkillSystem's TravelledDistance),
        // so a path bent or cut short by a wall still spaces its spawns evenly. Zero or less falls
        // back to the inherited Interval (time-based) instead - see IsDueThisTick - so one asset can
        // pace itself by distance or by rate, whichever fits the skill.
        public FP Spacing = 1;

        // Everything else the spawn does is authored on the prototype's own AreaDamage (see
        // SpawnedEntitySpawner) - these two are the exception, so one prototype (e.g. a generic
        // "lingering patch") can be reused by different skills/upgrades that each want their own
        // Damage or TargetMask instead of needing a separate prototype per config. Unchecked, the
        // prototype's own authored value is left untouched.
        [Header("Overrides")]
        public bool OverrideDamage;
        public FP Damage = 10;

        public bool OverrideTargetMask;
        public DamageTargetMask TargetMask = DamageTargetMask.Both;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            FPVector3 anchorPosition = Anchor == SkillSpawnAnchor.SkillDestination ? slot->TargetPosition : filter.Transform3D->Position;
            FPVector3 position = anchorPosition + ResolveOffset(ref filter);
            EntityRef spawned = SpawnAt(f, filter.Entity, position);

            Align(f, ref filter, slot, spawned);
            ApplyScale(f, spawned, slot);

            Log.Debug($"[Skill] {filter.Entity} spawned {spawned} at {position} on {firedPhase}");
        }

        // Distance analog of the base's time pacing: the same boundary-crossing test, TravelledDistance
        // standing in for ActiveTime and this tick's step for DeltaTime (both on the slot, so two spawn
        // actions can run different Spacings off one activation). A single tick is assumed shorter than
        // Spacing - a step covering several boundaries still spawns once, which any normal move speed
        // (a dash steps well under a unit a tick) never hits. Spacing <= 0 defers to the base's own
        // Interval-based pacing instead of firing every tick, so leaving Spacing unset makes this a
        // plain rate-paced spawn (or an every-tick one, if Interval is also left at 0 - same as base).
        protected override bool IsDueThisTick(Frame f, SkillSlot* slot)
        {
            if (Spacing <= FP._0)
                return base.IsDueThisTick(f, slot);

            FP travelled = slot->TravelledDistance;
            FP step = slot->LastStepDistance;

            return FPMath.FloorToInt(travelled / Spacing) > FPMath.FloorToInt((travelled - step) / Spacing);
        }

        // Turned by the caster's facing so an authored "forward" offset follows them around. A Y
        // offset is unaffected either way, which is how a path-fitted box gets lifted off the floor.
        private FPVector3 ResolveOffset(ref SkillSystem.Filter filter)
        {
            if (Offset == default)
                return default;

            return FPQuaternion.Euler(0, filter.Aim->Angle, 0) * Offset;
        }

        private void Align(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, EntityRef spawned)
        {
            if (TryResolveRotation(ref filter, slot, out FPQuaternion rotation) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(spawned, out var transform) == true)
            {
                transform->Rotation = rotation;
            }
        }

        // False leaves whatever rotation the prototype was authored with - which is also the fallback
        // when a heading was asked for but none exists yet.
        private bool TryResolveRotation(ref SkillSystem.Filter filter, SkillSlot* slot, out FPQuaternion rotation)
        {
            rotation = default;

            switch (Alignment)
            {
                case SpawnAlignment.Facing:
                    rotation = FPQuaternion.Euler(0, filter.Aim->Angle, 0);
                    return true;

                case SpawnAlignment.Path:
                    FPVector3 delta = filter.Transform3D->Position - slot->StartPosition;

                    // A Begin spawn has travelled nothing yet, so the destination the skill just
                    // committed to is the only heading available.
                    if (delta.SqrMagnitude <= FP._0)
                        delta = slot->TargetPosition - slot->StartPosition;

                    if (delta.SqrMagnitude <= FP._0)
                        return false;

                    rotation = FPQuaternion.LookRotation(delta.Normalized, FPVector3.Up);
                    return true;

                default:
                    return false;
            }
        }

        // Scaling a spawn means resizing its collider, since Quantum has no transform scale. Each
        // shape takes what its own parameters can express: only a box has three independent axes.
        // Folds in slot->AreaMultiplier (see IncreaseAreaSkillAction) on top of the authored Scale,
        // so the zero-component early-out below has to test the combined value, not just Scale - an
        // unscaled (One) spawn still has to grow when a multiplier is active.
        private void ApplyScale(Frame f, EntityRef spawned, SkillSlot* slot)
        {
            FPVector3 scale = Scale * slot->AreaMultiplier;

            // A zero component would collapse the collider to nothing, which is never what an author
            // meant - treat an unscaled result as unscaled rather than silently deleting what hurts.
            if (scale == FPVector3.One || scale.X <= FP._0 || scale.Y <= FP._0 || scale.Z <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(spawned, out var collider) == false)
                return;

            switch (collider->Shape.Type)
            {
                case Shape3DType.Box:
                    collider->Shape.Box.Extents = FPVector3.Scale(collider->Shape.Box.Extents, scale);
                    break;

                case Shape3DType.Sphere:
                    // One radius can't hold three axes - the largest wins, so a lopsided Scale grows
                    // a sphere to fit rather than quietly picking an axis and ignoring the rest.
                    collider->Shape.Sphere.Radius *= FPMath.Max(scale.X, FPMath.Max(scale.Y, scale.Z));
                    break;

                case Shape3DType.Capsule:
                    collider->Shape.Capsule.Radius *= FPMath.Max(scale.X, scale.Z);
                    collider->Shape.Capsule.Extent *= scale.Y;
                    break;

                default:
                    Log.Error($"[Skill] {spawned} has a {collider->Shape.Type} collider - Scale only " +
                              $"applies to Box, Sphere and Capsule, so it spawned unscaled");
                    break;
            }
        }

        private EntityRef SpawnAt(Frame f, EntityRef owner, FPVector3 position)
        {
            FP? damageOverride = OverrideDamage == true ? Damage : (FP?)null;
            DamageTargetMask? targetMaskOverride = OverrideTargetMask == true ? TargetMask : (DamageTargetMask?)null;

            return SpawnedEntitySpawner.Spawn(f, owner, Prototype, Duration, position, DamageSource.Skill,
                damageOverride: damageOverride, targetMaskOverride: targetMaskOverride);
        }
    }
}
