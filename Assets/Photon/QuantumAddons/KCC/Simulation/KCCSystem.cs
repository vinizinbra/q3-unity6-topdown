namespace Quantum
{
	using UnityEngine.Scripting;

	[Preserve]
	public unsafe class KCCSystem : SystemMainThreadFilter<KCCSystem.Filter>, ISignalOnComponentAdded<KCC>, ISignalOnComponentRemoved<KCC>
	{
		public override void Update(Frame frame, ref Filter filter)
		{
			KCCContext context = KCCContext.Get(frame, filter.Entity, filter.KCC);
			filter.KCC->Update(context);
			KCCContext.Return(context);
		}

		void ISignalOnComponentAdded<KCC>.OnAdded(Frame frame, EntityRef entity, KCC* kcc)
		{
			KCCContext context = KCCContext.Get(frame, entity, kcc);
			kcc->Initialize(context);
			KCCContext.Return(context);
		}

		void ISignalOnComponentRemoved<KCC>.OnRemoved(Frame frame, EntityRef entity, KCC* kcc)
		{
			KCCContext context = KCCContext.Get(frame, entity, kcc);
			kcc->Deinitialize(context);
			KCCContext.Return(context);
		}

		public struct Filter
		{
			public EntityRef Entity;
			public KCC*      KCC;
		}
	}
}
