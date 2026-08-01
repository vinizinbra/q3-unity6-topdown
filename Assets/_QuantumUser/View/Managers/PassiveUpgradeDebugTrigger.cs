using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

namespace QuantumUser.View
{
    // Fires a GrantPassiveUpgradeCommand for the local player - lets any PassiveUpgradeData (Lux's
    // Efficient Salvage, Kai's Void Pressure, etc.) be tried out at runtime without a real
    // level-up/pickup screen. Works identically networked, since a command is replicated and
    // executed on the same tick by every client - it isn't a local-only shortcut. Two ways in: the
    // field+button below (pick any asset without opening it), or the "Grant To Local Player" button
    // on the asset's own Inspector (PassiveUpgradeData.DebugGrantToLocalPlayer), which reaches here
    // via PassiveUpgradeDataDebug.OnGrantRequested since Simulation-side code can't call
    // QuantumRunner/SendCommand directly - see that event's own comment.
    public class PassiveUpgradeDebugTrigger : QuantumGlobalMonoBehaviour
    {
        [SerializeField] private AssetRef<PassiveUpgradeData> _upgrade;

        private void OnEnable()
        {
            PassiveUpgradeDataDebug.OnGrantRequested += SendGrant;
        }

        private void OnDisable()
        {
            PassiveUpgradeDataDebug.OnGrantRequested -= SendGrant;
        }

        public void SendGrant(AssetRef<PassiveUpgradeData> upgrade)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            {
                LogHelper.Warn("PassiveUpgradeDebugTrigger", "no local player set up yet");
                return;
            }

            _game.SendCommand(new GrantPassiveUpgradeCommand
            {
                Upgrade = upgrade
            });
        }

        public override void QStart(QuantumGame game)
        {
        }
        public override void QUpdate(QuantumGame game) { }
        public override void QLateUpdate(QuantumGame game) { }
    }
}
