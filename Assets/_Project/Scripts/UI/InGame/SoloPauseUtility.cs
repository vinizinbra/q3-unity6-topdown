using System.Collections.Generic;
using Quantum;
using UnityEngine;

/// <summary>
/// Pauses the simulation while a menu is open, but only when the local player is playing completely alone
/// (human players only - bots don't count, same rule as the tutorial popups). Reuses the tutorial pause
/// (SetTutorialPauseCommand disables GameplaySystemGroup), so it is deterministic and rollback-safe.
///
/// Pauses are requested by an owner (the settings popup, the hero info panel) and the game only resumes when
/// the LAST owner lets go, so two menus open at once can't un-pause each other. A request is ignored when the
/// game is already paused by something else (a level-up, the boss reveal, a tutorial popup): this never takes
/// over a pause, so it can never resume one it didn't start.
/// </summary>
public static class SoloPauseUtility
{
    private static readonly HashSet<object> Owners = new HashSet<object>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay() => Owners.Clear();

    /// <summary>Human players only - bots don't count against solo (see docs/bots.md).</summary>
    public static unsafe bool IsSolo(Frame frame)
    {
        int humanCount = 0;
        var players = frame.Filter<PlayerLink>();

        while (players.Next(out EntityRef entity, out PlayerLink _))
        {
            if (frame.Has<BotBrain>(entity) == false)
                humanCount++;
        }

        return humanCount <= 1;
    }

    /// <summary>Pauses on behalf of <paramref name="owner"/> if playing solo and not already paused. Safe to call every frame.</summary>
    public static void Request(object owner)
    {
        if (owner == null || Owners.Contains(owner))
            return;

        Owners.RemoveWhere(IsDestroyed);

        Frame frame = QuantumRunner.Default != null && QuantumRunner.Default.Game != null ? QuantumRunner.Default.Game.Frames.Predicted : null;
        if (frame == null || IsSolo(frame) == false)
            return;

        if (Owners.Count == 0)
        {
            // Already paused by something else (level-up, boss, tutorial popup): leave it alone.
            if (frame.SystemIsEnabledSelf<GameplaySystemGroup>() == false)
                return;

            TutorialPopup.SendPause(true);
        }

        Owners.Add(owner);
    }

    /// <summary>Lets go of <paramref name="owner"/>'s pause; the game resumes once nobody is holding it. Safe to call without a request.</summary>
    public static void Release(object owner)
    {
        if (owner == null || Owners.Remove(owner) == false)
            return;

        Owners.RemoveWhere(IsDestroyed);

        if (Owners.Count == 0)
            TutorialPopup.SendPause(false);
    }

    /// <summary>Drops <paramref name="owner"/> without resuming - for a match that is being torn down.</summary>
    public static void Forget(object owner) => Owners.Remove(owner);

    private static bool IsDestroyed(object owner) => owner is Object unityObject && unityObject == null;
}
