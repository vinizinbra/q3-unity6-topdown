namespace Quantum
{
	using Photon.Deterministic;

	/// <summary>
	/// This processor snaps character down after losing grounded state.
	/// </summary>
	public unsafe class GroundSnapProcessor : KCCProcessor, IAfterMoveStep
	{
		[KCCTooltip("Maximum ground check distance for snapping.")]
		public FP SnapDistance = FP._0_25;
		[KCCTooltip("Ground snapping speed per second.")]
		public FP SnapSpeed = 4;

		/// <summary>
		/// This callback is invoked after each KCC move step - this happens if the KCC
		/// moves too fast and the continuous collision detection (CCD) algorithm splits translation vector into multiple smaller steps.
		/// During CCD movement the KCCData.DeltaTime is scaled proportionally to size of the step.
		/// Not called if KCC.IsActive is set to false.
		/// </summary>
		/// <param name="context">Reference to KCC context.</param>
		/// <param name="processorInfo">Contains information about the processor registration source and a collider/entity that is referencing this processor.</param>
		/// <param name="overlapInfo">Reference to all physics overlap hits in current move step and other metadata.</param>
		public void AfterMoveStep(KCCContext context, KCCProcessorInfo processorInfo, KCCOverlapInfo overlapInfo)
		{
			if (SnapDistance <= FP._0)
				return;

			ref KCCData baseData = ref context.KCC->Data;

			// Ground snapping activates only if ground is lost and there's no jump or step-up active.
			if (baseData.IsGrounded == true || baseData.WasGrounded == false || baseData.HasJumped == true || baseData.IsSteppingUp == true || baseData.WasSteppingUp == true)
				return;

			// Ignore ground snapping if there is a force pushing the character upwards.
			if (baseData.DynamicVelocity.Y > FP._0)
				return;

			FP  maxPenetrationDistance  = SnapDistance;
			FP  maxStepPenetrationDelta = context.Settings.Radius * FP._0_25;
			int penetrationSteps        = FPMath.CeilToInt(maxPenetrationDistance / maxStepPenetrationDelta);
			FP  penetrationDelta        = maxPenetrationDistance / penetrationSteps;
			FP  overlapRadius           = context.Settings.Radius * FP._1_50;

			KCCOverlapInfo checkOverlapInfo = KCCOverlapInfo.Get();
			QueryOptions   queryOptions     = QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.HitDynamics;

			// Make a bigger overlap to correctly resolve penetrations along the way down.
			context.KCC->CapsuleOverlap(context, checkOverlapInfo, baseData.TargetPosition - new FPVector3(FP._0, SnapDistance, FP._0), overlapRadius, context.Settings.Height + SnapDistance, queryOptions);

			if (checkOverlapInfo.ColliderHits.Count == 0)
			{
				KCCOverlapInfo.Return(checkOverlapInfo);
				return;
			}

			// For ground check we try to resolve the ground on separate KCCOverlapInfo and KCCData instances to avoid side effects on main data.
			KCCData checkOverlapData = context.KCC->Data;

			// Checking collisions with full snap distance could lead to incorrect collision type (ground/slope/wall) detection.
			// So we split the downward movenent into more steps and move by 1/4 of radius at max in single step.
			for (int i = 0; i < penetrationSteps; ++i)
			{
				checkOverlapData.TargetPosition.Y -= penetrationDelta;

				// Resolve penetration on new candidate position.
				context.KCC->ResolvePenetration(context, checkOverlapInfo, ref checkOverlapData, 1);

				if (checkOverlapData.IsGrounded == true)
				{
					// We found the ground, now move the KCC towards the grounded position.

					FP maxSnapDelta = SnapSpeed * baseData.DeltaTime;

					if (baseData.WasSnappingToGround == false)
					{
						// First max snap delta is reduced by half to smooth out the snapping.
						maxSnapDelta *= FP._0_50;
					}

					FPVector3 targetGroundedPosition = checkOverlapData.TargetPosition;
					FPVector3 targetSnappedPosition  = targetGroundedPosition;

					FPVector3 snapPositionOffset = targetSnappedPosition - baseData.TargetPosition;
					if (snapPositionOffset.SqrMagnitude > maxSnapDelta * maxSnapDelta)
					{
						targetSnappedPosition = baseData.TargetPosition + snapPositionOffset.Normalized * maxSnapDelta;
					}

					baseData.TargetPosition     = targetSnappedPosition;
					baseData.IsGrounded         = checkOverlapData.IsGrounded;
					baseData.GroundNormal       = checkOverlapData.GroundNormal;
					baseData.GroundTangent      = checkOverlapData.GroundTangent;
					baseData.GroundPosition     = checkOverlapData.GroundPosition;
					baseData.GroundDistance     = FPMath.Max(FP._0, FPVector3.Distance(targetSnappedPosition, targetGroundedPosition) - context.Settings.Radius);
					baseData.GroundAngle        = checkOverlapData.GroundAngle;
					baseData.IsSnappingToGround = true;

					break;
				}
			}

			KCCOverlapInfo.Return(checkOverlapInfo);
		}
	}
}
