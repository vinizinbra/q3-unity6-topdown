using Quantum;
using QuantumUser.View;
using UnityEngine;

// Plays a sound on behalf of a specific PLAYER entity, quietening it when that player isn't local
// to this client (see SoundData.remotePlayerVolume).
//
// Why this is separate from AudioManager: AudioManager knows nothing about Quantum or about who owns
// what, and shouldn't - it is a mixer, not a game system. This is the one small seam where "whose
// sound is this" is resolved, so the ownership rule lives in a single place instead of being
// re-derived at every call site.
//
// IMPORTANT: only call this for sounds produced by a PLAYER. MyLocalPlayer.IsLocalEntity returns
// false for anything that isn't a local player's character - including every enemy - so passing an
// enemy here would quieten it as though it were a remote teammate. Enemy sounds should go through
// AudioManager directly.
//
// Couch co-op: every local split-screen player counts as local (IsLocalEntity checks all slots), so
// only a genuinely networked teammate is scaled down. That is the intent - you are listening from
// between the local players (LocalPlayerAudioListener), so all of them are "here".
//
// Bots (RuntimePlayer.IsBot, see docs/bots.md) are deliberately NOT local, even though on a
// local-debug session they are literally this client's own player slots. They never register with
// MyLocalPlayer, so IsLocalEntity returns false for one and a bot's sounds are mixed exactly like a
// networked teammate's: quieterWhenRemote scales them down, localPlayerOnly drops them entirely.
// That is what keeps a bot Pixie's reload clicks and ability cues out of the mix that should only
// ever be about the player actually holding the controller. No bot-specific code here - the rule is
// the same single MyLocalPlayer check it always was.
public static class EntitySound
{
    public static SoundHandle PlayAttached(SoundData sound, Transform follow, EntityRef owner)
    {
        float volume = ResolveVolume(sound, owner);

        // Zero means localPlayerOnly rejected it - return before taking a voice at all, rather than
        // starting a silent one that would still count against the group budget and could steal a
        // voice from something audible.
        if (sound == null || volume <= 0f)
            return SoundHandle.None;

        return AudioManager.PlayAttached(sound, follow, volume);
    }

    // Overload for a call site that already has its own per-play scale to apply (e.g. a landing
    // scaled by impact speed) - the two multiply rather than one replacing the other.
    public static SoundHandle PlayAttached(SoundData sound, Transform follow, EntityRef owner, float volumeScale)
    {
        float volume = ResolveVolume(sound, owner) * volumeScale;

        if (sound == null || volume <= 0f)
            return SoundHandle.None;

        return AudioManager.PlayAttached(sound, follow, volume);
    }

    public static SoundHandle PlayAt(SoundData sound, Vector3 position, EntityRef owner)
    {
        float volume = ResolveVolume(sound, owner);

        if (sound == null || volume <= 0f)
            return SoundHandle.None;

        return AudioManager.PlayAt(sound, position, volume);
    }

    // Full volume unless this is a remote player's sound AND the asset actually asked to be
    // quietened. Before any player exists (menus, pre-spawn) there is nothing to compare against, so
    // nothing is scaled - a menu sound is not "someone else's".
    public static float ResolveVolume(SoundData sound, EntityRef owner)
    {
        if (sound == null)
            return 1f;

        if (sound.localPlayerOnly == false && sound.quieterWhenRemote == false)
            return 1f;

        // No local player resolved yet (menus, pre-spawn) or no owner supplied - there is nothing to
        // compare against, so nothing is "someone else's". Full volume rather than silence: a
        // localPlayerOnly sound going mute during those windows would be a far worse failure than it
        // occasionally playing when it shouldn't.
        var local = MyLocalPlayer.Instance;
        if (local == null || owner == EntityRef.None)
            return 1f;

        if (local.IsLocalEntity(owner) == true)
            return 1f;

        // localPlayerOnly wins over quieterWhenRemote - "don't play it" is the stronger statement.
        return sound.localPlayerOnly == true ? 0f : AudioManager.RemotePlayerVolume;
    }
}
