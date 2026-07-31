namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using Quantum;
    using UnityEditor;
    using UnityEditor.Toolbars;
    using UnityEngine;

    // Main Editor toolbar buttons (Unity 6.3's MainToolbarElementAttribute), one per hero. Clicking one
    // finds the QuantumRunnerLocalDebug in the open scene (the "QuantumDebugRunner" object in
    // QuantumGameScene.unity), points its LocalPlayers[0].PlayerAvatar at that hero's EntityPrototype,
    // and enters Play mode - a shortcut for the manual "edit QuantumDebugRunner's Inspector, drag a
    // prefab, press Play" flow.
    // The field edit is made directly on the scene's SerializedObject and never saved to disk, so it's
    // gone again the next time the scene is loaded/reloaded (Unity discards in-memory Play mode edits).
    public static class HeroQuickPlayToolbar
    {
        private static readonly (string Name, string PrototypePath)[] Heroes =
        {
            ("Max", "Assets/_QuantumUser/Entities/Characters/MaxEntityPrototype.qprototype"),
            ("Brute", "Assets/_QuantumUser/Entities/Characters/BruteEntityPrototype.qprototype"),
            ("Pixie", "Assets/_QuantumUser/Entities/Characters/PixieEntityPrototype.qprototype"),
            ("Zara", "Assets/_QuantumUser/Entities/Characters/ZaraEntityPrototype.qprototype"),
            ("Kai", "Assets/_QuantumUser/Entities/Characters/KaiEntityPrototype.qprototype"),
            ("Lux", "Assets/_QuantumUser/Entities/Characters/LuxEntityPrototype.qprototype"),
        };

        [MainToolbarElement("RiftRaiders/Hero Quick Play", defaultDockPosition = MainToolbarDockPosition.Left)]
        private static IEnumerable<MainToolbarElement> CreateHeroButtons()
        {
            foreach (var (name, prototypePath) in Heroes)
            {
                var content = new MainToolbarContent(name, $"Play as {name} (local debug runner)");
                yield return new MainToolbarButton(content, () => PlayAsHero(name, prototypePath));
            }
        }

        private static void PlayAsHero(string heroName, string prototypePath)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Hero Quick Play: stop the current Play session before switching heroes.");
                return;
            }

            var debugRunner = Object.FindFirstObjectByType<QuantumRunnerLocalDebug>();
            if (debugRunner == null)
            {
                Debug.LogWarning("Hero Quick Play: no QuantumRunnerLocalDebug found in the open scene(s) - open QuantumGameScene first.");
                return;
            }

            var prototype = AssetDatabase.LoadAssetAtPath<EntityPrototype>(prototypePath);
            if (prototype == null)
            {
                Debug.LogError($"Hero Quick Play: couldn't load EntityPrototype at '{prototypePath}'.");
                return;
            }

            var so = new SerializedObject(debugRunner);
            var localPlayersProp = so.FindProperty("LocalPlayers");
            if (localPlayersProp.arraySize == 0)
            {
                localPlayersProp.arraySize = 1;
            }

            var playerProp = localPlayersProp.GetArrayElementAtIndex(0);
            playerProp.FindPropertyRelative("PlayerAvatar.Id.Value").longValue = prototype.Guid.Value;
            playerProp.FindPropertyRelative("PlayerNickname").stringValue = heroName;
            so.ApplyModifiedProperties();

            EditorApplication.isPlaying = true;
        }
    }
}
