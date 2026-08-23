using NaughtyAttributes;
using UnityEngine;

// Sound authored directly on a particle prefab, next to the effect it belongs to - the convenience
// of dropping an AudioSource on the prefab, without any of what that costs.
//
// A raw AudioSource on a pooled effect breaks in three specific ways here: EffectsManager releases
// an instance the moment ParticleSystem.IsAlive() goes false, deactivating the object and cutting
// any sound longer than its particle; the clip plays byte-identically every time, which is the
// canned-audio problem in a game where the same explosion happens many times a second; and the
// voices are uncapped, so Unity's own ~32-voice ceiling starts culling by ITS priority rules rather
// than the SoundGroup budgets. Routing through AudioManager fixes all three - a one-shot here is a
// pooled voice that outlives the particle, varies per play, and counts against its group.
//
// State is POLLED off the ParticleSystem rather than acted on in OnEnable, which matters for
// pooling: EffectsManager.Prewarm calls pool.Get() (activating the instance) and pool.Release()
// back to back, synchronously, inside Awake. An OnEnable hook would fire a burst of sounds at scene
// load, once per prewarmed instance; polling can't, because no Update runs between that Get and
// Release - and Prewarm never calls Play() at all, so isPlaying stays false either way.
[AddComponentMenu("Audio/Particle Sound")]
public class ParticleSound : MonoBehaviour
{
    public enum Mode
    {
        // Fired once each time the effect starts. The sound is an independent pooled voice, so it
        // finishes on its own even after the particle is released - which is the whole point.
        OneShot,

        // Held for as long as the effect is emitting, and stopped when it stops, is pooled away, or
        // is destroyed. For looping effects - a fire aura, a beam impact, a channelled zone.
        Loop,
    }

    [SerializeField, Tooltip("OneShot fires once when the effect starts. Loop is held while the effect emits and stopped when it stops. Pick by what the PARTICLE does - a looping system with a one-shot sound goes silent after the first cycle.")]
    private Mode mode = Mode.OneShot;

    [SerializeField, ShowIf("mode", Mode.OneShot), AllowNesting]
    [Tooltip("Played once per effect start. Author its variation/cooldown/group on the SoundData itself - a high-frequency effect wants a cooldown and a tight group budget, or a burst of them turns to mush.")]
    private SoundData sound;

    [SerializeField, ShowIf("mode", Mode.Loop), AllowNesting]
    [Tooltip("Held while the effect emits - optional intro, the loop itself, optional tail. The tail plays when the effect stops naturally, but is skipped when the object is pooled away or destroyed, where a trailing sound would outlive its cause.")]
    private SustainedSound loop = new SustainedSound();

    [SerializeField, Tooltip("Volume multiplier on top of whatever the SoundData rolls - for reusing one shared sound across a big and a small version of the same effect.")]
    [Range(0f, 2f)] private float volumeScale = 1f;

    [SerializeField, Tooltip("Which ParticleSystem drives this. Leave empty to use this GameObject's own, or the first one found in its children.")]
    private ParticleSystem source;

    // Safety net only. The falling edge below stops the loop immediately; this covers a frame where
    // Update somehow doesn't run, so a loop can never be orphaned by a missed tick.
    private const float LoopKeepAliveGrace = 0.5f;

    private bool _wasActive;

    private void Awake()
    {
        if (source == null)
            source = GetComponent<ParticleSystem>();

        if (source == null)
            source = GetComponentInChildren<ParticleSystem>();
    }

    private void Update()
    {
        if (source == null)
            return;

        bool active = mode == Mode.Loop
            // isEmitting, not isPlaying: a looping system stays "playing" while its last particles
            // die out after Stop(), and the sound should end with the emission, not the stragglers.
            ? source.isPlaying && source.isEmitting
            : source.isPlaying;

        if (mode == Mode.Loop)
        {
            if (active)
                loop.Keep(transform, LoopKeepAliveGrace);
            else if (_wasActive)
                loop.Stop();

            loop.Tick(Time.deltaTime);
        }
        else if (active && _wasActive == false && sound != null)
        {
            // PlayAt, deliberately not PlayAttached: this instance goes back to a pool and gets
            // repositioned for its next use, and a sound still following that transform would be
            // dragged across the level to wherever the effect is played next.
            AudioManager.PlayAt(sound, transform.position, volumeScale);
        }

        _wasActive = active;
    }

    // Pooled away (EffectsManager.actionOnRelease deactivates the instance) or the whole effect was
    // destroyed. Either way a held loop has to end here - nothing else will ever tick it again, and
    // AudioManager would happily keep playing it at its last position forever.
    private void OnDisable() => StopLoop();

    private void OnDestroy() => StopLoop();

    private void StopLoop()
    {
        // playTail: false - the effect is gone rather than finished, so a spin-down trailing after
        // it would be describing something that is no longer there. A natural stop already played
        // its tail on the falling edge above.
        loop?.Stop(false);

        // Re-arm, so the next time this pooled instance is played it triggers again.
        _wasActive = false;
    }

    [Button("Test (Play Mode)")]
    private void Test()
    {
        if (Application.isPlaying == false)
            return;

        if (source != null)
            source.Play(true);
    }
}
