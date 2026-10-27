# Emoji Reactions

Four per-hero emojis (Happy / Angry / Cry / Shock) that pop above the sender's head for every player,
online included. Purely cosmetic.

## Input (local player one)
| Source | Happy | Angry | Cry | Shock |
|---|---|---|---|---|
| Gamepad right stick (flick past 0.75, re-arms under 0.35) | Up | Down | Left | Right |
| Keyboard | 1 | 2 | 3 | 4 |
| Touch (CF2 mobile mode) | "..." button (EmojiPicker prefab in the CF2 panel) → bubble with the hero's 4 emojis | | | |

Keyboard P2 / a second couch pad have no emoji binding (same as Ping).

## Sim - `Emoji.qtn`, `EmojiSystem`, `PlayEmojiCommand`
- Sent as a `PlayEmojiCommand { Kind }` (not Input: no per-tick bandwidth, and it works while
  `GameplaySystemGroup` is paused). `EmojiSystem` sits **outside** the group, after `TutorialSystem`.
- Validates range, skips bots, 1s cooldown per player (`PlayerEmoji.NextAllowedTick`, added lazily).
  Any life state is allowed (Downed/KO can cry).
- Raises `synced event EmojiPlayed { Player, Owner, Kind }`.

## View - `Scripts/UI/InGame/Hud/Emoji/`
- `EmojiWidget` - self-building (`GameplayUiController.QStart` → `Ensure`), polls keys 1-4 +
  `GamepadControls.Emoji` (right stick), sends the command for local slot 0, spawns bubbles on the event.
- `EmojiBubbleWidget` - camera-facing world `SpriteRenderer` following the hero's `CharView`, height from
  the top of the hero's sprites; pop-in overshoot, bob, pop-out, 2.5s; one per player.
  Per-hero tuning on `CharacterData`: `EmojiOffset` (world offset added on top of the auto height) and
  `EmojiScale` (size multiplier, 0 = 1) - read every frame, so they can be tuned live in Play mode.
  Shared defaults (`WorldSize`, `HeadClearance`, min/max height) are constants in the widget.
- `EmojiPickerWidget` - touch picker, **authored**: `Prefabs/UI/EmojiPicker.prefab`, instanced under the CF2 rig's
  `CF2-Canvas/CF2-Panel` (next to Dash-Button/SkillButton). Children: `EmojiButton` ("..." toggle), `Bubble`
  (sliced rounded Image + HorizontalLayoutGroup + ContentSizeFitter) and the inactive `Bubble/OptionTemplate`
  (Button + `Icon` Image). Only the 4 options are built in code - clones of the template, one per EmojiKind,
  sprite set on open from the local hero. Hidden unless `CF2Input.IsInMobileMode()` (`onlyInMobileMode`).
- `EmojiPresentation.GetSprite(entity, kind)` - resolves `CharacterData.Emojis[(int)kind]`.

## Art
`Art/Sprites/Emoji/HeroEmojis.png` - 948×1659, 4 columns (Happy/Angry/Cry/Shock) × 7 rows of 237px cells:
Max, Brute, Pixie, Robot (future hero, sliced only), Zara, Yoru, Capy (capybara). Sprites are named
`Emoji_<Row>_<Kind>`; the texture is packed in `GameplaySprites`. Replacing the PNG with the same
size/layout keeps the slices and every reference.

## Known simplifications / TODO
- The picker shows the same sprites in uGUI, so the Sprite Atlas Optimizer will flag the sheet as
  shared UI/Gameplay - duplicate it for UI if the batch split matters.
- No sound; the bubble is a plain sprite (no speech-bubble backdrop) and can hide behind tall walls.
- The CF2 rig and its touch buttons (Dash/Skill/EmojiPicker) are per-scene additions on the rig instance, not a
  shared prefab - add the EmojiPicker instance to each gameplay scene's CF2-Panel.
