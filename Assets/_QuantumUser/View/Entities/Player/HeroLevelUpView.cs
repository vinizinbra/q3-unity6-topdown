using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Level-up reaction on the character itself - a burst on the hero plus its sound, rather than a
    // HUD-only fanfare. Lives on the hero prefab so the effect is a real child of the character and
    // follows it while the burst plays.
    //
    // Triggered off Global.Level increasing rather than Global.LevelUpScreenOpen: the level is the
    // actual event, the screen is one consequence of it. Reading the level directly means this still
    // fires if a level-up ever resolves without opening a screen (an auto-pick, a future instant
    // grant), and it can't double-fire if the screen is reopened for any other reason.
    //
    // Experience is shared run-wide in this co-op game (see Experience.qtn - one total, one Level for
    // the whole party), so EVERY hero levels on the same frame and every one of them plays this. For
    // the burst that's correct - the whole party just levelled. For the SOUND it usually isn't: four
    // copies at once is a mess, so tick Local Player Only on the SoundData and only your own plays.
    public class HeroLevelUpView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Burst played on this character when the party levels up. A child ParticleSystem of the hero prefab, so it follows the character while it plays - authored in place rather than pooled, since there is exactly one per hero and it fires rarely. Leave empty to skip.")]
        private ParticleSystem levelUpEffect;

        [SerializeField, Tooltip("Played on the level-up. Every hero fires this on the same frame (experience is shared run-wide), so tick Local Player Only on the asset unless you genuinely want one copy per player. Leave empty to skip.")]
        private SoundData levelUpSound;

        // -1 rather than 0: Level legitimately starts at 0 (see Experience.qtn), so a 0 seed here
        // would be indistinguishable from "not seeded yet" and could swallow the first level-up.
        private int _lastLevel = -1;

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);

            // Re-seeded on the next QUpdate. Without this a pooled/respawned view would compare
            // against a level from a previous life and fire a spurious burst.
            _lastLevel = -1;
        }

        protected override unsafe void QUpdate(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;
            int level = frame.Global->Level;

            // First frame just records where we are - joining a run already at level 7 must not
            // play seven level-ups, or even one.
            if (_lastLevel < 0)
            {
                _lastLevel = level;
                return;
            }

            if (level <= _lastLevel)
            {
                // Only ever moves forward in practice; the assignment keeps this honest if a
                // rollback or a run reset ever walks it backwards.
                _lastLevel = level;
                return;
            }

            _lastLevel = level;
            Play();
        }

        [NaughtyAttributes.Button("Test Level Up")]
        private void Play()
        {
            if (levelUpEffect != null)
                levelUpEffect.Play(true);

            if (levelUpSound != null)
                EntitySound.PlayAttached(levelUpSound, transform, _entityRef);
        }
    }
}
