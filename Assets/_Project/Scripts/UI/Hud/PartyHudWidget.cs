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

    [Header("Skills")]
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentsInChildren (Awake, or the Populate Children button below) - only set these manually to override which widgets belong to this slot (e.g. excluding one).")]
    private SkillCooldownUiWidget[] skillCooldownWidgets;
    [SerializeField, Tooltip("Left empty, auto-populated via GetComponentsInChildren (Awake, or the Populate Children button below) - only set these manually to override which widgets belong to this slot (e.g. excluding one).")]
    private SkillProgressUiWidget[] skillProgressWidgets;

    [Header("Hero resource gauges - each self-hides unless the bound player's hero/passive matches")]
    [SerializeField, Tooltip("Max's Adrenaline stacks. Left empty, auto-populated via GetComponentInChildren.")]
    private AdrenalineUiWidget adrenalineWidget;
    [SerializeField, Tooltip("Zara's Remix pulse counter. Left empty, auto-populated via GetComponentInChildren.")]
    private RemixUiWidget remixWidget;
    [SerializeField, Tooltip("Lux's Scrap stacks. Left empty, auto-populated via GetComponentInChildren.")]
    private ScrapUiWidget scrapWidget;

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

        if (skillCooldownWidgets == null || skillCooldownWidgets.Length == 0)
            skillCooldownWidgets = GetComponentsInChildren<SkillCooldownUiWidget>(true);

        if (skillProgressWidgets == null || skillProgressWidgets.Length == 0)
            skillProgressWidgets = GetComponentsInChildren<SkillProgressUiWidget>(true);

        if (adrenalineWidget == null)
            adrenalineWidget = GetComponentInChildren<AdrenalineUiWidget>(true);

        if (remixWidget == null)
            remixWidget = GetComponentInChildren<RemixUiWidget>(true);

        if (scrapWidget == null)
            scrapWidget = GetComponentInChildren<ScrapUiWidget>(true);

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

        if (adrenalineWidget != null)
            adrenalineWidget.DisableAutoBind();

        if (remixWidget != null)
            remixWidget.DisableAutoBind();

        if (scrapWidget != null)
            scrapWidget.DisableAutoBind();
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

        foreach (var widget in skillCooldownWidgets)
            widget.Initialize(entityRef);

        foreach (var widget in skillProgressWidgets)
            widget.Initialize(entityRef);

        if (adrenalineWidget != null)
            adrenalineWidget.Initialize(entityRef);

        if (remixWidget != null)
            remixWidget.Initialize(entityRef);

        if (scrapWidget != null)
            scrapWidget.Initialize(entityRef);
    }

    public void Clear()
    {
        BoundEntityRef = default;
    }
}
