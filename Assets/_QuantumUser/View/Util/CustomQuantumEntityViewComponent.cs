namespace QuantumUser.View.Util
{
   using System;
using System.Collections.Generic;
using Quantum;
using Unity.Profiling;
using UnityEngine;

public abstract class CustomQuantumEntityViewComponent : MonoBehaviour
{
    
    protected QuantumEntityView entityView = default;
    [SerializeField]protected PlayerRef _playerRef = default;
    [SerializeField]protected EntityRef _entityRef = default;
    protected QuantumGame _game = null;
    public bool initialized = false;
    public bool executeOnlyOnLocal;
    // True once this entity matches any of MyLocalPlayer's registered slots - not just the first
    // local player, so couch co-op's second local player also gets its own local-only effects.
    public bool isLocal;
    protected bool _isQuittingApplication = false;

#if ENABLE_PROFILER
    // One marker per CONCRETE view component type (EnemyAttackVisualsView, ProjectileView, ...):
    // "View.Update/<Type>" is the per-frame QUpdate cost of every instance of it, and
    // "View.Initialize/<Type>" / "View.DeInitialize/<Type>" the spawn/despawn cost - the profiler
    // otherwise folds all of it into one QuantumEntityView.OnObservedGameUpdated /
    // CustomQuantumEntityViewComponent.Update line. Compiled out of release builds.
    private static readonly Dictionary<Type, ProfilerMarker[]> s_Markers = new Dictionary<Type, ProfilerMarker[]>();
    private ProfilerMarker[] _markers;

    private ProfilerMarker[] Markers
    {
        get
        {
            if (_markers != null)
                return _markers;

            Type type = GetType();
            if (s_Markers.TryGetValue(type, out _markers) == false)
            {
                _markers = new[]
                {
                    new ProfilerMarker("View.Initialize/" + type.Name),
                    new ProfilerMarker("View.DeInitialize/" + type.Name),
                    new ProfilerMarker("View.Update/" + type.Name),
                };
                s_Markers[type] = _markers;
            }

            return _markers;
        }
    }
#endif

    private void InitializeProfiled(QuantumGame game)
    {
#if ENABLE_PROFILER
        using (Markers[0].Auto())
#endif
        {
            Initialize(game);
        }
    }

    private void DeInitializeProfiled(QuantumGame game)
    {
#if ENABLE_PROFILER
        using (Markers[1].Auto())
#endif
        {
            DeInitialize(game);
        }
    }
    public virtual void Awake()
    {
        entityView = GetComponent<QuantumEntityView>();
        if (entityView == null)
            entityView = GetComponentInParent<QuantumEntityView>();
        if (entityView == null)
            entityView = transform.root.GetComponentInChildren<QuantumEntityView>();
        if (entityView)
        {
            entityView.OnEntityInstantiated.AddListener(InitializeProfiled);
            entityView.OnEntityDestroyed.AddListener(DeInitializeProfiled);

            // OnEntityInstantiated only fires once, right when the view is created. A component
            // added as a child afterwards (e.g. a weapon parented onto the character post-spawn)
            // would subscribe too late and never initialize, so catch up manually here.
            if (entityView.EntityRef.IsValid && entityView.Game != null)
            {
                Initialize(entityView.Game);
            }
        }
    }

    public virtual void Start()
    {
        // Absent outside a gameplay scene - the lobby character preview (CharacterPreviewWidget)
        // instantiates a real hero prefab into MenuScene, which has no QuantumRunner and no
        // MyLocalPlayer at all. Nothing local-player-specific applies to a rig that isn't in a
        // match, and Update() already no-ops there (_game stays null), so skipping is correct
        // rather than merely defensive.
        if (MyLocalPlayer.Instance == null)
            return;

        foreach (var slot in MyLocalPlayer.Instance.Slots)
        {
            if (slot.IsSet)
                OnLocalPlayerSetup(slot.EntityRef);
        }

        MyLocalPlayer.Instance.onLocalPlayerSetup += OnLocalPlayerSetup;
    }

    private void OnLocalPlayerSetup(EntityRef obj)
    {
        if(_entityRef == obj)
            isLocal = true;
    }

    private void OnApplicationQuit()
    {
        _isQuittingApplication = true;
    }

    public virtual void OnDestroy()
    {
        // Start() subscribes every view instance; without this every destroyed enemy/projectile view
        // leaked a handler, growing the delegate list (and its per-spawn copy cost) over a run.
        if (MyLocalPlayer.Instance != null)
            MyLocalPlayer.Instance.onLocalPlayerSetup -= OnLocalPlayerSetup;

        if (entityView)
        {
            entityView.OnEntityInstantiated.RemoveListener(InitializeProfiled);
            entityView.OnEntityDestroyed.RemoveListener(DeInitializeProfiled);
        }
    }
    
    public virtual void DeInitialize(QuantumGame game)
    {
        _entityRef = default;
        _playerRef = default;
        _game = null;
    }

    public virtual void Initialize(QuantumGame game)
    {
        _game = game;
        _entityRef = entityView.EntityRef;
        // PREDICTED, not Verified: the view is created off the predicted frame, and online a fresh
        // entity only reaches the verified frame a few ticks later - a Verified read here can miss
        // PlayerLink and leave _playerRef default (breaking local-player detection downstream).
        if (_game.Frames.Predicted.Has<PlayerLink>(_entityRef))
        {
            var playerLink = _game.Frames.Predicted.Get<PlayerLink>(_entityRef);
            _playerRef = playerLink.Player;
        }
        initialized = true;
    }

    public bool ShouldExecute()
    {
        if (executeOnlyOnLocal == false)
            return true;

        return isLocal;
    }
    
    private void Update()
    {
        if (_game == null) 
            return;
        if (_entityRef == EntityRef.None) 
            return;
        if (ShouldExecute() == false)
            return;
        
#if ENABLE_PROFILER
        using (Markers[2].Auto())
#endif
        {
            QUpdate(_game);
        }
    }

    protected abstract void QUpdate(QuantumGame game);
}
}