using NaughtyAttributes;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

namespace QuantumUser.View
{
    // Fires an EquipWeaponCommand for the local player - lets any WeaponDataAsset be equipped onto
    // the local player at runtime without a real pickup/Store/Choose-Weapon screen. Works identically
    // networked, since a command is replicated and executed on the same tick by every client - it
    // isn't a local-only shortcut. Two ways in: the field+button below (pick any asset without
    // opening it), or the "Equip To Local Player" button on the asset's own Inspector
    // (WeaponDataAsset.DebugEquipToLocalPlayer), which reaches here via
    // WeaponDataAssetDebug.OnEquipRequested since Simulation-side code can't call
    // QuantumRunner/SendCommand directly - see that event's own comment. Same shape as
    // WeaponPerkDebugTrigger.
    public class WeaponDataDebugTrigger : QuantumGlobalMonoBehaviour
    {
        [SerializeField] private AssetRef<WeaponDataAsset> _weapon;

        private void OnEnable()
        {
            WeaponDataAssetDebug.OnEquipRequested += SendEquip;
        }

        private void OnDisable()
        {
            WeaponDataAssetDebug.OnEquipRequested -= SendEquip;
        }

        // NaughtyAttributes' [Button] rather than Quantum's EditorButtonAttribute here - this class
        // is View-side (a plain MonoBehaviour), not Simulation, so NaughtyAttributes is actually
        // visible to it. Contrast WeaponDataAsset.DebugEquipToLocalPlayer, which is Simulation-side
        // and has to use EditorButtonAttribute instead - see that method's own comment.
        [Button("Equip To Local Player")]
        public void SendEquip()
        {
            SendEquip(_weapon);
        }

        public void SendEquip(AssetRef<WeaponDataAsset> weapon)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            {
                LogHelper.Warn("WeaponDataDebugTrigger", "no local player set up yet");
                return;
            }

            _game.SendCommand(new EquipWeaponCommand
            {
                WeaponData = weapon
            });
        }

        public override void QStart(QuantumGame game)
        {
        }
        public override void QUpdate(QuantumGame game) { }
        public override void QLateUpdate(QuantumGame game) { }
    }
}
