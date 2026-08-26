// Generic, hero-agnostic gameplay moments a voice line can react to. Deliberately no hero-specific
// values (no MaxRageFull, no PixieClusterBomb) - a hero's identity travels alongside the trigger as
// a HeroId, and anything genuinely unique goes through HeroExceptionalEvent with its own context.
//
// Append new values at the END: these are lookup keys in authored voice banks, so reordering
// silently reassigns lines.
public enum VoiceLineTrigger
{
    None = 0,

    // ---- Run flow. One per real state transition, never repeated within a state. ----
    RunStarted = 1,
    SurvivalStarted = 2,
    // Fired once the Break's area is actually SECURED, not the instant the phase begins - the
    // leftover enemies are still combat, and this should read as "we can breathe now".
    BreathingTimeStarted = 3,
    BossStarted = 4,
    BossDefeated = 5,

    // ---- Hero action ----
    HeroSkillUsed = 10,

    // ---- Progression ----

    // ANY level-up pick, whatever pool it came from - Weapon Perk, Global Upgrade, Rift Mutation,
    // Hero Ascension, Choose Weapon. One trigger rather than one per pool: the moment is the same
    // ("I picked something"), and everything that differs travels with the request instead.
    //
    // VoiceLineRequest.Upgrade carries the chosen UpgradeData (every pool's option derives from it),
    // and ContextValue carries the LevelUpPoolKind, so a line CAN vary by pool without the trigger
    // list growing a value per pool.
    UpgradeChosen = 22,

    // The pick that took a ranked Hero Ascension to its FINAL rank. Raised INSTEAD of UpgradeChosen
    // for that one pick, so a maxed line never doubles up with a generic "picked something".
    //
    // Resolved at click time from the upgrade's own MaxRank and how many times this player has
    // already taken it (IRankedUpgrade / UpgradeHistoryUtility), so it needs nothing new from the
    // simulation. An unranked upgrade can never reach it.
    UpgradeMaxed = 23,

    // Bought something at the Store - a weapon, a food/utility item, a weapon level. One trigger for
    // the act of spending, whatever was bought; StoreCardKind rides along in ContextValue for a line
    // that wants to be specific.
    //
    // NOT raised for the accessory repair/replace card: every accessory restore is a Merchant
    // purchase, so that would be two lines for one click. AccessoryRestored covers it, and the more
    // specific trigger wins - the general rule wherever a purchase has its own dedicated moment.
    ItemPurchased = 24,

    // ---- Combat ----
    // A single damage event above a configured fraction of the victim's max health.
    HeavyHitReceived = 30,

    // A rejected activation press - NOT merely "a cooldown exists", but the player actually pressing
    // the button and nothing happening.
    //
    // One trigger for Dash and Hero Skill alike: the moment is identical ("that's not up yet") and a
    // hero rarely wants two different complaints about it. Which slot was pressed rides along in
    // VoiceLineRequest.ContextValue as the SkillSlotId, for the rare line that does want to name it.
    //
    // Wants aggressive rate limiting - a player can mash a cooldown several times a second, so its
    // rule is authored Low priority with a long cooldown.
    AbilityNotReady = 40,

    // ---- Life state ----
    PlayerDowned = 50,
    PlayerKO = 51,
    // Raised on the OBSERVERS, never on the player who went down (that's PlayerDowned).
    TeammateDowned = 53,

    // I have STARTED reviving a teammate. Spoken by the reviver, with the downed player as Other.
    //
    // Named for the ACTION, and hosted on the bank of whoever performs it - the same rule the whole
    // pair family follows: if this hero did something, the line lives here. That keeps "where is the
    // line?" answerable without knowing which side of the interaction a hero was on.
    //
    // Always a two-player moment - self-revive is a separate press-and-confirm path, and the
    // auto-revive on securing an area has nobody performing it, so neither reaches these.
    RevivingTeammate = 55,

    // I FINISHED reviving them and they are back on their feet.
    RevivedTeammate = 56,

    // I got back up WITHOUT an ally doing it - either spending one of my own charges, or the
    // automatic revive when a Breathing area is finally secured. Both are the same beat ("I'm back")
    // with nobody to thank, so they share one trigger rather than splitting hairs over the mechanism.
    //
    // ContextValue tells them apart for anything that cares: 0 = spent a charge, 1 = area secured.
    // A distinction carried as context rather than as another enum value, since the LINE is usually
    // the same either way.
    //
    // Never carries an Other - the ally cases are the two triggers above.
    SelfRevive = 57,

    // ---- Accessory. See docs/accessory-guard.md. ----
    AccessoryDropped = 60,
    // I picked my OWN accessory back up - nobody else involved.
    AccessorySelfRecovered = 61,
    // I collected a TEAMMATE's accessory and it went back to them. Spoken by whoever picked it up,
    // with the owner as Other - so Pixie fetching MAX's cap and fetching BRUTE's mask are different
    // lines, both on Pixie's bank.
    //
    // Only one trigger for this moment, not one per side: the clip is the whole interaction, and the
    // owner is held silent while it plays (see VoiceDirector's cross-talk lock).
    AccessoryReturnedToAlly = 62,

    AccessoryBroken = 63,
    // Paid for at the Merchant and back to full - whether that was a repair or a whole replacement.
    // Deliberately ONE trigger: both are the same beat for the player ("I've got it back"), and the
    // distinction is a price bracket, not a different reaction. AccessoryGuardConfig still tracks
    // which happened; nothing about the voice line needs to.
    AccessoryRestored = 64,


    // ---- Extension point. Keyed by a context value rather than growing this enum per mechanic. ----
    HeroExceptionalEvent = 90,
}

// Who is meant to hear a line. Presentation decides what each of these means in practice; the
// gameplay side only states intent.
public enum VoiceAudience
{
    // Only the client whose own local player is the speaker. The right default for anything
    // self-referential - upgrades, failed presses, personal accessory reactions.
    LocalOnly = 0,

    // Everyone, positioned in the world - falls off with distance like any other 3D sound.
    NearbyPlayers = 1,

    // Everyone, flat. For run-wide announcements (boss start, a player going down).
    AllPlayers = 2,

    // The speaker and the one other player the moment involves (Owner + Recoverer). The audience
    // that makes hero-pair dialogue possible.
    RelevantPlayers = 3,
}

// Higher priority can suppress lower-priority chatter that is still playing.
public enum VoicePriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3,
}
