namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Runs a hit on everyone the skill swept past - a dash that knocks back what it runs through.
    // Composed of the same HitEffectData assets a projectile impact uses, so what it does to each
    // target is data (knock back, damage, both) rather than fields here.
    //
    // The shape follows the phase, because mid-skill there is no finished path to span:
    //
    // Begin and End sweep a box over the whole path, off different endpoints - see ResolvePath. Begin
    // lands the hit up front on everyone standing in the way, which is what a dash that shoves through
    // a crowd wants: by End the crowd has already been pushed aside by the caster's own collider and
    // the box finds nobody. Phase = Begin | End fires it twice, once against each path, which is two
    // hits on anyone caught in both.
    //
    // OnGoing pulses a Radius sphere around the caster each tick instead - an aura dragged along the
    // path rather than one query over it. It re-hits whoever stays in range, so it wants Interval to
    // pace it and Effects that are worth repeating (damage, not a one-shot knockback). It also tunnels
    // past anyone the caster steps clean over between two ticks, which the box cannot do - so this is
    // the mode for "burns what it drags through", not for "catches everything on the way".
    //
    // Where the path should also be seen or should linger (a fire trail), SpawnEntitySkillAction
    // spawns a real entity instead - that is what an entity buys, not this.
    public unsafe partial class HitPathSkillAction : SkillActionData
    {
        [ExpandableAsset] public List<AssetRef<HitEffectData>> Effects = new();

        // Seeds HitEffectContext.Damage; the Effects scale off it. The caster's skill multipliers
        // apply on top via DamageSource.Skill - see DamageUtility.ResolveOutgoingDamage.
        public FP Damage;

        // Begin/End only: the path supplies the box's length, these supply its cross-section.
        public FP Width = 1;
        public FP Height = 1;

        // OnGoing only: how far the caster's aura reaches.
        public FP Radius = 1;

        public HitPathSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.OnGoing)
            {
                HitAroundCaster(f, ref filter, slot);
                return;
            }

            HitAlongPath(f, ref filter, slot, firedPhase);
        }

        // Lifted by Radius so the sphere sits on the caster's body rather than half in the floor -
        // same reason the box gets its own lift below. Radial push (ApplyInRadius' default) is what an
        // aura wants: shoved away from the caster, not along a path this query never measured.
        private void HitAroundCaster(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            FP radius = Radius * slot->AreaMultiplier;
            FPVector3 center = filter.Transform3D->Position + FPVector3.Up * radius;

            HitEffectUtility.ApplyInRadius(f, Effects, center, radius, filter.Entity, Damage,
                DamageSource.Skill);

            Log.Debug($"[Skill] {filter.Entity} pulsed radius {radius} at {center}");
        }

        private void HitAlongPath(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillActionPhase firedPhase)
        {
            FPVector3 delta = ResolvePath(ref filter, slot, firedPhase);
            FP length = delta.Magnitude;

            // A skill that swept nothing - a zero-length box would catch nobody anyway.
            if (length <= FP._0)
                return;

            FPVector3 direction = delta / length;
            FP width = Width * slot->AreaMultiplier;
            FP height = Height * slot->AreaMultiplier;

            // Lifted to body height: the path runs along the caster's feet, so a box centered on it
            // sits half underground and catches the floor instead of what it swept past. Same lift
            // DashSkillData applies to its own wall check.
            FPVector3 center = slot->StartPosition + delta / 2 + FPVector3.Up * (height / 2);

            // Shape3D box extents are half-sizes, and LookRotation puts the length on Z.
            Shape3D box = Shape3D.CreateBox(new FPVector3(width, height, length) / 2);
            FPQuaternion rotation = FPQuaternion.LookRotation(direction, FPVector3.Up);

            HitEffectUtility.ApplyInShape(f, Effects, center, rotation, box, filter.Entity, Damage,
                DamageSource.Skill, direction);

            Log.Debug($"[Skill] {filter.Entity} swept {length} from {slot->StartPosition} toward {direction} on {firedPhase}");
        }

        // Begin runs before the caster has travelled anything (SkillSystem calls it right after
        // SkillData.Begin commits a destination), so the path can only be the one just planned; every
        // later phase measures the one actually covered, which is shorter whenever a wall cut the dash
        // short. Not SpawnEntitySkillAction's "zero delta falls back to TargetPosition" - a dash
        // blocked on its first tick ends where it started, and that fallback would silently sweep the
        // full planned length straight through the wall.
        private FPVector3 ResolvePath(ref SkillSystem.Filter filter, SkillSlot* slot, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
                return slot->TargetPosition - slot->StartPosition;

            return filter.Transform3D->Position - slot->StartPosition;
        }
    }
}
