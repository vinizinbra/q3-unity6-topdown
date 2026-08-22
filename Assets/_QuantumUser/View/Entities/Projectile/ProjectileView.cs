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
    // So on spawn this DETACHES visualRoot from this GameObject, puts it back on the simulation's
    // own Projectile.SpawnPosition (the muzzle), and hands it to a ProjectileVisualController that
    // chases this entity's interpolated position at catchUpSpeedMultiplier x the projectile's real
    // speed until it converges. The deficit is paid back in around a tenth of a second, and the shot
    // reads correctly the whole way. A locally simulated shot (every enemy's, and this client's own)
    // has no deficit to pay back in the first place, so it just tracks its entity exactly.
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
        [SerializeField, Tooltip("Trail particle child that should keep playing/fading out where the shot landed instead of being cut off mid-emission. Must be a separate child under visualRoot, not on this same GameObject. Leave empty if this projectile has no trail.")]
        private ParticleSystem trailParticle;
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
                spawnPosition = projectile.SpawnPosition.ToUnityVector3();

            var settings = new ProjectileVisualController.Settings
            {
                CatchUpSpeedMultiplier = catchUpSpeedMultiplier,
                MinImpactDuration = minImpactDuration,
                MaxImpactDuration = maxImpactDuration,
                OrphanTimeout = orphanTimeout,
                DestroyEffectPrefab = destroyEffectPrefab,
                TrailParticle = trailParticle,
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
            if (_visual != null)
                _visual.NotifyEntityGone();

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
