# Off-screen Teammate Indicators

Mario-style hex heads pinned to the screen edge, pointing at every **remote** teammate (online players and
bots) whose hero has left the camera view. **Local feedback only:** there's no sim state, no `.qtn` and
no commands. Each client projects the other heroes' views itself.

## Behavior
- One indicator per remote hero. It shows while the hero's projected point (view position + `worldOffset`) is
  outside the screen, then pops in (`OutBack`) pinned where the line from the screen centre toward the hero
  crosses the edge. It pops out (`InBack`) once the hero is back on screen.
- Local couch-coop slots never get one. The camera frames every local slot (`MyLocalPlayer.Register`
  adds them to `FollowCamera`), so they can't be off-screen.
- **Head** = the live in-game rig head (`BlobAnimationView.Head`'s `SpriteRenderer.sprite`, re-read every
  frame). It deliberately does **not** use the `CharacterData.UIHead` portrait. A rig with no separate head (Capy) falls back to its `BlobAnimationView.Torso` sprite, which is the whole body. The head sits fully inside the hex with no mask (a uGUI stencil `Mask` gives a jagged, un-antialiased edge) and always stays upright.
- **Border + arrow** = the hero's `CharacterData.RingColor`, or `downedColor` (red) while that teammate is
  **Downed**. A **KO** teammate's indicator is hidden (`hideWhenKO`).
- **Low HP pulse:** at or below `lowHealthPercent` (0.3, `Health` only, not Shield, the same rule as
  `HurtOverlayUiWidget`) the border + arrow pulse `RingColor` ↔ `downedColor`. The period lerps from
  `lowHealthPulseSlow` (0.8s, at the threshold) to `lowHealthPulseFast` (0.3s, near 0 HP), like a
  heartbeat. That gives three steps toward "go help": normal → pulsing → solid red (Downed). The ping rings
  read the border colour, so they pulse too. `lowHealthPercent = 0` turns it off.
  One 0..1 "urgency" drives both the tint and a **`downedColor` glow** behind the hex: 0 while healthy, the pulse
  at low HP, and a steady 1 while Downed. The glow's peak alpha is `lowHealthGlowAlpha` (0.85, 0 = no glow).
  Its sprite is `Art/Sprites/UI/Focus/RR_HexagonOutline_Focus.png`, the border's halo from the focus-glow
  generator (Tools ▸ RiftRaiders ▸ UI ▸ Generate Focus Outlines). `RR_HexagonOutline` sits in that
  generator's `ExtraSourcePaths` because no `FocusOutlineWidget` uses it. The widget looks it up in
  `FocusOutlineSet` and creates the `LowHealthGlow` Image lazily as the first child of `Visual`, so it sits
  behind the frame and pops with it.
- **Distance** = XZ world distance from local player one, as `"{n}m"`. The label sits on the side opposite the
  arrow so it never overlaps it, whichever edge the indicator is on.
- **Ping ripple:** when that teammate pings (`PingPlaced`, matched on `Owner`) while their indicator is
  showing, hex outline rings (`PingRipple/Ring0..1`, `RR_HexagonOutline`) expand from behind the frame
  (1→`pingMaxScale` 2.2) and fade, staggered half a `pingPulsePeriod` (1s) apart, for `pingRippleDuration`
  (3s, unscaled). The whole ripple fades over its last pulse. They use the **border's**
  colour, read live, so they're the hero's `RingColor`, or `downedColor` if that teammate is Downed. An on-screen
  pinger gets nothing here, because the world ripple already marks them.
- Pinned inside `Screen.safeArea` (intersected with the canvas rect), inset by `edgePadding`.
- A point behind the camera is mirrored back through the screen centre, so the arrow still points the right way.

## Files
- `Scripts/UI/InGame/Hud/OffscreenPlayerIndicatorManager.cs`: singleton on the prefab root (full-stretch
  rect). `Register(entity, transform)` / `Unregister(entity)` clone/destroy the inactive `IndicatorTemplate`
  child. A single `LateUpdate` does all the projection, clamping and show/hide. Tuning: `edgePadding`,
  `visibleMargin`, `worldOffset`, `downedColor`, `lowHealthPercent`, `lowHealthPulseSlow`/`Fast`,
  `hideWhenKO`, `showDistance`.
- `Scripts/UI/InGame/Hud/OffscreenPlayerIndicatorWidget.cs`: one indicator. It paints the head and tint, rotates
  `ArrowPivot`, places the label and runs the pop tweens (PrimeTween). It never decides its own position.
- `CharView.Initialize` registers every non-local hero, and `DeInitialize` unregisters. This is the same
  manager+widget pattern `CharacterUiWidgetManager` uses.
- **Prefab (template):** `Prefabs/UI/OffscreenPlayerIndicators.prefab`, nested in
  `Prefabs/GameplayScene/Canvas.prefab` under `HUD` (after `MiniMapRoot`), so both gameplay scenes have it
  and it hides with the HUD.
  `IndicatorTemplate` (inactive, 96×96) → `Visual` (scaled by the pop) → `PingRipple` → `Ring0`/`Ring1`, `Shadow`, `Fill` → `Head` (72×72, fully inside the hex),
  `Border`, `ArrowPivot` → `Arrow` (authored pointing up at y=64), `Distance` (TMP).

## Placeholder art
`RR_Hexagon` (shadow + fill), `RR_HexagonOutline` (border), `StageTriangle` (arrow), Oxanium ExtraBold
Shadow (label). These are all white sprites tinted in the prefab or code, so swapping the art is a sprite swap on the prefab.

## Testing
Offline, run with a bot player (`RuntimePlayer.IsBot`, see `docs/bots.md`). Bots count as remote, so walk away
from it. Downed shows when enemies down the bot (there's no "down" cheat). The **Become Bot** cheat doesn't
help here: that hero stays a local slot, so it never gets an indicator.

## Known simplifications
- The head follows the rig's current sprite (expressions included) but not the rig's facing flip.
- Indicators don't avoid each other or other HUD elements (party bar, minimap). Two teammates in the same
  direction overlap.
- Distance is always measured from local player one, even with couch co-op.
- Ping rings are scaled Images, so the outline gets thicker as they grow. That's fine for the placeholder;
  a thinner ring sprite would keep the line from getting chunky at full size.

## Off-screen enemy arrows ("clear all enemies" window)

Red arrows pinned to the screen edge pointing at the enemies still alive while a Breathing Break waits to be
secured. That's the "CLEAR ALL ENEMIES TO SECURE THE AREA" window (`CurrentState == Survival`,
`CurrentPhaseKind == Breathing`, `!BreathingAreaSecured`), the same one `SurvivalWidget`'s banner and the
minimap's clear-enemy markers use.

- **Who gets one:** every enemy that isn't `EnemyActionPhase.Dead` and is off-screen. Elites/Persistent are included (unlike
  the minimap's clear-enemy markers): `SurvivalProgressionUtility.IsEncounterCleared` waits for every enemy, so each one is
  something to go kill. Only the `maxIndicators` (8) nearest to local player one get an arrow at once.
- **Where:** the same projection/edge clamp as the teammate indicators (safe area, `edgePadding` 48), aimed at the
  enemy's entity view (interpolated) or its `Transform3D` when it has no view, plus `worldOffset`.
- **Look:** `StageTriangle` fill (#FF3B3B), dark `StageTriangleOutline`, soft shadow. Pops in `OutBack` and out `InBack`, and pulses
  (1 to 1.15, 0.8 s, unscaled) while shown.
- **Elites always get one**, for the whole run (any state except Lobby), not just the clear window. They sort ahead of other
  enemies and are exempt from `maxIndicators`, so every off-screen Elite has an arrow (`EnemyTier.Elite`, same test as the minimap).
  Same red look as the other arrows for now.
- Everything else pops out the moment the area secures or the window ends some other way.

Files:
- `Scripts/UI/InGame/Hud/OffscreenEnemyIndicatorManager.cs`: self-contained like `MinimapWidget`. It reads
  `Frames.Predicted` every `LateUpdate` (no view registration, because enemies only matter for this short window) and pools arrows.
- `Scripts/UI/InGame/Hud/OffscreenEnemyIndicatorWidget.cs`: one arrow (rotate, pop, pulse).
- `Prefabs/UI/OffscreenEnemyIndicators.prefab`: full-stretch root with the manager, plus an inactive `ArrowTemplate` →
  `Visual` → `ArrowPivot` → `Shadow`/`Arrow`/`Outline` (authored pointing up). It's nested in `Prefabs/GameplayScene/Canvas.prefab`
  under `HUD`, right after `OffscreenPlayerIndicators`.

To test, reach the end of a Survival segment with enemies still alive (the timeline hits a Breathing boundary and the
"CLEAR ALL ENEMIES" banner shows), then walk away from them.
