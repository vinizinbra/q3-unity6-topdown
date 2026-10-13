namespace Quantum
{
    // Training Zone end-of-course POI ("Finish Training" sign) - only needs the shared interaction
    // prompt; the press itself (EventTrainingFinished -> back to menu) is handled by
    // TrainingZoneGuideWidget. See docs/training-zone.md.
    public class TrainingFinishPoiView : InteractionPromptPoiView
    {
        protected override void QUpdate(QuantumGame game)
        {
        }
    }
}
