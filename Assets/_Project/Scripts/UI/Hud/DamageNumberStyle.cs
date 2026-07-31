using System;
using UnityEngine;

// Every kind is stated relative to the local player, because DamageFeedbackManager only reports
// hits (and heals) they were part of. Burn splits into Taken/Dealt the same way normal hits do, but
// has no Critical variant - DoT ticks always bypass crit resolution (DamageUtility.ApplyDamage's
// bypassOutgoingResolution), so isCritical is always false for it. Healed likewise splits into
// Taken/Dealt, off EventEntityHealed rather than EventEntityDamaged. Adding a kind means an entry
// here, a case in DamageFeedbackManager.ResolveKind (or OnEntityHealed), and a row on its styles
// list. 5/6 (formerly PoisonTakenByMe/PoisonDealtByMe) are deliberately left unused rather than
// renumbered - Poison was removed from ElementType (see docs/elemental-reactions.md) and nothing
// tags a damage event with it anymore; renumbering would shift every value below it in any already-
// serialized data.
public enum DamageNumberKind
{
    TakenByMe = 0,
    DealtByMe = 1,
    CriticalDealtByMe = 2,
    BurnTakenByMe = 3,
    BurnDealtByMe = 4,
    HealedTakenByMe = 7,
    HealedDealtByMe = 8,

    // FrontalDamageReduction only ever applies to an Enemy target, and the local player is never
    // one - so unlike Burn/Healed this only ever comes up as something I dealt, never took. Takes
    // priority over CriticalDealtByMe/the elemental kinds in ResolveKind, same as HitFeedback's own
    // flash-color priority for the same event field.
    FrontalReducedDealtByMe = 9,

    // Unlike every other kind, not scoped to the local player - an enemy healing another enemy
    // (e.g. FlyingShielder) involves neither, but is still worth surfacing: it tells every nearby
    // player their damage on that target just got partially undone.
    HealedEnemy = 10,

    // Shield counterpart to the Healed trio above, off EventEntityShielded rather than
    // EventEntityHealed - same Taken/Dealt/Enemy split, same reasoning for each.
    ShieldedTakenByMe = 11,
    ShieldedDealtByMe = 12,
    ShieldedEnemy = 13,

    // Unlike every damage kind, Healed/Shielded are NOT dropped when neither party is the local
    // player - an ally getting healed/shielded is useful co-op information worth seeing regardless
    // of who cast it (unlike damage-on-someone-else, which is just noise - see this file's own
    // header comment on why damage stays local-only). These are the "bystander" fallback: local
    // player is neither Target nor Owner, and Target isn't an Enemy either (that's
    // Healed/ShieldedEnemy instead).
    HealedAlly = 14,
    ShieldedAlly = 15,
}

// The per-kind look, so one DamageNumberUiWidget prefab covers all of them.
[Serializable]
public class DamageNumberStyle
{
    public DamageNumberKind Kind;
    public Color Color = Color.white;

    [Tooltip("Scales the prefab's authored font size - lets a crit read bigger without a second prefab.")]
    public float FontSizeMultiplier = 1f;

    [Tooltip("What the punch-in (Ease.OutBack overshoot) settles at - 1 matches the prefab's authored size, higher keeps a crit visibly bigger for its whole rise instead of just a brief pop.")]
    public float PunchScaleMultiplier = 1f;

    [Tooltip("Prepended to the number, e.g. \"+\" for a heal - empty for no prefix.")]
    public string Prefix = "";

    [Tooltip("Appended to the number, e.g. \"!\" for a crit - empty for no suffix.")]
    public string Suffix = "";
}
