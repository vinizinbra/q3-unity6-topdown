namespace Quantum
{
	using System;
	using Quantum.Physics3D;

	/// <summary>
	/// Container that provides access to Frame, KCC component, settings, reference to its entity and custom user data.
	/// An instance is passed as argument in KCC methods and callbacks and provides an easy way to extend with custom functionality.
	/// </summary>
	public unsafe partial class KCCContext
	{
		public Frame                         Frame;
		public EntityRef                     Entity;
		public KCC*                          KCC;
		public KCCSettings                   Settings;
		public KCCStageInfo                  StageInfo = new KCCStageInfo();
		public Func<KCCContext, Hit3D, bool> ResolveCollision;

		/// <summary>
		/// Called before KCC initialization (ISignalOnComponentAdded), before KCC deinitialization (ISignalOnComponentRemoved) and before the KCC.Update(). Do not use.
		/// </summary>
		public void Prepare(Frame frame, EntityRef entity, KCC* kcc)
		{
			Frame    = frame;
			Entity   = entity;
			KCC      = kcc;
			Settings = frame.FindAsset(kcc->Settings);

			StageInfo.Reset();

			PrepareUserContext();
		}

		/// <summary>
		/// Called before returning KCCContext back to cache. Do not use.
		/// </summary>
		public void Reset()
		{
			ResetUserContext();

			Frame            = default;
			Entity           = default;
			KCC              = default;
			Settings         = default;
			ResolveCollision = default;

			StageInfo.Reset();
		}

		public static KCCContext Get()
		{
			return KCCThreadStaticCache<KCCContext>.Get();
		}

		public static KCCContext Get(Frame frame, EntityRef entity)
		{
			if (frame.Unsafe.TryGetPointer<KCC>(entity, out KCC* kcc) == false)
				return default;

			return Get(frame, entity, kcc);
		}

		public static KCCContext Get(Frame frame, EntityRef entity, KCC* kcc)
		{
			KCCContext context = KCCThreadStaticCache<KCCContext>.Get();
			context.Prepare(frame, entity, kcc);
			return context;
		}

		public static void Return(KCCContext instance)
		{
			instance.Reset();
			KCCThreadStaticCache<KCCContext>.Return(instance);
		}

		/// <summary>
		/// Use this to prepare your data in KCCContext.
		/// </summary>
		partial void PrepareUserContext();

		/// <summary>
		/// Use this to reset your data in KCCContext.
		/// </summary>
		partial void ResetUserContext();
	}
}
