namespace Quantum
{
	using System.Collections.Generic;

	unsafe partial struct KCC
	{
		private void InvokeBeforeMove(KCCContext context)
		{
			context.StageInfo.PrepareStageProcessors();

			List<KCCProcessor>     processors     = context.StageInfo.Processors;
			List<KCCProcessorInfo> processorInfos = context.StageInfo.ProcessorInfos;

			for (int i = 0, count = processors.Count; i < count; ++i)
			{
				KCCProcessor processor = processors[i];
				if (ReferenceEquals(processor, null) == false)
				{
					if (processor is IBeforeMove beforeMoveProcessor)
					{
						beforeMoveProcessor.BeforeMove(context, processorInfos[i]);
					}
				}
			}
		}

		private void InvokeAfterMoveStep(KCCContext context, KCCOverlapInfo overlapInfo)
		{
			context.StageInfo.PrepareStageProcessors();

			List<KCCProcessor>     processors     = context.StageInfo.Processors;
			List<KCCProcessorInfo> processorInfos = context.StageInfo.ProcessorInfos;

			for (int i = 0, count = processors.Count; i < count; ++i)
			{
				KCCProcessor processor = processors[i];
				if (ReferenceEquals(processor, null) == false)
				{
					if (processor is IAfterMoveStep afterMoveStepProcessor)
					{
						afterMoveStepProcessor.AfterMoveStep(context, processorInfos[i], overlapInfo);
					}
				}
			}
		}

		private void InvokeAfterMove(KCCContext context)
		{
			context.StageInfo.PrepareStageProcessors();

			List<KCCProcessor>     processors     = context.StageInfo.Processors;
			List<KCCProcessorInfo> processorInfos = context.StageInfo.ProcessorInfos;

			for (int i = 0, count = processors.Count; i < count; ++i)
			{
				KCCProcessor processor = processors[i];
				if (ReferenceEquals(processor, null) == false)
				{
					if (processor is IAfterMove afterMoveProcessor)
					{
						afterMoveProcessor.AfterMove(context, processorInfos[i]);
					}
				}
			}
		}
	}
}


