using System;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Audio;

// One authored sound "event" - the thing gameplay code actually references and plays, as opposed to
// a raw AudioClip. Holds a set of interchangeable clips plus every per-play modifier (random pick,
// volume/pitch variance, fade, trim, voice limits), so a designer retunes a sound entirely in the
// Inspector and no call site ever changes.
//
// Play it with the static facade: AudioManager.Play(mySound) / PlayAt(mySound, worldPos) /
// PlayAttached(mySound, transform). See AudioManager for the pooling/voice model.
[CreateAssetMenu(fileName = "NewSound", menuName = "RiftRaiders/Audio/Sound Data")]
public class SoundData : ScriptableObject
{
    // How the next clip is chosen out of `clips` on each play. Only meaningful with 2+ clips.
    public enum PickMode
    {
        // Uniform random, can repeat the same clip back to back.
        Random,
        // Uniform random, but never the same clip twice in a row - the sane default for a gunshot
        // or footstep, where an immediate repeat is the one thing that reads as "canned".
        RandomNoRepeat,
        // Walks the list in order, wrapping - for deliberate sequences (combo hit 1/2/3).
        Sequential,
        // Plays every clip once in a random order before any repeats (a "bag" shuffle) - the most
        // even-sounding option for a large variation set.
        Shuffle,
    }

    [Header("Clips")]
    [Tooltip("Interchangeable variations of this one sound. One entry is fine; two or more turns on the pick mode below. Null/missing entries are skipped at play time rather than erroring.")]
    public AudioClip[] clips = Array.Empty<AudioClip>();

    [Tooltip("How the next clip is picked out of the list above. RandomNoRepeat is the usual choice for combat sounds - plain Random will audibly double up.")]
    public PickMode pick = PickMode.RandomNoRepeat;

    [Header("Routing")]
    [Tooltip("Which group this sound is filed under. Drives BOTH its volume bus (an options slider calls AudioManager.SetGroupVolume) and its shared voice budget (see the Groups table on AudioManager). Every sound belongs to exactly one.")]
    public SoundGroup group = SoundGroup.Sfx;

    [Tooltip("Optional AudioMixerGroup. Left empty by default since this project has no mixer authored yet; assign it if one is added and both this and the group volume multiplier will apply.")]
    public AudioMixerGroup output;

    [Header("Volume & Pitch")]
    [Tooltip("Linear volume range, rolled per play. Set both ends equal for a fixed volume. Keep the spread small (~0.05) - large volume variance reads as inconsistent rather than natural.")]
    [MinMaxSlider(0f, 1f)] public Vector2 volume = new Vector2(1f, 1f);

    [Tooltip("Playback-rate multiplier, rolled per play. 1 = original. This is the single highest-value knob for making a repeated clip stop sounding repeated - 0.95/1.05 is subtle, 0.9/1.1 is obvious. NOTE: pitch also changes duration, and the Trim times below are compensated for it automatically.")]
    [MinMaxSlider(0.1f, 3f)] public Vector2 pitch = new Vector2(0.95f, 1.05f);

    [Tooltip("Seconds to wait before the sound actually starts. Rolled per play, so a burst of simultaneous plays can be smeared apart slightly instead of landing as one flam.")]
    [MinMaxSlider(0f, 5f)] public Vector2 delay = Vector2.zero;

    [Header("Trim (seconds into the clip)")]
    [Tooltip("Skip this many seconds of the clip's head. Useful for shaving dead air or a soft attack off a sample without re-exporting it. 0 = play from the start.")]
    [Min(0f)] public float startAt;

    [Tooltip("Stop this many seconds into the clip. 0 (or anything past the clip's length) means 'play to the end'. Combined with startAt this cuts an arbitrary sub-region out of a longer sample - and with Loop on, that sub-region is what loops.")]
    [Min(0f)] public float endAt;

    [Header("Fade")]
    [Tooltip("Seconds to ramp from silence up to the rolled volume at the start of a play. 0 = start at full volume.")]
    [Min(0f)] public float fadeIn;

    [Tooltip("Seconds to ramp down to silence at the end. For a one-shot this is subtracted from the trimmed duration so the fade finishes exactly at the end; for a loop (or any manual Stop) it's the default ramp AudioManager uses when the sound is asked to stop.")]
    [Min(0f)] public float fadeOut;

    [Header("Looping")]
    [Tooltip("Repeat forever until stopped via the returned SoundHandle (or AudioManager.Stop/StopAll). A looping play ALWAYS returns a handle you're expected to keep - nothing else will ever stop it.")]
    public bool loop;

    [Header("Spatialisation")]
    [Tooltip("0 = pure 2D (same in both ears regardless of position - UI, music, global cues). 1 = fully 3D positioned. Only meaningful for PlayAt/PlayAttached; a plain Play() is always effectively 2D since it has no position.")]
    [Range(0f, 1f)] public float spatialBlend;

    [Tooltip("Distance within which a 3D sound stays at full volume.")]
    [Min(0.01f)] public float minDistance = 5f;

    [Tooltip("Distance at which a 3D sound has fully attenuated to silence. On a top-down camera this wants to be generous - the listener is far above the action.")]
    [Min(0.01f)] public float maxDistance = 60f;

    [Tooltip("How volume falls off between min and max distance. Logarithmic is physically accurate; Linear is easier to reason about for a fixed-camera top-down game.")]
    public AudioRolloffMode rolloff = AudioRolloffMode.Linear;

    [Header("Voice management")]
    [Tooltip("Minimum seconds between two plays of THIS SoundData. A second play inside the window is dropped silently. This is the fix for the bullet-hell case - 30 projectiles spawning on one frame should not layer 30 copies of the same shot into a wall of phasing. 0 disables it.")]
    [Min(0f)] public float cooldown;

    [Tooltip("Maximum simultaneously-playing voices of THIS SoundData. Exceeding it steals the oldest one rather than dropping the new play, so the newest (most relevant) hit is always the one you hear. 0 = unlimited. For a cap SHARED with every other sound of the same Group, set that budget on the AudioManager Groups table instead - the two compose, and both are usually wanted.")]
    [Min(0)] public int maxConcurrent = 8;

    [Tooltip("Higher wins when the pool is exhausted and a voice has to be stolen from another sound. Keep music/important cues high and chatter low. Same 0-255 convention as AudioSource.priority, but inverted for readability: higher number = more important.")]
    [Range(0, 255)] public int priority = 128;

    [Tooltip("Advance fades/trim on unscaled time. Leave ON for UI and music so they behave during a pause; the simulation-side pause in this project (SystemDisable<GameplaySystemGroup>) does NOT touch Time.timeScale, so this only matters if something ever does.")]
    public bool useUnscaledTime = true;

    // ---- Runtime pick state (not serialized - reset on domain reload, which is fine) ----
    [NonSerialized] private int _lastIndex = -1;
    [NonSerialized] private int[] _bag;
    [NonSerialized] private int _bagCursor;

    // Rolls the next clip according to `pick`. Returns null if nothing is authored - callers are
    // expected to no-op rather than error, so a half-authored SoundData is silent, not a crash.
    public AudioClip NextClip()
    {
        if (clips == null || clips.Length == 0)
            return null;

        if (clips.Length == 1)
            return clips[0];

        int index;
        switch (pick)
        {
            case PickMode.Sequential:
                index = (_lastIndex + 1) % clips.Length;
                break;

            case PickMode.Shuffle:
                index = NextFromBag();
                break;

            case PickMode.RandomNoRepeat:
                index = UnityEngine.Random.Range(0, clips.Length - 1);
                // Fold the excluded slot out of the range instead of rejection-sampling, so this
                // stays one roll regardless of how unlucky it gets.
                if (index >= _lastIndex && _lastIndex >= 0)
                    index++;
                break;

            default:
                index = UnityEngine.Random.Range(0, clips.Length);
                break;
        }

        _lastIndex = index;
        return clips[index];
    }

    // "Bag" shuffle - every clip plays once before any plays twice, reshuffling when exhausted.
    private int NextFromBag()
    {
        if (_bag == null || _bag.Length != clips.Length || _bagCursor >= _bag.Length)
        {
            _bag = new int[clips.Length];
            for (var i = 0; i < _bag.Length; i++)
                _bag[i] = i;

            // Fisher-Yates.
            for (var i = _bag.Length - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
            }

            // Avoid the seam case where a reshuffle puts the previous clip first.
            if (_bag.Length > 1 && _bag[0] == _lastIndex)
                (_bag[0], _bag[_bag.Length - 1]) = (_bag[_bag.Length - 1], _bag[0]);

            _bagCursor = 0;
        }

        return _bag[_bagCursor++];
    }

    public float RollVolume() => UnityEngine.Random.Range(volume.x, volume.y);
    public float RollPitch() => Mathf.Max(0.01f, UnityEngine.Random.Range(pitch.x, pitch.y));
    public float RollDelay() => Mathf.Max(0f, UnityEngine.Random.Range(delay.x, delay.y));

    // Resolves the trimmed [start, end] window against a specific clip's real length, since clips in
    // the same SoundData can differ in length and `endAt` is authored once for all of them.
    public void ResolveTrim(AudioClip clip, out float start, out float end)
    {
        var length = clip != null ? clip.length : 0f;
        start = Mathf.Clamp(startAt, 0f, Mathf.Max(0f, length - 0.01f));
        end = endAt > 0f ? Mathf.Min(endAt, length) : length;
        if (end <= start)
            end = length;
    }

    // ------------------------------------------------------------------ Inspector audition
    //
    // All three work in Edit Mode - no Play Mode needed. They route through the real AudioManager
    // (a hidden preview rig in Edit Mode, the live one in Play Mode), so what you hear here is
    // exactly what the game plays: same clip roll, same pitch/volume jitter, same fades and trim.

    [Button("Play (Random Variant)")]
    private void PlayVariantButton()
    {
#if UNITY_EDITOR
        SoundDataEditorPreview.PlayVariant(this);
#endif
    }

    [Button("Play Every Clip")]
    private void PlayEveryClipButton()
    {
#if UNITY_EDITOR
        SoundDataEditorPreview.PlayEveryClip(this);
#endif
    }

    [Button("Stop")]
    private void StopButton()
    {
#if UNITY_EDITOR
        SoundDataEditorPreview.Stop(this);
#else
        AudioManager.StopAllOf(this, 0f);
#endif
    }
}
