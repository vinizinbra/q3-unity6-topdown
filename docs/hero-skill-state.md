# Hero Skill State (ring + badge + countdown feedback)

One resolver, three presentations, for "what is this hero's skill/passive doing right now".

## Design
`HeroSkillStateUtility.TryGet(frame, entity, out HeroSkillState)` (`Assets/_Project/Scripts/UI/InGame/Hud/Heroes/`)
reads the Predicted frame and returns `Active` (show it), `Full` (skill on / meter at payoff), `Fill` (0-1),
`SecondsLeft` (-1 = no clock), `Icon`.

| Hero | Source | Shape |
|---|---|---|
| Max (Overdrive), Brute (Juggernaut) | any hero/dash slot `Active` with `GetActiveDuration() > 0` | TIMED skill: Fill drains 1 -> 0, Full while running |
| Zara (Flow State, passive) | `ZaraFlow.Progress`; Full = `IsActive` | PASSIVE METER, always shown while the passive exists |
| Lux (Scavenge, passive) | `ScrapStacks / StacksRequired`; Full = free cast banked | PASSIVE METER, always shown |

Passives are checked first (the component is hero-specific). A future timed skill that overrides
`GetActiveDuration` is picked up for free.

## Presentations
- **Foot ring** (`MovementRingView`, local player only): the authored ring/glow keep the hero's own colour. A
  separate procedural **radial fill ring** (vertex-coloured mesh, `BuildFillRing`) sits just outside it in an
  ANALOGOUS hue (`skillFillHueShift`) - it drains for timed skills and is the meter for Flow/Scavenge, with a faint
  track for the empty part. Passive at its payoff (`Full`) -> a white **shine** travels around the ring (Lux banked
  free cast, Zara Flow active). Punch-scale pop when a skill turns on / a meter fills; fast blink in the last 3s of a
  timed skill. Optional glow scale (`skillGlowScaleBoost`, default 0).
- **Last-3-seconds countdown** (`SkillUrgentCountdownWidget`, created at runtime by `CharacterUiWidget
  .UpdateSkillCountdown`, no prefab authoring): a small 3-2-1 in the gap between the health bars and the head, pop per number; digits take the hero's ring colour (`skillCountdownUsesRingColor`, off = white) with a dark outline. Timed skills only -
  passives have no clock. Position/size: `skillCountdownHeadOffset` / `skillCountdownSize` on `CharacterUiWidget`.
- **Hero hexagons are OFF** (`CharacterUiWidget.showHeroHexBadges = false`, default): the shared SkillsOverlay object
  holding every per-hero hexagon (Max Rage/Overdrive, Brute Juggernaut/charge/Aftershock, Zara Flow, Lux Scrap) is
  deactivated for all heroes - the ring + head countdown replace it. Flip the flag to bring them back.
- **Nameplate badges** (only when the flag is on; `HeroHudWidget.Section`): whole seconds until the last 3s, then the badge steps aside
  (the big number takes over); punch-pop on appear and when a meter reaches full; optional `iconImage` painted from
  the active `SkillData.Icon`. A Section with no `root` assigned (Max/Brute countdown) resolves its own root: the
  outermost stack of background graphics behind the text (`EffectiveRoot`).

## Editor authoring needed
- Optional: assign `iconImage` on the Max/Brute `timer` sections of the nameplate (unassigned = no icon).
- Optional: tune the fill ring / shine fields on `MovementRingView` in `CharFeedbacks.prefab`, and the countdown
  offset on `CharacterUiWidget` (defaults tuned at the gameplay camera).

## Known simplifications
- Ring only exists for the local player; teammates get the nameplate badge + head countdown only.
- With the hexagons off, exact numbers are gone (Max Rage n/N, Brute charge/Aftershock stacks, Lux n/10); the ring fill shows the proportion.
- Fill-ring colour is derived from `CharacterData.RingColor`; a hero whose ring is already yellow gets a yellow-green
  fill - override with `skillFillColor` if one looks off.
- Verified in-Editor 2026-10-22 (Max, Brute, Zara, Lux; Zara/Lux meters were forced through the Verified frame).
