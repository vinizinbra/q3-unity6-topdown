using System.Collections;
using System.Collections.Generic;
using Photon.Deterministic;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Generic dispatcher for SkillActionData's BeginFx/OnGoingFx/EndFx (see SkillActionData.View.cs)
    // - the skill-upgrade equivalent of EnemyAttackVisualsView, one component per player instead of
    // per-action code. Purely event-driven (SkillActionBeginExecuted/OnGoingExecuted/EndExecuted,
    // fired by SkillSystem.Invoke - see that file) rather than polling SkillSlot.State like
    // DashFxView/BerserkFxView: those two care about one specific fixed skill, this one has to react
    // to whichever of up to 5 Upgrades + the baseline skill's own Actions happen to have FX
    // configured, which isn't something worth re-deriving here when SkillSystem already resolves it
    // every tick to decide whether to fire the event at all.
    public class SkillActionFxView : CustomQuantumEntityViewComponent
    {
        private class HeldFx
        {
            public ParticleSystem Prefab;
            public ParticleSystem Instance;
            public bool Parented;
            public Vector3 Offset;
            public SkillFxAlignment Alignment;
        }

        // Keyed by the resolved SkillActionData instance (Quantum's asset DB hands back the same
        // C# object for the same AssetRef within a session, so reference equality is stable) rather
        // than AssetRef<SkillActionData> itself, sidestepping any assumption about that struct's own
        // equality implementation.
        private readonly Dictionary<SkillActionData, HeldFx> _held = new();

        public override void Awake()
        {
            base.Awake();

            QuantumEvent.Subscribe<EventSkillActionBeginExecuted>(this, OnBeginExecuted);
            QuantumEvent.Subscribe<EventSkillActionOnGoingExecuted>(this, OnOnGoingExecuted);
            QuantumEvent.Subscribe<EventSkillActionEndExecuted>(this, OnEndExecuted);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
            ReleaseAllHeldImmediate();
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            ReleaseAllHeldImmediate();
        }

        // A Parented HeldFx is a real child of this transform (see AcquireHeld), so its POSITION
        // would already follow for free via Unity's own hierarchy if Offset were zero - but a
        // rotating Alignment means Offset (e.g. "in front of the caster") has to keep rotating
        // around the caster too, not just spin the particle in place, so this still re-sets both
        // position and rotation every frame rather than leaving position to the parent alone. A
        // non-Parented HeldFx is deliberately fixed - both its position AND its facing were
        // snapshotted once at spawn (see AcquireHeld) - so it's skipped here entirely, same as
        // OneShot/Parented's own one-time snapshot.
        protected override void QUpdate(QuantumGame game)
        {
            if (_held.Count == 0)
                return;

            Frame frame = game.Frames.Verified;
            if (frame == null || frame.Has<Transform3D>(_entityRef) == false)
                return;

            Vector3 casterPosition = frame.Get<Transform3D>(_entityRef).Position.ToUnityVector3();

            foreach (HeldFx heldFx in _held.Values)
            {
                if (heldFx.Instance == null || heldFx.Parented == false || heldFx.Alignment == SkillFxAlignment.None)
                    continue;

                Quaternion yaw = ResolveYaw(frame, heldFx.Alignment);
                Quaternion rotation = yaw * heldFx.Prefab.transform.rotation;
                heldFx.Instance.transform.SetPositionAndRotation(casterPosition + yaw * heldFx.Offset, rotation);
            }
        }

        private void OnBeginExecuted(EventSkillActionBeginExecuted e)
        {
            if (e.Entity != _entityRef)
                return;

            HandlePhase(e.Game, e.Action, SkillActionPhase.Begin, e.Position);
        }

        private void OnOnGoingExecuted(EventSkillActionOnGoingExecuted e)
        {
            if (e.Entity != _entityRef)
                return;

            HandlePhase(e.Game, e.Action, SkillActionPhase.OnGoing, e.Position);
        }

        private void OnEndExecuted(EventSkillActionEndExecuted e)
        {
            if (e.Entity != _entityRef)
                return;

            Frame frame = e.Game.Frames.Predicted;

            if (frame != null)
                ReleaseHeld(frame.FindAsset(e.Action));

            HandlePhase(e.Game, e.Action, SkillActionPhase.End, e.Position);
        }

        private void HandlePhase(QuantumGame game, AssetRef<SkillActionData> actionRef, SkillActionPhase phase, FPVector3 position)
        {
            Frame frame = game.Frames.Predicted;
            if (frame == null)
                return;

            SkillActionData action = frame.FindAsset(actionRef);
            SkillFxStep step = action.ResolveFxStep(phase);

            if (step == null || step.ParticlePrefab == null)
                return;

            // ScaleByRadius is a full override, not another multiplier on the prefab's own authored
            // scale - same "authored at a reference radius of 1" convention EffectsManager's own
            // radius-scaled handlers already use (OnShockwaveReleased, OnHealPulseReleased, ...:
            // Vector3.one * e.Radius, not prefab.transform.localScale * e.Radius). Radius is a
            // gameplay-meaningful size (how far this actually reaches), so it has to win outright
            // over whatever the prefab happens to be authored at, not blend with it. Off keeps the
            // ordinary "multiply the prefab's own authored scale" behavior (see
            // EnemyAttackVisualsView.Scale) - Vector3.one * multiplier there would instead force
            // every particle to exactly that size regardless of how it was authored, collapsing
            // anything not already (1,1,1).
            Vector3 scale = step.ScaleByRadius == true
                ? Vector3.one * (action.EffectRadius.AsFloat * step.Scale)
                : step.ParticlePrefab.transform.localScale * step.Scale;
            Vector3 casterPosition = position.ToUnityVector3();

            // yaw is a pure Y-axis rotation representing the direction to face - used both to rotate
            // Offset (so "1 unit in front" means in front of whichever way Alignment resolved to,
            // not always world +Z) and combined with the prefab's OWN authored rotation below for
            // the actual applied rotation. Many ground-effect prefabs (e.g. a shockwave/wave sprite)
            // are authored with a baked tilt - a 90-degree rotation so the effect lies flat instead
            // of standing up like a camera-facing decal - see the comment on `rotation` below for why
            // that tilt has to be preserved rather than discarded.
            Quaternion yaw = ResolveYaw(frame, step.Alignment);
            Vector3 worldPosition = casterPosition + yaw * step.Offset;

            // Composed as yaw * the prefab's OWN authored rotation, not yaw alone (which is what a
            // plain Quaternion.LookRotation(direction, Vector3.up) would produce) - that would force
            // every particle upright/camera-facing, destroying a ground-effect prefab's own baked
            // tilt (e.g. SwordWaveBlue is authored at -90 degrees X specifically so it lies flat).
            // yaw*identity==yaw for a prefab with no baked tilt, so untilted prefabs are unaffected.
            Quaternion rotation = yaw * step.ParticlePrefab.transform.rotation;

            switch (step.SpawnMode)
            {
                case SkillFxSpawnMode.OneShot:
                    PlayOneShot(step.ParticlePrefab, worldPosition, rotation, scale);
                    break;

                case SkillFxSpawnMode.Parented:
                    SpawnParented(step.ParticlePrefab, worldPosition, rotation, scale);
                    break;

                case SkillFxSpawnMode.HeldUntilEnd:
                    AcquireHeld(action, step, worldPosition, rotation, scale);
                    break;
            }
        }

        // Pure Y-axis rotation, not a full LookRotation - see the comment on HandlePhase's own
        // `rotation` local for why: composing this with a prefab's own authored rotation (which may
        // carry a baked tilt) is what actually gets applied, so this only ever needs to describe the
        // horizontal direction to face, never the prefab's vertical tilt.
        private Quaternion ResolveYaw(Frame frame, SkillFxAlignment alignment)
        {
            switch (alignment)
            {
                case SkillFxAlignment.AimDirection: return Quaternion.AngleAxis(ResolveAimAngle(frame), Vector3.up);
                case SkillFxAlignment.DashDirection: return Quaternion.AngleAxis(ResolveDashAngle(frame), Vector3.up);
                default: return Quaternion.identity;
            }
        }

        // Same source EnemyAttackVisualsView.ResolveEnemyDirectionRotation reads off Enemy.Phase's
        // aim, just off the player's own Aim component instead - Angle is already in the same
        // atan2(X, Z) degrees convention Quaternion.AngleAxis(_, Vector3.up) expects (0 = +Z, 90 = +X
        // - see AimSystem.Update), so no Sin/Cos reconstruction needed.
        private float ResolveAimAngle(Frame frame)
        {
            if (frame.Has<Aim>(_entityRef) == false)
                return 0f;

            return frame.Get<Aim>(_entityRef).Angle.AsFloat;
        }

        // DashSkill's own StartPosition->TargetPosition (see DashSkillData.Begin) - fixed for the
        // whole dash, no re-homing mid-flight (DashSkillData never rewrites TargetPosition after
        // Begin), so this reads the same throughout OnGoing/End regardless of how far the dash has
        // actually travelled yet. Falls back to Aim direction when that delta isn't resolved:
        // SkillSystem.TryBegin resets TargetPosition to StartPosition (zero delta) before firing THIS
        // activation's own BeginFx, and DashSkillData.Begin (which writes the real TargetPosition)
        // doesn't run until just after - so a BeginFx step asking for DashDirection genuinely has no
        // dash direction to read yet at that exact instant.
        private float ResolveDashAngle(Frame frame)
        {
            if (frame.Has<CharacterSkills>(_entityRef) == false)
                return ResolveAimAngle(frame);

            SkillSlot dash = frame.Get<CharacterSkills>(_entityRef).DashSkill;
            FPVector3 delta = dash.TargetPosition - dash.StartPosition;

            if (delta.SqrMagnitude <= FP._0_01)
                return ResolveAimAngle(frame);

            // Same atan2(X, Z) * Rad2Deg convention AimSystem.Update uses for Aim.Angle itself, so
            // this composes identically to a live Aim.Angle read - just reconstructed from the
            // dash's own fixed delta instead.
            return (FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg).AsFloat;
        }

        private static void PlayOneShot(ParticleSystem prefab, Vector3 worldPosition, Quaternion rotation, Vector3 scale)
        {
            if (EffectsManager.Instance == null)
                return;

            EffectsManager.Instance.PlayEffect(prefab, worldPosition, rotation, scale);
        }

        // Parented purely for tracking the caster's POSITION as they move - rotation is still set
        // via the world-space setter below (not SetLocalPositionAndRotation), since treating an
        // already-world-space Alignment rotation as this instance's LOCAL rotation would compound
        // with whatever rotation the caster's own transform happens to have instead of matching
        // Alignment exactly.
        private void SpawnParented(ParticleSystem prefab, Vector3 worldPosition, Quaternion rotation, Vector3 scale)
        {
            GameObject instance = Instantiate(prefab.gameObject, transform);
            instance.transform.SetPositionAndRotation(worldPosition, rotation);
            instance.transform.localScale = scale;
            instance.AddComponent<ParticleAutoDestroy>();
        }

        // Parent==true makes this a real child of this transform so it keeps tracking the caster's
        // position for free between QUpdate's own per-frame re-sets (see QUpdate) - still positioned/
        // rotated via the world-space setter here too, for the same reason SpawnParented uses it
        // instead of SetLocalPositionAndRotation. Parent==false instead sets a plain world
        // position/rotation once and never touches it again - a zone effect left behind at the cast
        // position, held (and eventually released) the same way but never following.
        private void AcquireHeld(SkillActionData action, SkillFxStep step, Vector3 worldPosition, Quaternion rotation, Vector3 scale)
        {
            // Idempotent - an interval-paced OnGoingFx re-fires this every due tick for as long as
            // the skill stays Active, but should only ever hold one instance per action at a time.
            if (_held.ContainsKey(action) == true || EffectsManager.Instance == null)
                return;

            ParticleSystem instance = EffectsManager.Instance.GetHeldInstance(step.ParticlePrefab);
            if (instance == null)
                return;

            if (step.Parent == true)
                instance.transform.SetParent(transform, worldPositionStays: false);

            instance.transform.SetPositionAndRotation(worldPosition, rotation);
            instance.transform.localScale = scale;
            instance.Play();

            _held[action] = new HeldFx
            {
                Prefab = step.ParticlePrefab,
                Instance = instance,
                Parented = step.Parent,
                Offset = step.Offset,
                Alignment = step.Alignment,
            };
        }

        // Stops emission, waits for whatever's still alive to finish naturally (not
        // StopEmittingAndClear - an abrupt cut looks wrong for a held effect that's been visible for
        // a while), THEN unparents and releases back to EffectsManager's pool. Unparenting first
        // matters: a pooled instance released while still parented under this (possibly
        // about-to-despawn) character would get destroyed along with it the next time this entity
        // goes away, silently corrupting the shared pool for every other user of the same prefab.
        private void ReleaseHeld(SkillActionData action)
        {
            if (_held.TryGetValue(action, out HeldFx heldFx) == false)
                return;

            _held.Remove(action);
            StartCoroutine(StopThenRelease(heldFx));
        }

        private IEnumerator StopThenRelease(HeldFx heldFx)
        {
            if (heldFx.Instance != null)
            {
                heldFx.Instance.Stop(true, ParticleSystemStopBehavior.StopEmitting);

                while (heldFx.Instance != null && heldFx.Instance.IsAlive(true) == true)
                    yield return null;
            }

            ReparentAndRelease(heldFx);
        }

        // Safety-net path for teardown (DeInitialize/OnDestroy) - immediate, not graceful: this
        // component (and likely the coroutines StopThenRelease would keep running on) is going away
        // regardless, so there's nothing to gracefully wait on.
        private void ReleaseAllHeldImmediate()
        {
            foreach (HeldFx heldFx in _held.Values)
            {
                heldFx.Instance?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ReparentAndRelease(heldFx);
            }

            _held.Clear();
        }

        private static void ReparentAndRelease(HeldFx heldFx)
        {
            if (heldFx.Instance != null)
            {
                Transform releaseParent = EffectsManager.Instance != null ? EffectsManager.Instance.transform : null;
                heldFx.Instance.transform.SetParent(releaseParent, worldPositionStays: true);
            }

            EffectsManager.Instance?.ReleaseHeldInstance(heldFx.Prefab, heldFx.Instance);
        }
    }
}
