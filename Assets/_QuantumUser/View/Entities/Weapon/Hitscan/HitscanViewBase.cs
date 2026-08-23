using System.Collections.Generic;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // One segment of a hitscan shot, as handed to a view style. A segment always begins exactly
    // where the previous one ended, which is what lets a view stitch a ricocheted shot back into one
    // continuous path without the simulation telling it anything more.
    public readonly struct HitscanSegment
    {
        public readonly Vector3 Origin;
        public readonly Vector3 EndPoint;

        // Hit ANYTHING - a wall counts. Target is what it actually landed on, None for a miss or for
        // level geometry, so HitEnemy is the one to gate an enemy-only impact effect on.
        public readonly bool DidHit;
        public readonly bool HitEnemy;
        public readonly EntityRef Target;

        public HitscanSegment(Vector3 origin, Vector3 endPoint, bool didHit, bool hitEnemy, EntityRef target)
        {
            Origin = origin;
            EndPoint = endPoint;
            DidHit = didHit;
            HitEnemy = hitEnemy;
            Target = target;
        }

        public Vector3 Direction
        {
            get
            {
                Vector3 delta = EndPoint - Origin;
                return delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;
            }
        }

        public float Length => Vector3.Distance(Origin, EndPoint);
    }

    // Base for the interchangeable ways a hitscan weapon draws its shot. Add exactly ONE style to a
    // weapon's own visual prefab, alongside WeaponView:
    //   - LineRendererHitscanView - an instant line per segment, fading out.
    //   - ParticleHitscanView     - a fast particle that travels the path, chaining bounces.
    //   - ContinuousHitscanView   - one persistent beam, for a weapon that fires every tick.
    //
    // WeaponSystem raises one EventHitscanFired per SEGMENT: one per pellet of a volley, plus one
    // more per Ricochet bounce (see FireHitscanPellet). Everything arrives in path order within the
    // same frame, so a view that wants to animate along a bounced path has the whole path in hand
    // before it draws the first leg of it.
    //
    // Effect templates are DISABLED CHILDREN of this weapon's prefab rather than project prefab
    // assets - an artist authors the effect where they can see it in context, and there is no
    // separate asset per weapon to keep in sync. Acquire instantiates copies on demand, UNPARENTED,
    // and reuses them by activity: the weapon's own transform is billboard-rotated, Y-flipped and
    // non-uniformly scaled every frame (see WeaponView.ApplyAim's own comments on the shear that
    // causes), while every position a hitscan visual works with is already in world space. Nothing
    // is ever re-parented back, so the pool also has to be torn down by hand - see OnDestroy.
    public abstract class HitscanViewBase : CustomQuantumEntityViewComponent
    {
        [Header("References")]
        [SerializeField, Tooltip("Where a shot is drawn FROM - the barrel tip. Parent this under the weapon's own visual so it rotates, flips and moves with the gun; its position is read LIVE, at the moment the segment arrives, not off the simulation. Leave empty to draw from the simulation's own spawn position exactly as before, which is where the shot really came from but is a tick old and sits at the weapon HOLD offset rather than the visible barrel (see EventHitscanFired.Origin / StatUtility.GetWeaponHoldOffset).")]
        private Transform muzzle;

        // Every copy Acquire has ever handed out, across all of a style's pools - the styles keep
        // their own typed lists for the free-instance lookup, this one exists purely to own their
        // lifetime, since nothing else can: they are unparented (see Acquire).
        private readonly List<Component> instances = new List<Component>();

        // Segments of one path are contiguous to the float, since a continuation's origin IS the
        // previous segment's endpoint carried through the same FPVector3 -> Vector3 conversion - true
        // of both kinds (a Ricochet bounce, and a pierce that carries on level from the enemy it just
        // went through; see WeaponSystem.FireHitscanPellet). Compared with a small tolerance anyway
        // rather than exactly, so nothing hinges on that staying true.
        protected const float ChainTolerance = 0.01f;

        // The live barrel tip, or this component's own transform when no muzzle is assigned - it is
        // already parented under the weapon socket, so it at least moves with the player either way.
        protected Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

        // The same fallback as MuzzlePosition, as a Transform - for anything that has to FOLLOW the
        // barrel across frames rather than sample it once (a held muzzle sound, see
        // ContinuousHitscanView.fireLoop) and so cannot use a position snapshot.
        protected Transform MuzzleTransform => muzzle != null ? muzzle : transform;

        // Where the previous segment ENDED, in the simulation's own coordinates (never the
        // substituted muzzle position - see OnHitscanFired) - the only thing that distinguishes a
        // continuation of a path from the opening leg of a new one.
        private Vector3 lastSegmentEnd;
        private bool hasLastSegment;

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventHitscanFired>(this, OnHitscanFired);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);

            // Pooled copies live unparented at the scene root, so destroying this weapon (a hero
            // swapping guns via WeaponViewController.SpawnWeaponView, the player dying, a
            // disconnect) would otherwise strand every one of them there for the rest of the match.
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] != null)
                    Destroy(instances[i].gameObject);
            }

            instances.Clear();
        }

        // This weapon can be switched off out from under a shot in flight: WeaponViewController
        // hides the entire weapon socket - and therefore the gun and everything on it - for as long
        // as its owner is Downed or KO'd (see docs/revive.md). Pooled copies are unparented, so they
        // do NOT go inactive with it: a continuous beam would hang in the air for the whole down
        // state, and a travelling particle would freeze mid-flight. Everything goes back in the pool
        // instead, and the next shot re-acquires from it.
        private void OnDisable()
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] == null)
                    continue;

                // Both targets: a style may be tweening the component (a line's color fade) or the
                // GameObject (a particle's return-to-pool delay). A stopped tween never fires its
                // OnComplete, so neither can reach back in and touch a reused instance later.
                Tween.StopAll(instances[i]);
                Tween.StopAll(instances[i].gameObject);

                instances[i].gameObject.SetActive(false);
            }

            hasLastSegment = false;

            OnInstancesReturned();
        }

        // Per-shot state a style is holding across frames (a half-flown path, a beam's points) is
        // no longer valid once its instances have gone back in the pool.
        protected virtual void OnInstancesReturned()
        {
        }

        // One EventHitscanFired = one segment. Filtered to this weapon's own shooter, same as every
        // other event WeaponView listens for.
        private void OnHitscanFired(EventHitscanFired e)
        {
            if (e.Owner != _entityRef)
                return;

            bool hitEnemy = e.Target != EntityRef.None
                && e.Game != null
                && e.Game.Frames.Predicted != null
                && e.Game.Frames.Predicted.Has<Enemy>(e.Target) == true;

            Vector3 origin = e.Origin.ToUnityVector3();
            Vector3 endPoint = e.EndPoint.ToUnityVector3();

            // The OPENING leg of a path is redrawn from the live muzzle; every later leg (a Ricochet
            // bounce, a pierce carrying on level) is a resolved world position and must stay exactly
            // where the simulation put it. Which one this is comes from contiguity, not a flag: a
            // continuation always begins where the previous segment ended, and the first leg of a
            // shot - or of the next pellet of a volley - begins back at the weapon.
            //
            // Why substitute at all: the simulation's own spawn position is a tick old and sits at
            // the weapon HOLD offset (StatUtility.GetWeaponHoldOffset), not at the visible barrel, so
            // a tracer drawn from it starts slightly off the gun and visibly drags behind it while
            // the player runs. Only done when a muzzle is actually assigned, so an unauthored view
            // draws exactly what it drew before this existed.
            bool continues = hasLastSegment == true
                && (lastSegmentEnd - origin).sqrMagnitude <= ChainTolerance * ChainTolerance;

            lastSegmentEnd = endPoint;
            hasLastSegment = true;

            if (continues == false && muzzle != null)
                origin = muzzle.position;

            OnSegment(new HitscanSegment(origin, endPoint, e.DidHit, hitEnemy, e.Target));
        }

        protected abstract void OnSegment(in HitscanSegment segment);

        // A template must never render in place - it lives in the prefab purely to be copied. Forced
        // off here rather than trusted to authoring, since an accidentally-enabled one shows up as a
        // permanent effect stuck to the muzzle.
        protected static void PrepareTemplate(Component template)
        {
            if (template != null)
                template.gameObject.SetActive(false);
        }

        // Reuses the first inactive copy, otherwise grows the pool by one. Deliberately keyed on
        // activeSelf rather than a per-style "am I done" flag: every style here finishes by
        // deactivating its instance, and that keeps the pool honest even if a tween or coroutine
        // driving one is cut short.
        protected T Acquire<T>(T template, List<T> pool) where T : Component
        {
            if (template == null)
                return null;

            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (pool[i] == null)
                {
                    pool.RemoveAt(i);
                    continue;
                }

                if (pool[i].gameObject.activeSelf == false)
                {
                    pool[i].gameObject.SetActive(true);
                    return pool[i];
                }
            }

            T instance = Instantiate(template, null);
            instance.gameObject.SetActive(true);
            pool.Add(instance);
            instances.Add(instance);
            return instance;
        }

        // How long a one-shot particle needs before it can go back in the pool. Derived from the
        // system itself so an artist retuning the effect doesn't also have to remember to retune a
        // duration field next to it.
        protected static float ResolveParticleLifetime(ParticleSystem particle)
        {
            if (particle == null)
                return 0f;

            var main = particle.main;

            // Floored rather than trusted: a system authored with a 0 duration and a 0 lifetime
            // would otherwise schedule a zero-length delay and pool the instance before it has drawn
            // a single frame.
            return Mathf.Max(main.duration + main.startLifetime.constantMax, 0.05f);
        }

        // Makes a scrolling beam material (Project/Scrolling Beam) read the same on a point-blank
        // shot as on one across the map. A LineRenderer's default Stretch texture mode maps the
        // texture exactly ONCE over the whole line, so a scroll authored to look right at 30m crawls
        // at 3m and a tiled pattern never repeats at all; Tile instead repeats it per world unit,
        // which is what keeps the flow rate constant. tilesPerUnit is that density.
        //
        // Set on the LINE, never on its material: assigning Material.mainTextureScale on a pooled
        // copy instances the material for that copy alone, so every live tracer ends up with its own
        // material to bind, for a value the renderer itself can already carry. The scroll itself
        // stays entirely in the shader - nothing here, or anywhere else, writes a material property
        // per frame.
        //
        // 0 leaves whatever the template was authored with, so a plain solid-color tracer is
        // untouched.
        protected static void ApplyTextureTiling(LineRenderer line, float tilesPerUnit)
        {
            if (line == null || tilesPerUnit <= 0f)
                return;

            // Both guarded: either assignment re-marks the line's geometry dirty, and this runs on
            // every segment of every shot - a fast multi-pellet weapon is a lot of segments.
            if (line.textureMode != LineTextureMode.Tile)
                line.textureMode = LineTextureMode.Tile;

            if (Mathf.Approximately(line.textureScale.x, tilesPerUnit) == false)
                line.textureScale = new Vector2(tilesPerUnit, 1f);
        }

        protected static void PlayOneShot(ParticleSystem instance, Vector3 position, Vector3 forward)
        {
            if (instance == null)
                return;

            instance.transform.SetPositionAndRotation(position,
                forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity);

            instance.Clear(true);
            instance.Play(true);
        }
    }
}
