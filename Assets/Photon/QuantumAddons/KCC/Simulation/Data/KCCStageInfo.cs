namespace Quantum
{
	using System;
	using System.Collections.Generic;
	using Photon.Deterministic;
	using Quantum.Collections;

	/// <summary>
	/// Internal helper container for execution of processor callbacks. Do not use directly.
	/// </summary>
	public sealed unsafe class KCCStageInfo
	{
		public List<KCCProcessor>     Processors     = new List<KCCProcessor>();
		public List<KCCProcessorInfo> ProcessorInfos = new List<KCCProcessorInfo>();

		private List<KCCProcessor>     _cachedProcessors     = new List<KCCProcessor>();
		private List<KCCProcessorInfo> _cachedProcessorInfos = new List<KCCProcessorInfo>();

		[ThreadStatic]
		private static FP[] _sortPriorities;

		public bool HasProcessor<T>() where T : class
		{
			for (int i = 0, count = Processors.Count; i < count; ++i)
			{
				if (Processors[i] is T)
					return true;
			}

			return false;
		}

		public bool TryGetProcessor<T>(out T processor, out KCCProcessorInfo processorInfo) where T : class
		{
			for (int i = 0, count = Processors.Count; i < count; ++i)
			{
				if (Processors[i] is T targetProcessor)
				{
					processor     = targetProcessor;
					processorInfo = ProcessorInfos[i];

					return true;
				}
			}

			processor     = default;
			processorInfo = default;

			return false;
		}

		public void SuppressProcessor(KCCProcessor processor)
		{
			for (int i = 0, count = Processors.Count; i < count; ++i)
			{
				if (ReferenceEquals(Processors[i], processor) == true)
				{
					Processors[i]     = default;
					ProcessorInfos[i] = default;
				}
			}
		}

		public void SuppressProcessors<T>() where T : class
		{
			for (int i = 0, count = Processors.Count; i < count; ++i)
			{
				if (Processors[i] is T)
				{
					Processors[i]     = default;
					ProcessorInfos[i] = default;
				}
			}
		}

		public void Reset()
		{
			_cachedProcessors.Clear();
			_cachedProcessorInfos.Clear();

			Processors.Clear();
			ProcessorInfos.Clear();
		}

		public void CacheProcessors(KCCContext context)
		{
			_cachedProcessors.Clear();
			_cachedProcessorInfos.Clear();

			List<KCCProcessor> runtimeProcessors = context.Settings.RuntimeProcessors;
			for (int i = 0, count = runtimeProcessors.Count; i < count; ++i)
			{
				_cachedProcessors.Add(runtimeProcessors[i]);
				_cachedProcessorInfos.Add(KCCProcessorInfo.Default);
			}

			Frame frame = context.Frame;

			QList<KCCCollision> collisions = frame.ResolveList(context.KCC->Collisions);
			for (int i = 0, count = collisions.Count; i < count; ++i)
			{
				KCCCollision collision = collisions[i];
				if (KCCUtility.ResolveProcessor(frame, collision.Processor, out KCCProcessor processor) == true)
				{
					_cachedProcessors.Add(processor);
					_cachedProcessorInfos.Add(collision.GetProcessorInfo());
				}
			}

			QList<KCCModifier> modifiers = frame.ResolveList(context.KCC->Modifiers);
			for (int i = 0, count = modifiers.Count; i < count; ++i)
			{
				KCCModifier modifier = modifiers[i];
				if (KCCUtility.ResolveProcessor(frame, modifier.Processor, out KCCProcessor processor) == true)
				{
					_cachedProcessors.Add(processor);
					_cachedProcessorInfos.Add(modifier.GetProcessorInfo());
				}
			}

			SortProcessors(context, _cachedProcessors, _cachedProcessorInfos);
		}

		public void PrepareStageProcessors()
		{
			Processors.Clear();
			Processors.AddRange(_cachedProcessors);

			ProcessorInfos.Clear();
			ProcessorInfos.AddRange(_cachedProcessorInfos);
		}

		private static void SortProcessors(KCCContext context, List<KCCProcessor> processors, List<KCCProcessorInfo> processorInfos)
		{
			int count = processors.Count;
			if (count <= 1)
				return;

			if (_sortPriorities == null || _sortPriorities.Length < count)
			{
				_sortPriorities = new FP[count * 2];
			}

			bool             isSorted = false;
			int              leftIndex;
			FP               leftPriority;
			KCCProcessor     leftProcessor;
			KCCProcessorInfo leftProcessorInfo;
			int              rightIndex;
			FP               rightPriority;
			KCCProcessor     rightProcessor;
			KCCProcessorInfo rightProcessorInfo;

			for (int i = 0; i < count; ++i)
			{
				_sortPriorities[i] = processors[i].GetPriority(context, processorInfos[i]);
			}

			while (isSorted == false)
			{
				isSorted = true;

				leftIndex     = 0;
				rightIndex    = 1;
				leftPriority = _sortPriorities[leftIndex];

				while (rightIndex < count)
				{
					rightPriority = _sortPriorities[rightIndex];

					if (leftPriority >= rightPriority)
					{
						leftPriority = rightPriority;
					}
					else
					{
						_sortPriorities[leftIndex]  = rightPriority;
						_sortPriorities[rightIndex] = leftPriority;

						leftProcessor      = processors[leftIndex];
						leftProcessorInfo  = processorInfos[leftIndex];
						rightProcessor     = processors[rightIndex];
						rightProcessorInfo = processorInfos[rightIndex];

						processors[leftIndex]      = rightProcessor;
						processorInfos[leftIndex]  = rightProcessorInfo;
						processors[rightIndex]     = leftProcessor;
						processorInfos[rightIndex] = leftProcessorInfo;

						isSorted = false;
					}

					++leftIndex;
					++rightIndex;
				}
			}
		}
	}
}
