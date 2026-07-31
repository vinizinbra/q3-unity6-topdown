using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Scales this object's transform to visually match the caster's own ProjectileSlowField.Radius
    // (Kai's Void Field passive - see VoidFieldPassiveData/VoidFieldSystem). Attach to a child object
    // under Kai's player prefab (a flat disc/ring/sphere mesh authored at radius 1 in its own local
    // space, so localScale directly reads as world-space radius once scaleMultiplier accounts for
    // however that mesh's own unit size relates to "radius 1"). No-ops to zero scale once the entity
    // no longer carries ProjectileSlowField - any hero without the passive, or before this
    // character's own CharacterSystem seeding has run - rather than assuming Kai is the only hero
    // this could ever be attached to.
    public class KaiVoidFieldView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Multiplies Radius before applying it to localScale - use 2 if the " +
            "mesh's own local radius is 0.5 (i.e. authored at diameter 1), or 1 if it's already authored at radius 1.")]
        private float scaleMultiplier = 2f;

        protected override void QUpdate(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;

            if (frame.Has<ProjectileSlowField>(_entityRef) == false)
            {
                transform.localScale = Vector3.zero;
                return;
            }

            float radius = frame.Get<ProjectileSlowField>(_entityRef).Radius.AsFloat;
            transform.localScale = Vector3.one * (radius * scaleMultiplier);
        }
    }
}
