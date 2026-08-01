using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

namespace QuantumUser.View
{
    // Fires a GrantRiftMutationCommand for the local player - lets any RiftMutationData be tried
    // out at runtime without a real level-up/pickup screen. Works identically networked, since a
    // command is replicated and executed on the same tick by every client - it isn't a local-only
    // shortcut. Mirrors GlobalUpgradeDebugTrigger exactly. Two ways in: the field+button below (pick
    // any asset without opening it), or the "Grant To Local Player" button on the asset's own
    // Inspector (RiftMutationData.DebugGrantToLocalPlayer), which reaches here via
    // RiftMutationDataDebug.OnGrantRequested since Simulation-side code can't call
    // QuantumRunner/SendCommand directly - see that event's own comment.
    public class RiftMutationDebugTrigger : QuantumGlobalMonoBehaviour
    {
        [SerializeField] private AssetRef<RiftMutationData> _mutation;

        private void OnEnable()
        {
            RiftMutationDataDebug.OnGrantRequested += SendGrant;
        }

        private void OnDisable()
        {
            RiftMutationDataDebug.OnGrantRequested -= SendGrant;
        }

        public void SendGrant(AssetRef<RiftMutationData> mutation)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            {
                LogHelper.Warn("RiftMutationDebugTrigger", "no local player set up yet");
                return;
            }

            _game.SendCommand(new GrantRiftMutationCommand
            {
                Mutation = mutation
            });
        }

        public override void QStart(QuantumGame game)
        {
        }
        public override void QUpdate(QuantumGame game) { }
        public override void QLateUpdate(QuantumGame game) { }
    }
}
