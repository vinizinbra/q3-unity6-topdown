using System.Collections.Generic;
using Photon.Deterministic;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Per-entity view for an autonomous Sentry's "chassis" (e.g. Lux's sentry gun) - wires up its
    // HUD health widget (SentryUiWidgetManager), same Initialize/DeInitialize hook shape EnemyView
    // uses for its own widget manager, activates the gun sprite matching each SentryBarrel slot as
    // it's actually armed, and keeps a direct Transform reference to each spawned barrel's own View
    // (see EventSentryBarrelSpawned) - a barrel is its own separate entity (SentryBarrel/SentryGunView
    // own its rotation/firing), this is purely the chassis's own bookkeeping of where they ended up.
    //
    // No OnEntityDied early-despawn hook like EnemyView has: a Sentry has no Enemy component, so
    // DamageUtility.ApplyDamage destroys it immediately on death instead of lingering as a corpse -
    // DeInitialize already fires right away.
    //
    // Also drives a health-threshold "damage shake" on shakeTarget (see UpdateDamageShake): a single
    // burst the moment health first drops below shakeOnceThreshold, then a continuous shake that
    // escalates in two steps (mildShakeThreshold, harshShakeThreshold) as health keeps falling -
    // read directly off this entity's own Health each frame rather than reacting to EventEntityDamaged,
    // since the tier depends on the current fraction, not on any single hit.
    public class SentryView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("One entry per SentryWeaponUpgrade slot (index 0-3) - activated when EventSentryBarrelSpawned reports that slot armed for this entity. Leave entries unassigned for slots this sentry prefab doesn't visually support.")]
        private List<GameObject> gunSprites = new List<GameObject>();

        [SerializeField, Tooltip("Hide every tentacle ARM at spawn and reveal each one only when its own weapon slot actually arms (EventSentryBarrelSpawned), so a sentry with an empty slot doesn't wave an arm holding nothing. Only ever touches tentacle indices 0-3 - one per SentryWeaponUpgrade slot, the same index SetPinnedTarget already pins by - so any extra legs a future chassis authors past those are left alone. Turn OFF for a rig whose first four tentacles are structural walking legs rather than arms.")]
        private bool hideUnarmedArms = true;

        [SerializeField, Tooltip("The sentry's own leg rig, if it has one (e.g. a spider-like chassis standing on ProceduralTentacleWalker2D legs). Falls back to GetComponentInChildren if left empty. Each spawned barrel's slot index pins that same-indexed tentacle onto the barrel's own Transform - see OnSentryBarrelSpawned.")]
        private ProceduralTentacleWalker2D tentacleWalker;

        [SerializeField, Tooltip("Played only while this sentry actually has Fortification's Covering Fire (SentryFortificationUpgrade.GuardDuration above 0) - left stopped/inactive otherwise. Its own transform is scaled to match the aura's real reach (Sentry.Range * SentryFortificationUpgrade.AuraRangeRatio) every frame, since that's the exact radius SentryAuraSystem uses to find allies.")]
        private ParticleSystem shieldAreaParticle;

        [SerializeField, Tooltip("Multiplies Sentry.Range when sizing shieldAreaParticle's own transform - tune to match however the particle's shape/size was authored. Defaults to half of Range.")]
        private float shieldAreaScaleMultiplier = 0.5f;

        private bool shieldAreaParticleActive;

        [SerializeField, Tooltip("Played only while this sentry actually has Fortification's Fire Support (SentryFortificationUpgrade.FireSupportEffect assigned) - left stopped/inactive otherwise. Same aura radius as the Shield Battery ring above; both halves share one aura now.")]
        private ParticleSystem fireRateAreaParticle;

        [SerializeField, Tooltip("Multiplies Sentry.Range when sizing fireRateAreaParticle's own transform - tune to match however the particle's shape/size was authored. Defaults to the full Range.")]
        private float fireRateAreaScaleMultiplier = 1f;

        private bool fireRateAreaParticleActive;

        [Header("Damage Shake (reads this entity's own Health)")]
        [SerializeField, Tooltip("Child transform that shakes as health drops - not this entity's own root, which QuantumEntityView drives every frame. Left unassigned disables all shaking.")]
        private Transform shakeTarget;

        [SerializeField, Range(0f, 1f), Tooltip("Health fraction that triggers a single shake burst, once, on the way down.")]
        private float shakeOnceThreshold = 0.5f;
        [SerializeField] private Vector3 shakeOnceStrength = new Vector3(0.06f, 0.06f, 0f);
        [SerializeField] private float shakeOnceDuration = 0.3f;
        [SerializeField] private float shakeOnceFrequency = 20f;

        [SerializeField, Range(0f, 1f), Tooltip("Health fraction below which a continuous mild shake starts.")]
        private float mildShakeThreshold = 0.25f;
        [SerializeField] private Vector3 mildShakeStrength = new Vector3(0.03f, 0.03f, 0f);
        [SerializeField] private float mildShakeFrequency = 14f;

        [SerializeField, Range(0f, 1f), Tooltip("Health fraction below which the continuous shake intensifies - broken-motor territory.")]
        private float harshShakeThreshold = 0.1f;
        [SerializeField] private Vector3 harshShakeStrength = new Vector3(0.09f, 0.09f, 0f);
        [SerializeField] private float harshShakeFrequency = 26f;

        private enum ShakeTier { None, Mild, Harsh }
        private ShakeTier shakeTier = ShakeTier.None;
        private bool shakeOnceFired;
        private Vector3 shakeTargetBaseScale;
        private Tween continuousShake;

        // One entry per SentryWeaponUpgrade slot, resolved via QuantumEntityViewUpdater.GetView the
        // instant EventSentryBarrelSpawned reports that barrel for this sentry - null until then (or
        // if that barrel's own View genuinely hasn't been instantiated yet the same tick it fires).
        private readonly Transform[] barrelTransforms = new Transform[4];

        public Transform GetBarrelTransform(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < barrelTransforms.Length ? barrelTransforms[slotIndex] : null;
        }

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventSentryBarrelSpawned>(this, OnSentryBarrelSpawned);

            if (tentacleWalker == null)
                tentacleWalker = GetComponentInChildren<ProceduralTentacleWalker2D>();

            foreach (GameObject gunSprite in gunSprites)
            {
                if (gunSprite != null)
                    gunSprite.SetActive(false);
            }

            // Same shape as the gunSprites loop above - everything off up front, each slot switched
            // back on by its own EventSentryBarrelSpawned. A slot that never arms simply never fires
            // that event, so its arm stays hidden for this sentry's whole life. Bounded by
            // barrelTransforms.Length rather than by the walker's own tentacle count, so only the
            // arms that map to a weapon slot are ever touched.
            if (hideUnarmedArms == true && tentacleWalker != null)
            {
                for (int i = 0; i < barrelTransforms.Length; i++)
                    tentacleWalker.SetTentacleVisible(i, false);
            }

            if (shieldAreaParticle != null)
                shieldAreaParticle.gameObject.SetActive(false);

            if (fireRateAreaParticle != null)
                fireRateAreaParticle.gameObject.SetActive(false);

            if (shakeTarget != null)
                shakeTargetBaseScale = shakeTarget.localScale;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);

            // Both the one-shot shakeOnceStrength burst and the continuous mild/harsh shake target
            // shakeTarget directly (not this) and can still be running when the sentry dies - without
            // this, PrimeTween logs a stack-trace-capturing error per orphaned tween every time that
            // happens. continuousShake.Stop() alone (used on tier change above) isn't enough here
            // since it doesn't cover the untracked one-shot burst.
            if (shakeTarget != null)
                Tween.StopAll(shakeTarget);
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            SentryUiWidgetManager.Instance?.SpawnWidget(_entityRef, game, transform);
        }

        public override void DeInitialize(QuantumGame game)
        {
            SentryUiWidgetManager.Instance?.DespawnWidget(_entityRef);
            base.DeInitialize(game);
        }

        protected override void QUpdate(QuantumGame game)
        {
            UpdateShieldAreaParticle(game.Frames.Predicted);
            UpdateFireRateAreaParticle(game.Frames.Predicted);
            UpdateDamageShake(game.Frames.Predicted);
        }

        private void UpdateDamageShake(Frame frame)
        {
            if (shakeTarget == null)
                return;

            if (frame.TryGet<Health>(_entityRef, out Health health) == false || health.MaxHealth <= FP._0)
                return;

            float fraction = (health.CurrentHealth / health.MaxHealth).AsFloat;

            // Latch resets once healed back above the threshold, so a heal-then-redamage pass
            // can trigger the one-time burst again instead of firing only ever once per entity.
            if (fraction > shakeOnceThreshold)
            {
                shakeOnceFired = false;
            }
            else if (shakeOnceFired == false)
            {
                shakeOnceFired = true;
                Tween.ShakeScale(shakeTarget, shakeOnceStrength, shakeOnceDuration, shakeOnceFrequency);
            }

            ShakeTier targetTier = fraction <= harshShakeThreshold ? ShakeTier.Harsh
                : fraction <= mildShakeThreshold ? ShakeTier.Mild
                : ShakeTier.None;

            if (targetTier == shakeTier)
                return;

            shakeTier = targetTier;
            continuousShake.Stop();
            shakeTarget.localScale = shakeTargetBaseScale;

            switch (targetTier)
            {
                case ShakeTier.Mild:
                    continuousShake = Tween.ShakeScale(shakeTarget, mildShakeStrength, 1f, mildShakeFrequency, enableFalloff: false, cycles: -1);
                    break;
                case ShakeTier.Harsh:
                    continuousShake = Tween.ShakeScale(shakeTarget, harshShakeStrength, 1f, harshShakeFrequency, enableFalloff: false, cycles: -1);
                    break;
            }
        }

        // Mirrors SentryAuraSystem's own fallback - an unauthored ratio means "the full range",
        // never a zero-radius aura.
        private static float ResolveAuraRatio(SentryFortificationUpgrade fortification)
        {
            return fortification.AuraRangeRatio > FP._0 ? fortification.AuraRangeRatio.AsFloat : 1f;
        }

        private void UpdateShieldAreaParticle(Frame frame)
        {
            if (shieldAreaParticle == null)
                return;

            // Fortification rank 2 - present only once Covering Fire is actually active on this
            // machine (a Fortification rank 1 sentry carries the component but grants nothing).
            bool hasCoveringFire = frame.TryGet<SentryFortificationUpgrade>(_entityRef, out var shieldFortification)
                && shieldFortification.GuardDuration > FP._0;

            if (hasCoveringFire == false)
            {
                if (shieldAreaParticleActive == true)
                {
                    shieldAreaParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    shieldAreaParticle.gameObject.SetActive(false);
                    shieldAreaParticleActive = false;
                }

                return;
            }

            // Scaled to the aura's REAL reach, not the sentry's targeting range - the two differ by
            // AuraRangeRatio, and a ring drawn at the wrong one reads as a bug to a player standing
            // just outside it (see SentryAuraSystem, which uses exactly this radius).
            float auraRange = frame.Get<Sentry>(_entityRef).Range.AsFloat * ResolveAuraRatio(shieldFortification);
            shieldAreaParticle.transform.localScale = Vector3.one * (auraRange * shieldAreaScaleMultiplier);

            if (shieldAreaParticleActive == false)
            {
                shieldAreaParticle.gameObject.SetActive(true);
                shieldAreaParticle.Play(true);
                shieldAreaParticleActive = true;
            }
        }

        private void UpdateFireRateAreaParticle(Frame frame)
        {
            if (fireRateAreaParticle == null)
                return;

            // Fortification rank 3 - present only once Fire Support is actually assigned.
            bool hasFireSupport = frame.TryGet<SentryFortificationUpgrade>(_entityRef, out var fireFortification)
                && fireFortification.FireSupportEffect.IsValid;

            if (hasFireSupport == false)
            {
                if (fireRateAreaParticleActive == true)
                {
                    fireRateAreaParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    fireRateAreaParticle.gameObject.SetActive(false);
                    fireRateAreaParticleActive = false;
                }

                return;
            }

            // Same aura radius as the Shield Battery ring - both halves of Fortification share one
            // aura now, rather than the old "fire rate reaches full range, shield only half".
            float fireAuraRange = frame.Get<Sentry>(_entityRef).Range.AsFloat * ResolveAuraRatio(fireFortification);
            fireRateAreaParticle.transform.localScale = Vector3.one * (fireAuraRange * fireRateAreaScaleMultiplier);

            if (fireRateAreaParticleActive == false)
            {
                fireRateAreaParticle.gameObject.SetActive(true);
                fireRateAreaParticle.Play(true);
                fireRateAreaParticleActive = true;
            }
        }

        // The point on a spawned barrel a tentacle should actually grab. Falls back to the barrel's
        // own root only when it has no SentryGunView at all (a barrel prefab that isn't a gun) - a
        // SentryGunView with no gripAnchor authored already falls back to its own visualRoot, which
        // still tracks the gun's motion, just at its pivot rather than a hand-placed spot on it.
        private static Transform ResolveGripTarget(QuantumEntityView barrelView)
        {
            SentryGunView gunView = barrelView.GetComponentInChildren<SentryGunView>();

            return gunView != null && gunView.GripAnchor != null ? gunView.GripAnchor : barrelView.transform;
        }

        private void OnSentryBarrelSpawned(EventSentryBarrelSpawned e)
        {
            if (e.Sentry != _entityRef)
                return;

            if (e.SlotIndex < gunSprites.Count && gunSprites[e.SlotIndex] != null)
            {
                gunSprites[e.SlotIndex].SetActive(true);
            }

            // Revealed HERE rather than beside the SetPinnedTarget call below, which sits after two
            // early returns (an out-of-range slot, and a barrel whose View isn't up yet) - this
            // event firing at all is what "this slot is armed" means, and an armed slot must show
            // its arm even on the tick where the pin itself can't be resolved.
            if (hideUnarmedArms == true)
                tentacleWalker?.SetTentacleVisible(e.SlotIndex, true);

            if (e.SlotIndex >= barrelTransforms.Length)
                return;

            QuantumEntityView barrelView = entityView.EntityViewUpdater.GetView(e.Barrel);

            if (barrelView == null)
            {
                LogHelper.Warn("Sentry", $"Barrel {e.Barrel} for slot {e.SlotIndex} has no View yet - GetView returned null the same tick it spawned.");
                return;
            }

            barrelTransforms[e.SlotIndex] = barrelView.transform;

            // Grips the same-indexed tentacle leg onto this barrel, replacing whatever fixed
            // homeTarget/pinnedTarget it was authored with - barrels are spawned dynamically (0-4 of
            // them, at whichever WeaponOffset each upgrade authored), so a static Inspector-wired pin
            // can't cover every possible loadout the way a runtime one can.
            //
            // Pinned to the GUN's own grip anchor, not to barrelView.transform. That root is only ever
            // the barrel entity's raw simulated position - SentryGunView applies the aim rotation, the
            // left-facing flip, the idle float and the shoot punch to a CHILD (visualRoot) instead. A
            // tentacle pinned to the root therefore reaches for a point the gun continuously moves away
            // from, which reads as the tentacle gesturing near the gun rather than holding it. This is
            // the sentry's counterpart to WeaponHandGripView, which resolves the player's hand grip
            // through the weapon's own live transform for exactly the same reason.
            tentacleWalker?.SetPinnedTarget(e.SlotIndex, ResolveGripTarget(barrelView));
        }
    }
}
