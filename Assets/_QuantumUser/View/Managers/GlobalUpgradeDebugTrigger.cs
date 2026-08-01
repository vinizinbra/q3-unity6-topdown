using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

namespace QuantumUser.View
{
    // Fires a GrantGlobalUpgradeCommand for the local player - lets any GlobalUpgradeData be tried
    // out at runtime without a real level-up/pickup screen. Works identically networked, since a
    // command is replicated and executed on the same tick by every client - it isn't a local-only
    // shortcut. Two ways in: the field+button below (pick any asset without opening it), or the
    // "Grant To Local Player" button on the asset's own Inspector
    // (GlobalUpgradeData.DebugGrantToLocalPlayer), which reaches here via
    // GlobalUpgradeDataDebug.OnGrantRequested since Simulation-side code can't call
    // QuantumRunner/SendCommand directly - see that event's own comment.
    public class GlobalUpgradeDebugTrigger : QuantumGlobalMonoBehaviour
    {
        [SerializeField] private AssetRef<GlobalUpgradeData> _upgrade;

        private void OnEnable()
        {
            GlobalUpgradeDataDebug.OnGrantRequested += SendGrant;
        }

        private void OnDisable()
        {
            GlobalUpgradeDataDebug.OnGrantRequested -= SendGrant;
        }

        public void SendGrant(AssetRef<GlobalUpgradeData> upgrade)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            {
                LogHelper.Warn("GlobalUpgradeDebugTrigger", "no local player set up yet");
                return;
            }

            _game.SendCommand(new GrantGlobalUpgradeCommand
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
