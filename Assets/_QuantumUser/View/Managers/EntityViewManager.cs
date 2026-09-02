using QuantumUser.View.Util;

namespace QuantumUser.View.Managers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Quantum;
    using UnityEngine;


    public class EntityViewManager : QuantumGlobalMonoBehaviour
    {

        public static EntityViewManager Instance;
        private readonly Dictionary<EntityRef, CharView> _charsInGame = new Dictionary<EntityRef, CharView>();
        private readonly List<CharView> AllCharViews = new List<CharView>();

        // Generic EntityRef -> view Transform lookup, populated by EntityViewCacheInit on any
        // entity view prefab (player, enemy, projectile, etc.) - not just CharViews. Used for fast
        // "where is entity X right now" lookups (e.g. MovementRingView tracking Aim.Target) instead
        // of searching for the view each time.
        private readonly Dictionary<EntityRef, Transform> _entityTransforms = new Dictionary<EntityRef, Transform>();
        public int AllCharsCount => AllCharViews.Count();
        public int OnlinePlayerCount => _charsInGame.Count(x => !x.Value.isBot);
        public int BotPlayerCount => _charsInGame.Count(x => x.Value.isBot);
        public Action onAllPlayersConnected;
        public List<CharView> AllChars => AllCharViews;

        // Fires for every player CharView (local or remote) as it's added/removed - unlike
        // MyLocalPlayer's onLocalPlayerRegistered/Unregistered, which only ever fires for this
        // client's own local players. Used by party HUD UI (e.g. PartyHudManager)
        // that needs to track every player currently in the match.
        public Action<CharView> onPlayerAdded;
        public Action<EntityRef> onPlayerRemoved;

        private void Awake()
        {
            Instance = this;
        }

    
        public void AddView(PlayerRef playerRef, EntityRef entityRef,CharView charView, string playerName = null)
        {
            AllCharViews.Add(charView);
            
            if (playerName != null)
                _charsInGame.Add(entityRef, charView);
            else
            {
                _charsInGame.Add(entityRef, charView);
            }
            if (_game != null)
            {
                if (_charsInGame.Count == _game.Frames.Verified.PlayerCount)
                {
                    onAllPlayersConnected?.Invoke();
                }
            }

            onPlayerAdded?.Invoke(charView);
        }
        public int GetPlayerCount()
        {
            return _charsInGame.Count;
        }

        public CharView GetCharViewByEntityRef(EntityRef entityRef)
        {
            return _charsInGame.GetValueOrDefault(entityRef);
        }
        
        public CharView GetCharByIndex(int index)
        {
            return _charsInGame.ElementAt(index).Value;
        }  
        public int FindEntityRefIndex(EntityRef entityRef)
        {
            return _charsInGame.Keys.ToList().IndexOf(entityRef);
        }

        public async void RemoveView( EntityRef entityRef)
        {
            await Task.Delay(TimeSpan.FromSeconds(0.5f));
            _charsInGame.Remove(entityRef);
            onPlayerRemoved?.Invoke(entityRef);
        }

        // Generic cache (any entity, not just CharViews) - populated by EntityViewCacheInit.
        public void RegisterEntityTransform(EntityRef entityRef, Transform entityTransform)
        {
            LogHelper.Log("EntityViewManager", $"Registering entity transform {entityRef} -> {entityTransform.name}");
            _entityTransforms.Add(entityRef, entityTransform);
        }

        public void UnregisterEntityTransform(EntityRef entityRef)
        {
            LogHelper.Log("EntityViewManager", $"Unregistering entity transform {entityRef}");
            _entityTransforms.Remove(entityRef);
        }

        // GetValueOrDefault, not the indexer - callers like MovementRingView poll this every frame off a
        // simulation-side EntityRef (e.g. Aim.Target) that can still point at an entity whose view
        // was just destroyed/unregistered (e.g. it died) for a frame or two.
        public Transform GetEntityTransform(EntityRef entityRef)
        {
            return _entityTransforms.GetValueOrDefault(entityRef);
        }

        public override void QLateUpdate(QuantumGame game)
        {
            
        }

        public override void QUpdate(QuantumGame game)
        {
            
        }

        public override void QStart(QuantumGame game)
        {
            
        }

        public Dictionary<EntityRef,CharView> GetAllChars()
        {
            return _charsInGame;
        }
    }

}