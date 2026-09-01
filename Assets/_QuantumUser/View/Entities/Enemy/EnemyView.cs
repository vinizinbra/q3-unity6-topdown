using NaughtyAttributes;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    public class EnemyView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Where EnemyDataAsset.ViewPrefab is instantiated as a child - just an anchor point on the generic entity's view. The prefab brings its own EnemyViewRig; EnemyBlobAnimationView/EnemyArmAimView/EnemyAttackVisualsView/HitFeedback live here on the generic prototype instead and get that rig handed to them once it's instantiated (see SpawnSprite). A one-off prototype (e.g. a boss) can instead author its own EnemyViewRig as a REAL CHILD of this transform directly in the Editor - SpawnSprite finds it and uses it as-is, skipping ViewPrefab entirely.")]
        private Transform spriteRoot;

        [SerializeField, Tooltip("Extra radius (in world units) added ON TOP of the entity's collider radius purely for the visual sprite fit-scale, so the sprite renders slightly larger than the physics footprint (e.g. radius 2 fits as if it were 2.2). Only affects the sprite scale - the collider, shadow footprint, and feet-anchor offset all still use the raw radius.")]
        private float viewRadiusPadding = 0.2f;

        [SerializeField, Tooltip("Extra world-units clearance added ABOVE the resolved widget height (see ResolveWidgetOffset) as a guaranteed safety margin - covers sub-pixel/alpha-edge slack in the sprite's own bounds so the bar never reads as touching the art. On top of CharacterUiWidget.worldOffset's own shared base clearance.")]
        private float widgetSpriteTopPadding = 0.25f;

        [SerializeField, Tooltip("Multiplies the entity's collider radius (EnemyMovementUtility.ResolveEntityRadius) into a MINIMUM widget height, taken together with the measured sprite-top height in ResolveWidgetOffset (whichever is taller wins) - not just a fallback for when no sprite is measurable. A well fit-scaled sprite's own top sits at roughly 1x radius above the collider center, so this is also the floor that keeps the bar from sitting low/clipping into the body on whichever enemy types happen to under-measure via sprite bounds (e.g. a wide/landscape sprite fit-scaled by its longest side ends up shorter than a portrait one of the same radius).")]
        private float widgetRadiusOffsetMultiplier = 1f;

        // Tracked so DeInitialize can release the exact pooled instance/prefab pair back to
        // ViewPrefabPool - re-deriving ViewPrefab from frame data at DeInitialize time isn't safe
        // since the entity's components may already be gone by then.
        private GameObject _rigInstance;
        private GameObject _rigPrefab;

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventEntityDied>(this, OnEntityDied);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            RefreshSprite(game);
        }

        // Spawn the sprite FIRST so its rig is instantiated/scaled, then measure it - the widget's
        // vertical offset is derived from the rendered sprite's actual top edge (ResolveWidgetOffset),
        // not the raw collider radius, so it clears the visible body even when the art is taller than
        // the physics footprint. Pulled out of Initialize so the Resolve Scale button below can force
        // a fresh pass without re-invoking base.Initialize (which the entity-view lifecycle should
        // only ever call once). ReleaseSprite first so a repeated call (button re-click) doesn't leak
        // a second pooled ViewPrefab instance under spriteRoot - a no-op the very first time
        // (Initialize), since nothing's been spawned yet.
        private void RefreshSprite(QuantumGame game)
        {
            ReleaseSprite();

            float radius = EnemyMovementUtility.ResolveEntityRadius(game.Frames.Predicted, _entityRef).AsFloat;
            EnemyViewRig rig = SpawnSprite(game, radius);

            if (IsBoss(game) == false)
                EnemyUiWidgetManager.Instance?.SpawnWidget(_entityRef, game, transform, ResolveEnemyName(game), ResolveWidgetOffset(rig, radius));
        }

        // Boss gets no floating widget at all - its name/HP/shield are shown on the dedicated
        // top-screen BossWidget instead (see BossWidget.cs), gated on GameState.Boss.
        private bool IsBoss(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;
            if (frame.TryGet<Enemy>(_entityRef, out var enemy) == false)
                return false;

            EnemyDataAsset data = frame.FindAsset(enemy.EnemyData);
            return data != null && data.Tier == EnemyTier.Boss;
        }

        // Forces a fresh RefreshSprite pass on this already-live entity - SpawnSprite itself now
        // applies ResolveFitScale to a baked rig's scale exactly like it always did for a pooled
        // one (see SpawnSprite's own comment), so this button is just a live-preview convenience:
        // tweak viewRadiusPadding/a baked rig's ReferenceSprite/EnemyDataAsset.Stats.Radius in the
        // Inspector during Play Mode and re-click to see the result immediately, without respawning
        // the entity or restarting the session - same "in-Inspector test trigger" idiom every other
        // [Button] in this codebase already uses (e.g. HitFeedback.Flash).
        [Button("Resolve Scale")]
        private void ResolveScaleButton()
        {
            if (QuantumRunner.Default == null || QuantumRunner.Default.Game == null)
            {
                Debug.LogWarning("[EnemyView] Resolve Scale requires a running Play Mode session with this entity already spawned.");
                return;
            }

            RefreshSprite(QuantumRunner.Default.Game);
        }

        // World-space vertical offset handed to CharacterUiWidget as its per-character nudge (added on
        // top of the widget's shared worldOffset base clearance). SpriteRenderer.bounds is a world-space
        // AABB that already reflects the position/scale SpawnSprite just applied, so bounds.max.y is the
        // sprite's true top in world space; measured relative to transform.position (the collider center
        // the widget follows) it becomes the local vertical raise.
        //
        // Taken as the MAX of that measured height and a radius-based floor, not sprite-bounds alone -
        // different enemy art doesn't crop/pad identically, and ResolveFitScale fits a sprite's LONGEST
        // side to the collider diameter, so a wide/landscape sprite ends up proportionally shorter than a
        // portrait one at the same radius. Both effects can make the measured sprite top sit lower than
        // the body actually reads on screen, which is what "bar clips into the enemy" looks like - the
        // radius floor (roughly where a well fit-scaled sprite's own top would land) catches whichever
        // enemy type's measurement comes in short, while a genuinely tall sprite still gets its own taller
        // measured height rather than being clamped down to the floor. widgetSpriteTopPadding is a small
        // guaranteed margin on top of whichever one wins, so the bar reads as clearing the body rather
        // than just grazing it.
        private Vector3 ResolveWidgetOffset(EnemyViewRig rig, float radius)
        {
            float radiusFloor = radius * widgetRadiusOffsetMultiplier;

            if (rig != null && rig.ReferenceSprite != null && rig.ReferenceSprite.sprite != null)
            {
                float spriteTopLocalY = rig.ReferenceSprite.bounds.max.y - transform.position.y;
                return Vector3.up * (Mathf.Max(spriteTopLocalY, radiusFloor) + widgetSpriteTopPadding);
            }

            return Vector3.up * (radiusFloor + widgetSpriteTopPadding);
        }

        // Filler is the disposable/trash tier (destroyed instantly, no lingering die animation -
        // see DamageUtility.ApplyDamage) so it skips the name callout too; Normal and above get
        // one, even though Normal dies the same instant way Filler does. Elite gets an " Elite"
        // suffix so it reads as a step up from a same-named Specialist/Normal variant. The asset's own
        // name is appended in parens while running in-editor (not in a real build) so whichever
        // specific EnemyDataAsset spawned this instance is identifiable during playtesting - useful
        // once several assets share one EnemyName (e.g. a future per-world reskin variant).
        private string ResolveEnemyName(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;
            if (frame.TryGet<Enemy>(_entityRef, out var enemy) == false)
                return null;

            EnemyDataAsset data = frame.FindAsset(enemy.EnemyData);
            if (data == null || data.Tier == EnemyTier.Filler)
                return null;

            string name = string.IsNullOrEmpty(data.EnemyName) ? data.name : data.EnemyName;

            if (data.Tier == EnemyTier.Elite)
                name += " Elite";

            // Grayed out via TMP rich text (CharacterUiWidget's nameText) so it reads as secondary
            // debug info, not part of the actual in-game name.
            if (Application.isEditor)
                name += $" <color=#999999>({data.name})</color>";

            return name;
        }

        // Instantiates EnemyDataAsset.ViewPrefab under spriteRoot and fits it to the entity's
        // actual radius (read back via ResolveEntityRadius, same helper EnemyAllyLinkView uses, so
        // this reflects whatever SeedRadius applied to the collider rather than re-reading
        // data.Stats.Radius directly) - unless a rig is already baked as a real child of spriteRoot
        // (see the baked-rig check below), in which case ViewPrefab/ViewPrefabPool are skipped
        // entirely, but the SAME radius-based fit-scale/shadow math below still applies to it, so a
        // boss's own hand-placed rig tracks its collider exactly like every other enemy's pooled
        // sprite does. radius is resolved once by the caller (Initialize) and reused for the HUD
        // widget's offset too, rather than re-resolved here.
        private EnemyViewRig SpawnSprite(QuantumGame game, float radius)
        {
            Frame frame = game.Frames.Predicted;
            if (frame.Has<Enemy>(_entityRef) == false)
                return null;

            Enemy enemy = frame.Get<Enemy>(_entityRef);
            EnemyDataAsset data = frame.FindAsset(enemy.EnemyData);

            // HasShadow sits as a sibling on this same GameObject (see Enemy.prefab/
            // BasicEnemy.prefab) and already acquired its pooled blob in its own OnEnable, before
            // radius was known - update it now so the shadow footprint matches the entity's actual
            // collision size instead of staying at whatever flat baseScale the generic prototype
            // was authored with. baseScale is a diameter (blobPrefab's authored width at scale 1),
            // same convention as the rig's own fit scale, so radius needs doubling here too. Applies
            // whether the rig below ends up baked or pooled - confirmed with the user, a boss's
            // shadow should track its real collider size exactly like everything else does.
            HasShadow shadow = GetComponent<HasShadow>();
            if (shadow != null)
                shadow.SetBaseScale(radius * 2f);

            // A hand-baked visual already sitting under spriteRoot - e.g. a boss with its own
            // dedicated, one-off EntityPrototype (see RunPhaseUtility.SpawnBoss/docs/run-phase.md's
            // "Boss phase trigger"), authored with its rig as a real child in the Editor rather than
            // referencing a separate ViewPrefab - skips ViewPrefab/ViewPrefabPool below entirely
            // (there's nothing to instantiate or pool - the GameObject already exists), but still
            // gets the exact same ResolveFitScale sprite-bounds math AND the same
            // Vector3.down * radius bottom-pivot positioning the pooled path applies below,
            // confirmed with the user: a boss's rig should sit at its collider's bottom center and
            // dynamically match its radius exactly the same way a normal enemy's pooled sprite
            // already does, not stay at a fixed hand-authored scale/position. Only rotation is left
            // alone (the artist still controls the rig's own tilt/orientation). Never tracked in
            // _rigInstance/_rigPrefab - there's nothing to release back to ViewPrefabPool since it
            // was never pooled from it. Requires the baked rig's own EnemyViewRig to be parented
            // under spriteRoot specifically, same anchor the pooled path instantiates into - not
            // just anywhere else on the prototype.
            EnemyViewRig bakedRig = spriteRoot.GetComponentInChildren<EnemyViewRig>();
            if (bakedRig != null)
            {
                bakedRig.transform.localPosition = Vector3.down * radius;
                bakedRig.transform.localScale = Vector3.one * ResolveFitScale(bakedRig, radius + viewRadiusPadding, data);
                ConnectRig(bakedRig);
                return bakedRig;
            }

            GameObject viewPrefab = ResolveViewPrefab(data, enemy.Faction, out float skinScaleMultiplier);
            if (viewPrefab == null)
            {
                LogHelper.Error("Enemy", $"{_entityRef} EnemyDataAsset {data.name} has no ViewPrefab assigned");
                return null;
            }

            LogHelper.Log("Enemy", $"{_entityRef} ({data.name}) SpawnSprite resolved radius {radius}");

            GameObject instance = ViewPrefabPool.Instance.Get(viewPrefab, spriteRoot);
            _rigInstance = instance;
            _rigPrefab = viewPrefab;

            EnemyViewRig rig = instance.GetComponentInChildren<EnemyViewRig>();
            if (rig == null)
            {
                LogHelper.Error("Enemy", $"{_entityRef} EnemyDataAsset {data.name}'s ViewPrefab has no EnemyViewRig");
                return null;
            }

            // Transform3D.Position (spriteRoot's world position) sits at the collider's center -
            // Quantum spheres are center-pivoted - but ViewPrefab's rig is bottom-pivoted (see
            // EnemyDataAsset.ViewPrefab), so without this offset the rig's feet would render at the
            // sphere's center instead of its bottom. localPosition is read in spriteRoot's own
            // space, unaffected by instance's own localScale set below, so radius is the right unit
            // here directly - no separate unscaling needed.
            instance.transform.localPosition = Vector3.down * radius;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * ResolveFitScale(rig, radius + viewRadiusPadding, data) * skinScaleMultiplier;

            ConnectRig(rig);
            return rig;
        }

        // Faction is authored explicitly per-slot on whichever EnemyGroupConfig.GroupMemberEntry
        // spawned this enemy (GroupSpawnerUtility.SpawnMember sets Enemy.Faction directly,
        // deterministic/networked, not picked here) - this just looks up the matching skin.
        // Archetypes with no FactionSkins authored (most of them, at least at first - "not every
        // archetype needs a skin") always fall through to the default ViewPrefab, and so does a
        // Faction with no matching entry in FactionSkins. scaleMultiplier comes along with the
        // matched skin (EnemyFactionSkin.ScaleMultiplier) since a reskin's fit scale can want to
        // differ from the default ViewPrefab's - 1 for the ViewPrefab fallback, which has no
        // multiplier of its own.
        private GameObject ResolveViewPrefab(EnemyDataAsset data, EnemyFaction faction, out float scaleMultiplier)
        {
            scaleMultiplier = 1f;

            if (data.FactionSkins == null || data.FactionSkins.Count == 0)
                return data.ViewPrefab;

            foreach (EnemyFactionSkin skin in data.FactionSkins)
            {
                if (skin.Faction == faction)
                {
                    scaleMultiplier = skin.ScaleMultiplier > 0f ? skin.ScaleMultiplier : 1f;
                    return skin.ViewPrefab;
                }
            }

            return data.ViewPrefab;
        }

        // rig.ReferenceSprite.sprite.bounds is already Pixels-Per-Unit-corrected (bounds size =
        // pixel size / PPU), so dividing the target diameter by its unscaled longest side
        // self-corrects for whatever PPU the artist happened to import that sprite at - the rig
        // always ends up the same apparent size for a given Radius, instead of relying on the
        // artist hand-matching ViewPrefab's overall authored scale to the sprite's PPU. Takes the
        // longer of width/height (not width alone) so a non-square sprite's longer axis is what
        // ends up matching the target diameter - using width alone let a portrait sprite (taller
        // than wide) visually overshoot the intended size, since its actual tallest extent was
        // never checked against anything.
        private float ResolveFitScale(EnemyViewRig rig, float fitRadius, EnemyDataAsset data)
        {
            if (rig.ReferenceSprite == null || rig.ReferenceSprite.sprite == null)
            {
                LogHelper.Error("Enemy", $"{_entityRef} EnemyDataAsset {data.name}'s ViewPrefab has no EnemyViewRig.ReferenceSprite assigned - falling back to Radius as a raw scale multiplier, which won't correct for the sprite's Pixels Per Unit.");
                return fitRadius;
            }

            float targetDiameter = fitRadius * 2f;
            Vector3 unscaledSize = rig.ReferenceSprite.sprite.bounds.size;
            float unscaledLongestSide = Mathf.Max(unscaledSize.x, unscaledSize.y);
            return targetDiameter / unscaledLongestSide;
        }

        // EnemyBlobAnimationView/EnemyArmAimView/EnemyAttackVisualsView/HitFeedback live on this
        // generic prototype (shared across enemy types), not on ViewPrefab itself - so unlike a
        // normal sibling reference, their rig can't be wired in the Inspector; it only exists once
        // ViewPrefab is instantiated above, so it's handed to each sibling here instead.
        private void ConnectRig(EnemyViewRig rig)
        {
            EnemyBlobAnimationView blobAnimationView = GetComponent<EnemyBlobAnimationView>();
            if (blobAnimationView != null)
            {
                blobAnimationView.SetRig(rig);

                // HasShadow.SetBaseScale(radius * 2f) above already ran by this point, so
                // BaseScale here reflects this entity's real footprint, not HasShadow's authored
                // default - see EnemyBlobAnimationView.SetShadow's own comment for why this lets
                // Die/Burrow shrink the ground shadow in step with the sprite instead of leaving it
                // at full size while the sprite vanishes.
                HasShadow shadow = GetComponent<HasShadow>();
                if (shadow != null)
                    blobAnimationView.SetShadow(shadow);
            }

            EnemyArmAimView armAimView = GetComponent<EnemyArmAimView>();
            if (armAimView != null)
                armAimView.SetRig(rig);

            EnemyAttackVisualsView attackVisualsView = GetComponent<EnemyAttackVisualsView>();
            if (attackVisualsView != null)
                attackVisualsView.SetRig(rig);

            HitFeedback hitFeedback = GetComponent<HitFeedback>();
            if (hitFeedback != null)
                hitFeedback.SetRig(rig);
        }

        public override void DeInitialize(QuantumGame game)
        {
            EnemyUiWidgetManager.Instance?.DespawnWidget(_entityRef);
            ReleaseSprite();
            base.DeInitialize(game);
        }

        private void ReleaseSprite()
        {
            if (_rigInstance == null)
                return;

            ViewPrefabPool.Instance.Release(_rigPrefab, _rigInstance);
            _rigInstance = null;
            _rigPrefab = null;
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        // Fires the instant the enemy's health hits zero, well before EnemySystem actually
        // destroys the entity (it lingers as a corpse for EnemyDataAsset.DeathLingerTime) - so the
        // widget disappears with the death instead of hovering over the corpse for the whole
        // linger duration.
        private void OnEntityDied(EventEntityDied e)
        {
            if (e.Target != _entityRef)
                return;

            EnemyUiWidgetManager.Instance?.DespawnWidget(_entityRef);

        }
    }
}
