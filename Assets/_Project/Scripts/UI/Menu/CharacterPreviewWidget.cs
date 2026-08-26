using System.Collections.Generic;
using NaughtyAttributes;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Shows a live, animated hero in the menu UI by instantiating that hero's REAL gameplay prefab -
// the same Kai.prefab/Zara.prefab the match spawns - into a small offscreen "stage" and rendering
// it through its own camera into a RenderTexture, which a RawImage then draws inside the Canvas.
// Nothing about the rig is re-authored for the menu, so a hero whose art or proportions change
// in-game changes here for free.
//
// Why a RenderTexture rather than parenting the sprites into the Canvas: the menu's Canvases are
// Screen Space - Overlay, which always composites on top of everything the scene camera renders,
// so a SpriteRenderer simply cannot be interleaved with the UI. The alternative was converting the
// whole menu to Screen Space - Camera, which would re-sort every existing element.
//
// Why the prefab is stripped rather than used as-is: it carries the full gameplay view stack
// (weapon controllers, hit feedback, skill FX, the Quantum prototype components), all of which
// expect a running simulation and a MyLocalPlayer that MenuScene does not have. Everything except
// the rig animator is removed, and by default everything except the branch that animator actually
// poses (BlobAnimationView.Root) is deleted outright - see PruneToBodyRoot/Strip. The instance is
// built while parented to an INACTIVE holder specifically so none of it ever gets an Awake() call
// before being removed.
//
// It follows the local player's character pick on its own (PartyManager.OnLocalCharacterChanged)
// rather than being driven by whichever screen owns the picker, so it can sit on the main menu, in
// the party room, or on a dedicated character-select screen with no per-screen wiring. Call Show()
// directly instead if you want to drive it manually (turn off followLocalSelection).
//
// The rig itself is animated by driving BlobAnimationView.TickPreview every frame - the same pose
// math the live entity uses, fed a standing-still-on-the-ground input, so the idle breathe/bob/
// wobble is the real one and not a menu-only reimplementation of it.
public class CharacterPreviewWidget : MonoBehaviour
{
    [Header("Output")]
    [SerializeField, Tooltip("The RawImage in the UI that displays this preview. Its own RectTransform decides how big the character reads on screen; the RenderTexture below only decides how much resolution it has to work with.")]
    private RawImage targetImage;

    [SerializeField, Tooltip("Resolution of the RenderTexture the preview camera renders into. Keep it near the RawImage's real on-screen pixel size - oversizing it costs fill rate every frame for nothing, which matters on mobile.")]
    private Vector2Int textureSize = new Vector2Int(512, 512);

    [SerializeField, Tooltip("Point for a crisp pixel-art look (matches MinimapWidget), Bilinear to smooth the sprite when the RawImage is drawn at a size the texture doesn't exactly match.")]
    private FilterMode filterMode = FilterMode.Bilinear;

    [Header("Stage")]
    [SerializeField, Tooltip("Layer the instantiated character is moved to, and the ONLY layer the preview camera renders. Must be a layer nothing else in the menu uses, or the preview will pick up scene geometry. Create a dedicated one (e.g. \"CharacterPreview\") in Tags & Layers.")]
    private string previewLayerName = "CharacterPreview";

    [SerializeField, Tooltip("Where the offscreen stage is parked, far from anything the menu camera can see. Each widget instance is additionally offset along X by stageSpacing so several previews (e.g. one per party slot) never share a space.")]
    private Vector3 stageOrigin = new Vector3(0f, -1000f, 0f);

    [SerializeField, Tooltip("X distance between the stages of separate CharacterPreviewWidget instances.")]
    private float stageSpacing = 50f;

    [Header("Framing")]
    [SerializeField, Tooltip("Point the camera at the middle of whatever the character actually renders, instead of at the prefab's origin. On by default, and normally what you want: a hero prefab's origin is at its feet and its rig root carries its own authored offset on top, so a fixed camera position frames a different part of each hero. Turn off to place the camera purely by cameraOffset below.")]
    private bool autoCenter = true;

    [SerializeField, Tooltip("Also zoom so the character fills the frame. OFF by default, deliberately: fitting each hero individually makes them all render the same on-screen size and throws away the size differences between them - a Brute should look bigger than a Pixie. Turn it on only if every hero should be framed identically.")]
    private bool autoZoom;

    [SerializeField, Range(0f, 1f), Tooltip("Extra empty space around the character when autoZoom is on, as a fraction of its size.")]
    private float framePadding = 0.1f;

    [SerializeField, Tooltip("Deliberate off-centering, in screen units, applied after auto-centering - e.g. raise Y to frame the head and shoulders rather than the whole body. Ignored when autoCenter is off.")]
    private Vector2 framingNudge;

    [SerializeField, Tooltip("Half-height of the orthographic preview camera's view, in world units - the zoom. Used unless autoZoom above is on. Smaller = character fills more of the frame.")]
    private float orthographicSize = 1.6f;

    [SerializeField, Tooltip("Camera placement. Z is the pull-back distance (kept in both modes; must be negative to sit in front of the character). X/Y position the camera relative to the prefab's own origin and are used ONLY when autoCenter is off - with it on, framingNudge above does that job instead.")]
    private Vector3 cameraOffset = new Vector3(0f, 1f, -10f);

    [SerializeField, Tooltip("Camera tilt. The rig billboards toward whatever camera renders it, so this angles the ground/shadow rather than the character itself. Matching the gameplay camera's own pitch makes the preview read like the game.")]
    private Vector3 cameraEuler = new Vector3(10f, 0f, 0f);

    [SerializeField, Tooltip("Uniform scale applied to the instantiated character. 1 = exactly its gameplay size; adjust framing with orthographicSize first and only reach for this if a specific hero needs correcting.")]
    private float characterScale = 1f;

    [SerializeField, Tooltip("Y rotation applied to the character root. Only visible on non-billboarded parts of the rig (shadow, props), since the body always turns to face the camera.")]
    private float characterYaw;

    [Header("Stripping")]
    [SerializeField, Tooltip("Extra component TYPE NAMES to keep alive on the instantiated prefab, on top of BlobAnimationView. Everything else is removed. Only add something that can genuinely run with no Frame and no MyLocalPlayer behind it.")]
    private string[] additionalKeptComponents;

    [SerializeField, Tooltip("Leave the prefab's ParticleSystems and LineRenderers running. Off by default: the scripts that normally drive them (skill FX, Kai's link beams) are stripped, so what's left is usually a stale or permanently-looping effect rather than a deliberate one.")]
    private bool keepEffects;

    [Header("Selection")]
    [SerializeField, Tooltip("Follow the local player's character pick automatically (PartyManager.OnLocalCharacterChanged), so this works on the main menu, in the party room, or anywhere else it's placed without being wired to whichever screen owns the picker. Turn off to drive it yourself by calling Show().")]
    private bool followLocalSelection = true;

    [SerializeField, Tooltip("Shown before any pick has been made - the first entry in PartyManager's CharacterCatalog. Off leaves the preview empty until something actually selects a character.")]
    private bool showFirstCharacterByDefault = true;

    [SerializeField, Tooltip("Keep ONLY the branch of the prefab that BlobAnimationView actually poses (its Root) and discard every other child - weapon, ground shadow, collision capsule, effect emitters, the Downed/KO pose objects. On by default: a menu preview wants the character, not the gameplay entity assembled around them. Turn off to show the prefab whole.")]
    private bool bodyRootOnly = true;

    private static int _stageCount;

    private Transform _stage;
    private Transform _holder;      // inactive - prefabs are built under here so nothing Awakes early
    private Camera _camera;
    private RenderTexture _texture;
    private GameObject _instance;
    private BlobAnimationView _animator;
    private GameObject _sourcePrefab;
    private int _previewLayer = -1;
    private bool _subscribed;
    private bool _reframePending;
    private bool _restoringSelection;

    private void Awake()
    {
        EnsureStage();
    }

    // Binding happens here rather than in OnEnable because PartyManager is a scene singleton that
    // assigns its own Instance in Awake - OnEnable can run first and find nothing.
    private void Start()
    {
        Subscribe();
        RefreshFromSelection();
    }

    private void OnEnable()
    {
        // The stage lives at the scene root rather than under this widget, so it survives (and
        // would keep rendering) while the panel showing it is switched off. Nothing is looking at
        // the RawImage then, so stop paying for the render.
        if (_camera != null)
            _camera.enabled = true;

        // No-ops before Start has run once - see Subscribe.
        Subscribe();
        RefreshFromSelection();
    }

    private void OnDisable()
    {
        if (_camera != null)
            _camera.enabled = false;

        Unsubscribe();
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
    // frame rather than an empty box until someone touches the picker.
    //
    // Public because a party slot (RoomWidget) drives its own preview from a REMOTE player's pick,
    // which is a character id read off that player's Photon properties - it has no prefab to hand
    // to Show(). Those slots run with followLocalSelection off.
    public void ShowCharacterId(string characterId)
    {
        var catalog = PartyManager.Instance != null ? PartyManager.Instance.characterCatalog : null;
        if (catalog == null || catalog.characters == null || catalog.characters.Length == 0)
            return;

        GameObject prefab = catalog.ResolveViewPrefab(characterId);

        if (prefab == null && showFirstCharacterByDefault)
            prefab = catalog.characters[0].viewPrefab;

        Show(prefab);
    }

    // Called from Awake, and again from Show as a safety net: a widget sitting on a panel that
    // starts inactive gets no Awake until that panel is first shown, but Show can be called before
    // then (PartyRoomWidget commits a default character pick from its own Start).
    private void EnsureStage()
    {
        if (_stage != null)
            return;

        // Fully qualified - Quantum has its own LayerMask type, and `using Quantum` above (for
        // BlobAnimationView) makes the bare name ambiguous.
        _previewLayer = UnityEngine.LayerMask.NameToLayer(previewLayerName);
        if (_previewLayer < 0)
        {
            LogHelper.Error("CharacterPreview", $"Layer '{previewLayerName}' does not exist - create it in Tags & Layers. Falling back to the character's authored layers, which means the preview camera may render nothing or the menu camera may render the stage.");
        }

        BuildStage();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        DestroyInstance();

        if (_texture != null)
        {
            if (targetImage != null && targetImage.texture == _texture)
                targetImage.texture = null;

            _texture.Release();
            Destroy(_texture);
            _texture = null;
        }

        if (_stage != null)
            Destroy(_stage.gameObject);
    }

    private void Update()
    {
        // The whole point of the preview - BlobAnimationView's own Update never fires here (there
        // is no QuantumRunner in the menu), so the idle cycle has to be pushed frame by frame.
        if (_animator != null)
            _animator.TickPreview(Time.deltaTime);

        // Re-centre once the rig has actually struck its first pose. BuildInstance measures the
        // prefab as authored, but the first tick billboards the body toward this camera and applies
        // the idle offsets - which moves what's on screen, and therefore where the middle of it is.
        if (_reframePending)
        {
            _reframePending = false;
            ApplyFraming();
        }
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

        EnsureStage();

        DestroyInstance();
        _sourcePrefab = characterPrefab;
        BuildInstance(characterPrefab);
    }

    // Empties the preview. A widget that follows the local pick doesn't stay empty though - it goes
    // straight back to showing that pick, because "blank" is never a correct state for it: the local
    // player's character exists whether or not a party does, and the only thing that clears this
    // widget is a roster slot reporting nobody in it, which says nothing about the local pick.
    // Without that, leaving a party would leave the player staring at an empty portrait until they
    // next touched the character picker.
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

    private void BuildStage()
    {
        int index = _stageCount++;

        _stage = new GameObject($"{name} (Preview Stage)").transform;
        _stage.position = stageOrigin + new Vector3(stageSpacing * index, 0f, 0f);

        // Prefabs are instantiated under this, stripped, and only then re-parented to the stage
        // proper. An inactive parent means Unity never runs Awake/OnEnable on any of the gameplay
        // components in between - which is what keeps the stripping safe rather than a race
        // against a dozen components trying to find a simulation that isn't there.
        _holder = new GameObject("Holder").transform;
        _holder.SetParent(_stage, false);
        _holder.gameObject.SetActive(false);

        _texture = new RenderTexture(Mathf.Max(8, textureSize.x), Mathf.Max(8, textureSize.y), 16, RenderTextureFormat.ARGB32)
        {
            name = $"{name} (Preview RT)",
            filterMode = filterMode,
            useMipMap = false,
            antiAliasing = 1,
        };
        _texture.Create();

        var cameraObject = new GameObject("Preview Camera");
        cameraObject.transform.SetParent(_stage, false);
        cameraObject.transform.localPosition = cameraOffset;
        cameraObject.transform.localRotation = Quaternion.Euler(cameraEuler);

        _camera = cameraObject.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = orthographicSize;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = 100f;
        // Transparent background so the character composites over whatever the UI already draws
        // behind the RawImage, instead of punching a solid rectangle through the menu art.
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.cullingMask = _previewLayer >= 0 ? 1 << _previewLayer : ~0;
        _camera.targetTexture = _texture;
        _camera.useOcclusionCulling = false;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;

        // URP would otherwise composite post-processing over the render and destroy the alpha the
        // transparent background above depends on. Shadows are pointless for a single unlit sprite.
        var urp = _camera.GetUniversalAdditionalCameraData();
        if (urp != null)
        {
            urp.renderPostProcessing = false;
            urp.renderShadows = false;
            urp.requiresColorOption = CameraOverrideOption.Off;
            urp.requiresDepthOption = CameraOverrideOption.Off;
        }

        _camera.enabled = isActiveAndEnabled;

        if (targetImage != null)
            targetImage.texture = _texture;
    }

    private void BuildInstance(GameObject characterPrefab)
    {
        // Built under the inactive holder - see BuildStage.
        _instance = Instantiate(characterPrefab, _holder);
        _instance.name = characterPrefab.name + " (Preview)";

        _animator = _instance.GetComponentInChildren<BlobAnimationView>(true);
        if (_animator == null)
        {
            LogHelper.Warn("CharacterPreview", $"'{characterPrefab.name}' has no BlobAnimationView - it will be shown as a static pose with no idle animation.");
        }

        if (bodyRootOnly)
            PruneToBodyRoot(_instance);

        Strip(_instance);

        if (_previewLayer >= 0)
            SetLayerRecursively(_instance.transform, _previewLayer);

        _instance.transform.SetParent(_stage, false);
        _instance.transform.localPosition = Vector3.zero;
        _instance.transform.localRotation = Quaternion.Euler(0f, characterYaw, 0f);
        _instance.transform.localScale = Vector3.one * characterScale;
        _instance.SetActive(true);

        // Without this the rig billboards toward Camera.main - the MENU's camera, pointing
        // somewhere else entirely - and the sprite turns edge-on to the preview camera.
        if (_animator != null)
            _animator.SetPreviewCamera(_camera);

        ApplyFraming();
        _reframePending = _animator != null;
    }

    // Deletes every GameObject under the instance except the branch leading to (and hanging off)
    // BlobAnimationView.Root - the rig it actually poses. The prefab's own root object is always
    // kept, because that is where BlobAnimationView itself lives and it has to survive to drive
    // the idle cycle; only its unrelated siblings and their subtrees go.
    //
    // The animator's other authored roots (downedRoot/koRoot/handsRoot) are usually outside that
    // branch and so get destroyed here. That is fine and needs no bookkeeping: ApplyLifeStateVisuals
    // null-checks each one, and a preview is only ever shown Alive.
    private void PruneToBodyRoot(GameObject instance)
    {
        Transform keep = _animator != null ? _animator.Root : null;
        if (keep == null || keep == instance.transform)
            return;

        // The chain of ancestors between the instance root and the rig root - these have to stay
        // as intermediate parents even though nothing is drawn on them, since destroying one would
        // take the rig down with it.
        var chain = new HashSet<Transform>();
        for (Transform t = keep; t != null && t != instance.transform; t = t.parent)
            chain.Add(t);

        // Bail rather than gut the instance if Root turned out not to be under it at all - a rig
        // referenced across prefabs would otherwise leave a preview with nothing in it.
        if (chain.Contains(keep) == false || keep.IsChildOf(instance.transform) == false)
            return;

        Prune(instance.transform, keep, chain);
    }

    private static void Prune(Transform current, Transform keep, HashSet<Transform> chain)
    {
        // Everything below the rig root is the rig itself - stop here and keep it whole.
        if (current == keep)
            return;

        for (int i = current.childCount - 1; i >= 0; i--)
        {
            Transform child = current.GetChild(i);

            if (chain.Contains(child))
                Prune(child, keep, chain);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    // Removes everything the prefab carries for gameplay, leaving the rig transforms, their
    // renderers, and the one component that animates them.
    //
    // DestroyImmediate, not Destroy, throughout - and that's load-bearing rather than a shortcut.
    // Destroy defers removal to the end of the frame, so every component would still be present
    // when BuildInstance activates the object moments later, and Unity would run Awake() on all of
    // them before finally removing them - which is the exact thing building under an inactive
    // holder exists to prevent.
    private void Strip(GameObject instance)
    {
        var doomed = new List<MonoBehaviour>();

        foreach (var component in instance.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)   // a missing script on the prefab
                continue;
            if (component == _animator)
                continue;
            if (IsExplicitlyKept(component))
                continue;

            doomed.Add(component);
        }

        // A component another surviving component declares [RequireComponent] on can't be removed
        // while that dependent is still there - Unity refuses and logs an error. Quantum's own
        // QPrototype* components all require QuantumEntityPrototype, so this is a real case here,
        // not a hypothetical: destroy the depended-upon types last.
        var required = new HashSet<System.Type>();
        foreach (var component in doomed)
        {
            foreach (var attribute in component.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                var require = (RequireComponent)attribute;
                if (require.m_Type0 != null) required.Add(require.m_Type0);
                if (require.m_Type1 != null) required.Add(require.m_Type1);
                if (require.m_Type2 != null) required.Add(require.m_Type2);
            }
        }

        foreach (var component in doomed)
        {
            if (required.Contains(component.GetType()) == false)
                DestroyImmediate(component);
        }

        foreach (var component in doomed)
        {
            if (component != null)
                DestroyImmediate(component);
        }

        // Physics has no business in a UI stage - and a live Rigidbody would quietly fall forever.
        foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(collider);

        foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
            DestroyImmediate(body);

        if (keepEffects)
            return;

        foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            particles.gameObject.SetActive(false);

        foreach (var line in instance.GetComponentsInChildren<LineRenderer>(true))
            line.enabled = false;
    }

    private bool IsExplicitlyKept(MonoBehaviour component)
    {
        if (additionalKeptComponents == null)
            return false;

        string typeName = component.GetType().Name;
        foreach (string kept in additionalKeptComponents)
        {
            if (typeName == kept)
                return true;
        }

        return false;
    }

    private static void SetLayerRecursively(Transform transform, int layer)
    {
        transform.gameObject.layer = layer;

        for (int i = 0; i < transform.childCount; i++)
            SetLayerRecursively(transform.GetChild(i), layer);
    }

    // Live tuning: re-applies framing to the running stage so every field under Framing can be
    // dialled in during Play Mode without a rebuild. Also called automatically whenever a character
    // is built, since auto-centering has to re-measure per hero.
    [Button("Apply Framing")]
    private void ApplyFraming()
    {
        if (_instance != null)
        {
            _instance.transform.localRotation = Quaternion.Euler(0f, characterYaw, 0f);
            _instance.transform.localScale = Vector3.one * characterScale;
        }

        if (_camera == null)
            return;

        _camera.transform.localRotation = Quaternion.Euler(cameraEuler);
        _camera.orthographicSize = orthographicSize;

        if (autoCenter == false || TryGetVisualBounds(out Bounds bounds) == false)
        {
            _camera.transform.localPosition = cameraOffset;
            return;
        }

        if (autoZoom)
        {
            // Fit whichever axis is the tighter constraint. Width is divided by aspect because
            // orthographicSize is a HALF-HEIGHT - the horizontal half-extent it can show is
            // size * aspect, so a wide character needs its half-width scaled back down by aspect.
            float halfHeight = bounds.extents.y;
            float halfWidth = bounds.extents.x / Mathf.Max(0.0001f, _camera.aspect);
            _camera.orthographicSize = Mathf.Max(0.01f, Mathf.Max(halfHeight, halfWidth) * (1f + framePadding));
        }

        // Sit the camera back along its own facing from the middle of the character, so that point
        // lands dead centre of the render no matter how the camera is tilted or where the prefab
        // put its origin. The nudge is applied in the camera's own screen axes rather than world
        // ones, so "up" stays up on screen even with cameraEuler pitched.
        Transform cameraTransform = _camera.transform;
        float distance = Mathf.Max(0.01f, Mathf.Abs(cameraOffset.z));

        cameraTransform.position = bounds.center
                                   - cameraTransform.forward * distance
                                   + cameraTransform.right * framingNudge.x
                                   + cameraTransform.up * framingNudge.y;
    }

    // World-space bounds of everything the preview actually draws. Only enabled renderers on
    // active objects count, so the ParticleSystems/LineRenderers switched off during stripping
    // can't drag the centre off toward an effect that isn't visible.
    private bool TryGetVisualBounds(out Bounds bounds)
    {
        bounds = default;

        if (_instance == null)
            return false;

        bool found = false;

        foreach (var renderer in _instance.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled == false || renderer.gameObject.activeInHierarchy == false)
                continue;

            if (found)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                found = true;
            }
        }

        return found;
    }
}
