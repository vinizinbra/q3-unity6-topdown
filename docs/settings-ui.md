# Settings screens (menu + in match)

`MenuSettingsPopup` (MenuScene) and `InMatchSettingsPopup` (game scene) share one look and one set of widgets.

- **`SettingsSliderRow` prefab** (`Prefabs/UI/Settings/`): a row with label, slider (dark track, pink fill, white handle) and a
  live value readout. `SettingsSliderRowWidget` keeps the readout in sync by polling the slider, so popups keep using
  `SetValueWithoutNotify` as before. Optional step labels replace the percentage ("80%", ...). Handle size is in the
  prefab (26x36).
- **Focus:** a slider has no single shape to glow, so its `FocusOutlineWidget` uses `shape` = the row and
  `background` = the row's image (`FocusOutlineWidget.shape`): the whole row glows. Sliders navigate with
  `Navigation.Vertical` (left/right belong to the slider). Buttons and the region dropdown get the usual glow + scale.
- **Panel:** same sprite/colour in both popups, `VerticalLayoutGroup` + `ContentSizeFitter`, so adding a row just grows it.
  The region dropdown copies the HomeTab dropdown's styling.

## Quality (mobile only)
`QualitySettingRowWidget` on a row makes it the Quality slider; nothing to wire in code. It lists 100 / 90 / 80 / ... / 20 % (9 steps)
(`RenderScaleSetting.Levels`; the saved value is the step index, and the first steps keep their old meaning) and hides itself unless `Application.isMobilePlatform` (tick `showOnDesktop` only to see the
layout in the Editor).

- `RenderScaleSetting` = the URP asset's `renderScale` (3D camera only; UI is unaffected, see `docs/performance.md`).
  Persisted in PlayerPrefs (`render_scale_index`) and re-applied at startup once the player has chosen a step. With no
  saved choice the pipeline's own scale (the shipped Mobile default) is untouched and its nearest step is shown.
- The Editor never writes the scale (the URP asset is shared there), so it can only be verified on a device.
- To add a step, edit `Levels`; the slider and labels follow. Present in both popups.

## Haptics toggle
`HapticsSettingRowWidget` on a row with a `Toggle` makes it the Haptics on/off switch (`Haptics.Enabled`, persisted); nothing to wire in code. Present in both popups as `HapticsRow`, built from a copy of the Quality row (Slider swapped for a Toggle; the row-level focus glow is kept). See `docs/haptics.md`.
