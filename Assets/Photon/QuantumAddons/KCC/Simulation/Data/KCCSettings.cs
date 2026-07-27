namespace Quantum
{
	using System.Collections.Generic;
	using Photon.Deterministic;

	/// <summary>
	/// Base settings for KCC.
	/// </summary>
	public unsafe class KCCSettings : AssetObject
	{
		[KCCTooltip("Radius of the KCC capsule used for movement.")]
		public FP Radius = FP._0_33;
		[KCCTooltip("Height of the KCC capsule used for movement.")]
		public FP Height = FP._1_75;
		[KCCTooltip("Defines additional radius extent for ground detection and processors tracking. Recommended range is 10-20% of radius.\n" +
		"• Low value decreases stability and has potential performance impact when executing additional checks.\n" +
		"• High value increases stability at the cost of increased sustained performance impact.")]
		public FP Extent = FP._0_03;
		[KCCTooltip("Layer mask the KCC collides with.")]
		public LayerMask CollisionLayerMask;
		[KCCTooltip("Penetration in single move step is corrected in multiple steps which results in higher overall depenetration quality.")]
		public int MaxPenetrationSteps = 3;
		[KCCTooltip("Controls maximum distance the KCC moves in a single CCD step. Recommended range is 10% - 90% of the radius. Use lower values if the character passes through geometry.\n" +
		"CCD Max Step Distance = Radius * CCD Radius Multiplier")]
    	public FP CCDRadiusMultiplier = FP._0_75;
		[KCCTooltip("List of default processors which are always processed.")]
		public List<AssetRef<KCCProcessor>> Processors;

		public List<KCCProcessor> RuntimeProcessors { get; private set; }

		public override void Loaded(IResourceManager resourceManager, Native.Allocator allocator)
		{
			List<KCCProcessor> runtimeProcessors = new List<KCCProcessor>();

			foreach (AssetRef<KCCProcessor> processorAssetRef in Processors)
			{
				KCCProcessor processor = resourceManager.GetAsset(processorAssetRef.Id) as KCCProcessor;
				if (processor != null)
				{
					runtimeProcessors.Add(processor);
				}
			}

			RuntimeProcessors = runtimeProcessors;
		}
	}
}
