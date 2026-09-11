using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Drives a projectile's visual, which deliberately is NOT this entity's own GameObject.
    //
    // The problem this exists to solve: a projectile's view is created on the frame its entity first
    // shows up in Frames.Predicted. For anything fired by a remote player that is several ticks
    // after the shot actually happened - this client only learns about the input later, rolls back
    // and resimulates, and by the time the view exists the bullet is already meters downrange. The
    // visual therefore pops into existence halfway to the target instead of leaving the barrel.
    //
    // So on spawn this DETACHES visualRoot from this GameObject, puts it back at the muzzle, and
    // hands it to a ProjectileVisualController that chases this entity's interpolated position at
    // catchUpSpeedMultiplier x the projectile's real speed until it converges. The deficit is paid
    // back in around a tenth of a second, and the shot reads correctly the whole way. A locally
    // simulated shot (every enemy's, and this client's own) has no deficit to pay back in the first
    // place, so it just tracks its entity exactly.
    //
    // "The muzzle" is resolved LIVE off the firing Weapon's own WeaponView.MuzzleTransform
    // (ResolveMuzzleTransform below) rather than the simulation's own Projectile.SpawnPosition -
    // that field is only ever "caster position + a small forward nudge + hand height", never
    // anchored to this weapon's actual authored barrel length (see StatUtility.
    // GetWeaponHoldOffset/ProjectileSpawner.ResolveSpawnOrigin), so a shot spawned there visibly
    // starts behind or in front of the visible barrel. SpawnPosition remains the fallback for
    // anything with no resolvable WeaponView - every enemy attack, and a player shot spawned before
    // its owner's own view exists yet.
    //
    // Detaching also means the visual outlives this view - which is what let ManualDisposal go away
    // entirely. It used to be set here so this GameObject could survive its own entity long enough
    // to tween onto the resolved hit point, at the cost of being the only thing in the project
    // nothing else would ever clean up: a disconnect or a reconnect resync tears a view down without
    // ever raising EventProjectileDestroyed, and every bullet in flight hung in the air forever.
    // QuantumEntityViewUpdater now owns this GameObject normally, and ProjectileVisualController
    // owns the visual's own ending.
    public class ProjectileView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("The bullet's own visual root - detached from this GameObject on spawn and owned from then on by a ProjectileVisualController. Leave empty to auto-resolve the first child that has any Renderer under it, which is what every projectile prefab here needs anyway.")]
        private Transform visualRoot;

        [Header("Catch-up")]
        [SerializeField, Tooltip("How much faster than the projectile's own live speed the visual travels while it is still behind the simulation. 1 never converges (it moves exactly as fast as what it is chasing); 2 pays back a 4-unit deficit on a 40 u/s bolt in 0.1s.")]
        private float catchUpSpeedMultiplier = 2f;

        [Header("Impact")]
        [SerializeField, Tooltip("Pooled particle prefab played (via EffectsManager) once the visual reaches its resolved hit position - hit or expired. Leave empty for no effect.")]
        private ParticleSystem destroyEffectPrefab;
        [SerializeField, Tooltip("Trail particle that should keep playing/fading out where the shot landed instead of being cut off mid-emission. Either a child under visualRoot, or the visual root's own ParticleSystem (then the whole root stays behind to fade, with its non-particle renderers/lights switched off). Leave empty if this projectile has no trail.")]
        private ParticleSystem trailParticle;
        [SerializeField, Tooltip("TrailRenderer that should linger and fade where the shot landed instead of being cut off - same contract as trailParticle, for a ribbon-style trail. A child under visualRoot or the root's own TrailRenderer. Leave empty if this projectile has none.")]
        private TrailRenderer trailRenderer;
        [SerializeField, Tooltip("Clamped bounds on how long the final catch-up onto the hit point can take, regardless of the projectile's last known speed - guards against a near-zero speed (e.g. an already-grounded/settled projectile) producing a near-infinite tween.")]
        private float minImpactDuration = 0.03f;
        [SerializeField]
        private float maxImpactDuration = 0.2f;
        [SerializeField, Tooltip("Last-resort safety net: seconds without this view pushing a target before the detached visual cleans itself up on its own. Should never be reached - DeInitialize already covers every teardown QuantumEntityViewUpdater knows about.")]
        private float orphanTimeout = 3f;

        private ProjectileVisualController _visual;

        // Where the bullet actually IS on screen, which is not this GameObject during the catch-up.
        // Read by ProjectileElementalFxView so the elemental trail follows the visual rather than the
        // simulated entity. Falls back to this transform when there is no detached visual.
        public Transform VisualTransform => _visual != null ? _visual.transform : transform;

        // Read off the PREFAB ASSET's own component by ProjectileGhostTrailManager, for a projectile
        // that died before this view ever got created - never off a live instance.
        public ParticleSystem TrailParticle => trailParticle;
        public TrailRenderer TrailRenderer => trailRenderer;
        public ParticleSystem DestroyEffectPrefab => destroyEffectPrefab;

        public override void Awake()
        {
            base.Awake();

            // Forced rather than assumed: a couple of the projectile prefabs still have it ticked on
            // from when this class needed it (see the class comment), and leaving it on now would
            // leak the entity's GameObject on every shot.
            if (entityView != null)
                entityView.ManualDisposal = false;
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            // Before any early-out below: tells ProjectileGhostTrailManager this shot has a real
            // view, so it must NOT draw its own fallback for it when the destroy event lands.
            ProjectileVisualRegistry.Register(_entityRef);
            LogHelper.Log("ProjFlow", $"[{_entityRef}] VIEW Initialize ({name}) frame={game.Frames.Predicted?.Number}", this);

            if (_visual != null)
                return;

            Transform root = ResolveVisualRoot();
            if (root == null)
            {
                LogHelper.Warn("ProjectileView", $"{name}: no visual root found - assign visualRoot, or give this " +
                    "prefab a child with a Renderer under it. The projectile will still work, but it spawns wherever " +
                    "the simulation has already carried it instead of at the muzzle.", this);
                return;
            }

            Frame frame = game.Frames.Predicted;

            Vector3 spawnPosition = transform.position;
            if (frame != null && frame.TryGet<Projectile>(_entityRef, out var projectile) == true)
                spawnPosition = ResolveVisualSpawnPosition(projectile.Owner, projectile.SpawnPosition.ToUnityVector3());

            ParticleSystem echoGhostParticle = AttachEchoGhostParticle(frame, root);

            var settings = new ProjectileVisualController.Settings
            {
                CatchUpSpeedMultiplier = catchUpSpeedMultiplier,
                MinImpactDuration = minImpactDuration,
                MaxImpactDuration = maxImpactDuration,
                OrphanTimeout = orphanTimeout,
                DestroyEffectPrefab = destroyEffectPrefab,
                TrailParticle = trailParticle,
                TrailRenderer = trailRenderer,
                EchoGhostParticle = echoGhostParticle,
            };

            _visual = ProjectileVisualController.Detach(root, _entityRef, spawnPosition, transform.rotation, settings);

            // Straight away, not only from the next QUpdate: this hands over the entity's real
            // position and speed on the very frame the view appears, so a projectile that dies the
            // same frame it spawns (a fast bolt hitting on its first simulated tick, which was the
            // whole reason this class used to need ManualDisposal) still resolves its impact against
            // a real speed instead of a zero.
            if (frame != null)
                PushCurrentState(frame);
        }

        // Fires when QuantumEntityViewUpdater tears this view down - the normal death path, but also
        // a disconnect and a reconnect resync, neither of which ever raises EventProjectileDestroyed.
        // The visual is detached and knows nothing about any of that, so tell it; it waits one frame
        // for the event to still land (it is dispatched slightly later in the same Unity frame -
        // QuantumGame.OnUpdateDone calls InvokeOnUpdateView() and only then InvokeEvents()) before
        // concluding nothing was hit.
        public override void DeInitialize(QuantumGame game)
        {
            LogHelper.Log("ProjFlow", $"[{_entityRef}] VIEW DeInitialize hasVisual={_visual != null} frame={game?.Frames.Predicted?.Number}", this);

            if (_visual != null)
                _visual.NotifyEntityGone();

            // Grace-stamped, not removed: this runs in the same frame's view pass BEFORE the
            // destroy event is dispatched, and the registry must still answer "had a view" then.
            ProjectileVisualRegistry.MarkGone(_entityRef);

            _visual = null;
            base.DeInitialize(game);
        }

        protected override void QUpdate(QuantumGame game)
        {
            PushCurrentState(game.Frames.Predicted);
        }

        private void PushCurrentState(Frame frame)
        {
            if (_visual == null)
                return;

            // A planted AreaHitData bomb (see ProjectileSystem.TryPlant) swaps off Projectile onto
            // DestroyAfterTime while staying alive, so this deliberately keeps pushing regardless -
            // the visual should sit on the bomb until it detonates. Its own destruction raises no
            // ProjectileDestroyed event, which DeInitialize above already handles as "nothing was
            // hit": no impact effect, which is right, since the explosion draws its own.
            bool hasProjectile = frame.TryGet<Projectile>(_entityRef, out var projectile);

            // Deliberately the raw launch speed, ignoring SpeedMultiplier (Kai's Void Field slowing
            // an enemy shot): this only sets how fast the visual is allowed to close a gap, and
            // MoveTowards can't overshoot the target, so erring high costs nothing while reading a
            // multiplier that is only guaranteed seeded from the tick after a spawn could cost a
            // frame of crawling.
            float speed = hasProjectile ? projectile.Velocity.Magnitude.AsFloat : 0f;

            Quaternion rotation = transform.rotation;
            if (hasProjectile == true)
            {
                Vector3 velocity = projectile.Velocity.ToUnityVector3();
                if (velocity.sqrMagnitude > 0.0001f)
                    rotation = Quaternion.LookRotation(velocity, Vector3.up);
            }

            _visual.Push(transform.position, rotation, speed,
                visible: hasProjectile == false || projectile.RemainingSpawnDelay <= 0);
        }

        // How far the simulation's own SpawnPosition may sit from the owner's live muzzle and still
        // count as "this shot left the barrel". A weapon shot's SpawnPosition is only ever the caster
        // position plus a hand/forward nudge, well within this of the real muzzle. Anything spawned
        // mid-flight by a perk - a Split pellet or Critical Rebound at a contact point, a Cluster
        // bomblet at a detonation, a Ghost Shot echo, a Vortex bolt - has the same Owner but leaves
        // from wherever it was spawned, and snapping THAT onto the gun would draw it flying out of
        // the barrel to a place it never was.
        private const float MuzzleSnapDistance = 1.5f;

        // The point a projectile's visual should start from: the owner's live muzzle when this shot
        // actually left the weapon, otherwise the simulation's own SpawnPosition. Shared with
        // ProjectileGhostTrailManager so a shot with and without a view starts from the same place.
        public static Vector3 ResolveVisualSpawnPosition(EntityRef owner, Vector3 simulationSpawnPosition)
        {
            Transform muzzle = ResolveMuzzleTransform(owner);
            if (muzzle == null)
                return simulationSpawnPosition;

            return (muzzle.position - simulationSpawnPosition).sqrMagnitude <= MuzzleSnapDistance * MuzzleSnapDistance
                ? muzzle.position
                : simulationSpawnPosition;
        }

        // Shared across every ProjectileView instance rather than resolved per-spawn - a fast weapon
        // fires several of these a second, and FindFirstObjectByType is the expensive part. Unity's
        // overloaded null-check on a destroyed Object makes this self-healing across a scene
        // reload/reconnect for free, same as the field BossWidget caches per-instance.
        private static QuantumEntityViewUpdater _entityViewUpdater;

        // Resolves the firing Weapon's own live muzzle transform, or null if the owner has no
        // resolvable WeaponView (every enemy attack, or a player shot whose owner view does not
        // exist - e.g. already disconnected). See the class comment above for why this is preferred
        // over the simulation's own Projectile.SpawnPosition. Public for ProjectileGhostTrailManager,
        // which needs the same muzzle for a shot that never had a view.
        public static Transform ResolveMuzzleTransform(EntityRef owner)
        {
            if (owner == EntityRef.None)
                return null;

            if (_entityViewUpdater == null)
                _entityViewUpdater = FindFirstObjectByType<QuantumEntityViewUpdater>();

            if (_entityViewUpdater == null)
                return null;

            QuantumEntityView ownerView = _entityViewUpdater.GetView(owner);
            if (ownerView == null)
                return null;

            WeaponViewController weaponController = ownerView.GetComponentInChildren<WeaponViewController>();
            WeaponView weaponView = weaponController != null ? weaponController.CurrentWeaponView : null;

            return weaponView != null ? weaponView.MuzzleTransform : null;
        }

        // Generic Damage Echo hook (Kai's Ghost Shot, Neutral Mastery R3, is the first source) - any
        // projectile entity carrying an EchoProjectile component (see DamageEcho.qtn/DamageEchoSystem.
        // SpawnEchoProjectile) gets its own Visual's EffectPrefab (DamageEchoVisualData.View.cs)
        // instantiated as a child of the visual root, BEFORE Detach hands root off to
        // ProjectileVisualController - so it is picked up by that controller's own
        // GetComponentsInChildren<ParticleSystem> scan and gets exactly the same treatment as every
        // other particle already living under this prefab: cleared once the visual teleports onto the
        // muzzle (no streak across that jump), hidden while RemainingSpawnDelay hasn't elapsed, and
        // torn down along with the rest of the visual on impact/expiry - no bespoke lifecycle code
        // needed here. No-op for a normal shot (no EchoProjectile component on the entity) or an echo
        // whose Visual is unassigned/has no EffectPrefab configured yet. Returns the instantiated
        // particle (or null) so the caller can hand it to ProjectileVisualController.Settings.
        // EchoGhostParticle - without that it would be cut off mid-emission by Finish's own
        // Destroy(gameObject) instead of fading out gracefully like TrailParticle does.
        private ParticleSystem AttachEchoGhostParticle(Frame frame, Transform root)
        {
            if (frame == null || frame.TryGet<EchoProjectile>(_entityRef, out var echo) == false || echo.Visual.IsValid == false)
                return null;

            DamageEchoVisualData visual = frame.FindAsset(echo.Visual);
            if (visual == null || visual.EffectPrefab == null)
                return null;

            ParticleSystem ghost = Instantiate(visual.EffectPrefab, root);
            ghost.transform.localPosition = Vector3.zero;
            ghost.transform.localRotation = Quaternion.identity;

            // Explicit rather than relying on the prefab's own Play On Awake - this should always
            // start the moment it is parented on, regardless of how that field happens to be set on
            // whatever particle an artist drops into DamageEchoVisualData.EffectPrefab.
            ghost.Play();

            return ghost;
        }

        // The bullet mesh is a child in every projectile prefab here, but not always the FIRST one
        // (an enemy shot leads with a Light child), so this picks by what actually renders rather
        // than by index.
        private Transform ResolveVisualRoot()
        {
            if (visualRoot != null)
                return visualRoot;

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponentInChildren<Renderer>(includeInactive: true) != null)
                    return child;
            }

            return null;
        }
    }
}
