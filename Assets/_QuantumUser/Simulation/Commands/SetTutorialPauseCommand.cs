namespace Quantum
{
    using Photon.Deterministic;

    // Sent by the View around a solo-only tutorial popup (HowToPlayPopup/FirstBreakPopup) - Paused
    // = true right as the popup opens (InMatchTutorialManager, reacting locally to
    // EventGameStateChanged - the sim itself has no notion of "tutorial", it's a pure local/View
    // concern), Paused = false when the popup closes (TutorialPopup.Close). See TutorialSystem for
    // the handler.
    public unsafe class SetTutorialPauseCommand : DeterministicCommand
    {
        public bool Paused;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref Paused);
        }
    }
}
