namespace Quantum
{
	using System;
	using System.Collections.Generic;
	using Photon.Deterministic;
	using Quantum.Physics3D;

	/// <summary>
	/// Container for storing overlap hits and metadata. Used mainly for multi-pass depenetration algorithm.
	/// </summary>
	public sealed class KCCOverlapInfo
	{
		public FPVector3           Position;
		public FP                  Radius;
		public FP                  Height;
		public FP                  Extent;
		public LayerMask           LayerMask;
		public QueryOptions        QueryOptions;
		public List<KCCOverlapHit> AllHits      = new List<KCCOverlapHit>();
		public List<KCCOverlapHit> TriggerHits  = new List<KCCOverlapHit>();
		public List<KCCOverlapHit> ColliderHits = new List<KCCOverlapHit>();

		public KCCOverlapHit AddHit(Hit3D physicsHit)
		{
			KCCOverlapHit overlapHit = KCCThreadStaticCache.Get<KCCOverlapHit>();
			overlapHit.Set(physicsHit);

			AllHits.Add(overlapHit);

			if (physicsHit.IsTrigger == true)
			{
				TriggerHits.Add(overlapHit);
			}
			else
			{
				ColliderHits.Add(overlapHit);
			}

			return overlapHit;
		}

		public KCCOverlapHit GetOrAddHit(Hit3D physicsHit)
		{
			KCCOverlapHit overlapHit;

			if (physicsHit.Entity.IsValid == true)
			{
				for (int i = 0, count = AllHits.Count; i < count; ++i)
				{
					overlapHit = AllHits[i];
					if (overlapHit.PhysicsHit.Entity == physicsHit.Entity)
						return overlapHit;
				}
			}
			else if (physicsHit.StaticColliderIndex >= 0)
			{
				for (int i = 0, count = AllHits.Count; i < count; ++i)
				{
					overlapHit = AllHits[i];
					if (overlapHit.PhysicsHit.StaticColliderIndex == physicsHit.StaticColliderIndex)
						return overlapHit;
				}
			}
			else
			{
				throw new NotImplementedException("Unknown collision type.");
			}

			overlapHit = KCCThreadStaticCache.Get<KCCOverlapHit>();
			overlapHit.Set(physicsHit);

			AllHits.Add(overlapHit);

			if (physicsHit.IsTrigger == true)
			{
				TriggerHits.Add(overlapHit);
			}
			else
			{
				ColliderHits.Add(overlapHit);
			}

			return overlapHit;
		}

		public bool FindHit(EKCCCollisionSource source, EntityRef reference, out KCCOverlapHit hit)
		{
			if (source == EKCCCollisionSource.Entity)
			{
				for (int i = 0, count = AllHits.Count; i < count; ++i)
				{
					KCCOverlapHit overlapHit = AllHits[i];
					if (overlapHit.PhysicsHit.Entity == reference)
					{
						hit = overlapHit;
						return true;
					}
				}
			}
			else if (source == EKCCCollisionSource.Collider)
			{
				for (int i = 0, count = AllHits.Count; i < count; ++i)
				{
					KCCOverlapHit overlapHit = AllHits[i];
					if (overlapHit.PhysicsHit.Entity == EntityRef.None && overlapHit.PhysicsHit.StaticColliderIndex == reference.Index)
					{
						hit = overlapHit;
						return true;
					}
				}
			}
			else
			{
				throw new NotImplementedException(source.ToString());
			}

			hit = default;
			return false;
		}

		public void Reset()
		{
			Position     = default;
			Radius       = default;
			Height       = default;
			Extent       = default;
			LayerMask    = default;
			QueryOptions = default;

			for (int i = 0, count = AllHits.Count; i < count; ++i)
			{
				KCCOverlapHit overlapHit = AllHits[i];
				overlapHit.Reset();
				KCCThreadStaticCache.Return(overlapHit);
			}

			AllHits.Clear();
			TriggerHits.Clear();
			ColliderHits.Clear();
		}

		public static KCCOverlapInfo Get()
		{
			return KCCThreadStaticCache<KCCOverlapInfo>.Get();
		}

		public static void Return(KCCOverlapInfo instance)
		{
			instance.Reset();
			KCCThreadStaticCache<KCCOverlapInfo>.Return(instance);
		}
	}
}
