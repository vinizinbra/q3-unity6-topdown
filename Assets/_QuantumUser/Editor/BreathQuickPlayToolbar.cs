namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEditor.Toolbars;
    using UnityEngine;

    // Main Editor toolbar buttons (see HeroQuickPlayToolbar for the same MainToolbarElementAttribute
    // pattern), one per Breathing phase (1-4). Clicking one sets the local debug runner's
    // RuntimeConfig.DebugStartBreathIndex and enters Play mode - DebugCheatSystem reads it once at
    // match start and reuses CheatSystem.JumpToBreathing's landing logic (lands CurrentPhaseIndex/
    // PhaseTimer/SurvivalTime on the Nth Breathing phase, tops XP up to its paired display level).
    // Same direct in-memory field edit as DebugRuntimeConfigToolbar - never saved to disk, gone again
    // the next time the scene is loaded/reloaded. Does not touch LocalPlayers, so whatever hero is
    // already assigned (e.g. via Hero Quick Play) stays as-is.
    public static class BreathQuickPlayToolbar
    {
        [MainToolbarElement("RiftRaiders/Breath Quick Play", defaultDockPosition = MainToolbarDockPosition.Left)]
        private static IEnumerable<MainToolbarElement> CreateBreathButtons()
        {
            for (var n = 1; n <= 4; n++)
            {
                var breathNumber = n;
                var content = new MainToolbarContent($"Breath {breathNumber}", $"Start already landed on Breathing phase {breathNumber} (local debug runner)");
                yield return new MainToolbarButton(content, () => PlayFromBreath(breathNumber));
            }

            // DebugStartBreathIndex is a direct in-memory field edit on the scene object (see class
            // comment) - once a Breath button sets it, it stays set for every subsequent Play press,
            // including the plain Play button, until something clears it back to 0. This is that
            // "something": resets the skip and starts a normal, non-debug-skipped match.
            yield return new MainToolbarButton(
                new MainToolbarContent("Play (No Skip)", "Reset DebugStartBreathIndex to 0 and enter Play mode normally."),
                PlayWithoutSkip);
        }

        private static void PlayFromBreath(int breathNumber)
        {
            if (EditorApplication.isPlaying)
            {
                LogHelper.Warn("BreathQuickPlay", "stop the current Play session before changing the preset - RuntimeConfig is only read once, at session start.");
                return;
            }

            var debugRunner = Object.FindFirstObjectByType<QuantumRunnerLocalDebug>();
            if (debugRunner == null)
            {
                LogHelper.Warn("BreathQuickPlay", "no QuantumRunnerLocalDebug found in the open scene(s) - open QuantumGameScene first.");
                return;
            }

            Undo.RecordObject(debugRunner, "Set Debug Start Breath Index");
            debugRunner.RuntimeConfig.DebugStartBreathIndex = breathNumber;

            LogHelper.Log("BreathQuickPlay", $"starting at Breathing phase {breathNumber}.");
            EditorApplication.isPlaying = true;
        }

        private static void PlayWithoutSkip()
        {
            if (EditorApplication.isPlaying)
            {
                LogHelper.Warn("BreathQuickPlay", "stop the current Play session before changing the preset - RuntimeConfig is only read once, at session start.");
                return;
            }

            var debugRunner = Object.FindFirstObjectByType<QuantumRunnerLocalDebug>();
            if (debugRunner == null)
            {
                LogHelper.Warn("BreathQuickPlay", "no QuantumRunnerLocalDebug found in the open scene(s) - open QuantumGameScene first.");
                return;
            }

            if (debugRunner.RuntimeConfig.DebugStartBreathIndex != 0)
            {
                Undo.RecordObject(debugRunner, "Reset Debug Start Breath Index");
                debugRunner.RuntimeConfig.DebugStartBreathIndex = 0;
                LogHelper.Log("BreathQuickPlay", "reset DebugStartBreathIndex to 0.");
            }

            EditorApplication.isPlaying = true;
        }
    }
}
