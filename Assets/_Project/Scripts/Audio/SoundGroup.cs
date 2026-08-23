// The one axis every sound is filed under. A group serves BOTH purposes at once:
//
//   * Volume bus  - what an options-menu slider drives (AudioManager.SetGroupVolume).
//   * Voice budget - how many of this kind may be audible at once (AudioManager's group table).
//
// Deliberately one enum rather than a separate "category" and "group": they were the same concept
// split in two, and two parallel lists of near-identical names is exactly the kind of thing that
// drifts apart the moment someone adds to one and forgets the other.
//
// Per-group settings (volume, voice cap, overflow policy) live on the AudioManager as one row per
// value here - see AudioManager's Groups table, which auto-resizes when this enum changes.
//
// Adding a value: append it at the END. The AudioManager table is indexed by enum value, so
// reordering or inserting silently reassigns every existing row's settings.
public enum SoundGroup
{
    // Non-diegetic score. Usually one voice, looping, unaffected by combat density.
    Music = 0,

    // Looping world beds - wind, machinery, the hum of a chunk. Long, quiet, few voices.
    Ambience = 1,

    // Menus, buttons, level-up cards. Always 2D, and deliberately never starved by combat.
    Ui = 2,

    // Catch-all for anything that doesn't warrant its own budget.
    Sfx = 3,

    // Player weapon fire, reloads, casings. The highest-frequency group in the game by far, and
    // the one that most needs a tight cap - a bullet hell fires a lot of shots per second.
    Weapons = 4,

    // Hits, explosions, wall slams - the impact half of combat, separate from the firing half so
    // a dense firefight can't starve the feedback that tells a player they connected.
    Impacts = 5,

    // Enemy movement, telegraphs, attacks and deaths. Scales with enemy count, so it wants a cap
    // independent of how many enemy TYPES are on screen.
    Enemies = 6,

    // Orbs, coins, chests, upgrade grabs.
    Pickups = 7,

    // Hero barks and announcer lines. Small budget on purpose - overlapping dialogue is unreadable.
    Voice = 8,

    // Hero abilities - skills, dashes, ascension procs. Its own bus because these are low-frequency,
    // high-signal moments that have to cut through a firefight: sharing the Weapons or Sfx budget
    // would let constant chatter starve the one sound the player actually pressed a button for.
    Heroes = 9,
}

// What happens when a play would exceed its group's voice budget.
public enum SoundOverflowPolicy
{
    // The newest play wins - the oldest voice in the group is cut to make room. Right for
    // high-frequency combat chatter, where the most recent hit is the one the player caused and
    // is listening for.
    StealOldest = 0,

    // Existing voices win - the new play is dropped. Right for rare, deliberate cues (a boss
    // telegraph, a pickup fanfare) where truncating one already playing is worse than missing one.
    RejectNewest = 1,
}
