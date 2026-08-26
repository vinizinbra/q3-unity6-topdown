using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Picks the match's music from Global.CurrentState and crossfades whenever the answer changes.
//
// The one rule worth stating outright: a Breathing Break does NOT mean breathing music. Entering
// Breathing only starts the phase - the enemies still on the field are combat, PhaseTimer doesn't
// even begin until they're gone, and Global.BreathingAreaSecured is what flips once the area is
// actually clear. Switching on the state alone would drop the music into a calm track while the
// player is still being shot at. So combat music holds until secured, always - the same gate
// BreathingCountdownWidget uses to decide when to show AREA SECURED.
//
// Everything else is a plain lookup, and a state with no track authored simply fades the music out
// rather than holding the previous one, so a half-authored setup is obvious rather than subtly wrong.
//
// PLAYLISTS. A state's music can be several songs rather than one, and it needs no second mechanism
// and no field of its own: author the SoundData with several clips, untick Loop, and pick a Random /
// RandomNoRepeat / Shuffle pick mode. This class simply plays the track again (after an authored
// gap) whenever its voice is no longer playing, and each play rolls the next clip through the
// asset's own pick mode - so which song comes next, and whether it can repeat, is authored on the
// asset exactly like every other sound in the project, and no clip order lives here.
//
// That same "nothing is playing, so play it again" step is also what self-heals a LOOPING track
// whose voice was stolen when a group budget was exceeded (see AudioManager.CountActive) - one
// answer covers both cases, since neither has anything else that would ever restart it.
public class MusicDirector : QuantumGlobalMonoBehaviour
{
    // A track that refuses to play (no clips authored, no voice free) must not be retried every
    // frame - back off this long between attempts instead.
    private const float FailedPlayRetryDelay = 3f;

    [SerializeField, Tooltip("Played in the lobby, before anyone has walked out of the LobbyStart chunk. Leave empty for silence there.")]
    private SoundData lobbyMusic;

    [SerializeField, Tooltip("Combat music. Also holds through the first part of a Breathing Break - see breathingMusic - and stands in for Boss if no boss track is authored.\n\nFor several combat songs rather than one, put every song in this ONE asset's Clips list, untick its Loop and set Pick to Shuffle (every song plays once before any repeats) or Random No Repeat. Each song then plays to the end and the next is rolled automatically - see the Track Gap below. Every track on this component works that way, not just this one.")]
    private SoundData survivalMusic;

    [SerializeField, Tooltip("Calm/Break music. Deliberately NOT played the moment Breathing begins: it waits for Global.BreathingAreaSecured, i.e. every remaining enemy actually dead or retired. Until then survivalMusic keeps playing, because until then it is still a fight.\n\nMultiple Break songs are authored exactly like the combat ones - several clips on this one asset, Loop unticked, Pick set to Shuffle.")]
    private SoundData breathingMusic;

    [SerializeField, Tooltip("Boss encounter music. Falls back to survivalMusic if left empty, so a boss fight is never silent just because this wasn't authored.")]
    private SoundData bossMusic;

    [SerializeField, Tooltip("Seconds the OUTGOING track takes to fade out. The incoming track's own fade-in is authored on its SoundData (Fade In), so a true crossfade wants both set - roughly matching values feel best.")]
    private float crossfadeDuration = 2f;

    [SerializeField, NaughtyAttributes.MinMaxSlider(0f, 10f), Tooltip("Silence between two songs of a multi-clip (playlist) track, rolled per song between these two values. A short gap reads as one song ending and another starting; zero runs them back to back. Ignored by a looping track, which by definition never ends.")]
    private Vector2 trackGap = new Vector2(1.5f, 3f);

    private SoundData _current;
    private SoundHandle _handle;
    private bool _resolvedOnce;
    private float _nextPlayTime = -1f;
    private readonly System.Collections.Generic.HashSet<SoundData> _warnedSilent = new();

    // Music is authored to keep running through a pause (SoundData.useUnscaledTime), so its own
    // between-songs gap has to be measured the same way.
    private static float Now => Time.unscaledTime;

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;
        SoundData track = ResolveTrack(frame);

        if (_resolvedOnce == true && track == _current)
        {
            TickCurrentTrack(track);
            return;
        }

        _resolvedOnce = true;
        _current = track;

        // A null track is a real answer, not a no-op: PlayMusic fades the current one out and
        // leaves nothing playing.
        Play(track, crossfadeDuration);
    }

    // The state hasn't changed, so the only question left is whether the track it selected is still
    // audible - and if it isn't, when to start it again.
    private void TickCurrentTrack(SoundData track)
    {
        if (track == null || _handle.IsPlaying == true)
        {
            // Whatever gap was pending is moot the moment something is playing again.
            _nextPlayTime = -1f;
            return;
        }

        // Nothing playing, but a track IS selected. Either a song of a playlist just ended, or a
        // looping track's voice was stolen - both are answered by playing it again, the second one
        // immediately (silence there is a bug, not a beat between songs).
        if (_nextPlayTime < 0f)
        {
            _nextPlayTime = Now + (track.loop == true ? 0f : RollGap());
            return;
        }

        if (Now < _nextPlayTime)
            return;

        // No crossfade: there is nothing left playing to fade out. The incoming song's own Fade In
        // still applies, which is what keeps a playlist from starting each song at full volume.
        Play(track, 0f);
    }

    private void Play(SoundData track, float crossfade)
    {
        _handle = AudioManager.PlayMusic(track, crossfade);
        _nextPlayTime = -1f;

        if (track == null || _handle.IsPlaying == true)
            return;

        // Asked for a track and got silence - no clips authored, or no voice could be freed. Left
        // alone this would be re-attempted every frame for the rest of the run, so back off and say
        // so once per asset.
        _nextPlayTime = Now + FailedPlayRetryDelay;

        if (_warnedSilent.Add(track) == true)
            LogHelper.Warn("Music", $"'{track.name}' was selected but nothing played - check that it has clips assigned. Retrying every {FailedPlayRetryDelay}s.", track);
    }

    private float RollGap() => Mathf.Max(0f, UnityEngine.Random.Range(Mathf.Min(trackGap.x, trackGap.y), Mathf.Max(trackGap.x, trackGap.y)));

    private unsafe SoundData ResolveTrack(Frame frame)
    {
        switch (frame.Global->CurrentState)
        {
            case GameState.Lobby:
                return lobbyMusic;

            case GameState.Breathing:
                // THE rule - see the class comment. Not secured yet means enemies are still alive,
                // which means this is still combat however the state enum labels it.
                return frame.Global->BreathingAreaSecured == true ? breathingMusic : survivalMusic;

            case GameState.Boss:
                return bossMusic != null ? bossMusic : survivalMusic;

            case GameState.Survival:
                return survivalMusic;

            default:
                // Upgrade pauses the gameplay systems mid-phase and RunFailed/Event are transient or
                // unwired - none of them should reach in and change the music, so whatever was
                // playing keeps playing and resumes correctly once the state returns.
                return _current;
        }
    }
}
