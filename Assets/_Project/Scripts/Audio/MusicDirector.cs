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
public class MusicDirector : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Played in the lobby, before anyone has walked out of the LobbyStart chunk. Leave empty for silence there.")]
    private SoundData lobbyMusic;

    [SerializeField, Tooltip("Combat music. Also holds through the first part of a Breathing Break - see breathingMusic - and stands in for Boss if no boss track is authored.")]
    private SoundData survivalMusic;

    [SerializeField, Tooltip("Calm/Break music. Deliberately NOT played the moment Breathing begins: it waits for Global.BreathingAreaSecured, i.e. every remaining enemy actually dead or retired. Until then survivalMusic keeps playing, because until then it is still a fight.")]
    private SoundData breathingMusic;

    [SerializeField, Tooltip("Boss encounter music. Falls back to survivalMusic if left empty, so a boss fight is never silent just because this wasn't authored.")]
    private SoundData bossMusic;

    [SerializeField, Tooltip("Seconds the OUTGOING track takes to fade out. The incoming track's own fade-in is authored on its SoundData (Fade In), so a true crossfade wants both set - roughly matching values feel best.")]
    private float crossfadeDuration = 2f;

    private SoundData _current;
    private SoundHandle _handle;
    private bool _resolvedOnce;
    private readonly System.Collections.Generic.HashSet<SoundData> _warnedNotLooping = new();

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
            // Self-heal a looping track that stopped without us asking - its voice can be stolen
            // when a group budget is exceeded (see AudioManager.CountActive), and nothing else would
            // ever restart it, leaving the rest of the run silent.
            if (track != null && track.loop == true && _handle.IsPlaying == false)
                _handle = AudioManager.PlayMusic(track, 0f);

            return;
        }

        _resolvedOnce = true;
        _current = track;

        WarnIfNotLoopable(track);

        // A null track is a real answer, not a no-op: PlayMusic fades the current one out and
        // leaves nothing playing.
        _handle = AudioManager.PlayMusic(track, crossfadeDuration);
    }

    // A music track with Loop unticked plays once and then leaves the rest of the phase silent -
    // nothing restarts it, since the self-heal above deliberately only revives tracks that were
    // MEANT to loop. It's the kind of authoring slip that reads as "the music stopped working"
    // minutes later, far from its cause, so say so the moment the track is selected.
    private void WarnIfNotLoopable(SoundData track)
    {
        if (track == null || track.loop == true || _warnedNotLooping.Contains(track))
            return;

        // Once per asset - this runs on every track change, and a run alternates Survival/Breathing
        // many times.
        _warnedNotLooping.Add(track);

        LogHelper.Warn("Music", $"'{track.name}' has Loop unticked - it will play once and then stay silent for the rest of the phase. Tick Loop on the asset (Trim & Fade section).", track);
    }

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
