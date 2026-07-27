namespace Quantum
{
	using Photon.Deterministic;

	public abstract class KCCProcessor : AssetObject
	{
		/// <summary>
		/// Processors with higher priority are executed first.
		/// If two processors have same priority, they are sorted by their source category: Settings > Collision > Modifier.
		/// If two processors from same category have same priority, they are sorted by the time of registration to KCC - first registered is first executed.
		/// Priority affects all stages - IBeforeMove, IAfterMoveStep, IAfterMove.
		/// </summary>
		public virtual FP GetPriority(KCCContext context, KCCProcessorInfo processorInfo) => FP._0;

		/// <summary>
		/// This callback is invoked:
		/// <list type="bullet">
		/// <item><description>When the KCC starts colliding with a static collider/trigger with a KCCProcessor assigned to AssetObject.</description></item>
		/// <item><description>When the KCC starts colliding with an entity which has KCCProcessorSource component and valid KCCProcessor linked.</description></item>
		/// <item><description>When a custom processor is registered to the KCC using KCC.AddModifier().</description></item>
		/// </list>
		/// Return value:
		/// <list type="bullet">
		/// <item><description>True - the KCC starts "tracking" this processor and the processor starts getting callbacks - IBeforeMove, IAfterMoveStep, IAfterMove during KCC update and OnExit() when the collision ends.</description></item>
		/// <item><description>False - the KCC invokes OnEnter() next tick if the collision is still valid. This can be used to defer processor logic until some other condition is met.</description></item>
		/// </list>
		/// The return value is ignored if the processor is added via KCC.AddModifier() with 'forceAdd' parameter set to true.
		/// </summary>
		/// <param name="context">Reference to KCC context.</param>
		/// <param name="processorInfo">Contains information about the processor registration source and a collider/entity that is referencing this processor.</param>
		/// <param name="overlapHit">Reference to a collider/entity overlap hit which references this processor. The value is null for manually registered processors (modifiers) and processors linked in KCC settings.</param>
		public virtual bool OnEnter(KCCContext context, KCCProcessorInfo processorInfo, KCCOverlapHit overlapHit) => true;

		/// <summary>
		/// This callback is invoked:
		/// <list type="bullet">
		/// <item><description>When the KCC stops colliding with a static collider/trigger with a KCCProcessor assigned to AssetObject.</description></item>
		/// <item><description>When the KCC stops colliding with an entity which has KCCProcessorSource component and valid KCCProcessor linked.</description></item>
		/// <item><description>When a custom processor is unregistered from the KCC using KCC.RemoveModifier().</description></item>
		/// </list>
		/// Return value:
		/// <list type="bullet">
		/// <item><description>True - the KCC stops "tracking" this processor and the processor stops getting callbacks - IBeforeMove, IAfterMoveStep, IAfterMove.</description></item>
		/// <item><description>False - the KCC invokes OnExit() next tick if the collision is still invalid. This can be used to defer stopping processor logic until some other condition is met.</description></item>
		/// </list>
		/// The return value is ignored if the processor is removed via KCC.RemoveModifier() with 'forceRemove' parameter set to true.
		/// </summary>
		/// <param name="context">Reference to KCC context.</param>
		/// <param name="processorInfo">Contains information about the processor registration source and a collider/entity that is referencing this processor.</param>
		public virtual bool OnExit(KCCContext context, KCCProcessorInfo processorInfo) => true;
	}
}
