using System;
using UnityEngine;

// A held, looping sound driven by a keep-alive: something is "on" for as long as its owner keeps
// saying so, and stops by itself once it stops saying so. The shape every continuous emitter wants -
// a beam gun's muzzle, a flamethrower, an engine hum - so none of them re-implement the edge
// detection, handle bookkeeping and teardown, and so a leaked loop (which would play forever) has
// one place to go wrong instead of many.
//
// Three sounds, all optional, so a spin-up weapon reads correctly instead of the loop clicking on:
//   intro -> one-shot as it starts   (spin-up, ignition)
//   loop  -> held for as long as it runs
//   tail  -> one-shot as it stops    (spin-down, release)
//
// Driven with three calls from the owning view:
//   Keep(muzzle, holdSeconds)   every frame/shot the emitter is active
//   Tick(Time.deltaTime)        every frame
//   Stop()                      on reload / weapon swap / pooled away
//
// The hold window is passed per call rather than authored here on purpose: a weapon's correct
// window is derived from its own LIVE fire interval (see ContinuousHitscanView.ResolveStopGrace),
// which changes mid-run with Fire Rate perks and Haste, and must not be authored a second time.
[Serializable]
public class SustainedSound
{
    [SerializeField, SoundDataPicker, Tooltip("Optional one-shot played once as the sound starts - a spin-up or ignition. Leave empty to have the loop simply begin.")]
    private SoundData intro;

    [SerializeField, SoundDataPicker, Tooltip("The held loop. Its SoundData wants Loop ticked, and a small fadeIn/fadeOut on it smooths the start and stop. Leave empty to use this purely as an intro/tail pair.")]
    private SoundData loop;

    [SerializeField, SoundDataPicker, Tooltip("Optional one-shot played once as the sound stops - a spin-down or release tail. Skipped on a hard stop (weapon swapped away, entity pooled), where there is nothing left to trail off.")]
    private SoundData tail;

    private SoundHandle _handle = SoundHandle.None;
    private Transform _follow;
    private Vector3 _lastPosition;
    private float _remaining;
    private float _volumeScale = 1f;
    private bool _active;

    public bool IsActive => _active;

    // The held loop asset, so an owner-aware caller can resolve its remotePlayerVolume without this
    // class having to know anything about entities or who owns them.
    public SoundData Loop => loop;

    // Call whenever the emitter is on - every shot, every beam tick. Starts the sound on the first
    // call and refreshes the hold window on every call. holdSeconds is how long the loop survives
    // with no further Keep before Tick ends it.
    public void Keep(Transform follow, float holdSeconds, float volumeScale = 1f)
    {
        // Captured every call but only read when a voice actually starts, so a change takes effect
        // on the next (re)start rather than fighting the loop's own fade mid-play.
        _volumeScale = volumeScale;

        _follow = follow;
        if (follow != null)
            _lastPosition = follow.position;

        _remaining = Mathf.Max(0f, holdSeconds);

        // Zero scale means the caller resolved this to silence (a localPlayerOnly loop owned by
        // someone else). Holding a muted voice open for the whole burst would waste a slot and count
        // against the group budget, so end it instead of starting or sustaining it.
        if (volumeScale <= 0f)
        {
            Stop(false);
            return;
        }

        if (_active)
        {
            // Self-heal: a group voice budget can steal this loop (see AudioManager.CountActive,
            // which prefers stealing one-shots precisely because of this). Nothing would ever
            // replay it, so the emitter would stay silent for the rest of the burst - restart it
            // rather than assume it survived.
            if (loop != null && _handle.IsPlaying == false)
                _handle = PlayTracking(loop);

            return;
        }

        _active = true;

        if (intro != null)
            PlayTracking(intro);

        if (loop != null)
            _handle = PlayTracking(loop);
    }

    // Call every frame. Ends the sound once the hold window elapses with no further Keep.
    public void Tick(float deltaTime)
    {
        if (_active == false)
            return;

        if (_follow != null)
            _lastPosition = _follow.position;

        _remaining -= deltaTime;
        if (_remaining <= 0f)
            Stop();
    }

    // Ends it now without waiting out the hold window - a reload, a weapon swap, a pooled entity.
    // Pass playTail: false for a hard cut, where a trailing spin-down would be wrong.
    public void Stop(bool playTail = true)
    {
        if (_active == false)
            return;

        _active = false;
        _remaining = 0f;

        // Default (negative) fade means "use the loop's own authored fadeOut", so a stop is exactly
        // as smooth as the sound was authored to be.
        _handle.Stop();
        _handle = SoundHandle.None;

        if (playTail && tail != null)
            PlayTracking(tail);

        _follow = null;
    }

    // Follows the emitter while it exists, falling back to its last known position - a tail whose
    // weapon was just destroyed should still play where the weapon was, not at the world origin.
    private SoundHandle PlayTracking(SoundData data)
        => _follow != null
            ? AudioManager.PlayAttached(data, _follow, _volumeScale)
            : AudioManager.PlayAt(data, _lastPosition, _volumeScale);
}
