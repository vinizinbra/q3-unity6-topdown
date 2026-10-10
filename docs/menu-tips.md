# Menu Tips

Short teaching tips for the player, shown as a rotating bar on the Home tab (bottom left) and, optionally, on the
loading screen.

- **Data:** `Assets/_Project/Data/Tips/MenuTips.asset` (`TipsData`, `Create ▸ RiftRaiders ▸ Tips`): a plain list of
  strings. Rich text is fine; highlight the key word with `<color=#FD3971>…</color>`. One mechanic per tip, ~130
  characters or less (the bar fits two lines and shrinks the font down to 15). Keep them true to the docs in `docs/`.
- **`TipsWidget`** (on `HomeTab/Tips`): shuffle bag (every tip once before repeats, never the same twice in a row);
  reading time = max(`minSeconds`, length × `secondsPerCharacter`), unscaled; a thin line along the bottom fills as the
  time runs out. **Transition:** the old tip slides up and fades out, the new one slides in from below and types itself
  out (`typewriter`), the icon pops. Hover pauses, a click skips. The text sits in a `RectMask2D` so the slide is clipped by the bar.
- **LoadingWindow** (the match-start screen) uses the same asset through its `tipsData` field (wired in MenuScene; it replaces the screen's own `tips` array). It shows a tip at the bottom and swaps it every `tipInterval` (6 s) without the bar's transition. The generic `LoadingScreen` (scene transitions, 2 s) has no tips.
- Adding a tip = add a line to the asset. Retuning the feel = the `TipsWidget` fields (slide distance, durations, typewriter speed).
