namespace Quantum
{
	using System;
	using Quantum.Collections;

	public static unsafe partial class KCCUtility
	{
		public static bool ResolveProcessorAsset(Frame frame, ref KCCCollision collision)
		{
			if (collision.Source == EKCCCollisionSource.Entity)
			{
				if (frame.Unsafe.TryGetPointer(collision.Reference, out KCCProcessorLink* processorLink) == true)
				{
					collision.Processor = new AssetRef(processorLink->Processor.Id);
					return true;
				}
			}
			else if (collision.Source == EKCCCollisionSource.Collider)
			{
				collision.Processor = frame.Map.StaticColliders3D[collision.Reference.Index].StaticData.Asset;
				return true;
			}
			else
			{
				throw new NotImplementedException(collision.Source.ToString());
			}

			collision.Processor = default;
			return false;
		}

		public static bool ResolveProcessor<T>(Frame frame, AssetRef processorAsset, out T processor) where T : KCCProcessor
		{
			if (processorAsset.IsValid == true && frame.TryFindAsset(processorAsset, out T kccProcessor) == true)
			{
				processor = kccProcessor;
				return ReferenceEquals(processor, null) == false;
			}

			processor = default;
			return false;
		}

		public static bool ResolveProcessor<T>(Frame frame, AssetRef<T> processorAsset, out T processor) where T : KCCProcessor
		{
			if (processorAsset.IsValid == true && frame.TryFindAsset(processorAsset, out T kccProcessor) == true)
			{
				processor = kccProcessor;
				return ReferenceEquals(processor, null) == false;
			}

			processor = default;
			return false;
		}

		public static bool HasCollision(QList<KCCCollision> collisions, EKCCCollisionSource source, EntityRef reference)
		{
			for (int i = 0, count = collisions.Count; i < count; ++i)
			{
				KCCCollision collision = collisions[i];
				if (collision.Source == source && collision.Reference == reference)
					return true;
			}

			return false;
		}
	}
}
