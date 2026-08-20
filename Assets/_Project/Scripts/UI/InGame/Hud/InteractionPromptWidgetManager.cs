using System.Collections.Generic;
using Quantum;
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

    public void SpawnWidget(EntityRef entityRef, QuantumGame game, Transform followTarget, string title,
        string activeDescription, string phaseUnavailableDescription, string alreadyUsedDescription, string notNeededDescription,
        Vector3 worldOffset = default, string occupiedDescription = "")
    {
        if (_widgets.ContainsKey(entityRef))
            return;

        var widget = Instantiate(widgetPrefab, widgetParent);
        widget.Setup(game, entityRef, followTarget, title, activeDescription, phaseUnavailableDescription, alreadyUsedDescription, notNeededDescription, worldOffset, occupiedDescription);
        widget.gameObject.SetActive(true);
        _widgets.Add(entityRef, widget);
    }

    public void DespawnWidget(EntityRef entityRef)
    {
        if (_widgets.TryGetValue(entityRef, out var widget) == false)
            return;

        _widgets.Remove(entityRef);

        if (widget != null)
            Destroy(widget.gameObject);
    }
}
