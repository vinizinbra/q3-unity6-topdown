using System.Collections.Generic;
using Quantum;
using UnityEngine;

// Spawns/despawns one CharacterUiWidget per sentry entity, parented under widgetParent. Kept
// separate from CharacterUiWidgetManager/EnemyUiWidgetManager so sentries can use their own widget
// prefab/HUD slot without fighting over one dictionary - same reasoning as EnemyUiWidgetManager.
// Reuses CharacterUiWidget itself rather than a bespoke widget script: a sentry has Health (shown)
// and Weapon (ammo/reload bars shown too, which is a nice bonus, not a problem), but no
// Shield/StatusEffects - those sections already hide themselves when the component is absent, the
// same optional-per-entity behavior CharacterUiWidget already relies on for a bare-bones enemy.
public class SentryUiWidgetManager : MonoBehaviour
{
    public static SentryUiWidgetManager Instance;

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

    public void SpawnWidget(EntityRef entityRef, QuantumGame game, Transform followTarget)
    {
        if (_widgets.ContainsKey(entityRef))
            return;

        var widget = Instantiate(widgetPrefab, widgetParent);
        widget.Setup(game, entityRef, followTarget);

        // A sentry decays constantly and dies fast, so a trailing "recent damage" bar would spend
        // most of its life mid-drain and never settle - it reads as a lagging bar, not as impact.
        widget.SetBarsInstant(true);
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

    // Lets a HUD element that needs to point at a tracked sentry's own widget (e.g.
    // TargetArrowWidget aiming at its health bar) find it without duplicating this dictionary.
    public bool TryGetWidget(EntityRef entityRef, out CharacterUiWidget widget)
    {
        return _widgets.TryGetValue(entityRef, out widget);
    }
}
