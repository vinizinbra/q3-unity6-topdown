using NaughtyAttributes;
using Quantum;
using UnityEngine;

// One player's entry in the always-visible party HUD (PartyHudManager) - portrait, health, shield,
// and skill cooldowns. Deliberately shows NO per-hero resource (Juggernaut charge, Rage, Flow,
// Scrap): those live only under each character's own world-following CharacterUiWidget now, where
// they sit right next to the character they belong to instead of being duplicated per party slot
// and kept in sync by hand - see HeroHudWidget.
//
// Unlike the player's own full HUD cluster (SkillCooldownUiWidget etc. with autoBindLocalPlayerOne
// on), every child widget here is bound externally via Initialize, since this slot can end up
// showing any match player (local or remote), not just player 1.
public class PartyHudWidget : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentInChildren.")]
    private PlayerPortraitUiWidget portraitWidget;
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentInChildren.")]
    private PlayerNumberUiWidget numberWidget;

    [Header("Vitals")]
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentInChildren.")]
    private HealthUiWidget healthWidget;
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentInChildren.")]
    private ShieldUiWidget shieldWidget;
    [SerializeField, Tooltip("Current combined damage reduction % (any source - Juggernaut's own channel, an ally's Guardian aura, etc.). Left empty, auto-populated via GetComponentInChildren.")]
    private DamageReductionUiWidget damageReductionWidget;
    [SerializeField, Tooltip("Shows while this player has a Cursed Rift/Store/Blacksmith Choice Window open - most relevant during a Breathing grace hold (see docs/run-phase.md). Left empty, auto-populated via GetComponentInChildren.")]
    private ChoiceWindowIndicatorUiWidget choiceWindowIndicatorWidget;
    [SerializeField, Tooltip("Tints the slot while this player is Downed and dims it once KO. Left empty, auto-populated via GetComponentInChildren.")]
    private PartyLifeStateWidget lifeStateWidget;

    [Header("Skills")]
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentsInChildren (Awake, or the Populate Children button below) - only set these manually to override which widgets belong to this slot (e.g. excluding one).")]
    private SkillCooldownUiWidget[] skillCooldownWidgets;

    [Header("Upgrade History")]
    [SerializeField, Tooltip("Icon grids for the upgrades this slot's player has picked (see UpgradeHistory in LevelUp.qtn) - each filtered by its own kindFilter, e.g. Rift Mutations beside the banner and everything else below it; Weapon Perk excluded. Left empty, auto-populated via GetComponentsInChildren.")]
    private PartyHistoryUpgradeContainer[] upgradeHistoryContainers;

    public EntityRef BoundEntityRef { get; private set; }

    private void Awake()
    {
        PopulateChildren();
    }

    [Button("Populate Children")]
    private void PopulateChildren()
    {
        if (portraitWidget == null)
            portraitWidget = GetComponentInChildren<PlayerPortraitUiWidget>(true);

        if (numberWidget == null)
            numberWidget = GetComponentInChildren<PlayerNumberUiWidget>(true);

        if (healthWidget == null)
            healthWidget = GetComponentInChildren<HealthUiWidget>(true);

        if (shieldWidget == null)
            shieldWidget = GetComponentInChildren<ShieldUiWidget>(true);

        if (damageReductionWidget == null)
            damageReductionWidget = GetComponentInChildren<DamageReductionUiWidget>(true);

        if (choiceWindowIndicatorWidget == null)
            choiceWindowIndicatorWidget = GetComponentInChildren<ChoiceWindowIndicatorUiWidget>(true);

        if (lifeStateWidget == null)
            lifeStateWidget = GetComponentInChildren<PartyLifeStateWidget>(true);

        if (skillCooldownWidgets == null || skillCooldownWidgets.Length == 0)
            skillCooldownWidgets = GetComponentsInChildren<SkillCooldownUiWidget>(true);

        if (upgradeHistoryContainers == null || upgradeHistoryContainers.Length == 0)
            upgradeHistoryContainers = GetComponentsInChildren<PartyHistoryUpgradeContainer>(true);

        DisableChildAutoBind();
    }

    // This slot is always the one deciding which entity its children show (Initialize, below) -
    // never let a child's own autoBindLocalPlayerOne default fight that, whether this slot ends up
    // showing player 1 (defaultLocalSlot) or anyone else (an instantiated slot).
    private void DisableChildAutoBind()
    {
        foreach (var widget in skillCooldownWidgets)
            widget.DisableAutoBind();

        if (damageReductionWidget != null)
            damageReductionWidget.DisableAutoBind();

        foreach (var container in upgradeHistoryContainers)
            container.DisableAutoBind();
    }

    public void Initialize(EntityRef entityRef)
    {
        BoundEntityRef = entityRef;

        if (portraitWidget != null)
            portraitWidget.Initialize(entityRef);

        if (numberWidget != null)
            numberWidget.Initialize(entityRef);

        if (healthWidget != null)
            healthWidget.Initialize(entityRef);

        if (shieldWidget != null)
            shieldWidget.Initialize(entityRef);

        if (damageReductionWidget != null)
            damageReductionWidget.Initialize(entityRef);

        if (choiceWindowIndicatorWidget != null)
            choiceWindowIndicatorWidget.Initialize(entityRef);

        if (lifeStateWidget != null)
            lifeStateWidget.Initialize(entityRef);

        foreach (var widget in skillCooldownWidgets)
            widget.Initialize(entityRef);

        foreach (var container in upgradeHistoryContainers)
            container.Initialize(entityRef);
    }

    public void Clear()
    {
        BoundEntityRef = default;
    }
}
