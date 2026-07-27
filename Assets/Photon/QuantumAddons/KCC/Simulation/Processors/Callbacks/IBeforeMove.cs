namespace Quantum
{
	public interface IBeforeMove
	{
		/// <summary>
		/// This callback is invoked before KCC movement - on the beginning of KCC.Update().
		/// Not called if KCC.IsActive is set to false.
		/// </summary>
		/// <param name="context">Reference to KCC context.</param>
		/// <param name="processorInfo">Contains information about the processor registration source and a collider/entity that is referencing the processor.</param>
		void BeforeMove(KCCContext context, KCCProcessorInfo processorInfo);
	}
}


