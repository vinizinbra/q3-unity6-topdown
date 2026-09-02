using Quantum;
using QuantumUser.View.Util;

namespace QuantumUser.View.Managers
{
    // Attach to any entity view prefab (player, enemy, projectile, etc.) to register its Transform
    // into EntityViewManager's generic EntityRef->Transform cache on spawn, and remove it on
    // destroy - gives callers (e.g. MovementRingView tracking Aim.Target) an O(1) lookup for "where is
    // entity X right now" instead of searching for it each time.
    public class EntityViewCacheInit : CustomQuantumEntityViewComponent
    {
        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            EntityViewManager.Instance.RegisterEntityTransform(_entityRef, transform);
        }

        public override void DeInitialize(QuantumGame game)
        {
            EntityViewManager.Instance.UnregisterEntityTransform(_entityRef);
            base.DeInitialize(game);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }
    }
}
