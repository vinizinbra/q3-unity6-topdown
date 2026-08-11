using NaughtyAttributes;
using Quantum;
using UnityEngine;

// One player's entry in the always-visible party HUD (PartyHudManager) - portrait, health, shield,
// and skills. Unlike the player's own full HUD cluster (SkillCooldownUiWidget etc. with
// autoBindLocalPlayerOne on), every child widget here is bound externally via Initialize, since this
// slot can end up showing any match player (local or remote), not just player 1.
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

    [Header("Skills")]
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentsInChildren (Awake, or the Populate Children button below) - only set these manually to override which widgets belong to this slot (e.g. excluding one).")]
    private SkillCooldownUiWidget[] skillCooldownWidgets;
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentsInChildren (Awake, or the Populate Children button below) - only set these manually to override which widgets belong to this slot (e.g. excluding one).")]
    private SkillProgressUiWidget[] skillProgressWidgets;

    [Header("Hero resource gauges - each self-hides unless the bound player's hero/passive matches")]
    [SerializeField, Tooltip("Zara's Remix pulse counter. Left empty, auto-populated via GetComponentInChildren.")]
    private RemixUiWidget remixWidget;
    [SerializeField, Tooltip("Lux's Scrap stacks. Left empty, auto-populated via GetComponentInChildren.")]
    private ScrapUiWidget scrapWidget;
    [SerializeField, Tooltip("Brute's Juggernaut Stack Damage counter. Left empty, auto-populated via GetComponentInChildren.")]
    private JuggernautStackDamageUiWidget juggernautStackDamageWidget;

    [Header("Upgrade History")]
    [SerializeField, Tooltip("Grid of icons for every upgrade this slot's player has ever picked (see UpgradeHistory in LevelUp.qtn) - Skill Upgrade/Global Upgrade/Passive Upgrade/Rift Mutation alike; Weapon Perk excluded. Left empty, auto-populated via GetComponentInChildren.")]
    private PartyHistoryUpgradeContainer upgradeHistoryContainer;

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

        if (skillCooldownWidgets == null || skillCooldownWidgets.Length == 0)
            skillCooldownWidgets = GetComponentsInChildren<SkillCooldownUiWidget>(true);

        if (skillProgressWidgets == null || skillProgressWidgets.Length == 0)
            skillProgressWidgets = GetComponentsInChildren<SkillProgressUiWidget>(true);

        if (remixWidget == null)
            remixWidget = GetComponentInChildren<RemixUiWidget>(true);

        if (scrapWidget == null)
            scrapWidget = GetComponentInChildren<ScrapUiWidget>(true);

        if (juggernautStackDamageWidget == null)
            juggernautStackDamageWidget = GetComponentInChildren<JuggernautStackDamageUiWidget>(true);

        if (upgradeHistoryContainer == null)
            upgradeHistoryContainer = GetComponentInChildren<PartyHistoryUpgradeContainer>(true);

        DisableChildAutoBind();
    }

    // This slot is always the one deciding which entity its children show (Initialize, below) -
    // never let a child's own autoBindLocalPlayerOne default fight that, whether this slot ends up
    // showing player 1 (defaultLocalSlot) or anyone else (an instantiated slot).
    private void DisableChildAutoBind()
    {
        foreach (var widget in skillCooldownWidgets)
            widget.DisableAutoBind();

        foreach (var widget in skillProgressWidgets)
            widget.DisableAutoBind();

        if (remixWidget != null)
            remixWidget.DisableAutoBind();

        if (scrapWidget != null)
            scrapWidget.DisableAutoBind();

        if (juggernautStackDamageWidget != null)
            juggernautStackDamageWidget.DisableAutoBind();

        if (damageReductionWidget != null)
            damageReductionWidget.DisableAutoBind();

        if (upgradeHistoryContainer != null)
            upgradeHistoryContainer.DisableAutoBind();
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

        foreach (var widget in skillCooldownWidgets)
            widget.Initialize(entityRef);

        foreach (var widget in skillProgressWidgets)
            widget.Initialize(entityRef);

        if (remixWidget != null)
            remixWidget.Initialize(entityRef);

        if (scrapWidget != null)
            scrapWidget.Initialize(entityRef);

        if (juggernautStackDamageWidget != null)
            juggernautStackDamageWidget.Initialize(entityRef);

        if (upgradeHistoryContainer != null)
            upgradeHistoryContainer.Initialize(entityRef);
    }

    public void Clear()
    {
        BoundEntityRef = default;
    }
}
