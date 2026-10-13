using Quantum;
using UnityEngine;

/// <summary>
/// Optional companion to <see cref="QuantumRunnerLocalDebug"/> (playing the gameplay scene directly, no menu):
/// with <see cref="useSavedMetaProgression"/> on, copies this device's saved meta progression onto the debug
/// runner's LocalPlayers and RuntimeConfig before it starts - the same thing MatchMakingConfig does for a menu
/// start. Off = the Inspector-authored values are used as-is (WorldUnlocks.Enforce off = every POI exists).
/// </summary>
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(QuantumRunnerLocalDebug))]
public class LocalDebugMetaProgression : MonoBehaviour
{
    [SerializeField] private bool useSavedMetaProgression;

    private void Awake()
    {
        if (!useSavedMetaProgression)
            return;

        var runner = GetComponent<QuantumRunnerLocalDebug>();
        runner.RuntimeConfig.WorldUnlocks = TalentSaveMapper.BuildWorld();
        if (runner.LocalPlayers == null)
            return;

        foreach (RuntimePlayer player in runner.LocalPlayers)
        {
            if (player != null)
                TalentSaveMapper.ApplyPlayer(ref player.Talents);
        }
    }
}
