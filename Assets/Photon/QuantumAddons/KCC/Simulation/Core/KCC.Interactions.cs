namespace Quantum
{
	using Photon.Deterministic;
	using Quantum.Collections;

	unsafe partial struct KCC
	{
		/// <summary>
		/// Add/remove entity to/from custom ignore list.
		/// </summary>
		/// <returns><c>True</c> if there is a change in the ignore list.</returns>
		public bool SetIgnoreCollider(Frame frame, EntityRef entity, bool ignore)
		{
			QHashSet<KCCIgnore> ignores = frame.ResolveHashSet(Ignores);
			if (ignore == true)
			{
				return ignores.Add(new KCCIgnore(entity));
			}
			else
			{
				return ignores.Remove(new KCCIgnore(entity));
			}
		}

		/// <summary>
		/// Add/remove static collider to/from custom ignore list.
		/// </summary>
		/// <returns><c>True</c> if there is a change in the ignore list.</returns>
		public bool SetIgnoreCollider(Frame frame, int colliderIndex, bool ignore)
		{
			QHashSet<KCCIgnore> ignores = frame.ResolveHashSet(Ignores);
			if (ignore == true)
			{
				return ignores.Add(new KCCIgnore(colliderIndex));
			}
			else
			{
				return ignores.Remove(new KCCIgnore(colliderIndex));
			}
		}

		/// <summary>
		/// Register custom modifier processor to <c>KCC.Modifiers</c>.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC update.</param>
		/// <param name="processorAsset">Reference to a KCCProcessor asset.</param>
		/// <param name="forceAdd">Ignore result from <c>KCCProcessor.OnEnter()</c> and force add the modifier. Be careful with this option.</param>
		public bool AddModifier<T>(KCCContext context, AssetRef<T> processorAsset, bool forceAdd = false) where T : KCCProcessor
		{
			if (processorAsset.IsValid == false)
				return false;

			if (KCCUtility.ResolveProcessor(context.Frame, processorAsset, out T processor) == true)
			{
				KCCModifier modifier = new KCCModifier(new AssetRef(processorAsset.Id), EntityRef.None);
				if (processor.OnEnter(context, modifier.GetProcessorInfo(), default) == true || forceAdd == true)
				{
					context.Frame.ResolveList(Modifiers).Add(modifier);
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Register custom modifier processor to <c>KCC.Modifiers</c>.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC update.</param>
		/// <param name="processorAsset">Reference to a KCCProcessor asset.</param>
		/// <param name="entity">Reference to an entity assigned to the processor.</param>
		/// <param name="forceAdd">Ignore result from <c>KCCProcessor.OnEnter()</c> and force add the modifier. Be careful with this option.</param>
		public bool AddModifier<T>(KCCContext context, AssetRef<T> processorAsset, EntityRef entity, bool forceAdd = false) where T : KCCProcessor
		{
			if (processorAsset.IsValid == false)
				return false;

			if (KCCUtility.ResolveProcessor(context.Frame, processorAsset, out T processor) == true)
			{
				KCCModifier modifier = new KCCModifier(new AssetRef(processorAsset.Id), entity);
				if (processor.OnEnter(context, modifier.GetProcessorInfo(), default) == true || forceAdd == true)
				{
					context.Frame.ResolveList(Modifiers).Add(modifier);
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Try to register custom modifier processor to <c>KCC.Modifiers</c>.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC update.</param>
		/// <param name="asset">Reference to a KCCProcessor asset.</param>
		/// <param name="forceAdd">Ignore result from <c>KCCProcessor.OnEnter()</c> and force add the modifier. Be careful with this option.</param>
		public bool TryAddModifier(KCCContext context, AssetRef asset, bool forceAdd = false)
		{
			if (asset.IsValid == false)
				return false;

			if (KCCUtility.ResolveProcessor(context.Frame, asset, out KCCProcessor processor) == true)
			{
				KCCModifier modifier = new KCCModifier(asset, EntityRef.None);
				if (processor.OnEnter(context, modifier.GetProcessorInfo(), default) == true || forceAdd == true)
				{
					context.Frame.ResolveList(Modifiers).Add(modifier);
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Try to register custom modifier processor to <c>KCC.Modifiers</c>.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC update.</param>
		/// <param name="entity">Reference to an entity with <c>KCCProcessorSource</c> component.</param>
		/// <param name="forceAdd">Ignore result from <c>KCCProcessor.OnEnter()</c> and force add the modifier. Be careful with this option.</param>
		public bool TryAddModifier(KCCContext context, EntityRef entity, bool forceAdd = false)
		{
			if (entity.IsValid == false)
				return false;

			if (context.Frame.Unsafe.TryGetPointer(entity, out KCCProcessorLink* processorLink) == false)
				return false;

			AssetRef processorAsset = new AssetRef(processorLink->Processor.Id);

			if (KCCUtility.ResolveProcessor(context.Frame, processorAsset, out KCCProcessor processor) == true)
			{
				KCCModifier modifier = new KCCModifier(processorAsset, entity);
				if (processor.OnEnter(context, modifier.GetProcessorInfo(), default) == true || forceAdd == true)
				{
					context.Frame.ResolveList(Modifiers).Add(modifier);
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Unregister custom modifier processor from <c>KCC.Modifiers</c>. Removed processor won't execute any pending callbacks.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC update.</param>
		/// <param name="processorAsset">Reference to a KCCProcessor asset.</param>
		/// <param name="forceRemove">Ignore result from <c>KCCProcessor.OnExit()</c> and force remove the modifier. Be careful with this option.</param>
		public bool RemoveModifier<T>(KCCContext context, AssetRef<T> processorAsset, bool forceRemove = false) where T : KCCProcessor
		{
			if (processorAsset.IsValid == false)
				return false;

			QList<KCCModifier> modifiers = context.Frame.ResolveList(Modifiers);
			for (int i = modifiers.Count - 1; i >= 0; --i)
			{
				KCCModifier modifier = modifiers[i];
				if (modifier.Processor.Id == processorAsset.Id)
				{
					if (KCCUtility.ResolveProcessor(context.Frame, modifier.Processor, out T processor) == true)
					{
						if (processor.OnExit(context, modifier.GetProcessorInfo()) == true || forceRemove == true)
						{
							modifiers.RemoveAt(i);
							return true;
						}
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Unregister custom modifier processor from <c>KCC.Modifiers</c>. Removed processor won't execute any pending callbacks.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC update.</param>
		/// <param name="processorAsset">Reference to a KCCProcessor asset.</param>
		/// <param name="entity">Reference to an entity assigned to the processor.</param>
		/// <param name="forceRemove">Ignore result from <c>KCCProcessor.OnExit()</c> and force remove the modifier. Be careful with this option.</param>
		public bool RemoveModifier<T>(KCCContext context, AssetRef<T> processorAsset, EntityRef entity, bool forceRemove = false) where T : KCCProcessor
		{
			if (processorAsset.IsValid == false)
				return false;

			QList<KCCModifier> modifiers = context.Frame.ResolveList(Modifiers);
			for (int i = modifiers.Count - 1; i >= 0; --i)
			{
				KCCModifier modifier = modifiers[i];
				if (modifier.Processor.Id == processorAsset.Id && modifier.Entity == entity)
				{
					if (KCCUtility.ResolveProcessor(context.Frame, modifier.Processor, out T processor) == true)
					{
						if (processor.OnExit(context, modifier.GetProcessorInfo()) == true || forceRemove == true)
						{
							modifiers.RemoveAt(i);
							return true;
						}
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Try to unregister custom modifier processor from <c>KCC.Modifiers</c>. Removed processor won't execute any pending callbacks.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC update.</param>
		/// <param name="asset">Reference to a KCCProcessor asset.</param>
		/// <param name="forceRemove">Ignore result from <c>KCCProcessor.OnExit()</c> and force remove the modifier. Be careful with this option.</param>
		public bool TryRemoveModifier(KCCContext context, AssetRef asset, bool forceRemove = false)
		{
			if (asset.IsValid == false)
				return false;

			QList<KCCModifier> modifiers = context.Frame.ResolveList(Modifiers);
			for (int i = modifiers.Count - 1; i >= 0; --i)
			{
				KCCModifier modifier = modifiers[i];
				if (modifier.Processor.Id == asset.Id)
				{
					if (KCCUtility.ResolveProcessor(context.Frame, modifier.Processor, out KCCProcessor processor) == true)
					{
						if (processor.OnExit(context, modifier.GetProcessorInfo()) == true || forceRemove == true)
						{
							modifiers.RemoveAt(i);
							return true;
						}
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Try to unregister custom modifier processor from <c>KCC.Modifiers</c>. Removed processor won't execute any pending callbacks.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC update.</param>
		/// <param name="entity">Reference to an entity assigned to a modifier.</param>
		/// <param name="forceRemove">Ignore result from <c>KCCProcessor.OnExit()</c> and force remove the modifier. Be careful with this option.</param>
		public bool TryRemoveModifier(KCCContext context, EntityRef entity, bool forceRemove = false)
		{
			if (entity.IsValid == false)
				return false;

			QList<KCCModifier> modifiers = context.Frame.ResolveList(Modifiers);
			for (int i = modifiers.Count - 1; i >= 0; --i)
			{
				KCCModifier modifier = modifiers[i];
				if (modifier.Entity == entity)
				{
					if (KCCUtility.ResolveProcessor(context.Frame, modifier.Processor, out KCCProcessor processor) == true)
					{
						if (processor.OnExit(context, modifier.GetProcessorInfo()) == true || forceRemove == true)
						{
							modifiers.RemoveAt(i);
							return true;
						}
					}
				}
			}

			return false;
		}

		private void UpdateCollisions(KCCContext context, KCCOverlapInfo overlapInfo, FPVector3 targetPosition)
		{
			QList<KCCCollision> collisions = context.Frame.ResolveList(Collisions);

			for (int i = collisions.Count - 1; i >= 0; --i)
			{
				KCCCollision collision = collisions[i];

				if (overlapInfo.FindHit(collision.Source, collision.Reference, out KCCOverlapHit overlapHit) == false)
				{
					bool hasProcessor = KCCUtility.ResolveProcessor(context.Frame, collision.Processor, out KCCProcessor processor);
					if (hasProcessor == true)
					{
						KCCProcessorInfo processorInfo = collision.GetProcessorInfo();
						if (processor.OnExit(context, processorInfo) == true)
						{
							context.StageInfo.SuppressProcessor(processor);
							collisions.RemoveAt(i);
						}
					}
					else
					{
						collisions.RemoveAt(i);
					}
				}
			}

			for (int i = 0, count = overlapInfo.AllHits.Count; i < count; ++i)
			{
				KCCOverlapHit       overlapHit          = overlapInfo.AllHits[i];
				EKCCCollisionSource collisionSource     = default;
				EntityRef           collisionReference  = default;

				if (overlapHit.PhysicsHit.Entity != EntityRef.None)
				{
					collisionSource    = EKCCCollisionSource.Entity;
					collisionReference = overlapHit.PhysicsHit.Entity;
				}
				else if (overlapHit.PhysicsHit.StaticColliderIndex >= 0)
				{
					collisionSource          = EKCCCollisionSource.Collider;
					collisionReference.Index = overlapHit.PhysicsHit.StaticColliderIndex;
				}

				if (KCCUtility.HasCollision(collisions, collisionSource, collisionReference) == false)
				{
					KCCCollision collision = new KCCCollision();
					collision.Source    = collisionSource;
					collision.Reference = collisionReference;

					KCCUtility.ResolveProcessorAsset(context.Frame, ref collision);

					bool hasProcessor = KCCUtility.ResolveProcessor(context.Frame, collision.Processor, out KCCProcessor processor);
					if (hasProcessor == true)
					{
						KCCProcessorInfo processorInfo = collision.GetProcessorInfo();
						if (processor.OnEnter(context, processorInfo, overlapHit) == true)
						{
							collisions.Add(collision);
						}
					}
					else
					{
						collisions.Add(collision);
					}
				}
			}
		}

		private void ForceRemoveAllCollisions(KCCContext context)
		{
			QList<KCCCollision> collisions = context.Frame.ResolveList(Collisions);
			while (collisions.Count > 0)
			{
				int          index     = collisions.Count - 1;
				KCCCollision collision = collisions[index];

				if (KCCUtility.ResolveProcessor(context.Frame, collision.Processor, out KCCProcessor processor) == true)
				{
					KCCProcessorInfo processorInfo = collision.GetProcessorInfo();
					processor.OnExit(context, processorInfo);
				}

				collisions.RemoveAt(index);
			}
		}

		private void ForceRemoveAllModifiers(KCCContext context)
		{
			QList<KCCModifier> modifiers = context.Frame.ResolveList(Modifiers);
			while (modifiers.Count > 0)
			{
				int         index    = modifiers.Count - 1;
				KCCModifier modifier = modifiers[index];

				if (KCCUtility.ResolveProcessor(context.Frame, modifier.Processor, out KCCProcessor processor) == true)
				{
					KCCProcessorInfo processorInfo = modifier.GetProcessorInfo();
					processor.OnExit(context, processorInfo);
				}

				modifiers.RemoveAt(index);
			}
		}
	}
}


