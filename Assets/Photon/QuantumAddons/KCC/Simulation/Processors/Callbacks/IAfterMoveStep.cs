namespace Quantum
{
	public interface IAfterMoveStep
	{
		/// <summary>
		/// This callback is invoked after each KCC move step - this happens if the KCC
		/// moves too fast and the continuous collision detection (CCD) algorithm splits translation vector into multiple smaller steps.
		/// During CCD movement the KCCData.DeltaTime is scaled proportionally to size of the step.
		/// Not called if KCC.IsActive is set to false.
		/// </summary>
		/// <param name="context">Reference to KCC context.</param>
		/// <param name="processorInfo">Contains information about the processor registration source and a collider/entity that is referencing the processor.</param>
		/// <param name="overlapInfo">Reference to all physics overlap hits in current move step and other metadata.</param>
		void AfterMoveStep(KCCContext context, KCCProcessorInfo processorInfo, KCCOverlapInfo overlapInfo);
	}
}


