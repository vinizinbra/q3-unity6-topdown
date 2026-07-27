namespace Quantum
{
	using Photon.Deterministic;
	using Quantum.Physics3D;

	/// <summary>
	/// This processor detect steps (obstacles which block the character moving forward) and reflects blocked movement upwards.
	/// </summary>
	public unsafe class StepUpProcessor : KCCProcessor, IAfterMoveStep
	{
		[KCCTooltip("Maximum obstacle height to step on it.")]
		public FP StepHeight = FP._0_50;
		[KCCTooltip("Maximum depth of the step check.")]
		public FP StepDepth = FP._0_20;
		[KCCTooltip("Multiplier of unapplied movement projected to step up. This helps traversing obstacles faster.")]
		public FP StepSpeed = FP._1;
		[KCCTooltip("Minimum proportional penetration push-back distance to activate step-up. A value of 0.5 means the KCC must be pushed back from colliding geometry by at least 50% of desired movement.\n" +
		"Recommended range is 0.25 - 0.75.")]
		public FP MinPushBack = FP._0_50;
		[KCCTooltip("Radius multiplier used for last sphere-cast (ground surface detection). Lower value work better with shorter step depth.")]
		public FP GroundCheckRadiusScale = FP._0_50;
		[KCCTooltip("Clears dynamic velocity when the step up is over. This eliminates bumps when dynamic up velocity is positive (for example after triggering jump).")]
		public bool ClearDynamicVelocityOnEnd = true;
		[KCCTooltip("Step-up starts only if the target surface is walkable (angle <= KCCData.MaxGroundAngle).")]
		public bool RequireGroundTarget = false;

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
			if (StepHeight <= FP._0 | StepDepth <= FP._0 | StepSpeed <= FP._0)
				return;

			ref KCCData data = ref context.KCC->Data;

			// There was no collision.
			if (overlapInfo.ColliderHits.Count <= 0)
			{
				ProcessStepUpResult(ref data, false);
				return;
			}

			// Ignore step-up after jump and teleport.
			if (data.HasJumped == true || data.HasTeleported == true)
			{
				ProcessStepUpResult(ref data, false);
				return;
			}

			FPVector3 checkDesiredDelta      = data.DesiredPosition - data.BasePosition;
			FPVector3 checkDesiredDeltaXZ    = new FPVector3(checkDesiredDelta.X, FP._0, checkDesiredDelta.Z);
			FP        checkDesiredDistanceXZ = checkDesiredDeltaXZ.Magnitude;

			// No horizontal movement, stopping step-up.
			if (checkDesiredDistanceXZ < FP.EN3)
			{
				ProcessStepUpResult(ref data, false);
				return;
			}

			bool tryStepUp = false;

			if (HasCollisionsWithinExtent(overlapInfo, EKCCCollisionType.Slope | EKCCCollisionType.Wall | EKCCCollisionType.Hang) == true)
			{
				tryStepUp = true;
			}
			else
			{
				// Following check compares desired distance with real distance traveled by KCC and triggers step-up
				// if something is pushing KCC back on horizontal plane, lowering the distance traveled by more than min push back.

				FPVector3 targetDelta      = data.TargetPosition - data.BasePosition;
				FPVector3 targetDeltaXZ    = new FPVector3(targetDelta.X, FP._0, targetDelta.Z);
				FP        targetDistanceXZ = targetDeltaXZ.Magnitude;

				if (targetDistanceXZ / checkDesiredDistanceXZ < MinPushBack)
				{
					tryStepUp = true;
				}
			}

			if (tryStepUp == false)
			{
				ProcessStepUpResult(ref data, false);
				return;
			}

			FPVector3 basePosition    = data.BasePosition;
			FPVector3 desiredPosition = data.DesiredPosition;
			FPVector3 targetPosition  = data.TargetPosition;

			FPVector3 desiredDelta     = desiredPosition - basePosition;
			FPVector3 desiredDirection = FPVector3.Normalize(desiredDelta);

			// The step-up is not triggered if there is no pending movement from the player or external sources.
			if (desiredDirection == default)
			{
				ProcessStepUpResult(ref data, false);
				return;
			}

			// Ignore step-up while moving upwards or downwards (with ~25° deviation).
			FP desiredDirectionUpDot = FPVector3.Dot(desiredDirection, FPVector3.Up);
			if (FPMath.Abs(desiredDirectionUpDot) >= (FP._1 - FP._0_10))
			{
				ProcessStepUpResult(ref data, false);
				return;
			}

			FPVector3 correctionDelta     = targetPosition - desiredPosition;
			FP        correctionDistance  = correctionDelta.Magnitude;
			FPVector3 correctionDirection = correctionDistance > FP.EN3 ? correctionDelta / correctionDistance : -desiredDirection;

			// The step-up is not triggered if the correction vector overlaps desired movement hemisphere.
			if (FPVector3.Dot(desiredDirection, correctionDirection) >= FP._0)
			{
				ProcessStepUpResult(ref data, false);
				return;
			}

			FPVector3 desiredCheckDirectionXZ    = FPVector3.Normalize(new FPVector3(desiredDirection.X, FP._0, desiredDirection.Z));
			FPVector3 correctionCheckDirectionXZ = FPVector3.Normalize(new FPVector3(-correctionDirection.X, FP._0, -correctionDirection.Z));
			FPVector3 combinedCheckDirectionXZ   = FPVector3.Normalize(desiredCheckDirectionXZ + correctionCheckDirectionXZ);

			// Additional XZ comparison of desired direction and correction direction with deviation of ~85°.
			if (FPVector3.Dot(desiredCheckDirectionXZ, correctionCheckDirectionXZ) < FP._0_10)
			{
				ProcessStepUpResult(ref data, false);
				return;
			}

			// Following image shows movement step and collision with a geometry from top-down perspective.
			// P = Player position before movement.
			// D = Desired position before depenetration.
			// T = Target position after depenetration.
			// I = Impact position (intersection of P->D and collider plane).
			//
			//         D
			//         | \
			// --------T---I------- <- Obstacle
			//              \
			//                P
			//
			// Following code recalculates base step-up position from target position (T) to impact position (I).
			// This eliminates sliding along the collider when stepping up.
			if (correctionDirection != default && HasCollisionsWithinExtent(overlapInfo, EKCCCollisionType.Slope) == false)
			{
				FPVector3 rayOrigin = basePosition - desiredDelta * 2;

				if (KCCPhysicsUtility.Raycast(rayOrigin, desiredDirection, correctionDirection, targetPosition, out FP distance) == true)
				{
					targetPosition = rayOrigin + desiredDirection * distance;
				}
			}

			FP             checkRadius         = context.Settings.Radius - context.Settings.Extent;
			FPVector3      checkPosition       = targetPosition + new FPVector3(FP._0, StepHeight, FP._0);
			KCCOverlapInfo checkOverlapInfo    = KCCOverlapInfo.Get();
			QueryOptions   overlapQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.HitDynamics;

			// 1. Upward collision check.
			if (context.KCC->CapsuleOverlap(context, checkOverlapInfo, checkPosition, checkRadius, context.Settings.Height, overlapQueryOptions) == true)
			{
				KCCOverlapInfo.Return(checkOverlapInfo);
				ProcessStepUpResult(ref data, false);
				return;
			}

			checkPosition += combinedCheckDirectionXZ * StepDepth;

			// 2. Forward collision check. Forward = Combination of desired XZ direction and negative correction XZ direction - to eliminate stepping up along collider surface.
			if (context.KCC->CapsuleOverlap(context, checkOverlapInfo, checkPosition, checkRadius, context.Settings.Height, overlapQueryOptions) == true)
			{
				KCCOverlapInfo.Return(checkOverlapInfo);
				ProcessStepUpResult(ref data, false);
				return;
			}

			KCCOverlapInfo.Return(checkOverlapInfo);

			FP        maxStepHeight      = StepHeight;
			bool      highestPointFound  = default;
			FPVector3 highestPointNormal = default;

			if (GroundCheckRadiusScale < FP._1)
			{
				// Ground check can be done with smaller radius to compensate normal on edges.
				checkRadius = context.Settings.Radius * GroundCheckRadiusScale;
				checkPosition += combinedCheckDirectionXZ * (context.Settings.Radius - context.Settings.Extent - checkRadius);
			}

			KCCShapeCastInfo checkShapeCastInfo    = KCCShapeCastInfo.Get();
			QueryOptions     shapeCastQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics | QueryOptions.HitDynamics | QueryOptions.ComputeDetailedInfo;

			// 3. Downward collision check to get step height.
			if (context.KCC->SphereCast(context, checkShapeCastInfo, checkPosition + new FPVector3(FP._0, context.Settings.Radius, FP._0), checkRadius, FPVector3.Down, maxStepHeight + context.Settings.Radius, shapeCastQueryOptions) == true)
			{
				FPVector3 highestPoint = new FPVector3(FP._0, FP.MinValue, FP._0);

				for (int i = 0, count = checkShapeCastInfo.ColliderHits.Count; i < count; ++i)
				{
					Hit3D     raycastHit      = checkShapeCastInfo.ColliderHits[i].PhysicsHit;
					FPVector3 raycastHitPoint = raycastHit.Point;

					if (raycastHitPoint.Y > targetPosition.Y && raycastHitPoint.Y > highestPoint.Y)
					{
						highestPoint       = raycastHitPoint;
						highestPointNormal = raycastHit.Normal;
						highestPointFound  = true;
					}
				}

				if (highestPointFound == true)
				{
					maxStepHeight = FPMath.Clamp(highestPoint.Y - targetPosition.Y, FP._0, StepHeight);
				}
			}

			KCCShapeCastInfo.Return(checkShapeCastInfo);

			// For initial attempt, do not try to step up on non-ground surfaces.
			if (RequireGroundTarget == true && data.IsSteppingUp == false && data.WasSteppingUp == false)
			{
				if (highestPointFound == true)
				{
					FP minGroundDot      = FPMath.Cos(FPMath.Clamp(data.MaxGroundAngle,FP._0, 90) * FP.Deg2Rad);
					FP highestPointUpDot = FPVector3.Dot(highestPointNormal, FPVector3.Up);

					if (highestPointUpDot < minGroundDot)
					{
						ProcessStepUpResult(ref data, false);
						return;
					}
				}
			}

			// Project unapplied movement as step-up movement.
			FP desiredDistance   = FPVector3.Distance(basePosition, desiredPosition);
			FP traveledDistance  = FPVector3.Distance(basePosition, targetPosition);
			FP remainingDistance = FPMath.Clamp((desiredDistance - traveledDistance) * StepSpeed, FP._0, maxStepHeight);

			remainingDistance *= FPMath.Clamp01(FPVector3.Dot(desiredDirection, -correctionDirection));

			data.TargetPosition = targetPosition + new FPVector3(FP._0, remainingDistance, FP._0);

			// KCC remains grounded state while stepping up.
			data.IsGrounded     = true;
			data.GroundNormal   = FPVector3.Up;
			data.GroundDistance = context.Settings.Extent;
			data.GroundPosition = data.TargetPosition;
			data.GroundTangent  = data.TransformDirection;

			ProcessStepUpResult(ref data, true);
		}

		private void ProcessStepUpResult(ref KCCData data, bool isSteppingUp)
		{
			data.IsSteppingUp = isSteppingUp;

			if (data.IsSteppingUp == false && data.WasSteppingUp == true)
			{
				if (ClearDynamicVelocityOnEnd == true)
				{
					// Clearing dynamic velocity ensures that the movement from external sources and jump won't continue after hiting the edge.
					data.DynamicVelocity = default;
				}
			}
		}

		private static bool HasCollisionsWithinExtent(KCCOverlapInfo overlapInfo, EKCCCollisionType collisionTypes)
		{
			for (int i = 0, count = overlapInfo.ColliderHits.Count; i < count; ++i)
			{
				KCCOverlapHit hit = overlapInfo.ColliderHits[i];
				if ((collisionTypes & hit.CollisionType) != default)
					return true;
			}

			return false;
		}
	}
}
