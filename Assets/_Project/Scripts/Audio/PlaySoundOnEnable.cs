using NaughtyAttributes;
using UnityEngine;

// Sound authored directly on a prefab that plays whenever the object is switched on - the simplest
// possible sibling of ParticleSound, for everything whose trigger is "this appeared" rather than
// "this emitted": a HUD panel opening, a pickup spawning, a warning banner, a telegraph decal.
//
// Same reason ParticleSound exists rather than a raw AudioSource: routing through AudioManager gets
// the per-play variation, the cooldown gate, and the SoundGroup voice budget, and lets a one-shot
// outlive the object that started it instead of being cut when the object is pooled away.
//
// The play is ARMED in OnEnable and fired on the next Update, deliberately - not played inline.
// EffectsManager.Prewarm calls pool.Get() (which activates the instance) and pool.Release() back to
// back, synchronously, inside Awake; an inline OnEnable play would fire one sound per prewarmed
// instance in a burst at scene load. No Update runs between that Get and Release, so a deferred play
// is cancelled by the Release and prewarming stays silent - the same hazard ParticleSound documents,
// solved the same way. One frame is inaudible for the cases this component is for.
[AddComponentMenu("Audio/Play Sound On Enable")]
public class PlaySoundOnEnable : MonoBehaviour
{
    public enum Placement
    {
        // Positioned where this object is at the moment it plays, then left there. The right choice
        // for anything pooled: the instance goes back to its pool and is repositioned for its next
        // use, and a sound still following it would be dragged across the level with it.
        AtPosition,

        // Follows this transform for as long as it plays - for a sound belonging to something that
        // moves and is NOT pooled mid-sound.
        Attached,

        // No position at all, heard flat everywhere. For UI. Note a SoundData with Spatial unticked
        // is already flat wherever it plays, so this only matters for a shared sound that is
        // positional elsewhere.
        Flat,
    }

    [SerializeField, SoundDataPicker, Tooltip("Played each time this object is enabled. Author its variation, cooldown, group and volume on the SoundData itself - nothing about the sound is configured here.")]
    private SoundData sound;

    [SerializeField, Tooltip("Where the sound is heard from. AtPosition is correct for pooled objects; Attached follows this transform; Flat has no position at all (UI).")]
    private Placement placement = Placement.AtPosition;

    [SerializeField, Tooltip("Volume multiplier on top of whatever the SoundData rolls - for reusing one shared sound across a big and a small version of the same thing.")]
    [Range(0f, 2f)] private float volumeScale = 1f;

    [SerializeField, Tooltip("Extra seconds before the sound starts, ADDED to whatever delay the SoundData authors. For lining a sound up with an intro animation this object is already playing.")]
    [Min(0f)] private float delay;

    [SerializeField, Tooltip("ON = stop the sound when this object is disabled or destroyed. Required for a looping SoundData - nothing else will ever stop it, and AudioManager would keep playing it at its last position forever. For a one-shot, leave OFF so it finishes naturally even when its object is pooled away mid-sound (usually what you want - see ParticleSound).")]
    private bool stopOnDisable;

    [SerializeField, ShowIf("stopOnDisable"), AllowNesting]
    [Tooltip("Fade-out seconds used when stopping. Negative means 'use the sound's own authored Fade Out' (including a per-clip override).")]
    private float stopFade = -1f;

    // Set in OnEnable, consumed by the first Update after it. See the class comment - this is what
    // makes prewarming (activate and deactivate with no Update in between) silent.
    private bool _pending;

    private SoundHandle _handle = SoundHandle.None;

    private void OnEnable() => _pending = true;

    private void Update()
    {
        if (_pending == false)
            return;

        _pending = false;
        Play();
    }

    private void Play()
    {
        if (sound == null)
            return;

        switch (placement)
        {
            case Placement.Attached:
                _handle = AudioManager.PlayAttached(sound, transform, volumeScale, delay);
                break;

            case Placement.Flat:
                _handle = AudioManager.Play(sound, volumeScale, delay);
                break;

            default:
                _handle = AudioManager.PlayAt(sound, transform.position, volumeScale, delay);
                break;
        }
    }

    private void OnDisable()
    {
        // Cancel an armed-but-unfired play: the object was switched off before it ever ticked, so
        // the thing the sound was announcing never actually happened.
        _pending = false;

        if (stopOnDisable)
            Stop();

        _handle = SoundHandle.None;
    }

    private void OnDestroy()
    {
        if (stopOnDisable)
            Stop();
    }

    private void Stop()
    {
        if (_handle.IsPlaying)
            _handle.Stop(stopFade);
    }

    [Button("Test (Play Mode)")]
    private void Test()
    {
        if (Application.isPlaying)
            Play();
    }
}
