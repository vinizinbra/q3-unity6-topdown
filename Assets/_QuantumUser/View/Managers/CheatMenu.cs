// The whole overlay is stripped unless CHEATS_ENABLED is defined (Project Settings > Player >
// Scripting Define Symbols). Only the code that SENDS cheats is gated - CheatCommand and CheatSystem
// stay compiled on every build so networked command indices/effects match across clients (see
// CheatCommand). So a build without this define simply can't open the menu; the sim still
// understands the command if some other client sends it.
#if CHEATS_ENABLED
using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;

namespace QuantumUser.View
{
    // Code-only, real uGUI cheat overlay - no scene object or prefab needed: every control below is
    // built at runtime in BuildUi() and it instantiates itself (see Bootstrap) into a
    // DontDestroyOnLoad host, same self-bootstrapping shape the old OnGUI version used. A small
    // "Cheats" button toggles a compact draggable window that only ever covers a corner of the
    // screen, so it never blocks gameplay. Every button just fires one CheatCommand for the local
    // player, exactly like the existing debug-grant triggers (e.g. RiftMutationDebugTrigger) - the
    // sim does the actual work (CheatSystem), keeping it deterministic and network-safe.
    public class CheatMenu : QuantumGlobalMonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameObject host = new GameObject("CheatMenu");
            host.AddComponent<CheatMenu>();
            DontDestroyOnLoad(host);
        }

        private enum Picker { None, Weapon, Mutation, GlobalUpgrade, HeroUpgrade }

        private struct AssetEntry
        {
            public string Name;
            public string Description;
            public long Id;
            public Sprite Icon;

            // Which cheat this entry fires when clicked, and its Amount payload - set per-entry
            // (rather than derived from the picker as a whole) since Hero Upgrade mixes two different
            // actions in one list: GrantSkillUpgrade (Dash/Hero Skill, Amount = SkillSlotId) and
            // GrantPassiveUpgrade (Passive, Amount unused).
            public CheatActionKind Action;
            public int Amount;

            // Sub-group header shown above this entry in the side panel (e.g. "Dash Skill"/"Hero
            // Skill"/"Passive") - null/empty for the other 3 pickers, which have no sub-grouping.
            public string Section;
        }

        private struct PickerButtonRef
        {
            public TMP_Text Text;
            public string Label;
        }

        // Fully hides the overlay - not just closing the window, the small "Cheats" toggle button
        // too (e.g. for a clean screenshot/recording) - by disabling the whole Canvas, toggled by
        // HideKey without needing to touch `enabled` (which would also stop QUpdate below from
        // running and re-detecting the key). The Time.timeScale enforcer further down deliberately
        // keeps running while hidden - an active override shouldn't silently reset back to 1x just
        // because the overlay is hidden.
        private const KeyCode HideKey = KeyCode.F1;
        private bool _hidden;

        private bool _open;
        private bool _overrideTimeScale;
        private float _timeScale = 1f;
        private Picker _picker;

        private List<AssetEntry> _weapons;
        private List<AssetEntry> _mutations;
        private List<AssetEntry> _globalUpgrades;
        private List<AssetEntry> _heroUpgrades;

        private static readonly Color WindowBg = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        private static readonly Color ButtonBg = new Color(0.38f, 0.4f, 0.48f, 1f);
        private static readonly Color SectionColor = new Color(0.6f, 0.8f, 1f);

        private Canvas _canvas;
        private GameObject _toggleButtonGo;
        private GameObject _windowGo;
        private RectTransform _windowRect;

        private Toggle _overrideToggle;
        private TMP_Text _overrideToggleLabel;

        private readonly Dictionary<Picker, PickerButtonRef> _pickerButtons = new Dictionary<Picker, PickerButtonRef>();
        private GameObject _pickerScrollRootGo;
        private RectTransform _pickerListContent;
        private ScrollRect _pickerScrollRect;
        private TMP_Text _pickerNoneLabel;
        private TMP_Text _sidePanelTitle;
        private RectTransform _sidePanelRect;
        private GameObject _sidePanelGo;
        private CheatMenuDragHandle _dragHandle;

        private const float WindowWidth = 360f;

        private CheatMenuTimeScaleEnforcer _enforcer;

        private void Awake()
        {
            GameObject enforcerGo = new GameObject("CheatMenuTimeScaleEnforcer");
            enforcerGo.transform.SetParent(transform, false);
            _enforcer = enforcerGo.AddComponent<CheatMenuTimeScaleEnforcer>();

            BuildUi();
        }

        // QuantumGlobalMonoBehaviour requires this - only the hide-toggle hotkey is polled here
        // (never redeclare Update()/LateUpdate() on a QuantumGlobalMonoBehaviour subclass, see that
        // base class's own comment).
        public override void QUpdate(QuantumGame game)
        {
            if (UnityEngine.Input.GetKeyDown(HideKey))
            {
                _hidden = !_hidden;
                _canvas.enabled = !_hidden;
            }
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject go = new GameObject("CheatMenuEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(go);
        }

        private void BuildUi()
        {
            EnsureEventSystem();

            GameObject canvasGo = new GameObject("CheatMenuCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30000;
            // Mirrors the project's own in-match HUD Canvas (QuantumGameScene "Canvas" GameObject) so
            // this overlay scales exactly like the rest of the UI instead of using its own guessed
            // settings - same reference resolution, match mode, and full match-by-height.
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            canvasGo.AddComponent<GraphicRaycaster>();

            BuildToggleButton(canvasGo.transform);
            BuildWindow(canvasGo.transform);
            BuildSidePanel(canvasGo.transform);
            _dragHandle.Target2 = _sidePanelRect;

            UpdateOpenState();
        }

        private void BuildToggleButton(Transform parent)
        {
            RectTransform rt = CreateRect(parent, "ToggleButton");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(10, -110);
            rt.sizeDelta = new Vector2(120, 38);

            Image img = rt.gameObject.AddComponent<Image>();
            img.color = ButtonBg;
            Button btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                _open = true;
                UpdateOpenState();
            });

            CreateStretchedLabel(CreateRect(rt, "Text"), "Cheats", 17, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

            _toggleButtonGo = rt.gameObject;
        }

        private void BuildWindow(Transform parent)
        {
            RectTransform rt = CreateRect(parent, "Window");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(10, -110);
            rt.sizeDelta = new Vector2(WindowWidth, 0);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = WindowBg;

            VerticalLayoutGroup vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 4;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _windowRect = rt;
            _windowGo = rt.gameObject;

            BuildHeader(rt);

            CreateSectionLabel(rt, "Time scale");
            _overrideToggle = CreateToggle(rt, TimeScaleLabelText(), false, v =>
            {
                _overrideTimeScale = v;
                PushEnforcer();
            }, out _overrideToggleLabel);

            Transform speedRow = CreateRow(rt);
            CreateButton(speedRow, "0.25x", () => SetTimeScale(0.25f));
            CreateButton(speedRow, "0.5x", () => SetTimeScale(0.5f));
            CreateButton(speedRow, "1x", () => SetTimeScale(1f));
            CreateButton(speedRow, "2x", () => SetTimeScale(2f));
            CreateButton(speedRow, "10x", () => SetTimeScale(10f));

            CreateSectionLabel(rt, "Flow");
            Row(rt, ("Pause", CheatActionKind.Pause), ("Continue", CheatActionKind.Continue));
            Row(rt, ("+30s", CheatActionKind.Advance30Sec), ("+1 min", CheatActionKind.Advance1Min));
            Row(rt, ("Advance Phase", CheatActionKind.AdvancePhase), ("Next Breathing", CheatActionKind.AdvanceToNextBreathing));
            CreateButton(rt, "Level Up", () => Send(CheatActionKind.LevelUp));
            BreathingRow(rt, ("Breath 1 (Lv6)", 1), ("Breath 2 (Lv12)", 2));
            BreathingRow(rt, ("Breath 3 (Lv15)", 3), ("Breath 4 (Lv20)", 4));

            // One-click combo (see CheatActionKind.SetupTestRun): jumps to Breath 4 (Lv20, the last
            // Breathing phase) same as the button above, but also auto-resolves every level-up
            // screen that jump queues instead of leaving them to click through, reveals the whole
            // minimap, and grants 5000 coins - a fast "midgame test setup" instead of assembling it
            // by hand every time.
            CreateButton(rt, "Setup Test Run (Ph.4)", () => Send(CheatActionKind.SetupTestRun, amount: 4));

            // Runtime counterpart to the Editor-only "RiftRaiders/Disable Upgrade Screen Animation"
            // main-toolbar button - flips one static flag (UpgradeScreenDebugState.SkipAnimations)
            // ChooseWindow/GameplayUiController already check, so it works in a build too, not just
            // the Editor. Handy for chaining several debug level-ups back to back without sitting
            // through each screen's intro + Time.timeScale ramp.
            CreateToggle(rt, "Skip Upgrade Screen Animations", UpgradeScreenDebugState.SkipAnimations, v =>
            {
                UpgradeScreenDebugState.SkipAnimations = v;
            }, out _);

            CreateSectionLabel(rt, "Player");
            Row(rt, ("Buy Accessory", CheatActionKind.BuyAccessory), ("Heal Full", CheatActionKind.HealFull));
            Row(rt, ("God Mode", CheatActionKind.ToggleGodMode), ("Revive All", CheatActionKind.Revive));
            Row(rt, ("Kill All Enemies", CheatActionKind.KillAllEnemies), ("Open Chest", CheatActionKind.OpenChest));
            CreateButton(rt, "+1000 Coins", () => Send(CheatActionKind.GrantCoins, amount: 1000));
            Row(rt, ("Damage = 1", CheatActionKind.SetDamageToOne), ("Reset Damage", CheatActionKind.ResetDamage));
            CreateButton(rt, "Toggle Auto-Shoot", () => Send(CheatActionKind.ToggleManualFire));

            CreateSectionLabel(rt, "Grant");
            PickerButton(rt, "Get Weapon", Picker.Weapon);
            PickerButton(rt, "Get Rift Mutation", Picker.Mutation);
            PickerButton(rt, "Grant Global Upgrade", Picker.GlobalUpgrade);
            PickerButton(rt, "Grant Hero Upgrade", Picker.HeroUpgrade);
        }

        private void BuildHeader(Transform parent)
        {
            Transform row = CreateRow(parent);

            // Transparent full-row background so dragging works anywhere on the header, not just on
            // the title text - CheatMenuDragHandle needs a raycastable Graphic to receive drag events.
            Image dragBg = row.gameObject.AddComponent<Image>();
            dragBg.color = new Color(0f, 0f, 0f, 0.001f);
            _dragHandle = row.gameObject.AddComponent<CheatMenuDragHandle>();
            _dragHandle.Target = _windowRect;

            TMP_Text title = CreateStretchedLabel(CreateRect(row, "Title"), "Cheats", 19, Color.white, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            title.rectTransform.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            CreateButton(row, "Close", () =>
            {
                _open = false;
                UpdateOpenState();
            }, 72f);
        }

        private void UpdateOpenState()
        {
            _toggleButtonGo.SetActive(_open == false);
            _windowGo.SetActive(_open);

            // Closing the window should also drop whatever picker was open, so reopening never shows
            // a stale side panel or a picker button stuck on its "▼" (open) arrow.
            if (_open == false && _picker != Picker.None)
                OnPickerButtonClicked(_picker);
        }

        // Clicking any speed button both sets the value and enables the override, so it takes
        // effect immediately without also toggling the checkbox.
        private void SetTimeScale(float scale)
        {
            _timeScale = scale;
            _overrideTimeScale = true;
            _overrideToggle.SetIsOnWithoutNotify(true);
            _overrideToggleLabel.text = TimeScaleLabelText();
            PushEnforcer();
        }

        private string TimeScaleLabelText()
        {
            return $" Override = {_timeScale:0.##}x (also unfreezes upgrade/choose window)";
        }

        private void PushEnforcer()
        {
            _enforcer.Active = _overrideTimeScale;
            _enforcer.Scale = _timeScale;
        }

        private void Row(Transform parent, (string label, CheatActionKind action) a, (string label, CheatActionKind action) b)
        {
            Transform row = CreateRow(parent);
            CreateButton(row, a.label, () => Send(a.action));
            CreateButton(row, b.label, () => Send(b.action));
        }

        // Jumps straight to the Nth Breathing phase and tops the run's XP up to that phase's paired
        // level in one command - see CheatSystem.JumpToBreathing for the level pairing/why.
        private void BreathingRow(Transform parent, (string label, int n) a, (string label, int n) b)
        {
            Transform row = CreateRow(parent);
            CreateButton(row, a.label, () => Send(CheatActionKind.JumpToBreathing, amount: a.n));
            CreateButton(row, b.label, () => Send(CheatActionKind.JumpToBreathing, amount: b.n));
        }

        private void PickerButton(Transform parent, string label, Picker picker)
        {
            Button btn = CreateButton(parent, "▶ " + label, null);
            TMP_Text text = btn.GetComponentInChildren<TMP_Text>();
            _pickerButtons[picker] = new PickerButtonRef { Text = text, Label = label };
            btn.onClick.AddListener(() => OnPickerButtonClicked(picker));
        }

        private void OnPickerButtonClicked(Picker picker)
        {
            _picker = _picker == picker ? Picker.None : picker;

            foreach (KeyValuePair<Picker, PickerButtonRef> kvp in _pickerButtons)
            {
                bool isOpen = _picker == kvp.Key;
                kvp.Value.Text.text = (isOpen ? "▼ " : "▶ ") + kvp.Value.Label;
            }

            RebuildPickerList();
        }

        // A detached panel to the right of the main window (not stacked inline into it) - the main
        // window's ContentSizeFitter would otherwise keep growing every time a picker with many
        // entries opened, eventually running out of vertical screen space. Fixed size instead, with
        // its own scroll region, so opening a picker never resizes the main window at all. Dragged
        // together with the window (see BuildUi wiring _dragHandle.Target2) so it stays docked to it.
        private void BuildSidePanel(Transform parent)
        {
            RectTransform rt = CreateRect(parent, "SidePanel");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(380, 560);
            rt.anchoredPosition = _windowRect.anchoredPosition + new Vector2(WindowWidth + 12, 0);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = WindowBg;

            VerticalLayoutGroup vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 6;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            _sidePanelTitle = CreateSectionLabel(rt, "");

            RectTransform scrollRt = CreateRect(rt, "PickerScroll");
            LayoutElement scrollLe = scrollRt.gameObject.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1; // fills whatever height is left in the panel's fixed size

            Image scrollBg = scrollRt.gameObject.AddComponent<Image>();
            scrollBg.color = new Color(0f, 0f, 0f, 0.25f);

            ScrollRect scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewportRt = CreateRect(scrollRt, "Viewport");
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewportRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            viewportRt.gameObject.AddComponent<RectMask2D>();

            RectTransform contentRt = CreateRect(viewportRt, "Content");
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            // THE bug behind "icons invisible, description cut off": a fresh RectTransform's
            // sizeDelta defaults to (100,100) - left alone, stretch anchors (anchorMin.x=0,
            // anchorMax.x=1) add that 100 ON TOP of the parent width, split evenly around pivot.x=0.5
            // - so Content rendered ~50px wider on EACH side than the Viewport that masks it
            // (RectMask2D). Every row's icon sits at the very left edge, which fell inside that
            // clipped-off left margin - invisible - while the text further right just looked shifted/
            // cut on whichever edge it crossed. Zeroing sizeDelta.x makes Content exactly match the
            // Viewport's width, so nothing spills past the mask on either side.
            contentRt.sizeDelta = new Vector2(0, contentRt.sizeDelta.y);

            VerticalLayoutGroup contentVlg = contentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing = 4;
            contentVlg.childControlWidth = true;
            contentVlg.childForceExpandWidth = true;
            contentVlg.childControlHeight = true;
            contentVlg.childForceExpandHeight = false;
            ContentSizeFitter contentFitter = contentRt.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRt;
            scroll.content = contentRt;

            _pickerScrollRootGo = scrollRt.gameObject;
            _pickerListContent = contentRt;
            _pickerScrollRect = scroll;

            _pickerNoneLabel = CreateSectionLabel(rt, "  (none - not in a match yet, or config unassigned)");

            _sidePanelRect = rt;
            _sidePanelGo = rt.gameObject;
            _sidePanelGo.SetActive(false);
        }

        private void RebuildPickerList()
        {
            for (int i = _pickerListContent.childCount - 1; i >= 0; i--)
                Destroy(_pickerListContent.GetChild(i).gameObject);

            if (_picker == Picker.None)
            {
                _sidePanelGo.SetActive(false);
                return;
            }

            _sidePanelGo.SetActive(true);
            _sidePanelTitle.text = _pickerButtons[_picker].Label;

            List<AssetEntry> entries = EntriesFor(_picker);
            if (entries == null || entries.Count == 0)
            {
                _pickerScrollRootGo.SetActive(false);
                _pickerNoneLabel.gameObject.SetActive(true);
                return;
            }

            _pickerNoneLabel.gameObject.SetActive(false);
            _pickerScrollRootGo.SetActive(true);

            // Entries carry their own section header (Hero Upgrade's Dash Skill/Hero Skill/Passive
            // sub-groups - see AssetEntry.Section) - a header is inserted whenever it changes from the
            // previous entry, same grouping DebugUpgradeMenuWindow.AddLabel does for its Hero tab. The
            // other 3 pickers leave Section empty, so no header ever prints for them.
            string lastSection = null;
            foreach (AssetEntry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Section) == false && entry.Section != lastSection)
                {
                    CreateSectionLabel(_pickerListContent, entry.Section);
                    lastSection = entry.Section;
                }

                AssetEntry captured = entry;
                CreateEntryButton(_pickerListContent, captured, () => Send(captured.Action, captured.Id, captured.Amount));
            }

            // A row's Name/Description height depends on its wrapped-text width, which in turn
            // depends on its parent's width - all resolved in the same frame these rows were just
            // created. Without forcing one synchronous rebuild here, the very first frame can read
            // stale (pre-wrap) preferred heights and clip text until something else happens to
            // trigger a layout pass - this forces it immediately, bottom-up, so it's correct from the
            // first frame the panel shows.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_pickerListContent);

            _pickerScrollRect.verticalNormalizedPosition = 1f;
        }

        private List<AssetEntry> EntriesFor(Picker picker)
        {
            switch (picker)
            {
                case Picker.Weapon: return _weapons ?? (_weapons = BuildWeapons());
                case Picker.Mutation: return _mutations ?? (_mutations = BuildMutations());
                case Picker.HeroUpgrade: return _heroUpgrades ?? (_heroUpgrades = BuildHeroUpgrades());
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
                list.Add(new AssetEntry { Name = name, Id = weaponRef.Id.Value, Icon = weapon?.GetIcon(), Action = CheatActionKind.GetWeapon });
            }
            return list;
        }

        private List<AssetEntry> BuildMutations()
        {
            List<AssetEntry> list = new List<AssetEntry>();
            if (TryGetLevelUpConfig(out Frame f, out LevelUpConfig config) == false)
                return list;

            AddUpgrades(f, config.RiftMutations, list, CheatActionKind.GetRiftMutation);
            return list;
        }

        private List<AssetEntry> BuildGlobalUpgrades()
        {
            List<AssetEntry> list = new List<AssetEntry>();
            if (TryGetLevelUpConfig(out Frame f, out LevelUpConfig config) == false)
                return list;

            AddUpgrades(f, config.GlobalUpgrades, list, CheatActionKind.GrantGlobalUpgrade);
            return list;
        }

        // Unlike Weapon/Mutation/GlobalUpgrade (pooled globally on LevelUpConfig), Hero Upgrades are
        // pooled per-hero on CharacterData/HeroSkill - see LevelUpConfig's own comment on why - so
        // this reads the local player's own equipped CharacterData instead of the shared config.
        private unsafe bool TryGetLocalCharacterData(out Frame frame, out EntityRef entity, out CharacterData data)
        {
            frame = _game != null ? _game.Frames.Predicted : null;
            entity = default;
            data = null;
            if (frame == null || MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
                return false;

            entity = MyLocalPlayer.Instance.EntityRef;
            if (frame.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false || stats->CharacterData.IsValid == false)
                return false;

            data = frame.FindAsset(stats->CharacterData);
            return data != null;
        }

        // Mirrors DebugUpgradeMenuTrigger's own 3-category Hero tab (Dash Skill/Hero Skill/Passive),
        // including its rank-aware description preview for ranked Ascensions (MaxRank > 1, e.g. Hero
        // Mastery) - GetRank reads UpgradeHistory so an already-partially-ranked line previews its
        // NEXT rank rather than always rank 1.
        private List<AssetEntry> BuildHeroUpgrades()
        {
            List<AssetEntry> list = new List<AssetEntry>();
            if (TryGetLocalCharacterData(out Frame f, out EntityRef entity, out CharacterData data) == false)
                return list;

            AddSkillUpgrades(f, entity, data.DashSkillUpgrades, SkillSlotId.DashSkill, "Dash Skill", list);

            if (data.HeroSkill.IsValid == true)
            {
                SkillData heroSkillData = f.FindAsset(data.HeroSkill);
                AddSkillUpgrades(f, entity, heroSkillData?.Actions, SkillSlotId.HeroSkill, "Hero Skill", list);
            }

            AddPassiveUpgrades(f, entity, data.PassiveUpgrades, list);
            return list;
        }

        private static void AddSkillUpgrades(Frame f, EntityRef entity, List<AssetRef<SkillActionData>> refs, SkillSlotId slot, string section, List<AssetEntry> into)
        {
            if (refs == null)
                return;

            foreach (AssetRef<SkillActionData> upgradeRef in refs)
            {
                if (upgradeRef.IsValid == false)
                    continue;
                SkillActionData data = f.FindAsset(upgradeRef);
                if (data == null)
                    continue;

                string description = data.MaxRank > 1
                    ? data.GetDescription(SkillUpgradeUtility.GetRank(f, entity, upgradeRef) + 1)
                    : data.GetDescription();

                into.Add(new AssetEntry
                {
                    Name = data.DisplayName,
                    Id = upgradeRef.Id.Value,
                    Icon = data.Icon,
                    Description = description,
                    Action = CheatActionKind.GrantSkillUpgrade,
                    Amount = (int)slot,
                    Section = section,
                });
            }
        }

        private static void AddPassiveUpgrades(Frame f, EntityRef entity, List<AssetRef<PassiveUpgradeData>> refs, List<AssetEntry> into)
        {
            if (refs == null)
                return;

            foreach (AssetRef<PassiveUpgradeData> upgradeRef in refs)
            {
                if (upgradeRef.IsValid == false)
                    continue;
                PassiveUpgradeData data = f.FindAsset(upgradeRef);
                if (data == null)
                    continue;

                string description = data.MaxRank > 1
                    ? data.GetDescription(PassiveUpgradeUtility.GetRank(f, entity, upgradeRef) + 1)
                    : data.GetDescription();

                into.Add(new AssetEntry
                {
                    Name = data.DisplayName,
                    Id = upgradeRef.Id.Value,
                    Icon = data.Icon,
                    Description = description,
                    Action = CheatActionKind.GrantPassiveUpgrade,
                    Section = "Passive",
                });
            }
        }

        private static void AddUpgrades<T>(Frame f, List<AssetRef<T>> refs, List<AssetEntry> into, CheatActionKind action)
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
                into.Add(new AssetEntry { Name = name, Id = upgradeRef.Id.Value, Icon = upgrade?.Icon, Description = upgrade?.GetDescription(), Action = action });
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

        // --- Small runtime uGUI building blocks - no prefabs, everything below is plain
        // GameObject/Component construction so the whole menu stays self-contained in this file. ---

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Transform CreateRow(Transform parent)
        {
            RectTransform rt = CreateRect(parent, "Row");
            HorizontalLayoutGroup hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            rt.gameObject.AddComponent<LayoutElement>().minHeight = 30;
            return rt;
        }

        private static TMP_Text CreateSectionLabel(Transform parent, string text)
        {
            RectTransform rt = CreateRect(parent, "Label");
            TMP_Text tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 18;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = SectionColor;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            rt.gameObject.AddComponent<LayoutElement>().minHeight = 26;
            return tmp;
        }

        private static TMP_Text CreateStretchedLabel(RectTransform rt, string text, int fontSize, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            TMP_Text tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            return tmp;
        }

        private static Button CreateButton(Transform parent, string label, System.Action onClick, float? preferredWidth = null)
        {
            RectTransform rt = CreateRect(parent, "Button");
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = ButtonBg;
            Button btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());

            CreateStretchedLabel(CreateRect(rt, "Text"), label, 17, Color.white, FontStyles.Normal, TextAlignmentOptions.Center);

            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 36;
            if (preferredWidth.HasValue)
                le.preferredWidth = preferredWidth.Value;
            else
                le.flexibleWidth = 1;

            return btn;
        }

        // Like CreateButton, but lays out an optional icon beside the label instead of a single
        // centered/stretched label - used for the side-panel picker entries (weapons/mutations/
        // global+hero upgrades all carry an Icon, see UpgradeData.Icon/WeaponDataAsset.GetIcon()).
        // Icon + Name (bold) + wrapped Description stacked underneath, same fields DebugUpgradeButtonWidget
        // shows for a level-up/debug-grant row - unlike that widget this has no granted/checkmark
        // state (CheatMenu just fires the command, it doesn't track history locally), so it's a
        // plain click-to-grant row. Height is content-driven (ContentSizeFitter), not fixed, since
        // Description can wrap to a variable number of lines.
        private static Button CreateEntryButton(Transform parent, AssetEntry entry, System.Action onClick)
        {
            RectTransform rt = CreateRect(parent, "Entry");
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = ButtonBg;
            Button btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());

            HorizontalLayoutGroup hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 8, 8);
            hlg.spacing = 10;
            hlg.childAlignment = TextAnchor.UpperLeft;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = false;
            rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (entry.Icon != null)
            {
                RectTransform iconRt = CreateRect(rt, "Icon");
                LayoutElement iconLe = iconRt.gameObject.AddComponent<LayoutElement>();
                iconLe.minWidth = 52;
                iconLe.preferredWidth = 52;
                iconLe.minHeight = 52;
                iconLe.preferredHeight = 52;
                Image iconImg = iconRt.gameObject.AddComponent<Image>();
                iconImg.sprite = entry.Icon;
                iconImg.preserveAspect = true;
            }

            RectTransform textColRt = CreateRect(rt, "TextColumn");
            VerticalLayoutGroup textVlg = textColRt.gameObject.AddComponent<VerticalLayoutGroup>();
            textVlg.spacing = 3;
            textVlg.childControlWidth = true;
            textVlg.childForceExpandWidth = true;
            textVlg.childControlHeight = true;
            textVlg.childForceExpandHeight = false;

            // preferredWidth pinned to a tiny value (NOT left at -1/"unset") is the key fix - a
            // wrapping TMP_Text's own ILayoutElement.preferredWidth is its UNWRAPPED single-line
            // width (often huge), which VerticalLayoutGroup then bubbles up as textColRt's own
            // preferred width. Without this override, HorizontalLayoutGroup sees a total preferred
            // width far exceeding the row's available space and squeezes this column down toward its
            // (unset -> ~0) minimum instead of granting it the leftover space via flexibleWidth -
            // exactly the "title clipped to one word, description cut off" bug. Pinning preferredWidth
            // small makes the row's total preferred size trivially fit, so the real leftover space
            // (row width - icon - spacing) gets assigned here via flexibleWidth as intended.
            LayoutElement textColLe = textColRt.gameObject.AddComponent<LayoutElement>();
            textColLe.minWidth = 1;
            textColLe.preferredWidth = 1;
            textColLe.flexibleWidth = 1;

            CreateWrappedLabel(textColRt, entry.Name, 19, Color.white, FontStyles.Bold);

            if (string.IsNullOrEmpty(entry.Description) == false)
                CreateWrappedLabel(textColRt, entry.Description, 16, new Color(0.78f, 0.81f, 0.88f), FontStyles.Normal);

            return btn;
        }

        // Width-stretched (follows parent), height driven purely by wrapped text content - unlike
        // CreateStretchedLabel (fixed-rect single-line controls), this is for the picker entries'
        // variable-length Name/Description text.
        private static TMP_Text CreateWrappedLabel(Transform parent, string text, int fontSize, Color color, FontStyles style)
        {
            RectTransform rt = CreateRect(parent, "Text");
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);

            TMP_Text tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        private static Toggle CreateToggle(Transform parent, string label, bool initial, System.Action<bool> onChanged, out TMP_Text labelText)
        {
            Transform row = CreateRow(parent);
            HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            RectTransform boxRt = CreateRect(row, "Box");
            LayoutElement boxLe = boxRt.gameObject.AddComponent<LayoutElement>();
            boxLe.minWidth = 22;
            boxLe.preferredWidth = 22;
            boxLe.minHeight = 22;
            boxLe.preferredHeight = 22;
            Image boxImg = boxRt.gameObject.AddComponent<Image>();
            boxImg.color = new Color(0.25f, 0.25f, 0.3f, 1f);

            RectTransform checkRt = CreateRect(boxRt, "Checkmark");
            checkRt.anchorMin = new Vector2(0.15f, 0.15f);
            checkRt.anchorMax = new Vector2(0.85f, 0.85f);
            checkRt.offsetMin = Vector2.zero;
            checkRt.offsetMax = Vector2.zero;
            Image checkImg = checkRt.gameObject.AddComponent<Image>();
            checkImg.color = new Color(0.4f, 0.9f, 0.4f, 1f);

            Toggle toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            toggle.isOn = initial;

            RectTransform labelRt = CreateRect(row, "Label");
            labelText = CreateStretchedLabel(labelRt, label, 16, Color.white, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            labelRt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
            return toggle;
        }
    }

    // Reapplies the cheat time-scale override every LateUpdate, deliberately AFTER every other
    // script's LateUpdate (very high execution order) - the same guarantee the old OnGUI-based
    // override had (OnGUI runs after the whole Update/LateUpdate pass), so an active override
    // reliably wins even against systems that ease Time.timeScale toward 0 for the Level-Up/Chest
    // window. Kept as its own plain MonoBehaviour (not a QuantumGlobalMonoBehaviour) specifically so
    // it can declare LateUpdate - CheatMenu itself must never redeclare Update()/LateUpdate(), see
    // QuantumGlobalMonoBehaviour's own comment.
    [DefaultExecutionOrder(32000)]
    internal class CheatMenuTimeScaleEnforcer : MonoBehaviour
    {
        public bool Active;
        public float Scale = 1f;

        private bool _wasActive;

        private void LateUpdate()
        {
            if (Active)
                Time.timeScale = Scale;
            else if (_wasActive)
                Time.timeScale = 1f;
            _wasActive = Active;
        }
    }

    // Drag-moves the cheat window's RectTransform from its header row, replacing GUI.DragWindow.
    // Target2 (the detached side picker panel, docked beside the window) is dragged along with it so
    // the two stay visually attached instead of the panel being left behind.
    internal class CheatMenuDragHandle : MonoBehaviour, IDragHandler
    {
        public RectTransform Target;
        public RectTransform Target2;

        private Canvas _canvas;

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnDrag(PointerEventData eventData)
        {
            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            Vector2 delta = eventData.delta / scale;

            if (Target != null)
                Target.anchoredPosition += delta;
            if (Target2 != null)
                Target2.anchoredPosition += delta;
        }
    }
}
#endif
