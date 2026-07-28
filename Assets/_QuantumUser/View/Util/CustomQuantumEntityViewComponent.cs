namespace QuantumUser.View.Util
{
   using System;
using Quantum;
using UnityEngine;

public abstract class CustomQuantumEntityViewComponent : MonoBehaviour
{
    
    protected QuantumEntityView entityView = default;
    [SerializeField]protected PlayerRef _playerRef = default;
    [SerializeField]protected EntityRef _entityRef = default;
    protected QuantumGame _game = null;
    public bool initialized = false;
    public bool executeOnlyOnLocal;
    public bool isLocal;
    protected bool _isQuittingApplication = false;
    public virtual void Awake()
    {
        entityView = GetComponent<QuantumEntityView>();
        if (entityView == null)
            entityView = GetComponentInParent<QuantumEntityView>();
        if (entityView == null)
            entityView = transform.root.GetComponentInChildren<QuantumEntityView>();
        if (entityView)
        {
            entityView.OnEntityInstantiated.AddListener(Initialize);
            entityView.OnEntityDestroyed.AddListener(DeInitialize);

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
        if (MyLocalPlayer.Instance.IsLocalPlayerSetup)
        {
            OnLocalPlayerSetup(MyLocalPlayer.Instance.EntityRef);
        }
        else
        {
            MyLocalPlayer.Instance.onLocalPlayerSetup += OnLocalPlayerSetup;
        }
        
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
        if (entityView)
        {
            entityView.OnEntityInstantiated.RemoveListener(Initialize);
            entityView.OnEntityDestroyed.RemoveListener(DeInitialize);
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
        if (_game.Frames.Verified.Has<PlayerLink>(_entityRef))
        {
            var playerLink = _game.Frames.Verified.Get<PlayerLink>(_entityRef);
            _playerRef = playerLink.Player;
        }
        initialized = true;
    }

    public bool ShouldExecute()
    {
        if (executeOnlyOnLocal == false)
            return true;

        return QuantumHelper.IsLocalPlayer(_playerRef);
    }
    
    private void Update()
    {
        if (_game == null) 
            return;
        if (_entityRef == EntityRef.None) 
            return;
        if (ShouldExecute() == false)
            return;
        
        QUpdate(_game);
    }

    protected abstract void QUpdate(QuantumGame game);
}
}