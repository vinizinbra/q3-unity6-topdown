namespace QuantumUser.Editor
{
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // RuntimeConfig.SurvivalConfig/WorldBalance are hidden fields derived from RuntimeConfig.WorldDefinition
    // (see docs/worlds.md). The menu path derives them in MatchMakingConfig (Awake/ApplyWorld), but playing a
    // gameplay scene directly goes through QuantumRunnerLocalDebug, where nothing did - so its hidden fields
    // kept whatever world was derived last (e.g. World 2 enemies under a World 1 balance). This re-derives
    // them on every open debug runner right before entering Play mode, so the debug runner always plays the
    // world its WorldDefinition says.
    [InitializeOnLoad]
    public static class DebugRunnerWorldSync
    {
        private const string Tag = "DebugRunnerWorld";

        static DebugRunnerWorldSync()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
                return;

            foreach (QuantumRunnerLocalDebug runner in Object.FindObjectsByType<QuantumRunnerLocalDebug>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Sync(runner);
        }

        public static void Sync(QuantumRunnerLocalDebug runner)
        {
            RuntimeConfig config = runner.RuntimeConfig;
            if (config == null)
                return;

            if (QuantumUnityDB.TryGetGlobalAsset(config.WorldDefinition, out WorldDefinition world) == false)
            {
                LogHelper.Warn(Tag, $"{runner.name}: RuntimeConfig.WorldDefinition is unset or doesn't resolve - SurvivalConfig/WorldBalance left as saved.");
                return;
            }

            if (config.SurvivalConfig == world.SurvivalConfig && config.WorldBalance == world.Balance)
                return;

            Undo.RecordObject(runner, "Sync debug runner world");
            world.ApplyTo(config);
            EditorUtility.SetDirty(runner);
            LogHelper.Log(Tag, $"{runner.name}: SurvivalConfig/WorldBalance re-derived from {world.name}.");
        }
    }
}
