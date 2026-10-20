using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Managers;
using UnityEngine;

// Red arrows pinned to the screen edge pointing at every enemy still alive while a Breathing Break
// waits to be secured (see docs/offscreen-indicators.md, "Off-screen enemy arrows") - the same
// "CLEAR ALL ENEMIES TO SECURE THE AREA" window SurvivalWidget's banner and the minimap's clear-enemy
// markers use (CurrentState Survival + CurrentPhaseKind Breathing + !BreathingAreaSecured). Unlike the
// minimap it doesn't skip Elite/Persistent enemies: every one of them blocks securing the area
// (SurvivalProgressionUtility.IsEncounterCleared), so every one gets an arrow.
//
// Local feedback only, fully self-contained like MinimapWidget: reads game.Frames.Predicted every
// LateUpdate instead of having enemy views register (enemies only matter for this short window, and
// the frame filter is what decides who blocks securing). Positions come from the enemy's entity view
// when it has one (interpolated, matches what's on screen), else its Transform3D. Arrows are pooled
// clones of the inactive `template` child; only the `maxIndicators` nearest off-screen enemies get one,
// so a big leftover crowd doesn't wall the screen edge.
//
// Same geometry as OffscreenPlayerIndicatorManager: this rect stretches over the whole canvas (pivot
// centred, widgets anchored to its centre), so a local point IS a widget's anchoredPosition, clamped to
// the canvas rect intersected with Screen.safeArea.
public class OffscreenEnemyIndicatorManager : MonoBehaviour
{
    [SerializeField, Tooltip("Inactive child cloned for each arrow (pooled).")]
    private OffscreenEnemyIndicatorWidget template;

    [Header("Layout")]
    [SerializeField, Tooltip("How far inside the (safe-area) screen edge an arrow's centre is pinned, in canvas units.")]
    private float edgePadding = 48f;
    [SerializeField, Tooltip("An enemy counts as on-screen while its projected point is at least this far inside the screen edge (canvas units).")]
    private float visibleMargin = 0f;
    [SerializeField, Tooltip("World offset added to the enemy's position before projecting - aims at the body rather than the feet.")]
    private Vector3 worldOffset = new Vector3(0f, 0.75f, 0f);
    [SerializeField, Min(1), Tooltip("Only this many off-screen enemies (nearest to local player one) get an arrow at once.")]
    private int maxIndicators = 8;

    private readonly List<OffscreenEnemyIndicatorWidget> _pool = new List<OffscreenEnemyIndicatorWidget>();
    private readonly Dictionary<EntityRef, OffscreenEnemyIndicatorWidget> _active = new Dictionary<EntityRef, OffscreenEnemyIndicatorWidget>();
    private readonly List<Candidate> _candidates = new List<Candidate>();
    private readonly HashSet<EntityRef> _shownThisFrame = new HashSet<EntityRef>();
    private readonly List<EntityRef> _staleBuffer = new List<EntityRef>();

    private RectTransform _rect;
    private Canvas _canvas;

    private struct Candidate
    {
        public EntityRef Entity;
        public Vector2 Pinned;
        public Vector2 Direction;
        public float SqrDistance;
        public bool Elite;
    }

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _canvas = GetComponentInParent<Canvas>();

        if (template != null)
            template.gameObject.SetActive(false);
    }

    private unsafe void LateUpdate()
    {
        QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
        Frame frame = game?.Frames.Predicted;
        Camera worldCamera = FollowCamera.WorldCamera;

        // An uncleared Breathing phase still reads as GameState.Survival (GameState.Breathing only starts
        // once the area is secured - CombatDirectorSystem.ResolveDesiredState), same check as
        // MinimapWidget.UpdateClearEnemyMarkers / SurvivalWidget's clear-enemies banner.
        bool clearingWindow = frame != null
            && frame.Global->CurrentState == GameState.Survival
            && frame.Global->CurrentPhaseKind == SurvivalPhaseKind.Breathing
            && frame.Global->BreathingAreaSecured == false;

        // Elites always get an arrow while the match is running, not just during the clear window.
        bool inMatch = frame != null && frame.Global->CurrentState != GameState.Lobby;

        if ((clearingWindow == false && inMatch == false) || worldCamera == null || template == null)
        {
            HideAll();
            return;
        }

        Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        Rect safeRect = GetSafeRect(eventCamera);
        Rect pinRect = Inset(safeRect, edgePadding);
        Rect visibleRect = Inset(safeRect, visibleMargin);
        Vector3 origin = ResolveLocalPlayerPosition(worldCamera);

        _candidates.Clear();
        var enemies = frame.Filter<Enemy, Transform3D>();

        while (enemies.Next(out EntityRef entity, out Enemy enemy, out Transform3D transform3D))
        {
            if (enemy.Phase == EnemyActionPhase.Dead)
                continue;

            EnemyDataAsset data = frame.FindAsset(enemy.EnemyData);
            bool elite = data != null && data.Tier == EnemyTier.Elite;
            if (clearingWindow == false && elite == false)
                continue;

            Transform view = EntityViewManager.Instance != null ? EntityViewManager.Instance.GetEntityTransform(entity) : null;
            Vector3 worldPosition = view != null ? view.position : transform3D.Position.ToUnityVector3();

            if (TryProject(worldCamera, eventCamera, worldPosition + worldOffset, visibleRect, pinRect, out Vector2 pinned, out Vector2 direction) == false)
                continue;

            Vector3 delta = worldPosition - origin;
            _candidates.Add(new Candidate
            {
                Entity = entity,
                Pinned = pinned,
                Direction = direction,
                SqrDistance = delta.x * delta.x + delta.z * delta.z,
                Elite = elite,
            });
        }

        if (_candidates.Count > maxIndicators)
            _candidates.Sort((a, b) => a.Elite != b.Elite ? (a.Elite ? -1 : 1) : a.SqrDistance.CompareTo(b.SqrDistance));

        _shownThisFrame.Clear();
        int eliteCount = 0;
        foreach (Candidate candidate in _candidates)
            if (candidate.Elite) eliteCount++;

        // Every off-screen Elite gets an arrow; the cap only limits the rest.
        int count = Mathf.Min(Mathf.Max(maxIndicators, eliteCount), _candidates.Count);

        for (int i = 0; i < count; i++)
        {
            Candidate candidate = _candidates[i];

            if (_active.TryGetValue(candidate.Entity, out OffscreenEnemyIndicatorWidget widget) == false)
            {
                widget = Rent();
                _active.Add(candidate.Entity, widget);
            }

            widget.Refresh(candidate.Pinned, candidate.Direction);
            widget.SetShown(true);
            _shownThisFrame.Add(candidate.Entity);
        }

        // Back on screen, dead, retired or no longer among the nearest - pop out and return to the pool.
        _staleBuffer.Clear();
        foreach (var pair in _active)
        {
            if (_shownThisFrame.Contains(pair.Key) == false)
                _staleBuffer.Add(pair.Key);
        }

        foreach (EntityRef entity in _staleBuffer)
            Release(entity);
    }

    // True when the point is off-screen; `pinned` is where the arrow sits on the edge, `direction` the
    // screen-space direction from the centre toward the enemy.
    private bool TryProject(Camera worldCamera, Camera eventCamera, Vector3 worldPosition, Rect visibleRect, Rect pinRect, out Vector2 pinned, out Vector2 direction)
    {
        pinned = default;
        direction = Vector2.up;

        Vector3 screenPoint = worldCamera.WorldToScreenPoint(worldPosition);
        bool behindCamera = screenPoint.z < 0f;

        // A point behind the camera projects mirrored through the screen centre - flip it back.
        if (behindCamera)
            screenPoint = new Vector3(Screen.width - screenPoint.x, Screen.height - screenPoint.y, 0f);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPoint, eventCamera, out Vector2 localPoint) == false)
            return false;

        if (behindCamera == false && visibleRect.Contains(localPoint))
            return false;

        Vector2 center = pinRect.center;
        direction = localPoint - center;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.up;

        direction.Normalize();
        pinned = ClampToRect(center, direction, pinRect);
        return true;
    }

    private OffscreenEnemyIndicatorWidget Rent()
    {
        OffscreenEnemyIndicatorWidget widget;
        if (_pool.Count > 0)
        {
            widget = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
        }
        else
        {
            widget = Instantiate(template, template.transform.parent);
            widget.name = "EnemyArrow";
        }

        widget.Setup();
        return widget;
    }

    private void Release(EntityRef entity)
    {
        if (_active.TryGetValue(entity, out OffscreenEnemyIndicatorWidget widget) == false)
            return;

        _active.Remove(entity);
        if (widget == null)
            return;

        widget.Hide(() => _pool.Add(widget));
    }

    private void HideAll()
    {
        if (_active.Count == 0)
            return;

        _staleBuffer.Clear();
        _staleBuffer.AddRange(_active.Keys);
        foreach (EntityRef entity in _staleBuffer)
            Release(entity);
    }

    // Walks from the centre along `direction` until it hits the rect's border.
    private static Vector2 ClampToRect(Vector2 center, Vector2 direction, Rect rect)
    {
        float halfWidth = rect.width * 0.5f;
        float halfHeight = rect.height * 0.5f;

        float scaleX = Mathf.Abs(direction.x) > 0.0001f ? halfWidth / Mathf.Abs(direction.x) : float.MaxValue;
        float scaleY = Mathf.Abs(direction.y) > 0.0001f ? halfHeight / Mathf.Abs(direction.y) : float.MaxValue;

        return center + direction * Mathf.Min(scaleX, scaleY);
    }

    private static Rect Inset(Rect rect, float amount)
    {
        float x = Mathf.Min(amount, rect.width * 0.5f);
        float y = Mathf.Min(amount, rect.height * 0.5f);
        return new Rect(rect.xMin + x, rect.yMin + y, rect.width - x * 2f, rect.height - y * 2f);
    }

    // This rect intersected with Screen.safeArea, in this rect's local space.
    private Rect GetSafeRect(Camera eventCamera)
    {
        Rect rect = _rect.rect;
        Rect safeArea = Screen.safeArea;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, safeArea.min, eventCamera, out Vector2 min) == false
            || RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, safeArea.max, eventCamera, out Vector2 max) == false)
            return rect;

        float xMin = Mathf.Max(rect.xMin, min.x);
        float yMin = Mathf.Max(rect.yMin, min.y);
        float xMax = Mathf.Min(rect.xMax, max.x);
        float yMax = Mathf.Min(rect.yMax, max.y);

        return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : rect;
    }

    // Local player one's hero, for "nearest first" - falls back to the camera's ground point.
    private static Vector3 ResolveLocalPlayerPosition(Camera worldCamera)
    {
        if (MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.IsLocalPlayerSetup && EntityViewManager.Instance != null)
        {
            Transform hero = EntityViewManager.Instance.GetEntityTransform(MyLocalPlayer.Instance.EntityRef);
            if (hero != null)
                return hero.position;
        }

        return worldCamera.transform.position;
    }
}
