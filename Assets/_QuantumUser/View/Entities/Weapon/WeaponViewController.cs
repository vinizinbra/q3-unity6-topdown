using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Spawns the WeaponView prefab matching this entity's Weapon.WeaponData under weaponSocket
    // (MainChar's "WeaponLocator" transform) on init, instead of baking a fixed gun sprite into
    // the character prefab. PlayerGunAimView doesn't need to know about this - it already falls
    // back to a GetComponent/GetComponentInParent/transform.root lookup for its WeaponView
    // reference (see CustomQuantumEntityViewComponent.Awake's "catch up manually" handling for
    // components parented on post-spawn), so it picks up whatever gets instantiated here
    // automatically. WeaponView itself is a CustomQuantumEntityViewComponent too, so it
    // initializes off the same post-spawn catch-up path once parented under weaponSocket.
    //
    // Also re-spawns on a later WeaponEquipped event (e.g. a LevelUpCategory.ChooseWeapon pick
    // mid-match, see WeaponChoiceUtility.Grant) - Initialize's own cold read above already covers
    // a reconnecting client for free (it re-runs from scratch the moment this view is
    // (re)instantiated, picking up whatever the CURRENT Weapon.WeaponData is), so the event
    // subscription only needs to handle an ALREADY-connected client watching a live re-equip.
    //
    // Also drives weaponSocket's own local position from CharacterData.WeaponPosition - X is
    // authored as a positive "facing right" magnitude and mirrored to negative while facing left,
    // matching the same binary flip BlobAnimationView/WeaponView use for the sprite (rather than
    // continuously rotating with aim angle). WeaponSystem mirrors this same value in simulation
    // (see StatUtility.GetWeaponHoldOffset) so the muzzle lines up with wherever this socket
    // visually ends up.
    public class WeaponViewController : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Where the weapon prefab gets parented - MainChar's WeaponLocator transform. Falls back to this transform if left empty.")]
        private Transform weaponSocket;

        [Header("Socket Position")]
        [SerializeField, Tooltip("Falls back to a BlobAnimationView anywhere under the rig root if left empty. Source of the facing sign that mirrors CharacterData.WeaponPosition.X.")]
        private BlobAnimationView torsoFollow;
        [SerializeField, Tooltip("How quickly weaponSocket eases toward its target local position when facing flips. Higher = snappier.")]
        private float positionSmoothing = 12f;

        // The spawned GameObject, tracked SEPARATELY from the WeaponView component on it - a view
        // prefab whose root is missing that component (a beam weapon that is nothing but a
        // LineRenderer, say) would otherwise leave currentWeaponView null, so the "destroy the old
        // one first" guard below would never fire and every re-spawn - Initialize plus every
        // WeaponEquipped - would silently stack another copy in the socket.
        private GameObject currentWeaponInstance;
        private WeaponView currentWeaponView;
        private Vector3 restWeaponPosition;
        private bool hasWeaponPosition;

        // Read by WeaponHandGripView every frame rather than pushed via an event - keeps that
        // component decoupled from having to know when a weapon swap happens.
        public WeaponView CurrentWeaponView => currentWeaponView;

        private Transform Socket => weaponSocket != null ? weaponSocket : transform;

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventWeaponEquipped>(this, OnWeaponEquipped);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        private void OnWeaponEquipped(EventWeaponEquipped e)
        {
            if (e.Owner != _entityRef)
                return;

            WeaponDataAsset weaponData = _game.Frames.Verified.FindAsset(e.WeaponData);
            SpawnWeaponView(weaponData);
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            if (torsoFollow == null)
                torsoFollow = GetComponentInParent<BlobAnimationView>();
            if (torsoFollow == null)
                torsoFollow = transform.root.GetComponentInChildren<BlobAnimationView>();

            Frame frame = game.Frames.Verified;

            if (frame.Has<CharacterStats>(_entityRef))
            {
                CharacterStats stats = frame.Get<CharacterStats>(_entityRef);
                CharacterData characterData = frame.FindAsset(stats.CharacterData);
                restWeaponPosition = characterData.WeaponPosition.ToUnityVector3();
                hasWeaponPosition = true;
            }

            AssetRef<WeaponDataAsset> weaponDataRef = frame.Get<Weapon>(_entityRef).WeaponData;
            WeaponDataAsset weaponData = frame.FindAsset(weaponDataRef);
            SpawnWeaponView(weaponData);
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            DestroyCurrentWeaponView();
        }

        // Public so both Initialize (cold read) and OnWeaponEquipped (live re-equip) can call this -
        // see the class comment above.
        public void SpawnWeaponView(WeaponDataAsset weaponData)
        {
            DestroyCurrentWeaponView();

            if (weaponData == null || weaponData.ViewPrefab == null)
            {
                LogHelper.Warn("WeaponViewController", $"{weaponData} has no ViewPrefab assigned.", this);
                return;
            }

            currentWeaponInstance = Instantiate(weaponData.ViewPrefab, Socket);
            currentWeaponView = currentWeaponInstance.GetComponent<WeaponView>();

            if (currentWeaponView == null)
            {
                LogHelper.Warn("WeaponViewController", $"{weaponData.ViewPrefab.name} has no WeaponView on its root - " +
                    "it won't be aimed, won't recoil and won't place the hands (PlayerGunAimView/WeaponHandGripView both " +
                    "resolve through it). Only the hitscan view styles on it will do anything.", this);
            }
        }

        private void DestroyCurrentWeaponView()
        {
            if (currentWeaponInstance != null)
                Destroy(currentWeaponInstance);

            currentWeaponInstance = null;
            currentWeaponView = null;
        }

        protected override void QUpdate(QuantumGame game)
        {
            // Hides the whole weapon locator (weaponSocket / MainChar's WeaponLocator - and therefore
            // the gun plus anything else parented under it) while Downed/KO (see docs/revive.md) -
            // can't fire anyway (WeaponSystem's own IsIncapacitated gate), and a collapsed character
            // still visibly holding a raised weapon reads as broken. Restored the instant they're
            // revived. Also hidden while a fall-respawn delay is pending (see PlayerFallSystem/
            // LevelConfig.FallRespawnDelay) - a floating gun with no visible character underneath it
            // reads just as broken. Only toggles weaponSocket when it's explicitly assigned -
            // Socket's fallback is this component's own GameObject, which must stay active for
            // QUpdate to keep running.
            if (weaponSocket != null)
            {
                Frame frame = game.Frames.Predicted;
                bool hidden = PlayerLifeStateUtility.IsIncapacitated(frame, _entityRef)
                    || FallStateUtility.IsFallPending(frame, _entityRef);

                if (weaponSocket.gameObject.activeSelf == hidden)
                    weaponSocket.gameObject.SetActive(hidden == false);
            }

            if (hasWeaponPosition == false) return;

            float facingSign = torsoFollow != null ? torsoFollow.FacingSign : 1f;
            Vector3 target = restWeaponPosition;
            target.x = Mathf.Abs(target.x) * facingSign;
            // Z stays whatever the socket is currently at (authored on the transform) - only X/Y
            // are driven from CharacterData.WeaponPosition here. Z is still consumed on the
            // simulation side (see StatUtility.GetWeaponHoldOffset) for the spawn offset.
            target.z = Socket.localPosition.z;

            float t = 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime);
            Socket.localPosition = Vector3.Lerp(Socket.localPosition, target, t);
        }
    }
}
