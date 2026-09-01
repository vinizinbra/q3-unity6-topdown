using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

// Shows a live, animated hero in the menu by spawning that hero's own CharacterMenuPrefab - a
// dedicated, menu-ready display prefab (CharacterCatalog.Entry.menuPrefab), NOT the gameplay view
// prefab - as a child of this widget's own transform. Place the widget itself wherever the menu's
// 3D scene should show that character.
//
// Replaces the earlier RawImage/RenderTexture/offscreen-camera approach: that existed only because
// the gameplay prefab carried a whole gameplay stack that had to be stripped down and rendered
// through its own camera to composite over a Screen Space - Overlay Canvas. A prefab authored
// specifically for the menu needs none of that - it can just be spawned into the world like any
// other scene object and shown by the menu's own camera.
//
// It follows the local player's character pick on its own (PartyManager.OnLocalCharacterChanged)
// rather than being driven by whichever screen owns the picker, so it can sit on the main menu, in
// the party room, or on a dedicated character-select screen with no per-screen wiring. Call Show()
// directly instead if you want to drive it manually (turn off followLocalSelection).
//
// The rig itself is animated by driving BlobAnimationView.TickPreview every frame - the same pose
// math the live entity uses, fed a standing-still-on-the-ground input, so the idle breathe/bob/
// wobble is the real one and not a menu-only reimplementation of it. There is still no QuantumRunner
// in the menu, so nothing drives this automatically the way a live entity's Update would.
public class CharacterPreviewWidget : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField, Tooltip("Follow the local player's character pick automatically (PartyManager.OnLocalCharacterChanged), so this works on the main menu, in the party room, or anywhere else it's placed without being wired to whichever screen owns the picker. Turn off to drive it yourself by calling Show().")]
    private bool followLocalSelection = true;

    [SerializeField, Tooltip("Shown before any pick has been made - the first entry in PartyManager's CharacterCatalog. Off leaves the preview empty until something actually selects a character.")]
    private bool showFirstCharacterByDefault = true;

    private GameObject _instance;
    private BlobAnimationView _animator;
    private GameObject _sourcePrefab;
    private bool _subscribed;
    private bool _restoringSelection;

    // Binding happens here rather than in OnEnable because PartyManager is a scene singleton that
    // assigns its own Instance in Awake - OnEnable can run first and find nothing.
    private void Start()
    {
        Subscribe();
        RefreshFromSelection();
    }

    private void OnEnable()
    {
        // No-ops before Start has run once - see Subscribe.
        Subscribe();
        RefreshFromSelection();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        DestroyInstance();
    }

    private void Update()
    {
        // The whole point of the preview - BlobAnimationView's own Update never fires here (there
        // is no QuantumRunner in the menu), so the idle cycle has to be pushed frame by frame.
        if (_animator != null)
            _animator.TickPreview(Time.deltaTime);
    }

    private void Subscribe()
    {
        if (_subscribed || followLocalSelection == false || PartyManager.Instance == null)
            return;

        PartyManager.Instance.OnLocalCharacterChanged += HandleLocalCharacterChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_subscribed == false)
            return;

        if (PartyManager.Instance != null)
            PartyManager.Instance.OnLocalCharacterChanged -= HandleLocalCharacterChanged;

        _subscribed = false;
    }

    private void HandleLocalCharacterChanged(string characterId)
    {
        ShowCharacterId(characterId);
    }

    // Picks up whatever character is already selected, for the case where the pick was made before
    // this widget existed or was last enabled - the event alone only covers changes from here on.
    private void RefreshFromSelection()
    {
        if (followLocalSelection == false || PartyManager.Instance == null)
            return;

        ShowCharacterId(PartyManager.Instance.LocalCharacterId);
    }

    // Resolves a character id through PartyManager's catalog and shows it. An id that's null or
    // unknown falls back to the first catalog entry, so the menu shows a hero from the very first
    // frame rather than an empty spot until someone touches the picker.
    //
    // Public because a party slot (RoomWidget) drives its own preview from a REMOTE player's pick,
    // which is a character id read off that player's Photon properties - it has no prefab to hand
    // to Show(). Those slots run with followLocalSelection off.
    public void ShowCharacterId(string characterId)
    {
        var catalog = PartyManager.Instance != null ? PartyManager.Instance.characterCatalog : null;
        if (catalog == null || catalog.characters == null || catalog.characters.Length == 0)
            return;

        GameObject prefab = catalog.ResolveMenuPrefab(characterId);

        if (prefab == null && showFirstCharacterByDefault)
            prefab = catalog.characters[0].menuPrefab;

        Show(prefab);
    }

    // Swaps the previewed hero. Passing the prefab already showing is free, so this is safe to call
    // every time a selection changes without tracking the previous value at the call site.
    public void Show(GameObject characterPrefab)
    {
        if (characterPrefab == null)
        {
            Clear();
            return;
        }

        if (_sourcePrefab == characterPrefab && _instance != null)
            return;

        DestroyInstance();
        _sourcePrefab = characterPrefab;
        BuildInstance(characterPrefab);
    }

    // Empties the preview. A widget that follows the local pick doesn't stay empty though - it goes
    // straight back to showing that pick, because "blank" is never a correct state for it: the local
    // player's character exists whether or not a party does, and the only thing that clears this
    // widget is a roster slot reporting nobody in it, which says nothing about the local pick.
    // Without that, leaving a party would leave the player staring at an empty spot until they next
    // touched the character picker.
    public void Clear()
    {
        DestroyInstance();

        if (followLocalSelection == false || _restoringSelection)
            return;

        // RefreshFromSelection can route back here through Show(null) when nothing resolves, so the
        // guard is what stops that becoming an infinite bounce rather than a one-shot restore.
        _restoringSelection = true;
        try
        {
            RefreshFromSelection();
        }
        finally
        {
            _restoringSelection = false;
        }
    }

    private void DestroyInstance()
    {
        if (_instance != null)
            Destroy(_instance);

        _instance = null;
        _animator = null;
        _sourcePrefab = null;
    }

    private void BuildInstance(GameObject characterPrefab)
    {
        _instance = Instantiate(characterPrefab, transform);
        _instance.name = characterPrefab.name + " (Menu Preview)";
        _instance.transform.localPosition = Vector3.zero;
        _instance.transform.localRotation = Quaternion.identity;
        // Reset rather than inherit the prefab's own authored scale, so the ONLY thing scaling the
        // instance is this widget's OWN Transform scale - i.e. that scale IS the per-position scale
        // factor (bigger for the main preview, smaller for party slots, etc.), set directly in the
        // Inspector with no extra field to keep in sync.
        _instance.transform.localScale = Vector3.one;

        _animator = _instance.GetComponentInChildren<BlobAnimationView>(true);
        if (_animator == null)
        {
            LogHelper.Warn("CharacterPreview", $"'{characterPrefab.name}' has no BlobAnimationView - it will be shown as a static pose with no idle animation.");
        }
        // No SetPreviewCamera call - the rig billboards toward Camera.main by default, which is
        // correct here since the menu only ever has the one scene camera looking at the spawn point.
    }
}
