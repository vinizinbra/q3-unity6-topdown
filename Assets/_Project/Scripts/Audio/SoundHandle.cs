using UnityEngine;

// A revocable reference to one currently-playing voice, returned by every AudioManager.Play* call.
//
// It is a generation-stamped (index, generation) pair rather than a direct object reference, on
// purpose: voices are pooled and recycled, so a stale handle held past the end of its sound must not
// end up controlling whatever unrelated sound got that pool slot next. Every operation re-validates
// the generation first and silently no-ops if it has moved on - so it is always safe to keep a
// handle around and call Stop() on it later without checking anything.
//
// A one-shot can be ignored entirely (`AudioManager.Play(hitSound);`). A loop must NOT be - nothing
// else will ever stop it.
public readonly struct SoundHandle
{
    internal readonly int Index;
    internal readonly int Generation;

    internal SoundHandle(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }

    // The handle every failed/skipped play returns (cooldown gate, no clips authored, no manager).
    // Every method below no-ops on it, so callers never have to null-check a play result.
    public static SoundHandle None => new SoundHandle(-1, 0);

    public bool IsValid => Index >= 0;

    // True only while this exact voice is still playing this exact sound.
    public bool IsPlaying => AudioManager.IsPlaying(this);

    // Stops with a fade. Pass a negative value (the default) to use the SoundData's own authored
    // fadeOut; pass 0 for an immediate cut.
    public void Stop(float fadeOut = -1f) => AudioManager.Stop(this, fadeOut);

    // Overrides the rolled volume for the rest of this play. Fades and category/master volume still
    // apply on top of it.
    public void SetVolume(float volume) => AudioManager.SetVolume(this, volume);

    // Ramps to a new volume over `duration` seconds - the building block for ducking a loop under a
    // cutscene, or easing an ambience in as the player enters an area.
    public void FadeTo(float volume, float duration) => AudioManager.FadeTo(this, volume, duration);

    // Live pitch change (playback rate). Note this does not retime an already-scheduled trim end -
    // the remaining duration was computed from the pitch at play time.
    public void SetPitch(float pitch) => AudioManager.SetPitch(this, pitch);

    // Moves a positioned voice. Only meaningful for a sound played via PlayAt/PlayAttached with a
    // non-zero spatialBlend.
    public void MoveTo(Vector3 position) => AudioManager.MoveTo(this, position);
}
