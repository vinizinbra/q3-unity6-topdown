namespace Quantum
{
	using System.Collections.Generic;
	using Photon.Deterministic;
	using Quantum.Physics3D;

	/// <summary>
	/// Container for storing shape-cast hits and metadata.
	/// </summary>
	public sealed class KCCShapeCastInfo
	{
		public FPVector3             Position;
		public FP                    Radius;
		public FP                    Height;
		public FP                    Extent;
		public FPVector3             Direction;
		public FP                    MaxDistance;
		public LayerMask             LayerMask;
		public QueryOptions          QueryOptions;
		public List<KCCShapeCastHit> AllHits      = new List<KCCShapeCastHit>();
		public List<KCCShapeCastHit> TriggerHits  = new List<KCCShapeCastHit>();
		public List<KCCShapeCastHit> ColliderHits = new List<KCCShapeCastHit>();

		public void AddHit(Hit3D physicsHit)
		{
			KCCShapeCastHit shapeCastHit = KCCThreadStaticCache.Get<KCCShapeCastHit>();
			shapeCastHit.Set(physicsHit);

			AllHits.Add(shapeCastHit);

			if (physicsHit.IsTrigger == true)
			{
				TriggerHits.Add(shapeCastHit);
			}
			else
			{
				ColliderHits.Add(shapeCastHit);
			}
		}

		public void Sort()
		{
			int count = AllHits.Count;
			if (count <= 1)
				return;

			List<KCCShapeCastHit> hits       = AllHits;
			bool                  isSorted   = false;
			bool                  hasChanged = false;
			int                   leftIndex;
			int                   rightIndex;
			FP                    leftDistance;
			FP                    rightDistance;

			while (isSorted == false)
			{
				isSorted = true;

				leftIndex    = 0;
				rightIndex   = 1;
				leftDistance = hits[leftIndex].PhysicsHit.OverlapPenetration;

				while (rightIndex < count)
				{
					rightDistance = hits[rightIndex].PhysicsHit.OverlapPenetration;

					if (leftDistance <= rightDistance)
					{
						leftDistance = rightDistance;
					}
					else
					{
						KCCShapeCastHit leftHit = hits[leftIndex];

						hits[leftIndex]  = hits[rightIndex];
						hits[rightIndex] = leftHit;

						isSorted   = false;
						hasChanged = true;
					}

					++leftIndex;
					++rightIndex;
				}
			}

			if (hasChanged == true)
			{
				KCCShapeCastHit shapeCastHit;

				for (int i = 0; i < count; ++i)
				{
					shapeCastHit = AllHits[i];

					if (shapeCastHit.PhysicsHit.IsTrigger == true)
					{
						TriggerHits.Add(shapeCastHit);
					}
					else
					{
						ColliderHits.Add(shapeCastHit);
					}
				}
			}
		}

		public void Reset()
		{
			Position     = default;
			Radius       = default;
			Height       = default;
			Extent       = default;
			Direction    = default;
			MaxDistance  = default;
			LayerMask    = default;
			QueryOptions = default;

			for (int i = 0, count = AllHits.Count; i < count; ++i)
			{
				KCCShapeCastHit shapeCastHit = AllHits[i];
				shapeCastHit.Reset();
				KCCThreadStaticCache.Return(shapeCastHit);
			}

			AllHits.Clear();
			TriggerHits.Clear();
			ColliderHits.Clear();
		}

		public static KCCShapeCastInfo Get()
		{
			return KCCThreadStaticCache<KCCShapeCastInfo>.Get();
		}

		public static void Return(KCCShapeCastInfo instance)
		{
			instance.Reset();
			KCCThreadStaticCache<KCCShapeCastInfo>.Return(instance);
		}
	}
}
