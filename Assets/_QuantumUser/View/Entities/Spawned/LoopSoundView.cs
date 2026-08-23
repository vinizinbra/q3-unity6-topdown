using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // A held sound for an entity that spawns, persists, and is later destroyed - Kai's Vortex, a
    // Totem, a Sentry, a lingering area effect. Starts when the view initializes and fades out when
    // the entity dies, rather than being cut off mid-sample the instant it despawns.
    //
    // The fade genuinely outlives the entity: an AudioManager voice is a pooled AudioSource owned by
    // the manager, not a child of this GameObject, so it keeps fading after the view is gone. That's
    // the whole reason this doesn't just put an AudioSource on the prefab - Unity destroys children
    // with their parent, so a prefab-mounted source is silenced the moment the entity despawns,
    // which is exactly the cut-off this is meant to avoid.
    //
    // Deliberately NOT routed through EntitySound: a Vortex is not a player entity, so an ownership
    // check would resolve it as "someone else's" and quieten it for everyone (see EntitySound's own
    // warning about passing non-player entities).
    public class LoopSoundView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Optional intro, the held loop, and an optional tail played as it ends. The loop's own Fade Out on its SoundData is what the tail-off uses when the entity dies, so set that rather than expecting an instant stop.")]
        private SustainedSound sound = new SustainedSound();

        // Only a safety net. The loop is refreshed every QUpdate and stopped explicitly on
        // DeInitialize, so this just guarantees a loop can't outlive a view that somehow stopped
        // ticking without deinitializing (scene teardown mid-frame, a disabled view).
        private const float KeepAliveGrace = 0.5f;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            // Started here rather than waiting for the first QUpdate so the sound lands on the frame
            // the entity appears - a cast should be heard as it happens, not a frame later.
            sound.Keep(transform, KeepAliveGrace);
        }

        protected override void QUpdate(QuantumGame game)
        {
            sound.Keep(transform, KeepAliveGrace);
            sound.Tick(Time.deltaTime);
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);

            // playTail: true - this is the entity genuinely ending, which is exactly when a tail
            // belongs. The loop itself fades over its authored Fade Out rather than cutting.
            sound.Stop();
        }
    }
}
