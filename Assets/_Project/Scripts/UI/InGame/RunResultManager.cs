using System.Collections.Generic;
using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using UnityEngine;

// Detects the end of a run and opens RunResultPopup with every stat it needs - both Win
// (GameState.Victory, set by the new VictorySystem the instant the final boss dies) and Lose
// (GameState.RunFailed) are real sim events (EventGameStateChanged), so this is a plain reaction,
// same shape either way. Held off, like the tutorial popups (InMatchTutorialManager), while a
// level-up/Chest Choice Window - or its post-secure orb-vacuum sweep - is in progress.
public class RunResultManager : QuantumGlobalMonoBehaviour
{
    private bool _resultPending;
    private bool _pendingWon;

    private void Awake()
    {
        QuantumEvent.Subscribe<EventGameStateChanged>(this, OnGameStateChanged);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        if (_resultPending == false)
            return;

        Frame frame = game.Frames.Predicted;

        bool upgradePending = frame.Global->CurrentState == GameState.Upgrade
            || frame.Global->OrbVacuumTimeRemaining.AsFloat > 0f;

        if (upgradePending)
            return;

        _resultPending = false;
        ShowResult(frame, _pendingWon);
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    private void OnGameStateChanged(EventGameStateChanged e)
    {
        if (_resultPending)
            return;

        if (e.NewState == GameState.RunFailed)
        {
            _resultPending = true;
            _pendingWon = false;
        }
        else if (e.NewState == GameState.Victory)
        {
            _resultPending = true;
            _pendingWon = true;
        }
    }

    private unsafe void ShowResult(Frame frame, bool won)
    {
        var teamResults = new List<RunResultPopup.PlayerResult>();

        var players = frame.Filter<PlayerLink, CharacterStats>();
        while (players.Next(out EntityRef entity, out PlayerLink playerLink, out CharacterStats stats))
        {
            CharacterData data = frame.FindAsset(stats.CharacterData);

            teamResults.Add(new RunResultPopup.PlayerResult
            {
                HeroName = ResolvePlayerLabel(playerLink.Player, stats.CharacterData),
                HeroColor = data != null ? data.RingColor : Color.white,
                DamageDealt = stats.DamageDealt
            });
        }

        // Common stats are THIS CLIENT's own local player, never a team sum - Rift Shards Earned
        // in particular is inherently personal (a per-player wallet), and the team-wide damage
        // total already gets its own dedicated presentation (RunResultPopup's teamDamageDealtText
        // + the per-player breakdown list above) rather than being folded into these.
        int enemiesKilled = 0;
        FP damageDealt = FP._0;
        int timesDowned = 0;
        FP riftShardsEarned = FP._0;

        EntityRef localEntity = MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.IsLocalPlayerSetup
            ? MyLocalPlayer.Instance.EntityRef
            : EntityRef.None;

        if (localEntity != EntityRef.None && frame.Unsafe.TryGetPointer<CharacterStats>(localEntity, out var localStats))
        {
            enemiesKilled = localStats->MonstersKilled;
            damageDealt = localStats->DamageDealt;
            timesDowned = localStats->TimesDowned;
            riftShardsEarned = localStats->RiftShardsEarned;
        }

        // Not SurvivalTime - that's the combat-only curve clock (frozen through Breathing/Elite
        // holds/challenges, never ticked during Boss), so it read well short of the real run.
        FP runTime = frame.Global->RunTime;

        RunResultPopup popup = InMatchPopupManager.instance.Open<RunResultPopup>();
        popup?.Setup(won, enemiesKilled, damageDealt, timesDowned, riftShardsEarned, runTime, teamResults);
    }

    // Prefers the player's own Photon nickname over the hero name, falling back to the hero's
    // CharacterCatalog-authored display name (never the raw CharacterData asset name, e.g.
    // "KaiCharacterData") if no nickname is set - matches this project's other per-hero label,
    // PartyRoomWidget.DisplayNameFor, which does the same "nickname first" fallback at the menu.
    //
    // CAUTION: the PlayerRef -> Photon ActorNumber bridge below (playerRef._index) has no other
    // call site in this codebase to copy from (RuntimePlayer itself carries no nickname - see
    // RuntimePlayer.User.cs). QuantumHelper.GetLocalSlotIndex's own comment says PlayerRef is
    // assigned in room join order, which lines up with how Photon assigns ActorNumber, but this
    // exact lookup should be verified against a real networked (non-solo) session before trusting
    // it - if it ever mismatches, this silently falls back to the hero name rather than showing a
    // wrong player's nickname.
    //
    // internal, not private - reused by TeamChallengeWidget for its own "[HeroName] activated a
    // Rift Challenge" toast (see docs' Optional Team Challenge), the same "proper player/hero
    // display name" this popup already resolves - no reason for a second, divergent implementation.
    internal static string ResolvePlayerLabel(PlayerRef playerRef, AssetRef<CharacterData> characterData)
    {
        string heroName = "Hero";

        CharacterCatalog catalog = PartyManager.Instance != null ? PartyManager.Instance.characterCatalog : null;
        if (catalog != null && catalog.TryGetDisplayNameForCharacterData(characterData, out string displayName))
            heroName = displayName;

        var client = MatchMakingConfig.Instance != null ? MatchMakingConfig.Instance.Client : null;
        if (client != null && client.CurrentRoom != null
            && client.CurrentRoom.Players.TryGetValue(playerRef._index, out var player)
            && string.IsNullOrEmpty(player.NickName) == false)
        {
            return player.NickName;
        }

        return heroName;
    }
}
