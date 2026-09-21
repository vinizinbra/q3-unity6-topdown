using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Spawns/despawns one InteractionPromptWidget per Interactable POI entity, parented under
// widgetParent - same manager-pool pattern EnemyUiWidgetManager/CharacterUiWidgetManager already
// use. Called from PoiView.Initialize/DeInitialize (see docs/breathing-poi.md), guarded there on
// the entity actually carrying an Interactable component - Healing Shrine (no Interactable) never
// spawns one.
public class InteractionPromptWidgetManager : MonoBehaviour
{
    public static InteractionPromptWidgetManager Instance;

    [SerializeField] private InteractionPromptWidget widgetPrefab;
    [SerializeField] private Transform widgetParent;

    private readonly Dictionary<EntityRef, InteractionPromptWidget> _widgets = new Dictionary<EntityRef, InteractionPromptWidget>();

    private void Awake()
    {
        Instance = this;

        // The "prefab" is a scene object, so it renders as an unowned widget on the HUD until it's
        // switched off - clones inherit the off state and come up once Setup has filled them in.
        widgetPrefab.gameObject.SetActive(false);
    }

    // Returns the spawned (or already-existing, on a redundant call) widget so a caller like
    // InteractionPromptPoiView can hold onto it and push further live state onto it later (e.g.
    // TeamChallengeView.SetDescriptionOverride) - previously void, since no caller needed the
    // instance back before InteractionPromptPoiView existed.
    public InteractionPromptWidget SpawnWidget(EntityRef entityRef, QuantumGame game, Transform followTarget, string title,
        string activeDescription, string phaseUnavailableDescription, string alreadyUsedDescription, string notNeededDescription,
        Vector3 worldOffset = default, string occupiedDescription = "", Sprite rewardIcon = null, string rewardText = "")
    {
        if (_widgets.TryGetValue(entityRef, out var existing))
            return existing;

        var widget = Instantiate(widgetPrefab, widgetParent);
        widget.Setup(game, entityRef, followTarget, title, activeDescription, phaseUnavailableDescription, alreadyUsedDescription, notNeededDescription, worldOffset, occupiedDescription, rewardIcon, rewardText);
        widget.gameObject.SetActive(true);
        _widgets.Add(entityRef, widget);

        LogHelper.Log("Prompt", $"Spawned widget for {entityRef} '{title}': activeInHierarchy={widget.gameObject.activeInHierarchy} parent={(widgetParent != null ? widgetParent.name : "NULL")} parentActive={(widgetParent != null && widgetParent.gameObject.activeInHierarchy)} total={_widgets.Count}", widget);
        return widget;
    }

    public void DespawnWidget(EntityRef entityRef)
    {
        if (_widgets.TryGetValue(entityRef, out var widget) == false)
            return;

        LogHelper.Log("Prompt", $"Despawning widget for {entityRef}", widget);
        _widgets.Remove(entityRef);

        if (widget != null)
            Destroy(widget.gameObject);
    }
}
