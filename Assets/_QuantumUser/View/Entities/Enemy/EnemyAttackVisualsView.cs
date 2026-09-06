using Photon.Deterministic;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Drives an enemy's attack-phase visuals (body animation steps + particles + ground
    // telegraph) off Enemy.Phase edges and the enemy's own EnemyActionData - decides *when*/*which*,
    // EnemyBlobAnimationView still owns *how* to render a body-animation step (see
    // EnemyBlobAnimationView.PlayAttackStep). Since EnemyActionData is a shared, reusable asset,
    // this reads its configuration fresh off the frame every relevant edge rather than caching
    // anything at Initialize - two enemies sharing the same EnemyActionData get identical visuals
    // with zero per-prefab setup beyond wiring this component up once.
    //
    // Parented particles / the ground telegraph are tracked as single "current instance" slots
    // (not a list) - these phases play sequentially within one attack, never overlapping, so one
    // slot per concept is enough; a new one replaces whatever's still around from the previous edge.
    //
    // Most telegraphs are a fixed decal, positioned once at spawn - but QUpdate also re-poses an
    // active TelegraphData.LiveTracking telegraph every single frame (not just on phase edges,
    // unlike everything else here), for one that needs to visibly track a moving target through
    // the whole windup, e.g. a sniper's laser sight. See ComputeTelegraphPose/UpdateLiveTelegraph.
    public class EnemyAttackVisualsView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Sibling EnemyBlobAnimationView this component drives PlayAttackStep() calls on.")]
        private EnemyBlobAnimationView blobAnimationView;
        [SerializeField, Tooltip("Optional - sibling EnemyArmAimView on shooter enemies, fired the same tick the attack's Begin phase fires (i.e. the moment it actually shoots). Leave empty for enemies with no continuous-aim gun.")]
        private EnemyArmAimView armAimView;
        // This component lives on the generic enemy prototype (shared across enemy types), not on
        // EnemyDataAsset.ViewPrefab - the rig only exists once EnemyView.SpawnSprite instantiates
        // that prefab at runtime, so it can't be an Inspector-wired SerializeField. Optional -
        // provides Gun, used to parent a Parented step particle (see SpawnStepParticle) onto the
        // weapon instead of the entity root, so e.g. a muzzle/charge effect tracks aim. Stays null
        // (falls back to this component's own transform) if EnemyView.SpawnSprite's ViewPrefab has
        // no Gun wired on its EnemyViewRig.
        private EnemyViewRig rig;

        public void SetRig(EnemyViewRig rig) => this.rig = rig;

        // How far above/below a telegraph's computed position to search for real ground via
        // Physics.Raycast - generous enough to cover any realistic gap between the simulation's
        // idea of ground height and the actual Unity collider (see SnapToGround).
        private const float GroundSnapRayHeight = 20f;

        // Lazily computed, not a static field initializer - UnityEngine.LayerMask.GetMask (via
        // NameToLayer) isn't allowed to run in a MonoBehaviour's static constructor, only from
        // Awake/Start onward. Same lazy-cache shape as EnemyMovementUtility.GetGroundLayerMask/
        // GetPlayerLayerMask (Quantum's own, separate layer system - see feedback memory on the
        // two systems sharing layer names).
        private static int? _groundLayerMask;

        private static int GroundLayerMask
        {
            get
            {
                _groundLayerMask ??= UnityEngine.LayerMask.GetMask("Ground");
                return _groundLayerMask.Value;
            }
        }

        private EnemyActionPhase? _lastEnemyPhase;
        // See QUpdate's windupRestarted check - StateTimer counts down within one windup, so an
        // increase while still sampled as Preparation/Telegraph both times means a brand new windup
        // started without ever being observed as "not winding up" in between.
        private FP? _lastStateTimer;
        private ParticleSystem _currentAnticipationIcon;
        // Real time (Time.time), not simulation ticks - EffectsManager.MinimumAnticipationIconDuration
        // is a wall-clock readability floor, so it has to be measured the same way. See
        // RequestClearAnticipationIcon/UpdateAnticipationIcon.
        private float _anticipationIconSpawnTime;
        private bool _anticipationIconPendingClear;
        private GameObject _currentParentedParticle;
        private GameObject _currentTelegraph;
        private GameObject _currentTelegraphPrefab;
        private AttackPhase? _activeTelegraphStartPhase;
        // Only set while _currentTelegraph has LiveTracking enabled - see UpdateLiveTelegraph.
        // _liveTelegraphDamageRange/_liveTelegraphOriginIsSelf are cached at spawn time rather than
        // re-read from actionData every live-tracking frame - UpdateLiveTelegraph runs before
        // actionData is resolved in QUpdate, and neither DamageRange nor Origin changes over an
        // action's lifetime anyway.
        private TelegraphData _liveTelegraphData;
        private bool _liveTelegraphIgnoreY;
        private float _liveTelegraphDamageRange;
        private bool _liveTelegraphOriginIsSelf;

        public override void Awake()
        {
            base.Awake();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            ClearAnticipationIcon();
            ClearParentedParticle(instant: true);
            ClearTelegraph(instant: true);

            // Guards against ViewPrefabPool reuse - if this entity is torn down mid-attack, make
            // sure the pooled rig goes back to its rest sprite before some other enemy's SetRig
            // caches it as their own "default" (see EnemyBlobAnimationView._defaultBodySprite).
            if (blobAnimationView != null)
                blobAnimationView.ResetBodySprite();
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (blobAnimationView == null)
                return;

            Frame frame = game.Frames.Predicted;

            if (frame.Has<Enemy>(_entityRef) == false)
                return;

            Enemy enemy = frame.Get<Enemy>(_entityRef);
            EnemyActionPhase enemyPhase = enemy.Phase;
            EnemyDataAsset enemyData = frame.FindAsset(enemy.EnemyData);

            // Runs every frame regardless of phase edges, unlike everything below - a live
            // telegraph (e.g. a sniper's laser sight) needs to visibly track a moving target
            // throughout the whole windup, not just re-render once when a phase transition is
            // observed.
            UpdateLiveTelegraph(frame, enemy, enemyData);

            // Same "every frame, not just phase edges" reasoning as UpdateLiveTelegraph just above -
            // the enemy can keep moving throughout its own windup, so a position captured once at
            // SpawnAnticipationIcon would visibly detach from a moving head. No-ops instantly
            // whenever no icon is currently held (the common case, most frames).
            UpdateAnticipationIcon(frame);

            EnemyActionPhase? lastPhase = _lastEnemyPhase;
            FP? lastStateTimer = _lastStateTimer;
            _lastEnemyPhase = enemyPhase;
            _lastStateTimer = enemy.StateTimer;

            bool wasWindingUpSample = lastPhase == EnemyActionPhase.Preparation || lastPhase == EnemyActionPhase.Telegraph;
            bool isWindingUpSample = enemyPhase == EnemyActionPhase.Preparation || enemyPhase == EnemyActionPhase.Telegraph;

            // Quantum can advance many simulation ticks between two Unity frames (see
            // attackNoLongerActive's own comment below for the symmetric case) - if BOTH samples land
            // in the Preparation/Telegraph window, that's not necessarily the same windup: the whole
            // Recovery -> Cooldown -> Chasing -> next Preparation cycle can just as easily be skipped
            // between two frames as Active -> Recovery can. StateTimer counts DOWN monotonically
            // within one windup (EnemySystem.UpdatePreparation) - if the last sample was already
            // winding up and this one still is, but StateTimer went UP instead of down, a brand new
            // windup started in between and this is genuinely a different attack, not a continuation
            // of the one already tracked. Left undetected, lastPhase == enemyPhase (or both land in
            // the same Preparation/Telegraph bucket) keeps looking like "nothing changed" forever, so
            // none of the spawn/clear edges below ever fire again for the new attack - the stale
            // telegraph (and pre-shoot pose/anticipation icon) from the FIRST windup just keep
            // getting silently re-aimed at the second one's pose instead, which is what made
            // UpdateLiveTelegraph's own Lerp/Slerp visibly slide the old telegraph into the new one's
            // position/rotation rather than it snapping fresh.
            bool windupRestarted = wasWindingUpSample == true && isWindingUpSample == true
                && lastStateTimer.HasValue == true && enemy.StateTimer > lastStateTimer.Value;

            if (windupRestarted == true)
            {
                if (_currentTelegraph != null)
                    ClearTelegraph(instant: true);

                if (armAimView != null)
                    armAimView.StopPreShoot();

                RequestClearAnticipationIcon();
            }

            if (windupRestarted == false && (lastPhase.HasValue == false || lastPhase == enemyPhase))
                return; // only react on actual phase changes (or a detected windup restart) - nothing else below needs mid-phase reactions

            EnemyActionData actionData = EnemyDecisionUtility.ResolveAction(frame, enemyData, enemy.CurrentActionSlot);

            if (actionData == null)
                return;

            TelegraphData telegraph = actionData.Telegraph.IsValid == true ? frame.FindAsset(actionData.Telegraph) : null;

            // Preparation and Telegraph are one logical "windup" bucket sharing a single timer
            // (see EnemyActionPhase's own comment) - treated as such here too, not just checked
            // for Preparation specifically. TelegraphStartPercent < 1 (the default since it stopped
            // being a no-op) means the phase spends its second half as Telegraph, not Preparation,
            // so by the time Begin()/End actually fires, lastPhase is typically Telegraph already -
            // checking only for Preparation here missed that and silently killed BeginStep/EndStep.
            // (wasWindingUp/isWindingUp are the same buckets as wasWindingUpSample/isWindingUpSample
            // above, just named to match this section's own established comments below.)
            bool wasWindingUp = wasWindingUpSample;
            bool isWindingUp = isWindingUpSample;

            // Also fires on a detected windupRestarted, on top of the normal not-winding-up ->
            // winding-up edge - see that variable's own comment for why a restart needs to be
            // treated exactly like a fresh entry (respawns the telegraph/pre-shoot pose/icon that
            // windupRestarted's own block above just force-cleared).
            bool enteredAnticipating = (isWindingUp == true && wasWindingUp == false) || windupRestarted == true;

            // Fires exactly once, on whichever tick windup actually ends - covers both the normal
            // path (windup -> Begin, the shot fires) AND every interrupted path (the enemy dies or
            // its target is lost mid-windup, skipping Begin entirely) in one edge, rather than only
            // stopping PlayPreShoot's particle alongside Fire() and leaking it forever whenever an
            // attack gets cut short instead of actually firing.
            bool exitedAnticipating = wasWindingUp == true && isWindingUp == false;

            // Includes Dead alongside Recovery/Active - a delivery can kill its own enemy while
            // resolving (e.g. GroundAreaDeliveryData.SelfDestructs), which skips Recovery/Active
            // entirely (Begin() sets Phase = Dead directly, and EnemySystem.EnterRecovering
            // deliberately won't override that - see its own comment), so windup -> Dead is also a
            // legitimate "Begin fired" edge, not just windup -> Recovery/Active. Trade-off worth
            // knowing: this can't distinguish that case from an enemy that happens to take lethal
            // damage from something else mid-windup (unrelated to its own attack) - both look
            // identical from Phase sampling alone, so that rarer case will also (harmlessly, since
            // EnemyBlobAnimationView's own death pose takes over immediately after) play BeginStep's
            // visuals once. Judged worth it since not firing here at all was the confirmed, common
            // bug for any self-destructing delivery.
            bool firedBegin = wasWindingUp == true && (enemyPhase == EnemyActionPhase.Recovery || enemyPhase == EnemyActionPhase.Active || enemyPhase == EnemyActionPhase.Dead);
            bool enteredOnGoing = enemyPhase == EnemyActionPhase.Active;
            bool firedEnd = enemyPhase == EnemyActionPhase.Recovery && (lastPhase == EnemyActionPhase.Active || wasWindingUp == true);

            // Spawned/Destroyed - see AttackPhase's own comment. Enemy.SkillProjectile is only
            // ever set inside an attack's own Begin() (same tick as firedBegin) and only ever
            // observed destroyed inside Tick() right before it reports finished (same tick as
            // firedEnd) - gating on those keeps this exact and keeps it from firing for every
            // other attack type, which never touches SkillProjectile at all (stays
            // EntityRef.None forever).
            bool projectileSpawned = firedBegin == true && enemy.SkillProjectile != EntityRef.None && frame.Exists(enemy.SkillProjectile) == true;
            bool projectileDestroyed = firedEnd == true && enemy.SkillProjectile != EntityRef.None && frame.Exists(enemy.SkillProjectile) == false;

            if (enteredAnticipating == true)
            {
                PlayPhase(frame, enemy, enemyData, actionData.AnticipationStep, telegraph, actionData.IgnoreY, actionData.DamageRange.AsFloat, actionData.Origin == EnemyActionOrigin.Self, AttackPhase.Anticipation);

                if (armAimView != null)
                    armAimView.PlayPreShoot();

                SpawnAnticipationIcon(frame);
            }

            if (exitedAnticipating == true)
            {
                if (armAimView != null)
                    armAimView.StopPreShoot();

                RequestClearAnticipationIcon();
            }

            if (firedBegin == true)
            {
                PlayPhase(frame, enemy, enemyData, actionData.BeginStep, telegraph, actionData.IgnoreY, actionData.DamageRange.AsFloat, actionData.Origin == EnemyActionOrigin.Self, AttackPhase.Begin);

                // Not gated on projectileSpawned/SkillProjectile below - SkillProjectile is only
                // ever assigned for a ProjectileDeliveryData with WaitForImpact=true (the "stand
                // and watch it land" case); a normal quick shot (WaitForImpact=false, the default)
                // spawns its projectile and resolves immediately without ever touching
                // SkillProjectile, so gating Fire() on that condition meant it silently never ran
                // for the common case. firedBegin fires exactly once per attack regardless of
                // delivery/WaitForImpact, matching the actual "the gun just went off" moment.
                if (armAimView != null)
                    armAimView.Fire();
            }

            if (enteredOnGoing == true)
                PlayPhase(frame, enemy, enemyData, actionData.OnGoingStep, telegraph, actionData.IgnoreY, actionData.DamageRange.AsFloat, actionData.Origin == EnemyActionOrigin.Self, AttackPhase.OnGoing);

            if (firedEnd == true)
                PlayPhase(frame, enemy, enemyData, actionData.EndStep, telegraph, actionData.IgnoreY, actionData.DamageRange.AsFloat, actionData.Origin == EnemyActionOrigin.Self, AttackPhase.End);

            if (projectileSpawned == true)
                PlayPhase(frame, enemy, enemyData, null, telegraph, actionData.IgnoreY, actionData.DamageRange.AsFloat, actionData.Origin == EnemyActionOrigin.Self, AttackPhase.Spawned);

            if (projectileDestroyed == true)
                PlayPhase(frame, enemy, enemyData, null, telegraph, actionData.IgnoreY, actionData.DamageRange.AsFloat, actionData.Origin == EnemyActionOrigin.Self, AttackPhase.Destroyed);

            // Safety net: firedEnd/projectileDestroyed require observing enemyPhase ==
            // Recovery on some QUpdate sample, but Quantum can advance several simulation
            // ticks between two Unity frames - a short DownTime can mean Active -> Recovery
            // -> Chasing all happen between samples, so Recovery is never actually seen and the
            // specific EndPhase edge above never fires, leaving the telegraph stuck showing
            // forever. Once the enemy is observed to be anything other than Preparation/Telegraph/
            // Active, the attack is unambiguously over regardless of which discrete phases got
            // skipped in between - clear it here if the phase-specific path above didn't already.
            bool attackNoLongerActive = enemyPhase != EnemyActionPhase.Preparation
                && enemyPhase != EnemyActionPhase.Telegraph
                && enemyPhase != EnemyActionPhase.Active;

            // BodySprite is deliberately NOT force-reverted here, unlike the telegraph below - a
            // step's sprite should stay up for its own full authored Duration even if the attack
            // itself (Enemy.Phase) already wrapped up first, so EnemyBlobAnimationView's own
            // _bodySpriteTimeRemaining countdown is the only thing that reverts it (see
            // ApplyStepSprite/QUpdate there). DeInitialize below still force-reverts on teardown -
            // that's a pool-reuse safeguard, not a "cut the visual short" concern.
            if (attackNoLongerActive == true && _currentTelegraph != null)
            {
                ClearTelegraph();
            }
        }

        private void PlayPhase(Frame frame, Enemy enemy, EnemyDataAsset enemyData, AttackVisualStep step, TelegraphData telegraph, bool ignoreY, float damageRange, bool originIsSelf, AttackPhase phase)
        {
            if (step != null)
            {
                blobAnimationView.PlayAttackStep(step);
                blobAnimationView.ApplyStepSprite(step.BodySprite, step.Duration, step.BodySpriteOffset, step.BodySpriteScale);
                SpawnStepParticle(frame, enemy, step);
                TriggerStepShake(frame, enemy, step);
            }

            if (telegraph != null && ResolveTelegraphPrefab(telegraph) != null)
            {
                if (telegraph.StartPhase == phase)
                    SpawnTelegraph(frame, enemy, enemyData, telegraph, ignoreY, damageRange, originIsSelf);

                if (telegraph.EndPhase == phase && _activeTelegraphStartPhase.HasValue == true)
                    ClearTelegraph();
            }
        }

        // TelegraphData.TelegraphPrefab is an explicit per-asset override; leaving it unset falls
        // back to TelegraphManager's per-shape default, so authoring a new TelegraphData doesn't
        // require re-dragging the same prefab in every time (see TelegraphManager.shapeDefaults).
        private static GameObject ResolveTelegraphPrefab(TelegraphData telegraph)
        {
            if (telegraph.TelegraphPrefab != null)
                return telegraph.TelegraphPrefab;

            return TelegraphManager.Instance != null && TelegraphManager.Instance.TryGetDefaultPrefab(telegraph.Shape, out GameObject prefab) == true
                ? prefab
                : null;
        }

        // Ground level directly beneath the enemy - ResolveDestination (EnemyMovementUtility) adds
        // FlightHeight on top of a target's position for Flying enemies to resolve a chase/attack
        // destination; this undoes exactly that offset on the enemy's own position, so a Flying
        // enemy's line telegraph starts at the ground it's hovering over instead of at its actual
        // (elevated) Transform3D height.
        private static Photon.Deterministic.FPVector3 GetGroundPosition(EnemyDataAsset enemyData, Photon.Deterministic.FPVector3 position)
        {
            if (enemyData.Stats.Height.InitialState == EnemyHeightState.Flying)
                position.Y -= enemyData.Stats.Height.FlightHeight;

            return position;
        }

        // Final visual correction: the simulation's idea of ground height (Quantum's deterministic
        // FP raycasts against baked static colliders) doesn't necessarily match the Unity-rendered
        // ground mesh/collider exactly - this makes sure the telegraph decal always actually sits
        // on the real Unity ground regardless of any small mismatch. Real UnityEngine.Physics
        // raycast, not Quantum's - purely a view-layer placement fix, no simulation involvement.
        // Leaves position.y untouched if nothing on the Ground layer is found beneath/above it.
        private static Vector3 SnapToGround(Vector3 position)
        {
            Vector3 rayOrigin = position + Vector3.up * GroundSnapRayHeight;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, GroundSnapRayHeight * 2f, GroundLayerMask))
                position.y = hit.point.y;

            return position;
        }

        // Prefers the captured skill-target point - EnemySystem.UpdateChasing freshly captures
        // this the instant the enemy commits to Preparation (before Begin() even runs), and
        // deliveries that need a more precise point (e.g. ChargeDeliveryData's DashDistance-clamped
        // endpoint) overwrite it themselves once Begin() fires - so this is always current, never
        // stale, for every phase including Preparation. Falls back to the target's live position
        // only if there's no target at all (shouldn't normally happen once Preparation starts).
        private bool TryGetAnchorPosition(Frame frame, Enemy enemy, out Photon.Deterministic.FPVector3 position)
        {
            if (enemy.SkillTargetPosition != default)
            {
                position = enemy.SkillTargetPosition;
                return true;
            }

            return EnemyMovementUtility.TryGetTargetPosition(frame, enemy.Target, out position);
        }

        // GetAnticipationIconInstance/ReleaseAnticipationIconInstance are thin wrappers on
        // EffectsManager around its own GetHeldInstance/ReleaseHeldInstance - the same shape
        // EnemyAllyLinkView already established for its own tether-endpoint particles, just bound to
        // EffectsManager.anticipationIconEffectPrefab instead of a SerializeField living here, so the
        // actual asset is configured alongside every other combat VFX. The returned instance is
        // parented onto this enemy's own transform (worldPositionStays: false, so it doesn't visibly
        // pop at wherever the pool last released it) so it tracks enemy movement for free -
        // UpdateAnticipationIcon still repositions it every frame, since the enemy's own Unity
        // transform never rotates (2D top-down; facing comes from Aim.Angle, not transform.rotation -
        // see ResolveEnemyDirectionRotation), so the offset itself still needs re-deriving from the
        // enemy's current facing even though position tracking is now free via parenting. Billboard,
        // on the prefab itself, overwrites its world rotation every LateUpdate regardless, so it
        // stays camera-facing with no rotation math needed here. Degrades to silently showing nothing
        // if EffectsManager isn't in the scene or its prefab is unassigned.
        private void SpawnAnticipationIcon(Frame frame)
        {
            if (EffectsManager.Instance == null)
                return;

            ClearAnticipationIcon();

            _currentAnticipationIcon = EffectsManager.Instance.GetAnticipationIconInstance();

            if (_currentAnticipationIcon != null)
                _currentAnticipationIcon.transform.SetParent(transform, worldPositionStays: false);

            _currentAnticipationIcon?.Play();
            _anticipationIconSpawnTime = Time.time;
            _anticipationIconPendingClear = false;
            UpdateAnticipationIcon(frame); // avoid a one-frame pop at wherever the pool last released it
        }

        // Deferred release, called from the exitedAnticipating edge instead of ClearAnticipationIcon
        // directly - EffectsManager.MinimumAnticipationIconDuration is a wall-clock readability floor
        // (a fast enemy's own AnticipationTime can otherwise end in a fraction of a second, flashing
        // the icon for a single frame), so a windup ending before that floor is reached leaves the
        // icon showing (and still tracking the head - UpdateAnticipationIcon owns the actual deferred
        // release once enough time has passed) rather than cutting it short.
        private void RequestClearAnticipationIcon()
        {
            if (_currentAnticipationIcon == null)
                return;

            float minimumDuration = EffectsManager.Instance != null ? EffectsManager.Instance.MinimumAnticipationIconDuration : 0f;

            if (Time.time - _anticipationIconSpawnTime >= minimumDuration)
            {
                ClearAnticipationIcon();
                return;
            }

            _anticipationIconPendingClear = true;
        }

        // Reparents back onto EffectsManager itself before release - a held instance is only
        // deactivated (not destroyed) by ReleaseHeldInstance, so leaving it parented to this enemy
        // would destroy it for good the moment this enemy's own view is torn down, silently poisoning
        // the pool with a dangling reference. worldPositionStays:true here (unlike the analogous
        // SetParent in SpawnAnticipationIcon, which deliberately wants false) - ReleaseHeldInstance
        // only stops NEW emission (ParticleSystemStopBehavior.StopEmitting), so already-alive
        // particles keep rendering, GameObject still active, until ReleaseWhenFinished's IsAlive()
        // poll finally goes false - worldPositionStays:false would instantly snap that still-visible
        // fade-out from over the enemy's head to wherever EffectsManager itself sits (its own
        // transform is never moved, i.e. world origin), reading as the icon teleporting to (0,0,0)
        // right as the windup ends. true keeps it fading out in place instead.
        private void ClearAnticipationIcon()
        {
            if (_currentAnticipationIcon != null && EffectsManager.Instance != null)
            {
                _currentAnticipationIcon.transform.SetParent(EffectsManager.Instance.transform, worldPositionStays: true);
                EffectsManager.Instance.ReleaseAnticipationIconInstance(_currentAnticipationIcon);
            }

            _currentAnticipationIcon = null;
            _anticipationIconPendingClear = false;
        }

        private void UpdateAnticipationIcon(Frame frame)
        {
            if (_currentAnticipationIcon == null)
                return;

            // A pending clear (windup ended before the minimum-duration floor) resolves here, the
            // one place that already runs every frame regardless of phase edges - once real time
            // catches up to the floor, release for real instead of continuing to reposition a
            // released-elsewhere instance.
            if (_anticipationIconPendingClear == true)
            {
                float minimumDuration = EffectsManager.Instance != null ? EffectsManager.Instance.MinimumAnticipationIconDuration : 0f;

                if (Time.time - _anticipationIconSpawnTime >= minimumDuration)
                {
                    ClearAnticipationIcon();
                    return;
                }
            }

            // Read fresh off EffectsManager every frame rather than cached at spawn - a designer
            // tweaking AnticipationIconOffset in the Inspector during Play Mode should see it move
            // immediately, same live-iteration expectation every other Inspector-tuned offset here has.
            Vector3 offset = EffectsManager.Instance != null ? EffectsManager.Instance.AnticipationIconOffset : Vector3.zero;

            // Scaled by the enemy's own live collider radius (post tier-scale, same call EnemyView
            // uses for its own fit-scale/widget-offset math) so one authored offset reads correctly
            // across every enemy size instead of a Filler and a Boss getting the exact same nudge.
            float radius = EnemyMovementUtility.ResolveEntityRadius(frame, _entityRef).AsFloat;
            Vector3 scaledOffset = offset * radius;

            // X only ever mirrors (left/right), not a full rotation - reuses
            // EnemyBlobAnimationView's own FacingSign (the exact sign its root transform's
            // localScale.x already flips by) rather than re-deriving facing here, so the icon can
            // never disagree with which way the enemy sprite itself is actually mirrored.
            scaledOffset.x *= blobAnimationView.FacingSign;

            Vector3 localPosition = ResolveHeadOffset() + scaledOffset;
            _currentAnticipationIcon.transform.SetLocalPositionAndRotation(localPosition, Quaternion.identity);
        }

        // Reads EnemyBlobAnimationView.HeadHeight, a baseline value cached once (before any attack
        // step ever squashes the rig) rather than measuring rig.ReferenceSprite.bounds.max.y live -
        // that renderer sits on root, which EnemyBlobAnimationView.ApplyPose actively
        // squashes/scales during attack steps (including the Anticipation step this icon exists
        // for), so a live read shrinks toward root's own base mid-windup and drags the icon down
        // to the enemy's feet instead of staying over its head. No radius-floor fallback (unlike
        // ResolveWidgetOffset) since a missing/unmeasurable sprite just means the icon renders at
        // the collider center instead of over an empty head - visible enough to notice and fix in
        // the Editor rather than silently wrong.
        private Vector3 ResolveHeadOffset()
        {
            if (blobAnimationView != null)
                return Vector3.up * blobAnimationView.HeadHeight;

            return Vector3.zero;
        }

        // Shared by SpawnStepParticle and TriggerStepShake - "where is this step actually
        // happening" is the same question for a particle spawn and a camera shake alike, so both
        // key off the step's own Anchor rather than each re-deriving it (self, or the resolved
        // SkillTargetPosition/live-target anchor).
        private bool TryResolveStepOrigin(Frame frame, Enemy enemy, AttackVisualStep step, out Photon.Deterministic.FPVector3 origin)
        {
            if (step.Anchor == ParticleAnchor.OnSelf)
            {
                origin = frame.Get<Transform3D>(_entityRef).Position;
                return true;
            }

            return TryGetAnchorPosition(frame, enemy, out origin);
        }

        // Distance-falloff camera shake for AttackVisualStep.ShakeImpact - see FollowCamera.
        // ShakeAtPosition. No-ops for 0 (the default, meaning "no shake authored on this step") or
        // if this step's own anchor can't currently be resolved (e.g. SkillTargetPosition with a
        // lost target).
        private void TriggerStepShake(Frame frame, Enemy enemy, AttackVisualStep step)
        {
            if (step.ShakeImpact <= 0f)
                return;

            if (FollowCamera.I == null)
            {
                LogHelper.Log("EnemyAttackVisualsView", $"TriggerStepShake: ShakeImpact={step.ShakeImpact} but FollowCamera.I is null.");
                return;
            }

            if (TryResolveStepOrigin(frame, enemy, step, out Photon.Deterministic.FPVector3 origin) == false)
            {
                LogHelper.Log("EnemyAttackVisualsView", $"TriggerStepShake: ShakeImpact={step.ShakeImpact} but couldn't resolve step origin (Anchor={step.Anchor}).");
                return;
            }

            LogHelper.Log("EnemyAttackVisualsView", $"TriggerStepShake: firing ShakeImpact={step.ShakeImpact} at {origin.ToUnityVector3()}.");
            FollowCamera.I.ShakeAtPosition(origin.ToUnityVector3(), step.ShakeImpact);
        }

        private void SpawnStepParticle(Frame frame, Enemy enemy, AttackVisualStep step)
        {
            // Every phase transition ends whatever parented particle (e.g. a charge trail) the
            // previous phase left running - phases play sequentially and never overlap (see class
            // comment), so a phase with no parented particle of its own (e.g. EndStep's one-shot
            // impact burst) must still stop the outgoing one instead of leaving it attached to the
            // enemy indefinitely.
            ClearParentedParticle();

            if (step.ParticlePrefab == null)
                return;

            if (TryResolveStepOrigin(frame, enemy, step, out Photon.Deterministic.FPVector3 anchorPosition) == false)
                return; // SkillTargetPosition but nothing valid to anchor to

            // Offset is authored relative to the enemy's own current facing (Z = forward along
            // Aim.Angle, X = to its right), not a raw world-space nudge - rotating it by the
            // enemy's full flat facing direction (any angle, not just a left/right mirror) before
            // adding it to the anchor is what keeps e.g. a muzzle offset on the correct side/in
            // front of the enemy regardless of which way it's actually facing.
            Quaternion directionRotation = ResolveEnemyDirectionRotation(frame);
            Vector3 worldPosition = anchorPosition.ToUnityVector3() + directionRotation * step.Offset;

            // AlignToEnemyDirection gives a base rotation matching the enemy's current facing
            // (same flat Aim.Angle convention EnemyBlobAnimationView/EnemyArmAimView already use);
            // RotationOffset then applies on top either way, so a purely fixed rotation is just
            // AlignToEnemyDirection=false with the desired Euler baked into RotationOffset.
            Quaternion baseRotation = step.AlignToEnemyDirection == true ? directionRotation : Quaternion.identity;
            Quaternion rotation = baseRotation * Quaternion.Euler(step.RotationOffset);

            // Scale multiplies the PREFAB's own authored scale (not whatever a previous pooled
            // instance happened to be scaled to), so "1 = unchanged" holds regardless of what the
            // prefab was actually authored at.
            Vector3 scale = step.ParticlePrefab.transform.localScale * step.Scale;

            // Parented only actually follows when anchored OnSelf - there's no readily-available
            // live Transform for an arbitrary target entity/point from here, so
            // Parented+SkillTargetPosition falls back to a fixed-position spawn (still shown, just
            // not tracking anything).
            if (step.Parented == true && step.Anchor == ParticleAnchor.OnSelf)
            {
                Transform parent = rig != null && rig.Gun != null ? rig.Gun : transform;
                GameObject instance = Instantiate(step.ParticlePrefab.gameObject, parent);
                instance.transform.SetLocalPositionAndRotation(step.Offset, rotation);
                instance.transform.localScale = scale;
                ApplySortingOrderOverride(instance, step);
                _currentParentedParticle = instance;
                return;
            }

            if (EffectsManager.Instance != null)
            {
                int? sortingOrder = step.OverrideSortingOrder == true ? step.SortingOrder : (int?)null;
                EffectsManager.Instance.PlayEffect(step.ParticlePrefab, worldPosition, rotation, scale, sortingOrder);
            }
        }

        // Covers the root's own ParticleSystemRenderer AND every child's (GetComponentsInChildren
        // includes inactive ones so a child that starts disabled still gets the override) - a
        // multi-emitter prefab (e.g. a burst plus a trailing ring on a child GameObject) needs every
        // sub-emitter forced to the same sorting order, not just the one on the root.
        private static void ApplySortingOrderOverride(GameObject instance, AttackVisualStep step)
        {
            if (step.OverrideSortingOrder == false)
                return;

            foreach (var renderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
                renderer.sortingOrder = step.SortingOrder;
        }

        // Same flat-direction formula as EnemyArmAimView.ResolveDefaultAimDirection's own Aim.Angle
        // fallback and EnemyBlobAnimationView.UpdateFacing - Aim.Angle is always flat (0 = world +Z,
        // increasing toward +X), so this only ever yields a horizontal facing, never a true 3D aim
        // direction. Falls back to identity (no rotation) if the entity has no Aim component at all.
        private Quaternion ResolveEnemyDirectionRotation(Frame frame)
        {
            if (frame.Has<Aim>(_entityRef) == false)
                return Quaternion.identity;

            float angleRad = frame.Get<Aim>(_entityRef).Angle.AsFloat * Mathf.Deg2Rad;
            Vector3 direction = new(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));
            return Quaternion.LookRotation(direction, Vector3.up);
        }

        // Assumes a SpriteRenderer-based prefab, which faces the camera by default like any 2D
        // sprite (its plane spans local X/Y, normal along local Z) - NOT lying flat. Using
        // Quaternion.LookRotation(Vector3.up, upTarget) rather than the usual
        // LookRotation(forward, up) swaps which local axis becomes the facing/normal one: local Z
        // ends up pointing world-up (so the sprite's own plane, X/Y, ends up lying horizontal -
        // flat on the ground, visible from above, not billboarded to camera), and local Y (the
        // sprite's own "up"/pointing edge, standard for a 2D directional sprite) ends up pointing
        // along whatever horizontal direction is passed as the second argument. Circle doesn't have
        // a meaningful facing direction (rotationally symmetric) so any horizontal reference works;
        // Cone/ChargeLane/Rectangle use the actual facing/target direction so the sprite's pointing
        // edge aims the right way. Cone's angular wedge shape comes entirely from the authored
        // sprite art (a filled sector), not procedural geometry - this only positions/rotates/
        // scales it uniformly like Circle, just anchored at the enemy instead of the target.
        // Rectangle reuses ChargeLane's exact box math (a rectangle and a "charge lane" are the
        // same decal, just different naming intent) - both are drawn with their long/pointing edge
        // along local X rather than Circle/Cone's local-Y convention, so their branch below adds a
        // compensating 90-degree twist and swaps which axis gets length vs Width. Unit-sized prefab
        // assumed (1x1 at scale 1, pivot centered). Returns false for any other TelegraphShape (see
        // that enum's own comment
        // for why they're declared but unimplemented) or if an anchor can't currently be resolved
        // (e.g. the target died) - shared by SpawnTelegraph (initial pose) and UpdateLiveTelegraph
        // (LiveTracking's per-frame re-pose) so the shape math only lives in one place.
        //
        // damageRange is the paired EnemyActionData.DamageRange (converted to float once by the
        // caller - PlayPhase/SpawnTelegraph pass plain floats all the way through, since this is
        // pure View-side rendering math with no simulation determinism concern). Circle/Cone always
        // derive their radius from it (damageRange * TelegraphData.RadiusMultiplier - see that
        // field's own comment) rather than an independently authored radius, so the decal can never
        // silently drift out of sync with the real hit area.
        //
        // originIsSelf mirrors EnemyActionData.Origin (again converted once by the caller) - Circle
        // reads it to decide whether it's centered on the locked target anchor or the enemy's own
        // position, the exact same choice GroundAreaDeliveryData.Begin() makes for the real hit
        // detection, so the decal and the actual damage area can never disagree about where the
        // attack is centered. Cone ignores it and stays hardcoded to the enemy's own position - see
        // EnemyActionData.Origin's own comment on why a cone centered on the point it's already
        // centered on has no sensible pointing direction.
        private bool ComputeTelegraphPose(Frame frame, Enemy enemy, EnemyDataAsset enemyData, TelegraphData telegraph, bool ignoreY, float damageRange, bool originIsSelf, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = default;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            // Ground-projected (FlightHeight subtracted for a Flying enemy) when this telegraph is
            // a floor decal - a telegraph is normally a floor decal, it shouldn't appear to float.
            // TelegraphData.SnapToGround = false opts out for an attack that happens at altitude
            // instead (a flying charge/dash, a flying sniper's shot) - uses the enemy's own real
            // Transform3D.Position directly, so the telegraph shows where the attack actually
            // passes through rather than projecting a misleading line onto the floor below it.
            Photon.Deterministic.FPVector3 selfPosition = telegraph.SnapToGround == true
                ? GetGroundPosition(enemyData, frame.Get<Transform3D>(_entityRef).Position)
                : frame.Get<Transform3D>(_entityRef).Position;

            if (telegraph.Shape == TelegraphShape.Circle)
            {
                Photon.Deterministic.FPVector3 areaPosition;

                if (originIsSelf == true)
                {
                    areaPosition = selfPosition;
                }
                else if (TryGetAnchorPosition(frame, enemy, out areaPosition) == false)
                {
                    return false;
                }

                // See EnemyActionData.IgnoreY - the anchor (e.g. Enemy.SkillTargetPosition during
                // Preparation) can still be the live target's raw pivot-height position, not yet
                // flattened by the delivery's own Begin(), so this keeps the decal on the ground
                // instead of floating at whatever height the target happened to be at. Moot when
                // originIsSelf (selfPosition is already grounded), but harmless either way.
                if (ignoreY == true)
                    areaPosition.Y = selfPosition.Y;

                float radius = damageRange * telegraph.RadiusMultiplier;
                Vector3 areaWorldPosition = areaPosition.ToUnityVector3();
                position = telegraph.SnapToGround == true ? SnapToGround(areaWorldPosition) : areaWorldPosition;
                rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

                // Optional continuous spin around the world-vertical axis (e.g. a rotating
                // danger-sector pinwheel - ScrapstormDeliveryData) - derived from enemy.StateTimer
                // (the same field driving GrowthDuration above) rather than a separately-tracked
                // elapsed-time value, deliberately matching the exact formula the paired
                // ScrapstormDeliveryData.Tick() uses sim-side, so the visible wedge pattern and the
                // actual danger-zone damage check can't drift apart. Post-multiplied as a local
                // Z-axis twist, same composition idiom the ChargeLane/Rectangle branch below already
                // uses for its own twist - local Z is what LookRotation(up, forward) mapped to world
                // up, so this spins the already-flattened decal in place rather than tilting it.
                if (telegraph.RotationDegreesPerSecond != 0f)
                {
                    float spinDegrees = -enemy.StateTimer.AsFloat * telegraph.RotationDegreesPerSecond;
                    rotation *= Quaternion.Euler(0f, 0f, spinDegrees);
                }

                scale = new Vector3(radius * 2f, radius * 2f, 1f);
                return true;
            }

            if (telegraph.Shape == TelegraphShape.Cone)
            {
                if (TryGetAnchorPosition(frame, enemy, out Photon.Deterministic.FPVector3 conePosition) == false)
                    return false;

                if (ignoreY == true)
                    conePosition.Y = selfPosition.Y;

                Vector3 coneOrigin = selfPosition.ToUnityVector3();
                Vector3 coneDirection = conePosition.ToUnityVector3() - coneOrigin;

                if (coneDirection.sqrMagnitude <= 0.0001f)
                    return false;

                coneDirection.Normalize();

                float radius = damageRange * telegraph.RadiusMultiplier;

                // Anchored at the enemy (the cone's own apex), not the target - unlike Circle,
                // which is anchored at the target/anchor point (a blast radius centered on where
                // it lands).
                position = telegraph.SnapToGround == true ? SnapToGround(coneOrigin) : coneOrigin;
                rotation = Quaternion.LookRotation(Vector3.up, coneDirection);
                scale = new Vector3(radius * 2f, radius * 2f, 1f);
                return true;
            }

            if (telegraph.Shape == TelegraphShape.ChargeLane || telegraph.Shape == TelegraphShape.Rectangle)
            {
                if (TryGetAnchorPosition(frame, enemy, out Photon.Deterministic.FPVector3 targetPosition) == false)
                    return false;

                // See EnemyActionData.IgnoreY above. When true, targetPosition.Y is flattened to
                // selfPosition.Y so direction below ends up perfectly horizontal and the rotation
                // math further down naturally lies flat. When false (e.g. Sniper), direction keeps
                // its real vertical delta and the lane tilts to match - see the rotation comment
                // below.
                if (ignoreY == true)
                    targetPosition.Y = selfPosition.Y;

                Vector3 origin = selfPosition.ToUnityVector3();
                Vector3 direction = targetPosition.ToUnityVector3() - origin;

                if (direction.sqrMagnitude <= 0.0001f)
                    return false;

                direction.Normalize();

                // ToTarget: end point tracks the real anchor distance, so the box visually
                // shortens as the target gets closer. FixedDistance: end point always sits
                // exactly FixedDistanceValue from the enemy regardless of the target's actual
                // distance, for showing a delivery's full potential reach (e.g. matching
                // ChargeDeliveryData.DashDistance) rather than "however far the target happens to be."
                // FromOffset/ToOffset nudge either endpoint along the direction afterward either way.
                Vector3 endBase = telegraph.LineLength == TelegraphLineLength.FixedDistance
                    ? origin + direction * telegraph.FixedDistanceValue
                    : targetPosition.ToUnityVector3();

                Vector3 fromPoint = origin + direction * telegraph.FromOffset;
                Vector3 toPoint = endBase + direction * telegraph.ToOffset;
                float length = Vector3.Distance(fromPoint, toPoint);

                if (length <= 0.0001f)
                    return false;

                Vector3 midpoint = (fromPoint + toPoint) * 0.5f;
                position = telegraph.SnapToGround == true ? SnapToGround(midpoint) : midpoint;

                // ChargeLane/Rectangle sprite art is drawn with its long/pointing edge along local
                // X, not the Y convention Circle/Cone's rotation trick otherwise sets up (see this
                // method's own doc comment) - length sits on X, Width on Y, with a 90-degree twist
                // around local Z compensating so the long edge still ends up pointing along
                // `direction` instead of sideways.
                //
                // The local-Z reference passed into LookRotation is normally just Vector3.up (a
                // pure flat lane), but that only works because `direction` used to be forced
                // horizontal by the ignoreY branch above. With IgnoreY=false, `direction` can carry
                // a real vertical delta (Sniper aiming up/down at an elevated/lowered target) - so
                // instead of the fixed world-up, use the component of world-up perpendicular to
                // `direction` (Gram-Schmidt). This is exactly Vector3.up whenever direction is
                // horizontal (zero behavior change for every other ChargeLane/Rectangle user, which
                // all still author IgnoreY=true), and smoothly tilts the lane's plane to match the
                // shot's real angle otherwise, so the telegraph visually follows the same up/down
                // aim the projectile will actually take.
                Vector3 zAxis = Vector3.up - direction * Vector3.Dot(Vector3.up, direction);
                if (zAxis.sqrMagnitude <= 0.0001f)
                    zAxis = Vector3.forward; // direction points straight up/down - arbitrary twist is fine
                else
                    zAxis.Normalize();

                rotation = Quaternion.LookRotation(zAxis, direction) * Quaternion.Euler(0f, 0f, 90f);
                scale = new Vector3(length, telegraph.Width, 1f);

                return true;
            }

            return false;
        }

        // Recomputes an active LiveTracking telegraph's pose every frame - see ComputeTelegraphPose
        // for the shared shape math and TelegraphData.LiveTracking for why this is opt-in (most
        // telegraphs are a fixed decal, only ones like a sniper's laser sight need to visibly
        // follow the target through the whole windup). Silently holds the last pose if the anchor
        // can't currently be resolved (e.g. the target died mid-track) rather than snapping or
        // hiding - ClearTelegraph (driven by the phase-edge logic in QUpdate) is what actually
        // tears it down once EndPhase is reached, this only ever repositions.
        //
        // Eases toward the freshly computed pose (LiveTrackingSmoothing) rather than snapping
        // straight to it - the anchor this reads only updates once per simulation tick, not per
        // render frame, so snapping directly to it reads as a jittery stepped motion at typical
        // render framerates. Same exponential-smoothing shape EnemyArmAimView uses for its own
        // continuous aim tracking. SpawnTelegraph already places the instance at the correct
        // initial pose directly (no smoothing needed for that first frame), so this only ever
        // starts easing from an already-correct pose.
        private void UpdateLiveTelegraph(Frame frame, Enemy enemy, EnemyDataAsset enemyData)
        {
            if (_currentTelegraph == null || _liveTelegraphData == null)
                return;

            if (ComputeTelegraphPose(frame, enemy, enemyData, _liveTelegraphData, _liveTelegraphIgnoreY, _liveTelegraphDamageRange, _liveTelegraphOriginIsSelf, out Vector3 position, out Quaternion rotation, out Vector3 scale) == false)
                return;

            float smoothT = 1f - Mathf.Exp(-_liveTelegraphData.LiveTrackingSmoothing * Time.deltaTime);
            Transform telegraphTransform = _currentTelegraph.transform;

            Vector3 smoothedPosition = Vector3.Lerp(telegraphTransform.position, position, smoothT);
            Quaternion smoothedRotation = Quaternion.Slerp(telegraphTransform.rotation, rotation, smoothT);
            telegraphTransform.SetPositionAndRotation(smoothedPosition, smoothedRotation);
            telegraphTransform.localScale = Vector3.Lerp(telegraphTransform.localScale, scale, smoothT);
        }

        private void SpawnTelegraph(Frame frame, Enemy enemy, EnemyDataAsset enemyData, TelegraphData telegraph, bool ignoreY, float damageRange, bool originIsSelf)
        {
            ClearTelegraph(instant: true);

            GameObject prefab = ResolveTelegraphPrefab(telegraph);

            if (prefab == null)
                return;

            if (ComputeTelegraphPose(frame, enemy, enemyData, telegraph, ignoreY, damageRange, originIsSelf, out Vector3 position, out Quaternion rotation, out Vector3 scale) == false)
            {
                bool isImplementedShape = telegraph.Shape == TelegraphShape.Circle || telegraph.Shape == TelegraphShape.Cone
                    || telegraph.Shape == TelegraphShape.ChargeLane || telegraph.Shape == TelegraphShape.Rectangle;

                if (isImplementedShape == false)
                    LogHelper.Log("EnemyAttackVisualsView", $"TelegraphShape.{telegraph.Shape} has no rendering implementation yet - not spawning anything.");

                return;
            }

            GameObject instance = GetTelegraphInstance(prefab, position, rotation);
            instance.transform.localScale = scale;

            if (telegraph.LiveTracking == true)
            {
                _liveTelegraphData = telegraph;
                _liveTelegraphIgnoreY = ignoreY;
                _liveTelegraphDamageRange = damageRange;
                _liveTelegraphOriginIsSelf = originIsSelf;
            }

            // Pre-attached on TelegraphPrefab's root (see TelegraphFade) - not AddComponent'd,
            // since pooled instances are reused rather than recreated and repeatedly adding a
            // fresh component on every reuse would stack duplicates. Owns fading itself in/out
            // and (if wired) kicking off a child TelegraphGrow's growth animation internally.
            TelegraphFade fade = instance.GetComponent<TelegraphFade>();
            if (fade != null)
            {
                // enemy.StateTimer at this exact tick is the same field every action sets to its
                // own real duration (UpdateChasing to AnticipationTime, ChargeDeliveryData/
                // LeapDeliveryData to their own dash/jump time) - TelegraphFade falls back to the
                // hand-tuned GrowthDuration only if this comes back <= 0.
                float growDuration = enemy.StateTimer.AsFloat > 0f ? enemy.StateTimer.AsFloat : telegraph.GrowthDuration;
                fade.Initialize(prefab, telegraph.FadeInDuration, telegraph.FadeOutDuration, growDuration, _entityRef);
            }

            _currentTelegraph = instance;
            _currentTelegraphPrefab = prefab;
            _activeTelegraphStartPhase = telegraph.StartPhase;
        }

        // Pools through TelegraphManager when one's present in the scene (see that class),
        // falling back to a plain Instantiate otherwise so this doesn't hard-fail if pooling
        // isn't set up.
        private static GameObject GetTelegraphInstance(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            return TelegraphManager.Instance != null
                ? TelegraphManager.Instance.Get(prefab, position, rotation)
                : Instantiate(prefab, position, rotation);
        }

        // instant: true (only used from DeInitialize, when this whole view is being torn down)
        // destroys immediately, same as ClearTelegraph's own instant param. Otherwise hands off to
        // ParticleGracefulStop instead of destroying directly - a plain Destroy() on a
        // ParticleSystem cuts off every currently-emitted particle instantly instead of letting
        // them finish their own lifetime, which read as particles vanishing mid-flight whenever
        // the next phase edge replaced this one before it was actually done.
        private void ClearParentedParticle(bool instant = false)
        {
            if (_currentParentedParticle != null)
            {
                if (instant == true)
                    Destroy(_currentParentedParticle);
                else
                    _currentParentedParticle.AddComponent<ParticleGracefulStop>().StopAndDestroyWhenFinished();
            }

            _currentParentedParticle = null;
        }

        // instant: true skips the fade-out (used for teardown/replacement, where either nothing
        // will be watching it finish, or it's about to be replaced by a new instance anyway) -
        // released to the pool immediately instead. Non-instant hands off to TelegraphFade, which
        // releases itself once its own fade-out finishes.
        private void ClearTelegraph(bool instant = false)
        {
            if (_currentTelegraph != null)
            {
                TelegraphFade fade = instant == false ? _currentTelegraph.GetComponent<TelegraphFade>() : null;

                if (fade != null)
                {
                    fade.FadeOutAndRelease();
                }
                else if (TelegraphManager.Instance != null && _currentTelegraphPrefab != null)
                {
                    TelegraphManager.Instance.Release(_currentTelegraphPrefab, _currentTelegraph);
                }
                else
                {
                    Destroy(_currentTelegraph);
                }
            }

            _currentTelegraph = null;
            _currentTelegraphPrefab = null;
            _activeTelegraphStartPhase = null;
            _liveTelegraphData = null;
        }
    }
}
