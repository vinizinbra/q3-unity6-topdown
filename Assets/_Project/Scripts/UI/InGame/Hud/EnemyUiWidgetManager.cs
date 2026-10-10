using System.Collections.Generic;
using Quantum;
using UnityEngine;

// Spawns/despawns one CharacterUiWidget per enemy entity, parented under widgetParent. Kept
// separate from CharacterUiWidgetManager so enemies can use their own widget prefab/HUD slot
// (e.g. a differently colored bar) without the two entity types fighting over one dictionary.
//
// Widgets are POOLED: a finished enemy's widget is reset and switched off, and the next enemy takes
// it back. Instantiating one is expensive on a low-end phone - the widget is a full UI hierarchy
// (Images, TMP labels, the TextBatchOptimizer hoist), ~1-2 ms to create and ~5 ms for the whole
// EnemyView.Initialize in a profiled capture - and enemies spawn in bursts of 2-4 per frame, which
// showed up as 10-20 ms hitches. TextBatchOptimizer is written for exactly this (hides its hoisted
// texts in OnDisable, resyncs in OnEnable).
public class EnemyUiWidgetManager : MonoBehaviour
{
    public static EnemyUiWidgetManager Instance;

    [SerializeField] private CharacterUiWidget widgetPrefab;
    [SerializeField] private Transform widgetParent;

    [SerializeField, Min(0), Tooltip("Widgets built (and switched on/off once, so their Awake/OnEnable cost is paid too) in Start, so the first waves don't pay for creating them mid-fight. The pool still grows past this on demand.")]
    private int prewarmCount = 12;

    [SerializeField, Min(0), Tooltip("Idle widgets kept. Anything released beyond this is destroyed.")]
    private int maxPooled = 64;

    private readonly Dictionary<EntityRef, CharacterUiWidget> _widgets = new Dictionary<EntityRef, CharacterUiWidget>();
    private readonly Stack<CharacterUiWidget> _free = new Stack<CharacterUiWidget>();

    private void Awake()
    {
        Instance = this;

        // The "prefab" is a scene object, so it renders as an unowned widget on the HUD until it's
        // switched off - clones inherit the off state and come up once Setup has filled them in.
        widgetPrefab.gameObject.SetActive(false);
    }

    // Start rather than Awake: the HUD canvas (and TextBatchOptimizerManager) have to be up before a
    // clone is activated, since the first OnEnable hoists its texts under the Canvas.
    private void Start()
    {
        for (int i = 0; i < prewarmCount; i++)
        {
            CharacterUiWidget widget = Instantiate(widgetPrefab, widgetParent);

            // One on/off cycle runs the deferred Awake and the optimizer's hoist now. Nothing is
            // Setup, so its LateUpdate does nothing in the meantime.
            widget.gameObject.SetActive(true);
            widget.gameObject.SetActive(false);
            _free.Push(widget);
        }
    }

    public void SpawnWidget(EntityRef entityRef, QuantumGame game, Transform followTarget, string displayName = null, Vector3 characterOffset = default)
    {
        if (_widgets.ContainsKey(entityRef))
            return;

        CharacterUiWidget widget = TakeFree();
        if (widget == null)
            widget = Instantiate(widgetPrefab, widgetParent);

        widget.Setup(game, entityRef, followTarget, displayName, characterOffset);
        widget.gameObject.SetActive(true);
        _widgets.Add(entityRef, widget);
    }

    public void DespawnWidget(EntityRef entityRef)
    {
        if (_widgets.TryGetValue(entityRef, out var widget) == false)
            return;

        _widgets.Remove(entityRef);

        if (widget == null)
            return;

        if (_free.Count >= maxPooled)
        {
            Destroy(widget.gameObject);
            return;
        }

        // Reset BEFORE switching off: it stops coroutines and tweens that SetActive(false) would
        // otherwise leave half-run with a non-null handle.
        widget.ResetForPool();
        widget.gameObject.SetActive(false);
        _free.Push(widget);
    }

    private CharacterUiWidget TakeFree()
    {
        while (_free.Count > 0)
        {
            CharacterUiWidget widget = _free.Pop();

            // Destroyed while idle (HUD teardown) - skip it.
            if (widget != null)
                return widget;
        }

        return null;
    }

    // Lets a HUD element that needs to point at a tracked enemy's own widget (e.g.
    // TargetArrowWidget aiming at its health bar) find it without duplicating this dictionary.
    public bool TryGetWidget(EntityRef entityRef, out CharacterUiWidget widget)
    {
        return _widgets.TryGetValue(entityRef, out widget);
    }
}
