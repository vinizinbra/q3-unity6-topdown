namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player presses SELF REVIVE in their own SelfReviveWidget (see docs/revive.md) -
    // a deliberate single press/confirm, unlike a teammate revive's own hold/channel (ReviveChannel/
    // ReviveChannelSystem). Processed by PlayerLifeStateSystem, which calls
    // ReviveUtility.TryPerformSelfRevive - fully re-validated there (incapacitated, charges left,
    // not already revived), never trusted from the View alone. Harmless if sent while Alive or with
    // 0 charges - TryPerformSelfRevive just no-ops.
    public unsafe class SelfReviveCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
