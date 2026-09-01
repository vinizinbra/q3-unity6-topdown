using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // The hero's own "I got hit" sound - there wasn't one before this. Lives on the same
    // CharFeedbacks root as SkillSoundView/HeroLevelUpView rather than as a HUD-side reaction
    // (HurtOverlayUiWidget), so every player in co-op plays it at their own position, at a volume
    // EntitySound.ResolveVolume already scales down for remote entities - not just the local one.
    //
    // Reacts to the same three "impact" events HitFeedback's own FlashDamage tier does: a normal
    // hit (EventEntityDamaged), an Accessory Guard block, and a Free Hit Guard consumption - the
    // latter two deal no damage and never reach EventEntityDamaged, so without them a negated hit
    // would land completely silently and read as a miss (see docs/accessory-guard.md).
    public class HurtSoundView : CustomQuantumEntityViewComponent
    {
        [SerializeField, SoundDataPicker, Tooltip("Played whenever this character takes a hit - a normal damaging hit, an Accessory Guard block, or a Free Hit Guard save. Author cooldown/voice limits on the SoundData itself so a fast multi-hit burst (e.g. shotgun pellets) doesn't turn to mush. Leave empty to skip.")]
        private SoundData hurtSound;

        public override void Awake()
        {
            base.Awake();

            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
            QuantumEvent.Subscribe<EventAccessoryBlocked>(this, OnAccessoryBlocked);
            QuantumEvent.Subscribe<EventFreeHitGuardConsumed>(this, OnFreeHitGuardConsumed);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        private void OnEntityDamaged(EventEntityDamaged e)
        {
            if (e.Target != _entityRef)
                return;

            // Silent = a passive/self-inflicted tick (e.g. SentryDecaySystem) that shouldn't read as
            // "damage" - same rule HitFeedback/HurtOverlayUiWidget use to skip their own reactions.
            if (e.Silent == true)
                return;

            Play();
        }

        private void OnAccessoryBlocked(EventAccessoryBlocked e)
        {
            if (e.Owner != _entityRef)
                return;

            Play();
        }

        private void OnFreeHitGuardConsumed(EventFreeHitGuardConsumed e)
        {
            if (e.Target != _entityRef)
                return;

            Play();
        }

        private void Play()
        {
            if (hurtSound != null)
                EntitySound.PlayAttached(hurtSound, transform, _entityRef);
        }

        // Purely event-driven - required override only because CustomQuantumEntityViewComponent.
        // QUpdate is abstract (same as WeaponView's own no-op override).
        protected override void QUpdate(QuantumGame game)
        {
        }
    }
}
