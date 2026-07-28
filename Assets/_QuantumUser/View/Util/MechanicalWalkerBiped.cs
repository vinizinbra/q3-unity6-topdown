using UnityEngine;

namespace QuantumUser.View.Util
{
    // Mutual step-lock for a pair of MechanicalLegRigs: a leg is only allowed to start a new
    // step while the other is planted, which turns two independently-stepping legs into a
    // readable alternating walk instead of both lifting at once. Runs in Update (not
    // LateUpdate) so the lock is guaranteed to land before each leg's own LateUpdate evaluates
    // it, regardless of GameObject/script execution order.
    public class MechanicalWalkerBiped : MonoBehaviour
    {
        [SerializeField] private MechanicalLegRig leftLeg;
        [SerializeField] private MechanicalLegRig rightLeg;

        private void Update()
        {
            leftLeg.ExternalStepLock = rightLeg.IsStepping;
            rightLeg.ExternalStepLock = leftLeg.IsStepping;
        }
    }
}
