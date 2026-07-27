using NaughtyAttributes;
using UnityEngine;

// Snaps a decal particle system onto the ground it spawned above and tilts it to match the surface
// normal, so it reads correctly on sloped terrain instead of always lying flat. Stops itself if no
// ground is found (e.g. spawned mid-air over a pit) - the parent explosion still plays normally.
//
// This is very often a child of a pooled parent effect (see EffectsManager's ObjectPool<ParticleSystem>).
// Two things follow from that pooling:
//
// 1. GetPooledInstance calls pool.Get() - which SetActive(true)s the root and fires this script's
//    OnEnable on every child - BEFORE it calls SetPositionAndRotation with the new spawn point.
//    Snapping directly from OnEnable would raycast from the instance's stale last-release position.
//    Instead OnEnable just arms a flag, and the actual raycast happens in that same frame's
//    LateUpdate, by which point the parent has already finished repositioning it.
//
// 2. EffectsManager.PlayEffect calls instance.Play(true) on the ROOT, which unconditionally cascades
//    Play() to this decal's ParticleSystem too - before this script's raycast even runs. So on a
//    miss, this can't just skip playing; it has to Stop(withChildren: true, StopEmittingAndClear) the
//    ParticleSystem it already started. That also matters for reuse: ReleaseWhenFinished only
//    releases the pooled instance back to the pool once instance.IsAlive(true) (root + all children)
//    goes false, so a decal left playing without ground would hold up releasing/reusing the whole
//    Explosion. Stopping it immediately keeps its aliveness in sync with whatever it's actually
//    showing, exactly like the root's own aliveness drives release.
//
// Deactivating gameObject on a miss (instead of stopping the ParticleSystem) would be worse: this
// script's own GameObject must stay active, otherwise the pooled parent's later SetActive(true) won't
// cascade OnEnable back to it (Unity only re-activates children whose own activeSelf is still true),
// permanently killing this decal on every future reuse of that pooled instance even once ground is
// present again.
[RequireComponent(typeof(ParticleSystem))]
public class GroundDecal : MonoBehaviour
{
    [SerializeField, Tooltip("Layers considered ground for the raycast.")]
    private LayerMask groundLayerMask;
    [SerializeField, Tooltip("How far above the decal's current position the raycast starts, so it can find ground even if spawned slightly under/inside it.")]
    private float raycastHeight = 2f;
    [SerializeField, Tooltip("Max ray length below raycastHeight. No ground within this range stops the decal instead of playing it.")]
    private float raycastDistance = 4f;
    [SerializeField, Tooltip("Pushed out along the surface normal to avoid z-fighting with the ground mesh.")]
    private float surfaceOffset = 0.01f;

    private ParticleSystem _particleSystem;
    private bool _pendingSnap;

    private void Awake()
    {
        _particleSystem = GetComponent<ParticleSystem>();
    }

    private void OnEnable()
    {
        _pendingSnap = true;
    }

    private void LateUpdate()
    {
        if (!_pendingSnap) return;

        _pendingSnap = false;
        SnapToGround();
    }

    [Button("Snap To Ground")]
    private void SnapToGround()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * raycastHeight;

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastHeight + raycastDistance, groundLayerMask))
        {
            _particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        Quaternion alignedRotation = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
        transform.SetPositionAndRotation(hit.point + hit.normal * surfaceOffset, alignedRotation);
    }
}
