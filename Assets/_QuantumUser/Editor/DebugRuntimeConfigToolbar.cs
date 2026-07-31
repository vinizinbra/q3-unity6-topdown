namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum;
    using UnityEditor;
    using UnityEditor.Toolbars;
    using UnityEngine;

    // Main Editor toolbar buttons (see HeroQuickPlayToolbar for the same MainToolbarElementAttribute
    // pattern) for quickly testing low-health/no-shield scenarios without hand-editing RuntimeConfig.
    // Targets the same QuantumRunnerLocalDebug as Hero Quick Play (the "QuantumDebugRunner" object in
    // QuantumGameScene.unity) - the edit is a direct in-memory field set, never saved to disk, so it's
    // gone again the next time the scene is loaded/reloaded, same as Hero Quick Play's edits.
    public static class DebugRuntimeConfigToolbar
    {
        [MainToolbarElement("RiftRaiders/Debug Health Shield", defaultDockPosition = MainToolbarDockPosition.Left)]
        private static IEnumerable<MainToolbarElement> CreateDebugButtons()
        {
            yield return new MainToolbarButton(
                new MainToolbarContent("Half HP / No Shield", "Set the local debug runner's DebugInitialHealthMultiplier to 0.5 and DebugInitialShieldMultiplier to 0"),
                () => SetDebugMultipliers(FP._0_50, FP._0));

            yield return new MainToolbarButton(
                new MainToolbarContent("Reset HP/Shield", "Reset the local debug runner's DebugInitialHealthMultiplier/DebugInitialShieldMultiplier back to 1"),
                () => SetDebugMultipliers(FP._1, FP._1));
        }

        private static void SetDebugMultipliers(FP healthMultiplier, FP shieldMultiplier)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Debug Health/Shield: stop the current Play session before changing the preset - RuntimeConfig is only read once, at session start.");
                return;
            }

            var debugRunner = Object.FindFirstObjectByType<QuantumRunnerLocalDebug>();
            if (debugRunner == null)
            {
                Debug.LogWarning("Debug Health/Shield: no QuantumRunnerLocalDebug found in the open scene(s) - open QuantumGameScene first.");
                return;
            }

            Undo.RecordObject(debugRunner, "Set Debug Health/Shield Preset");
            debugRunner.RuntimeConfig.DebugInitialHealthMultiplier = healthMultiplier;
            debugRunner.RuntimeConfig.DebugInitialShieldMultiplier = shieldMultiplier;

            Debug.Log($"Debug Health/Shield: health x{healthMultiplier}, shield x{shieldMultiplier}");
        }
    }
}
