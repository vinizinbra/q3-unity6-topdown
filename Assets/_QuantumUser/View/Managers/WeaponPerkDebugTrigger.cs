using Quantum;
using UnityEngine;

namespace QuantumUser.View
{
    // Fires a GrantWeaponPerkCommand for the local player - lets any WeaponPerkData be tried out on
    // the local player's currently equipped weapon at runtime without a real drop/level-up screen.
    // Works identically networked, since a command is replicated and executed on the same tick by
    // every client - it isn't a local-only shortcut. Two ways in: the field+button below (pick any
    // asset without opening it), or the "Grant To Local Player" button on the asset's own Inspector
    // (WeaponPerkData.DebugGrantToLocalPlayer), which reaches here via
    // WeaponPerkDataDebug.OnGrantRequested since Simulation-side code can't call
    // QuantumRunner/SendCommand directly - see that event's own comment.
    public class WeaponPerkDebugTrigger : QuantumGlobalMonoBehaviour
    {
        [SerializeField] private AssetRef<WeaponPerkData> _perk;

        private void OnEnable()
        {
            WeaponPerkDataDebug.OnGrantRequested += SendGrant;
        }

        private void OnDisable()
        {
            WeaponPerkDataDebug.OnGrantRequested -= SendGrant;
        }

        public void SendGrant(AssetRef<WeaponPerkData> perk)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            {
                Debug.LogWarning("[WeaponPerkDebugTrigger] no local player set up yet");
                return;
            }

            _game.SendCommand(new GrantWeaponPerkCommand
            {
                Perk = perk
            });
        }

        public override void QStart(QuantumGame game)
        {
        }
        public override void QUpdate(QuantumGame game) { }
        public override void QLateUpdate(QuantumGame game) { }
    }
}
