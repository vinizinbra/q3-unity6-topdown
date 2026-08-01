using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    public class EnemyView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Where EnemyDataAsset.ViewPrefab is instantiated as a child - just an anchor point on the generic entity's view. The prefab brings its own EnemyViewRig; EnemyBlobAnimationView/EnemyArmAimView/EnemyAttackVisualsView/HitFeedback live here on the generic prototype instead and get that rig handed to them once it's instantiated (see SpawnSprite).")]
        private Transform spriteRoot;

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
            EnemyUiWidgetManager.Instance?.SpawnWidget(_entityRef, game, transform, ResolveEnemyName(game));
            SpawnSprite(game);
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
        // data.Stats.Radius directly).
        private void SpawnSprite(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;
            if (frame.Has<Enemy>(_entityRef) == false)
                return;

            EnemyDataAsset data = frame.FindAsset(frame.Get<Enemy>(_entityRef).EnemyData);
            if (data.ViewPrefab == null)
            {
                LogHelper.Error("Enemy", $"{_entityRef} EnemyDataAsset {data.name} has no ViewPrefab assigned");
                return;
            }

            float radius = EnemyMovementUtility.ResolveEntityRadius(frame, _entityRef).AsFloat;
            LogHelper.Log("Enemy", $"{_entityRef} ({data.name}) SpawnSprite resolved radius {radius}");

            // HasShadow sits as a sibling on this same GameObject (see Enemy.prefab/
            // BasicEnemy.prefab) and already acquired its pooled blob in its own OnEnable, before
            // radius was known - update it now so the shadow footprint matches the entity's actual
            // collision size instead of staying at whatever flat baseScale the generic prototype
            // was authored with. baseScale is a diameter (blobPrefab's authored width at scale 1),
            // same convention as the rig's own fit scale, so radius needs doubling here too.
            HasShadow shadow = GetComponent<HasShadow>();
            if (shadow != null)
                shadow.SetBaseScale(radius * 2f);

            GameObject instance = ViewPrefabPool.Instance.Get(data.ViewPrefab, spriteRoot);
            _rigInstance = instance;
            _rigPrefab = data.ViewPrefab;

            EnemyViewRig rig = instance.GetComponentInChildren<EnemyViewRig>();
            if (rig == null)
            {
                LogHelper.Error("Enemy", $"{_entityRef} EnemyDataAsset {data.name}'s ViewPrefab has no EnemyViewRig");
                return;
            }

            // Transform3D.Position (spriteRoot's world position) sits at the collider's center -
            // Quantum spheres are center-pivoted - but ViewPrefab's rig is bottom-pivoted (see
            // EnemyDataAsset.ViewPrefab), so without this offset the rig's feet would render at the
            // sphere's center instead of its bottom. localPosition is read in spriteRoot's own
            // space, unaffected by instance's own localScale set below, so radius is the right unit
            // here directly - no separate unscaling needed.
            instance.transform.localPosition = Vector3.down * radius;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * ResolveFitScale(rig, radius, data);

            ConnectRig(rig);
        }

        // rig.ReferenceSprite.sprite.bounds is already Pixels-Per-Unit-corrected (bounds size =
        // pixel size / PPU), so dividing the target diameter by its unscaled width self-corrects
        // for whatever PPU the artist happened to import that sprite at - the rig always ends up
        // the same apparent size for a given Radius, instead of relying on the artist hand-
        // matching ViewPrefab's overall authored scale to the sprite's PPU.
        private float ResolveFitScale(EnemyViewRig rig, float radius, EnemyDataAsset data)
        {
            if (rig.ReferenceSprite == null || rig.ReferenceSprite.sprite == null)
            {
                LogHelper.Error("Enemy", $"{_entityRef} EnemyDataAsset {data.name}'s ViewPrefab has no EnemyViewRig.ReferenceSprite assigned - falling back to Radius as a raw scale multiplier, which won't correct for the sprite's Pixels Per Unit.");
                return radius;
            }

            float targetDiameter = radius * 2f;
            float unscaledWidth = rig.ReferenceSprite.sprite.bounds.size.x;
            return targetDiameter / unscaledWidth;
        }

        // EnemyBlobAnimationView/EnemyArmAimView/EnemyAttackVisualsView/HitFeedback live on this
        // generic prototype (shared across enemy types), not on ViewPrefab itself - so unlike a
        // normal sibling reference, their rig can't be wired in the Inspector; it only exists once
        // ViewPrefab is instantiated above, so it's handed to each sibling here instead.
        private void ConnectRig(EnemyViewRig rig)
        {
            EnemyBlobAnimationView blobAnimationView = GetComponent<EnemyBlobAnimationView>();
            if (blobAnimationView != null)
                blobAnimationView.SetRig(rig);

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
