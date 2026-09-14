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
        int enemiesKilled = 0;
        FP damageDealt = FP._0;
        int timesDowned = 0;
        FP riftShardsEarned = FP._0;
        var teamResults = new List<RunResultPopup.PlayerResult>();

        var players = frame.Filter<PlayerLink, CharacterStats>();
        while (players.Next(out EntityRef entity, out PlayerLink _, out CharacterStats stats))
        {
            enemiesKilled += stats.MonstersKilled;
            damageDealt += stats.DamageDealt;
            timesDowned += stats.TimesDowned;
            riftShardsEarned += stats.RiftShardsEarned;

            CharacterData data = frame.FindAsset(stats.CharacterData);

            teamResults.Add(new RunResultPopup.PlayerResult
            {
                HeroName = data != null ? data.name : "Hero",
                HeroColor = data != null ? data.RingColor : Color.white,
                DamageDealt = stats.DamageDealt
            });
        }

        FP runTime = frame.Global->SurvivalTime;

        RunResultPopup popup = InMatchPopupManager.instance.Open<RunResultPopup>();
        popup?.Setup(won, enemiesKilled, damageDealt, timesDowned, riftShardsEarned, runTime, teamResults);
    }
}
