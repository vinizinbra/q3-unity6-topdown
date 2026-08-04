namespace Quantum
{
    using Photon.Deterministic;

    // Plain C# enum - never persisted (see RiftMarkApplicationRequest's own comment for why). One
    // entry per mechanic that can request a Rift Mark application - request-object identity/debug
    // logging only, never used to index anything (that's RiftMarkCooldownKey's job below). See
    // docs/weapon-perks.md/docs/rift-mutations.md for what each source is.
    public enum RiftMarkApplicationSource : byte
    {
        None,
        WeaponPerkFractureRounds,
        WeaponPerkCriticalFracture,
        WeaponPerkUnstablePayload,
        WeaponPerkFocusedBreach,
        WeaponPerkRiftAftershock,
        MutationCriticalFracture,
        MutationSkillFracture,
        MutationRiftDash,
        MutationHeavyFracture,
        MutationCloseFracture,
        MutationLongFracture,
        MutationExecutionFracture,
        MutationFirstContact,
        MutationLastStand,
        MutationFracturedPresence,
        MutationOverflowingRift,
    }

    // Indexes StatusEffects.MarkApplicationCooldowns[8] - shared per-target cooldown slots for
    // mechanics that need "don't reapply against the same target too often" gating. None means "no
    // shared-array cooldown" - Fracture Rounds/Unstable Payload/Rift Aftershock/Rift Dash/First
    // Contact/Last Stand/Overflowing Rift each have their own dedicated state instead (a per-weapon
    // counter, a per-explosion-event natural once-each, a per-dash tracker, a one-time flag, a
    // per-player cooldown, or their own cooldown field) - see docs/rift-mutations.md.
    //
    // CriticalFracture is deliberately shared by BOTH the Weapon Perk and the Rift Mutation version -
    // this single shared slot is what makes "should not both add separate stacks from the same
    // critical hit" fall out for free, rather than needing a bespoke cross-mechanic merge step.
    public enum RiftMarkCooldownKey : byte
    {
        None,
        CriticalFracture,
        SkillFracture,
        HeavyFracture,
        CloseFracture,
        LongFracture,
        ExecutionFracture,
        FocusedBreach,
        FracturedPresence,
    }

    // A single hit/event's request to apply Rift Mark - collected and resolved entirely within one
    // synchronous call chain (never crosses a frame boundary), so this is a plain transient value
    // type, not a persisted Quantum component - same reasoning HitEffectContext already uses.
    // HitSequence exists for analytics/presentation payloads, not as load-bearing dedup state - since
    // every request is resolved synchronously, object identity of the call chain already provides the
    // uniqueness the dedup policy needs. See docs/rift-mutations.md's "Duplicate-application policy".
    public struct RiftMarkApplicationRequest
    {
        public EntityRef Source;
        public EntityRef Target;
        public int HitSequence;
        public RiftMarkApplicationSource ApplicationSource;
        public byte RequestedStacks;
        public EntityRef Owner;
        public RiftMarkCooldownKey CooldownKey;
    }

    public static unsafe class RiftMarkApplicationUtility
    {
        // Checked-then-set atomic, identical shape to every *CooldownRemaining gate in
        // StatusEffectUtility - Quantum's single-threaded, ordered per-frame system execution means
        // nothing can interleave between the check and the write within one tick, so this alone is
        // what prevents a single hit/event from generating more than one application through the same
        // cooldown key (including the Weapon-Perk-vs-Rift-Mutation Critical Fracture case above).
        // key == None always succeeds (mechanics with their own dedicated state pass None here).
        public static bool TryConsumeCooldown(StatusEffects* status, RiftMarkCooldownKey key, FP cooldownDuration)
        {
            if (key == RiftMarkCooldownKey.None)
                return true;

            int index = (int)key - 1;

            if (status->MarkApplicationCooldowns[index] > FP._0)
                return false;

            status->MarkApplicationCooldowns[index] = cooldownDuration;
            return true;
        }

        // Applies one already-validated request (caller is responsible for having already checked
        // eligibility/cooldown). Handles the Overflowing Rift branch inline: if the target was already
        // at MaxStacks before this application, ApplyRiftMark's own clamp leaves stacks unchanged (and
        // still refreshes duration, satisfying that rule) - this additionally fires the pulse when the
        // requesting player has the mutation active, instead of the application silently doing nothing
        // player-visible. See docs/rift-mutations.md.
        public static void ApplyRequest(Frame f, in RiftMarkApplicationRequest request, ElementalReactionConfig config)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(request.Target, out var status) == false)
                return;

            bool wasAtMax = status->RiftMarkStacks >= config.MaxStacks;

            FP duration = StatusEffectUtility.ScaleDuration(f, request.Owner, DamageSource.Skill, config.BaseDuration);
            StatusEffectUtility.ApplyRiftMark(f, request.Target, config, duration, request.RequestedStacks);

            Log.Debug($"[RiftMark] {request.ApplicationSource} applied to {request.Target} by {request.Owner} (hit #{request.HitSequence})");

            if (wasAtMax)
                TryTriggerOverflowingRift(f, status, request, config);
        }

        // Overflowing Rift (Rift Mutation) - see docs/rift-mutations.md. Explicitly does not stack a
        // 3rd mark (ApplyRequest above already left stacks clamped), does not consume a stack, and
        // cannot recursively re-trigger itself since it never calls back into ApplyRiftMark/
        // ApplyRequest - only DamageUtility.ApplyDamage for the pulse's own light damage.
        private static void TryTriggerOverflowingRift(Frame f, StatusEffects* status, in RiftMarkApplicationRequest request, ElementalReactionConfig config)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(request.Owner, out var stats) == false || stats->HasOverflowingRiftMutation == false)
                return;

            if (status->OverflowingRiftCooldownRemaining > FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(request.Target, out var transform) == false)
                return;

            status->OverflowingRiftCooldownRemaining = config.OverflowingRiftCooldown;

            HitEffectUtility.ApplyDamageInRadius(f, transform->Position, config.OverflowingRiftPulseRadius, request.Owner,
                config.OverflowingRiftPulseDamage, DamageSource.Skill, DamageTargetMask.Enemies);

            f.Events.OverflowingRiftTriggered(request.Owner, transform->Position, config.OverflowingRiftPulseRadius);

            Log.Debug($"[RiftMark] {request.Target} Overflowing Rift pulse triggered by {request.Owner}");
        }
    }
}
