using NaughtyAttributes;
using Quantum;
using UnityEngine;

namespace QuantumUser.View
{
    // Fires a GrantSkillUpgradeCommand for the local player - lets any SkillActionData (a
    // one-shot addition like IncreaseAreaSkillAction, or a whole new hit/spawn action) be tried out
    // at runtime without a real level-up/pickup screen. Works identically networked, since a command
    // is replicated and executed on the same tick by every client - it isn't a local-only shortcut.
    // Two ways in: the fields+button below (pick any asset without opening it), or the "Grant To
    // Local Player" button on the asset's own Inspector (SkillActionData.DebugGrantToLocalPlayer),
    // which reaches here via SkillActionDataDebug.OnGrantRequested since Simulation-side code can't
    // call QuantumRunner/SendCommand directly - see that event's own comment.
    public class SkillUpgradeDebugTrigger : QuantumGlobalMonoBehaviour
    {
        [SerializeField] private AssetRef<SkillActionData> _upgrade;

        private void OnEnable()
        {
            SkillActionDataDebug.OnGrantRequested += SendGrant;
        }

        private void OnDisable()
        {
            SkillActionDataDebug.OnGrantRequested -= SendGrant;
        }

        public void SendGrant(AssetRef<SkillActionData> upgrade, SkillSlotId slot)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            {
                Debug.LogWarning("[SkillUpgradeDebugTrigger] no local player set up yet");
                return;
            }

            _game.SendCommand(new GrantSkillUpgradeCommand
            {
                Upgrade = upgrade,
                Slot = slot
            });
        }

        public override void QStart(QuantumGame game)
        {
        }
        public override void QUpdate(QuantumGame game) { }
        public override void QLateUpdate(QuantumGame game) { }
    }
}
