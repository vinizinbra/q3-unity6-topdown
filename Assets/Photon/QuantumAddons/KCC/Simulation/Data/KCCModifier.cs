namespace Quantum
{
	using System;

	/// <summary>
	/// Internal data structure for tracking modifiers. Do not use directly.
	/// </summary>
	public partial struct KCCModifier : IEquatable<KCCModifier>
	{
		public KCCModifier(AssetRef processor, EntityRef entity)
		{
			Processor = processor;
			Entity    = entity;
		}

		public KCCProcessorInfo GetProcessorInfo()
		{
			return new KCCProcessorInfo(EKCCProcessorSource.Modifier, Entity, -1);
		}

		public bool Equals(KCCModifier other)
		{
			return Entity == other.Entity && Processor.Equals(other.Processor);
		}
	}
}


