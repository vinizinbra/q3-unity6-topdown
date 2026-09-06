// The whole overlay is stripped unless CHEATS_ENABLED is defined (Project Settings > Player >
// Scripting Define Symbols). Only the code that SENDS cheats is gated - CheatCommand and CheatSystem
// stay compiled on every build so networked command indices/effects match across clients (see
// CheatCommand). So a build without this define simply can't open the menu; the sim still
// understands the command if some other client sends it.
#if CHEATS_ENABLED
using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

namespace QuantumUser.View
{
    // Code-only, immediate-mode (OnGUI) cheat overlay - no scene object or prefab needed: it
    // instantiates itself at runtime (see Bootstrap) into a DontDestroyOnLoad host. A small "Cheats"
    // button toggles a compact draggable window that only ever covers a corner of the screen, so it
    // never blocks gameplay. Every button just fires one CheatCommand for the local player, exactly
    // like the existing debug-grant triggers (e.g. RiftMutationDebugTrigger) - the sim does the
    // actual work (CheatSystem), keeping it deterministic and network-safe.
    public class CheatMenu : QuantumGlobalMonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameObject host = new GameObject("CheatMenu");
            host.AddComponent<CheatMenu>();
            DontDestroyOnLoad(host);
        }

        private enum Picker { None, Weapon, Mutation, GlobalUpgrade }

        private struct AssetEntry
        {
            public string Name;
            public long Id;
        }

        private bool _open;
        private bool _overrideTimeScale;
        private bool _wasOverriding;
        private float _timeScale = 1f;
        private Picker _picker;
        private Rect _window = new Rect(10, 10, 300, 0);
        private Vector2 _scroll;

        private List<AssetEntry> _weapons;
        private List<AssetEntry> _mutations;
        private List<AssetEntry> _globalUpgrades;

        // Built once in EnsureStyles - the default IMGUI skin is translucent with tiny text, which
        // is unreadable over gameplay, so every control uses these opaque, larger-font styles.
        private Texture2D _solidBg;
        private GUIStyle _windowStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _toggleStyle;

        // QuantumGlobalMonoBehaviour requires this; the overlay does its work in OnGUI instead.
        public override void QUpdate(QuantumGame game) { }

        private void EnsureStyles()
        {
            if (_solidBg != null)
                return;

            _solidBg = new Texture2D(1, 1);
            _solidBg.SetPixel(0, 0, new Color(0.07f, 0.07f, 0.09f, 1f)); // fully opaque
            _solidBg.Apply();

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(10, 10, 22, 10),
            };
            _windowStyle.normal.background = _solidBg;
            _windowStyle.onNormal.background = _solidBg;
            _windowStyle.normal.textColor = Color.white;
            _windowStyle.onNormal.textColor = Color.white;

            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
            };
            _labelStyle.normal.textColor = new Color(0.6f, 0.8f, 1f);

            _toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 12 };
            _toggleStyle.normal.textColor = Color.white;
            _toggleStyle.onNormal.textColor = Color.white;
        }

        private void OnGUI()
        {
            EnsureStyles();

            // Pinned here (not in LateUpdate) because the base runs LateUpdate early (execution
            // order -10) - OnGUI runs after every LateUpdate, so this reliably overrides the
            // Level-Up/Chest screen easing Time.timeScale toward 0, and any speed-up sticks. When
            // the override is switched off, restore 1x once so a leftover 10x doesn't persist.
            if (_overrideTimeScale)
                Time.timeScale = _timeScale;
            else if (_wasOverriding)
                Time.timeScale = 1f;
            _wasOverriding = _overrideTimeScale;

            if (!_open)
            {
                if (GUI.Button(new Rect(10, 10, 90, 28), "Cheats", _buttonStyle))
                    _open = true;
                return;
            }

            _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, "Cheats",
                _windowStyle, GUILayout.Width(300));
        }

        private void DrawWindow(int id)
        {
            if (GUILayout.Button("Close", _buttonStyle))
                _open = false;

            GUILayout.Label("Time scale", _labelStyle);
            _overrideTimeScale = GUILayout.Toggle(_overrideTimeScale,
                $" Override = {_timeScale:0.##}x (also unfreezes upgrade/choose window)", _toggleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.25x", _buttonStyle)) SetTimeScale(0.25f);
            if (GUILayout.Button("1x", _buttonStyle)) SetTimeScale(1f);
            if (GUILayout.Button("2x", _buttonStyle)) SetTimeScale(2f);
            if (GUILayout.Button("5x", _buttonStyle)) SetTimeScale(5f);
            if (GUILayout.Button("10x", _buttonStyle)) SetTimeScale(10f);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("Flow", _labelStyle);
            Row(("Pause", CheatActionKind.Pause), ("Continue", CheatActionKind.Continue));
            Row(("+1 min", CheatActionKind.Advance1Min), ("Advance Phase", CheatActionKind.AdvancePhase));
            Row(("Next Breathing", CheatActionKind.AdvanceToNextBreathing), ("Level Up", CheatActionKind.LevelUp));

            GUILayout.Space(4);
            GUILayout.Label("Player", _labelStyle);
            Row(("Buy Accessory", CheatActionKind.BuyAccessory), ("Heal Full", CheatActionKind.HealFull));
            Row(("God Mode", CheatActionKind.ToggleGodMode), ("Revive All", CheatActionKind.Revive));
            Row(("Kill All Enemies", CheatActionKind.KillAllEnemies), ("Open Chest", CheatActionKind.OpenChest));
            if (GUILayout.Button("+1000 Coins", _buttonStyle))
                Send(CheatActionKind.GrantCoins, amount: 1000);

            GUILayout.Space(4);
            GUILayout.Label("Grant", _labelStyle);
            PickerButton("Get Weapon", Picker.Weapon);
            PickerButton("Get Rift Mutation", Picker.Mutation);
            PickerButton("Grant Global Upgrade", Picker.GlobalUpgrade);

            DrawPickerList();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        // Clicking any speed button both sets the value and enables the override, so it takes
        // effect immediately without also toggling the checkbox.
        private void SetTimeScale(float scale)
        {
            _timeScale = scale;
            _overrideTimeScale = true;
        }

        private void Row((string label, CheatActionKind action) a, (string label, CheatActionKind action) b)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(a.label, _buttonStyle))
                Send(a.action);
            if (GUILayout.Button(b.label, _buttonStyle))
                Send(b.action);
            GUILayout.EndHorizontal();
        }

        private void PickerButton(string label, Picker picker)
        {
            bool isOpen = _picker == picker;
            if (GUILayout.Button((isOpen ? "▼ " : "▶ ") + label, _buttonStyle))
            {
                _picker = isOpen ? Picker.None : picker;
                _scroll = Vector2.zero;
            }
        }

        private void DrawPickerList()
        {
            if (_picker == Picker.None)
                return;

            List<AssetEntry> entries = EntriesFor(_picker);
            if (entries == null || entries.Count == 0)
            {
                GUILayout.Label("  (none - not in a match yet, or config unassigned)", _labelStyle);
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(160));
            foreach (AssetEntry entry in entries)
            {
                if (GUILayout.Button(entry.Name, _buttonStyle))
                    Send(ActionFor(_picker), entry.Id);
            }
            GUILayout.EndScrollView();
        }

        private static CheatActionKind ActionFor(Picker picker)
        {
            switch (picker)
            {
                case Picker.Weapon: return CheatActionKind.GetWeapon;
                case Picker.Mutation: return CheatActionKind.GetRiftMutation;
                default: return CheatActionKind.GrantGlobalUpgrade;
            }
        }

        private List<AssetEntry> EntriesFor(Picker picker)
        {
            switch (picker)
            {
                case Picker.Weapon: return _weapons ?? (_weapons = BuildWeapons());
                case Picker.Mutation: return _mutations ?? (_mutations = BuildMutations());
                default: return _globalUpgrades ?? (_globalUpgrades = BuildGlobalUpgrades());
            }
        }

        private bool TryGetLevelUpConfig(out Frame frame, out LevelUpConfig config)
        {
            frame = _game != null ? _game.Frames.Predicted : null;
            config = null;
            if (frame == null || frame.RuntimeConfig.LevelUpConfig.IsValid == false)
                return false;
            config = frame.FindAsset(frame.RuntimeConfig.LevelUpConfig);
            return config != null;
        }

        private List<AssetEntry> BuildWeapons()
        {
            List<AssetEntry> list = new List<AssetEntry>();
            if (TryGetLevelUpConfig(out Frame f, out LevelUpConfig config) == false)
                return list;
            if (config.WeaponChoicePool.IsValid == false)
                return list;

            WeaponChoicePoolData pool = f.FindAsset(config.WeaponChoicePool);
            if (pool?.Weapons == null)
                return list;

            foreach (AssetRef<WeaponDataAsset> weaponRef in pool.Weapons)
            {
                if (weaponRef.IsValid == false)
                    continue;
                WeaponDataAsset weapon = f.FindAsset(weaponRef);
                string name = weapon != null && string.IsNullOrEmpty(weapon.DisplayName) == false
                    ? weapon.DisplayName
                    : weaponRef.Id.Value.ToString();
                list.Add(new AssetEntry { Name = name, Id = weaponRef.Id.Value });
            }
            return list;
        }

        private List<AssetEntry> BuildMutations()
        {
            List<AssetEntry> list = new List<AssetEntry>();
            if (TryGetLevelUpConfig(out Frame f, out LevelUpConfig config) == false)
                return list;

            AddUpgrades(f, config.RiftMutations, list);
            return list;
        }

        private List<AssetEntry> BuildGlobalUpgrades()
        {
            List<AssetEntry> list = new List<AssetEntry>();
            if (TryGetLevelUpConfig(out Frame f, out LevelUpConfig config) == false)
                return list;

            AddUpgrades(f, config.GlobalUpgrades, list);
            return list;
        }

        private static void AddUpgrades<T>(Frame f, List<AssetRef<T>> refs, List<AssetEntry> into)
            where T : UpgradeData
        {
            if (refs == null)
                return;

            foreach (AssetRef<T> upgradeRef in refs)
            {
                if (upgradeRef.IsValid == false)
                    continue;
                UpgradeData upgrade = f.FindAsset(upgradeRef);
                string name = upgrade != null && string.IsNullOrEmpty(upgrade.DisplayName) == false
                    ? upgrade.DisplayName
                    : upgradeRef.Id.Value.ToString();
                into.Add(new AssetEntry { Name = name, Id = upgradeRef.Id.Value });
            }
        }

        private void Send(CheatActionKind action, long assetId = 0, int amount = 0)
        {
            if (_game == null)
                return;
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            {
                LogHelper.Warn("CheatMenu", "no local player set up yet - ignoring cheat");
                return;
            }

            _game.SendCommand(new CheatCommand { Action = action, AssetId = assetId, Amount = amount });
        }
    }
}
#endif
