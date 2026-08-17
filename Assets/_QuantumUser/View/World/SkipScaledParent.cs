using UnityEngine;

namespace Quantum
{
    // Quantum itself carries no scale (Transform3D is position/rotation only), so nothing an
    // entity's view instantiates should ever sit under a parent with a non-1 local scale - if one
    // does (e.g. a ColliderVisualScaleView-driven wrapper resizing itself to fit a runtime-resized
    // collider), a fixed-scale visual authored underneath it would double up with that wrapper's
    // scale instead of just showing the intended size. Reparenting with worldPositionStays=true
    // keeps this transform's current world position/rotation/scale exactly as they appeared a
    // moment ago - Unity recomputes local scale to compensate for the parent change - so the visual
    // doesn't jump or resize the instant this runs, it just stops inheriting the wrapper's scale
    // going forward.
    [RequireComponent(typeof(CubeVisualBuilder))]
    public class SkipScaledParent : MonoBehaviour
    {
        private CubeVisualBuilder cubeVisualBuilder;

        private void Awake()
        {
            cubeVisualBuilder = GetComponent<CubeVisualBuilder>();
        }

        // LateUpdate, not Awake - the wrapper's scale isn't resolved yet at instantiation time.
        // ColliderVisualScaleView applies it later, from the same Update-phase callback Quantum's
        // view updater runs on, and can take a frame or two to succeed (the collider isn't always
        // readable on its first attempt) - checking in Awake would very likely see the wrapper's
        // stale authored scale instead of the final one. Unity guarantees every Update() call
        // finishes before any LateUpdate() call starts each frame, so checking here always sees
        // whatever that Update-phase resolve already landed this frame, without needing to know
        // anything about ColliderVisualScaleView's internals or explicit script execution order.
        // Runs every frame until fixed, then disables itself - once reparented, `parent` is the
        // original grandparent (already scale 1), so the condition goes false on its own and no
        // separate "already done" flag is needed.
        private void LateUpdate()
        {
            Transform parent = transform.parent;

            if (parent == null || parent.parent == null)
            {
                enabled = false;
                return;
            }

            if (parent.localScale != Vector3.one)
            {
                transform.SetParent(parent.parent, true);

                // CubeVisualBuilder.Start() already ran and tiled its edge/corner/center pieces
                // off this same GameObject's transform.localScale - before this reparent, that was
                // still whatever stale value it was authored at, since it never read the wrapper's
                // resolved scale in the first place. worldPositionStays=true just backed out a new
                // localScale here that matches the true final world size, so re-running Generate()
                // now (it resets its own previous output first) rebuilds against the correct size
                // instead of leaving the first, wrong-sized pass in the scene.
                cubeVisualBuilder.Generate();

                // One-shot: disable immediately after the single reparent+regenerate rather than
                // waiting for a future frame's parent==scale-1 check to disable. The reparent is a
                // terminal action - it only ever needs to happen once. Waiting (the old `return;`)
                // could re-fire every frame forever: CubeVisualBuilder.DrawMergingNeighbors reparents
                // merging cubes under each other's SCALED visual roots, so `parent.localScale != one`
                // stays true, and this kept reparenting + calling Generate() every LateUpdate - the
                // 99999-log regeneration loop, which also stacked duplicate/stale corner pieces.
                enabled = false;
                return;
            }

            enabled = false;
        }
    }
}
