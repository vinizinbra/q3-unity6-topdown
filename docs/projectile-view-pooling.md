# Projectile View Pooling

Projectile entity views are reused instead of `Instantiate`d/`Destroy`ed per shot. Motivation: an
Android profile of a 15-projectile weapon showed `QuantumGame.InvokeOnUpdateView` spiking to ~22 ms
in the worst frames — ~12.7 `Instantiate` calls per frame (`Instantiate.Copy` + `Produce` + `Awake`
≈ 0.85 ms each on a low-end device) creating projectile view prefabs. Quantum does **not** pool views
by default (`QuantumEntityViewPool` is an optional component and the project never had one).

## How it works

| Piece | Role |
|---|---|
| `ProjectileViewUpdater` (`_QuantumUser/View/Managers`) | Subclass of `QuantumEntityViewUpdater` on the game-scene updater GameObject. Overrides `CreateEntityViewInstance` / `DestroyEntityViewInstance`; only prefabs carrying a `ProjectileView` take the pooled path, everything else is the stock path with its exact position/rotation. Inspector: `Pool Projectile Views` (kill switch), `Max Pooled Per Prefab` (64), **Log Pool Stats** button. |
| `ProjectileView` | `EnablePooling` (snapshot taken right after Instantiate), `RequestRelease`, `BeginPiece`/`EndPiece`, release when everything is home. |
| `ProjectileVisualController` | With `Settings.Owner` set, `Finish` → `FinishRecycled`: the detached visual root goes **back under its view** instead of being destroyed. |
| `ParticleGracefulStop` | `StopAndReturnWhenFinished(home, onReturned)`: trail pieces fade where the shot landed, then re-parent home. |
| `ProjectileViewSnapshot` | Pristine copy of the view hierarchy (transforms, active, renderers, sprite, trail, behaviour enabled, particle color/lifetime/rate-over-distance, `HasLight` color) restored on release. |
| `IProjectilePoolPart` | Siblings with per-shot state implement `ResetForPool` (`ProjectileElementalFxView`, `ProjectileView`). |

## Lifecycle of one pooled view

1. `CreateEntityViewInstance`: pop an idle view (set parent/pose, `SetActive(true)`) or `Instantiate` + `EnablePooling`.
2. Quantum `Activate` → every view component `Initialize` (same as before). `ProjectileView.Initialize`
   detaches the visual root (`_piecesAway++`).
3. Entity dies → `DeInitialize` → updater calls `RequestRelease`. The view is **held, not released**: attached renderers/
   emitters/ground blobs are hidden (`HideAttachedWhileHeld`); the detached visual keeps tweening/fading.
4. Release happens when `_piecesAway == 0` **and** ≥ 2 frames have passed since `RequestRelease`. The 2 frames matter:
   `EventProjectileDestroyed` is dispatched after `DeInitialize` in the same Unity frame and the visual controller and
   `ProjectileElementalFxView` still need the old entity ref for it.
5. `CompleteRelease`: `SetActive(false)` **first** (so restoring children fires no `OnEnable`), `ResetForPool` on
   parts, `Snapshot.Restore()`, updater parks it under `ProjectileViewPool`.
6. Safety: if pieces never come home in 6 s the view is destroyed rather than pooled.

## Traps (read before touching projectile views)

- **The visual is part of the pooled hierarchy now.** Anything that used to be safe because "the whole
  visual is destroyed at Finish" is no longer: write per-shot mutations so the snapshot covers them, or add the
  property to `ProjectileViewSnapshot`. `ProjectileDataVisualsView` writes several conditionally ("only if override > 0")
  — that is why a generic snapshot exists.
- **Do not `Destroy` pieces of the template hierarchy** (trail PS, trail renderer, sprite) in new code on the pooled path;
  hand them to `ParticleGracefulStop.StopAndReturnWhenFinished`. Runtime-instantiated extras (echo ghost, weapon extra
  particle) are not in the snapshot and still die normally.
- `ProjectileGhostTrailManager` builds `Settings` from the prefab **asset**, so `Owner` is null and it keeps the
  destroy-on-finish path.
- Never add `Awake/Start/OnDestroy/Update/LateUpdate` to `ProjectileViewUpdater` (the base class has its own; Unity only
  runs the most derived one).
- `QuantumEvent.Subscribe` in `Awake` happens once per instance for a pooled view: handlers must ignore events when the
  per-shot entity ref is cleared (`ElementalFxView._ownEntityRef` is reset in `ResetForPool`).
- New projectile view components with per-shot fields must implement `IProjectilePoolPart`.

## Status

Code-complete and compiling. **Not yet verified in Play Mode** — see the checklist below. The scene
`GrasslandOutpostGameScene` has the updater component swapped (`QuantumEntityViewUpdater` → `ProjectileViewUpdater`).

### Verify in Play Mode
- Fire the 15-projectile weapon continuously, then press **Log Pool Stats** on the `QuantumEntityViewUpdater` object:
  `created` should plateau (≈ peak simultaneous projectiles) while `reused` keeps climbing.
- Look for: stale sprite/tint/trail after switching weapons mid-run, projectiles invisible on 2nd+ use, trail ribbons
  missing, ground lights stuck, impact effect missing, elemental particle not following / not releasing.
- If anything is wrong, untick `Pool Projectile Views`: views then take the stock Instantiate/Destroy path and the visual
  is destroyed on finish exactly as before pooling existed.
- Re-profile on the Android device (same weapon + split shot): `Instantiate`/`Instantiate.Copy` under
  `QuantumEntityView.OnObservedGameUpdated` should drop out of the spike frames.

## Known simplifications
- No prewarm: the pool grows to peak on first bursts (first bursts still pay `Instantiate`).
- Extra particle / echo ghost are still `Instantiate`d per shot (not pooled).
- `HideAttachedVisuals` / snapshot restore walk component lists per release (static scratch lists, no per-shot allocs
  in the controller; the snapshot itself allocates nothing after capture).
