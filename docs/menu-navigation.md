# Menu & in-game UI navigation (gamepad / keyboard focus)

How a controller or keyboard moves through the menu tabs, popups and the in-match windows, and how the
focused element is shown. Input plumbing itself (bindings, `GamepadControls`) lives in `docs/input.md`.

## Pieces

| Piece | File | Job |
|---|---|---|
| `FocusScopeWidget` | `UI/Menu/Navigation/` | One level of the cascade (main menu → tab content → detail panel). Only the active scope is stick-navigable. |
| `MenuNavigationController` | `UI/Menu/Navigation/` | Switches the active scope, **Cancel = back one level**, **L1/R1 (`[` `]`) = sub-tabs** of the open tab, focus recovery, popup awareness. |
| `TabStripWidget` | `UI/Menu/Heroes/` | A row of tab buttons showing one page each (`Step(dir)`, `Changed`). Shared by Heroes/Loadout/Upgrades/Catalog. |
| `TabContent.SubTabs` / `DefaultFocus` | `UI/Menu/Tabs/` | What L1/R1 act on / where focus lands when the tab is entered. |
| `FocusOutlineWidget` + `FocusOutlineSet` | `UI/Common/` | The focus glow (see below). |
| `CanvasInputGateWidget` | `UI/Common/` | Makes a `Canvas.enabled = false` canvas also stop taking input (see "Menu behind a match"). |
| `UiSelectionUtility` | `UI/Common/` | Default focus, "wait until the intro animation settled". |

## Cascade rules
- Main menu (left column) is vertical only and **focus follows the tab**: moving the stick onto a rail button opens
  that tab as a preview (focus stays on the button; mouse hover does nothing, only a click opens). A button without
  content (News) leaves the current preview. **Submit or Right** steps into the open tab's content; Submit on a list
  item enters its detail panel; **Cancel** goes back one level (the tab stays open).
- `TabGroup.PrewarmClosedTabs`: ~1 s after the menu opens, each closed tab is switched on invisibly for two frames so
  it builds its lists then, not on first focus (Catalog 36 ms → 2 ms). `Show()/Hide()` aren't called by it.
- A scope's own `Selectable`s get `Navigation.None` while it is not active, so the stick never leaks between levels.
- **L1/R1** switch the sub-tabs of the open tab (Power/Exploration, Enemies/Bosses/…, Skill/Mastery,
  Weapons/Perks), wrapping. Tabs without a `TabStripWidget` (Home) ignore them. Focus follows if the switch
  hides the focused page.
- Adding a tab: give its `TabContent` a `FocusScopeWidget` (parent = the LeftMenu scope), a `DefaultFocus`,
  and register it in the LeftMenu `TabGroup.tabContent`.

## Popups and windows
- `UiPopup.Show()` holds the popup **non-interactable and unfocused until its intro animations finished**
  (one-shot `UiTween`s such as `ScaleTween`, and `ShakeGrowImpactAnimation`; 2.5 s safety cap), so a stray
  Submit can't dismiss a tutorial that also unpauses the sim.
- **Cancel (B)** closes the top popup only if `CanCloseOnDimClick` (tutorial popups don't allow it). Menu and in-match managers both do this.
- A menu popup is modal for the menu: while open, scoped selectables are non-navigable, and focus returns to
  the button that opened it. `ChooseWindow`: Cancel presses its secondary button (Keep Current/Cancel/Close);
  stick/Submit with nothing focused re-focuses the first pickable card.

## Focus stays in the open popup
`UiSelectionUtility.TrapFocusInside` (called every frame by `PopupManager` and `InMatchPopupManager` for the popup on
top) makes a popup behave like a modal: if the selection is lost, the next stick/Submit input focuses the popup's
control **nearest to its centre**; if the selection leaks to something behind it (HUD, menu, the dim), it is pulled
straight back (to where it was, else the nearest control). A popup with nothing selectable leaves focus empty - input
never reaches what is behind it. Not applied while the popup is still popping in (its first focus is pending).

## Text fields
`SubmitToEditInputField` (a `TMP_InputField` subclass) is what text fields use. Navigating onto the field only focuses it;
**Submit** (or a mouse/touch click) starts typing, Enter ends it. Two stock-TMP behaviours are overridden: it no longer
starts editing on select (`shouldActivateOnSelect`), and TMP's `OnCancel` - which *activates* an unfocused field - is
ignored unless the field is being edited, so **Cancel (B)** on an idle field goes back a level and Cancel while typing
only leaves the edit (focus stays on the field; a second Cancel goes back). While editing, `MenuNavigationController`
ignores Back / L1 / R1 (they are just keys). The room-code field is digits only, 5 characters (codes are `00000`-`99999`).
`Add Focus Glow To Open Scenes` switches any plain `TMP_InputField` it finds to this subclass.

## Gameplay input while UI is open
`Quantum.UiInputGate` (next to `GamepadControls`, Quantum assembly): while any registered source is blocking,
`QuantumDebugInput.PollInput` sends **empty input** to the sim (still polled every tick, so a held stick/key is
released at once). Commands (card picks...) are unaffected. Sources:
- `UiPopup` registers itself in `Awake` (subclasses must call `base.Awake()`), so every popup blocks.
- `UiInputBlockerWidget` on a window/panel: ChooseWindow (level-up, chest, Store, Blacksmith, Cursed Rift) and BossWindow.
- Sources are polled and weakly owned: a disabled owner doesn't block, a destroyed one is dropped. Anything under the
  menu Canvas only blocks while that Canvas is on. New modal UI = use `UiPopup` or add the blocker.
- **Release latch** (`UiInputGate.FilterAfterUi`): a button held while a UI is open (or still held on the tick it closes - the
  Submit/click that picked a card or closed settings/a tutorial) stays muted per slot until it is seen released once, so
  closing a window can't fire a Dash/Fire. Movement is never latched.
- Not blocking: the Hero Info hold overlay, HUD buttons, and the debug CheatMenu.

## Solo pause
When the local player is the only human in the match (bots don't count), the **in-match settings popup** and the
**Hero Info panel** (hold Tab / **L2** / Select) pause the simulation while they are up. `SoloPauseUtility` reuses the
tutorial pause (`SetTutorialPauseCommand` → `GameplaySystemGroup` off), so it is deterministic. A pause is requested by
an owner and the game resumes only when the last owner lets go (settings + hero info at once don't un-pause each
other); a request is ignored while something else already paused the game (level-up, boss reveal, tutorial popup), so
it never resumes a pause it didn't start. Co-op (two or more humans) never pauses.

## Menu behind a match
`Canvas.enabled = false` only stops drawing. `CanvasInputGateWidget` (on the MenuScene root Canvas) mirrors
the canvas state onto a `CanvasGroup` + the raycaster and clears a selection under it, and
`MenuNavigationController` does nothing while the canvas is off. The EventSystem that runs during a match is the
MenuScene's (the game scene's one stays inactive).

## Focus glow
A fake glow: a pre-generated sprite (the element's background sprite dilated ~20 px, hollow inside, 9-slice
border grown by the same amount) drawn behind-and-around the focused element, tinted and pulsing. One extra quad, no
shader; follows chamfered corners exactly.

- `FocusOutlineWidget` on any focusable. Background = its own `Image`, else the Selectable's target graphic.
  Fields: `color`, `pulse`/`pulseDepth`/`pulseSpeed`. The glow object is created on first focus as the **last
  sibling** of the element's parent (so tightly packed neighbours don't cover it) and ignores layout.
- Sprites: `Tools ▸ RiftRaiders ▸ UI ▸ Generate Focus Outlines` (`FocusOutlineGenerator`). It discovers sources
  from every widget in the open scenes + prefabs under `Assets/_Project/Prefabs`, merges into
  `Resources/FocusOutlineSet`, writes PNGs to `Art/Sprites/UI/Focus/` and adds them to the `UISprites` atlas.
  Look constants (radius, core, power, strength) are at the top of the generator.
- **Apply to a whole scene:** `Tools ▸ RiftRaiders ▸ UI ▸ Add Focus Glow To Open Scenes`
  (`FocusGlowApplier`). Adds the widget to every Button/Toggle/Dropdown/InputField with a sprite background
  (skips Template/Debug/Dim/minimap, sliders, scrollbars), white around pink/green call-to-action sprites and pink
  elsewhere, then runs the generator. Idempotent. Save the scene afterwards.
- A focusable whose background sprite has no glow only logs a warning (`[FocusOutline]`): re-run the generator.
- Pair with `SelectableScaleWidget` for the pop; the glow mirrors the element's scale/offset every frame.

## Selectables inside a mask
Glow and scale both extend past the element's rect, so a `RectMask2D`/`Mask` always clips them (dropdown lists, tight scroll views).
Items inside a mask use **fill-only** feedback: the Toggle/Button `ColorBlock` tints the item background pink when focused
(dropdown `Item`s do this; no `FocusOutlineWidget`/`SelectableScaleWidget` on them). Scroll lists that need glow instead
pad the mask outward and give the content matching padding (Loadout/Upgrades/Catalog grids).

## Known gaps
- Sliders (volume rows) and scrollbars have no glow. Debug UI is excluded on purpose.
- Prefabs aren't touched by the applier (only scene objects); add the widget by hand there.
- L1/R1 use `Quantum.GamepadControls`; the real pad bindings weren't hardware-tested (verified by simulated events).
