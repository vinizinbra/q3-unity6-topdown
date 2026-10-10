using Quantum;
using UnityEngine;

// Sounds for the smart ping (see docs/ping.md). Deliberately only three, like most co-op games:
//   Normal - plain "look here" pings (ground, Stick together)
//   Poi    - anything that points at a place of interest (shops, shrines, rifts, challenges)
//   Elite  - threats / calls for help (Elite, Revive me)
// Each is optional; an empty slot is silent. Loaded from Resources by PingFeedWidget, so the asset
// lives at Assets/_Project/Resources/PingAudioData.asset.
[CreateAssetMenu(fileName = "PingAudioData", menuName = "RiftRaiders/Audio/Ping Audio Data")]
public class PingAudioData : ScriptableObject
{
    public SoundData normal;
    public SoundData poi;
    public SoundData elite;

    public SoundData Get(PingKind kind)
    {
        switch (kind)
        {
            case PingKind.Elite:
            case PingKind.ReviveMe:
                return elite;

            case PingKind.Store:
            case PingKind.Blacksmith:
            case PingKind.Shrine:
            case PingKind.Rift:
            case PingKind.Traversal:
            case PingKind.TeamChallengeFound:
            case PingKind.TeamChallengeStart:
            case PingKind.TeamChallengeReward:
            case PingKind.TeamChallengeDone:
            case PingKind.TeamChallengeFailed:
                return poi;

            default:
                return normal;
        }
    }
}
