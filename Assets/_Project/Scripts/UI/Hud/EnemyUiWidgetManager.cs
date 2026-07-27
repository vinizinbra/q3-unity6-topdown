using System.Collections.Generic;
using Quantum;
using UnityEngine;

// Spawns/despawns one CharacterUiWidget per enemy entity, parented under widgetParent. Kept
// separate from CharacterUiWidgetManager so enemies can use their own widget prefab/HUD slot
// (e.g. a differently colored bar) without the two entity types fighting over one dictionary.
public class EnemyUiWidgetManager : MonoBehaviour
{
    public static EnemyUiWidgetManager Instance;

    [SerializeField] private CharacterUiWidget widgetPrefab;
    [SerializeField] private Transform widgetParent;

    private readonly Dictionary<EntityRef, CharacterUiWidget> _widgets = new Dictionary<EntityRef, CharacterUiWidget>();

    private void Awake()
    {
        Instance = this;

        // The "prefab" is a scene object, so it renders as an unowned widget on the HUD until it's
        // switched off - clones inherit the off state and come up once Setup has filled them in.
        widgetPrefab.gameObject.SetActive(false);
    }

    public void SpawnWidget(EntityRef entityRef, QuantumGame game, Transform followTarget, string displayName = null)
    {
        if (_widgets.ContainsKey(entityRef))
            return;

        var widget = Instantiate(widgetPrefab, widgetParent);
        widget.Setup(game, entityRef, followTarget, displayName);
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
