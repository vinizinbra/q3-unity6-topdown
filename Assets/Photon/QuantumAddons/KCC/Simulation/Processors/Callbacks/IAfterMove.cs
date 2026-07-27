namespace Quantum
{
	public interface IAfterMove
	{
		/// <summary>
		/// This callback is invoked after KCC movement - on the end of KCC.Update().
		/// Not called if KCC.IsActive is set to false.
		/// </summary>
		/// <param name="context">Reference to KCC context.</param>
		/// <param name="processorInfo">Contains information about the processor registration source and a collider/entity that is referencing the processor.</param>
		void AfterMove(KCCContext context, KCCProcessorInfo processorInfo);
	}
}


