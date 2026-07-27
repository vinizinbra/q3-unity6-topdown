namespace Quantum
{
	using Photon.Deterministic;

	/// <summary>
	/// Movement implementation for default environment.
	/// </summary>
	public unsafe class EnvironmentProcessor : KCCProcessor, IBeforeMove, IAfterMoveStep
	{
		public interface IPrepareData
		{
			void PrepareData(KCCContext context, KCCProcessorInfo processorInfo);
		}

		[KCCHeader("General")]
		[KCCTooltip("Maximum allowed speed the KCC can move with player input.")]
		public FP KinematicSpeed = 8;
		[KCCTooltip("Custom jump multiplier.")]
		public FP JumpMultiplier = 1;
		[KCCTooltip("Custom gravity vector.")]
		public FPVector3 Gravity = new FPVector3(0, -20, 0);

		[KCCHeader("Ground")]
		[KCCTooltip("Maximum angle of walkable ground.")]
		public FP MaxGroundAngle = 60;
		[KCCTooltip("Dynamic velocity is decelerated by actual dynamic speed multiplied by this. The faster KCC moves, the more deceleration is applied.")]
		public FP DynamicGroundFriction = 20;
		[KCCTooltip("Kinematic velocity is accelerated by calculated kinematic speed multiplied by this.")]
		public FP KinematicGroundAcceleration = 50;
		[KCCTooltip("Kinematic velocity is decelerated by actual kinematic speed multiplied by this. The faster KCC moves, the more deceleration is applied.")]
		public FP KinematicGroundFriction = 35;

		[KCCHeader("Air")]
		[KCCTooltip("Dynamic velocity is decelerated by actual dynamic speed multiplied by this. The faster KCC moves, the more deceleration is applied.")]
		public FP DynamicAirFriction = 2;
		[KCCTooltip("Kinematic velocity is accelerated by calculated kinematic speed multiplied by this.")]
		public FP KinematicAirAcceleration = 5;
		[KCCTooltip("Kinematic velocity is decelerated by actual kinematic speed multiplied by this. The faster KCC moves, the more deceleration is applied.")]
		public FP KinematicAirFriction = 2;

		/// <summary>
		/// This callback is invoked before KCC movement - on the beginning of KCC.Update().
		/// Not called if KCC.IsActive is set to false.
		/// </summary>
		/// <param name="context">Reference to KCC context.</param>
		/// <param name="processorInfo">Contains information about the processor registration source and a collider/entity that is referencing this processor.</param>
		public void BeforeMove(KCCContext context, KCCProcessorInfo processorInfo)
		{
			context.KCC->Data.Gravity        = Gravity;
			context.KCC->Data.MaxGroundAngle = MaxGroundAngle;
			context.KCC->Data.MaxWallAngle   = 5;
			context.KCC->Data.MaxHangAngle   = 30;

			if (context.StageInfo.TryGetProcessor(out IPrepareData prepareDataProcessor, out KCCProcessorInfo prepareDataProcessorInfo) == true)
			{
				prepareDataProcessor.PrepareData(context, prepareDataProcessorInfo);
			}

			SetDynamicVelocity(context, ref context.KCC->Data, JumpMultiplier, DynamicGroundFriction, DynamicAirFriction);
			SetKinematicVelocity(context, ref context.KCC->Data, KinematicSpeed, KinematicGroundAcceleration, KinematicAirAcceleration, KinematicGroundFriction, KinematicAirFriction);
		}

		public void AfterMoveStep(KCCContext context, KCCProcessorInfo processorInfo, KCCOverlapInfo overlapInfo)
		{
			ProcessAfterMoveStep(context, processorInfo, overlapInfo);
		}

		/// <summary>
		/// This callback is invoked after each KCC move step - this happens if the KCC
		/// moves too fast and the continuous collision detection (CCD) algorithm splits translation vector into multiple smaller steps.
		/// During CCD movement the KCCData.DeltaTime is scaled proportionally to size of the step.
		/// Not called if KCC.IsActive is set to false.
		/// </summary>
		/// <param name="context">Reference to KCC context.</param>
		/// <param name="processorInfo">Contains information about the processor registration source and a collider/entity that is referencing this processor.</param>
		/// <param name="overlapInfo">Reference to all physics overlap hits in current move step and other metadata.</param>
		public static void ProcessAfterMoveStep(KCCContext context, KCCProcessorInfo processorInfo, KCCOverlapInfo overlapInfo)
		{
			// This code path can be executed multiple times in single update if CCD is active (Continuous Collision Detection).

			KCCData data = context.KCC->Data;

			if (data.IsGrounded == true)
			{
				if (data.WasGrounded == true && data.IsSnappingToGround == false && data.DynamicVelocity.Y < FP._0 && IsAlmostZero(new FPVector3(data.DynamicVelocity.X, FP._0, data.DynamicVelocity.Z), FP.EN2) == true)
				{
					// Reset dynamic velocity Y axis while grounded (to not accumulate gravity indefinitely and clamp to precise zero).
					data.DynamicVelocity.Y = FP._0;
				}

				if (data.WasGrounded == false)
				{
					if (IsAlmostZero(new FPVector3(data.KinematicVelocity.X, FP._0, data.KinematicVelocity.Z), FP.EN2) == true)
					{
						// Reset Y axis after getting grounded and there is no horizontal movement.
						data.KinematicVelocity.Y = FP._0;
					}
					else
					{
						// Otherwise try projecting kinematic velocity onto ground.
						if (KCCPhysicsUtility.ProjectOnGround(data.GroundNormal, data.KinematicVelocity, out FPVector3 projectedKinematicVelocity) == true)
						{
							data.KinematicVelocity = projectedKinematicVelocity.Normalized * data.KinematicVelocity.Magnitude;
						}
					}
				}
			}
			else
			{
				if (data.WasGrounded == false && data.DynamicVelocity.Y > FP._0 && data.DeltaTime > FP._0)
				{
					FPVector3 currentVelocity = (data.TargetPosition - data.BasePosition) / data.DeltaTime;
					if (IsAlmostZero(currentVelocity.Y, FP.EN2) == true)
					{
						// Clamping dynamic up velocity if there is no real position change => hitting a roof.
						data.DynamicVelocity.Y = FP._0;
					}
				}
			}

			context.KCC->Data = data;
		}

		public static void SetDynamicVelocity(KCCContext context, ref KCCData data, FP jumpMultiplier, FP groundFriction, FP airFriction)
		{
			FP        deltaTime       = context.Frame.DeltaTime;
			FPVector3 dynamicVelocity = data.DynamicVelocity;

			if (data.IsGrounded == false || (data.IsSteppingUp == false && (data.IsSnappingToGround == true || data.GroundDistance > FP.EN3)))
			{
				// Applying gravity only while in the air (not grounded) and not stepping up, ground snapping can be active.
				dynamicVelocity += data.Gravity * deltaTime;
			}

			if (data.JumpImpulse != default && jumpMultiplier > FP._0)
			{
				FPVector3 jumpDirection = data.JumpImpulse.Normalized;

				// Elimination of dynamic velocity in direction of jump, otherwise the jump trajectory would be distorted.
				dynamicVelocity -= FPVector3.Scale(dynamicVelocity, jumpDirection);

				// Applying jump impulse.
				dynamicVelocity += data.JumpImpulse * jumpMultiplier;

				data.HasJumped = true;
			}

			// Apply external forces.
			dynamicVelocity += data.ExternalImpulse;
			dynamicVelocity += data.ExternalForce * deltaTime;

			if (dynamicVelocity != default)
			{
				if (IsAlmostZero(dynamicVelocity, FP.EN3) == true)
				{
					// Clamping values near zero.
					dynamicVelocity = default;
				}
				else
				{
					// Applying ground (XYZ) and air (XZ) friction.
					if (data.IsGrounded == true)
					{
						FPVector3 frictionAxis = FPVector3.One;
						if (data.GroundDistance > FP.EN3 || data.IsSnappingToGround == true)
						{
							frictionAxis.Y = default;
						}

						dynamicVelocity += KCCPhysicsUtility.GetFriction(dynamicVelocity, dynamicVelocity, frictionAxis, data.GroundNormal, data.KinematicSpeed, true, FP._0, FP._0, groundFriction, deltaTime);
					}
					else
					{
						dynamicVelocity += KCCPhysicsUtility.GetFriction(dynamicVelocity, dynamicVelocity, new FPVector3(FP._1, FP._0, FP._1), data.KinematicSpeed, true, FP._0, FP._0, airFriction, deltaTime);
					}
				}
			}

			data.DynamicVelocity = dynamicVelocity;

			data.JumpImpulse     = default;
			data.ExternalImpulse = default;
			data.ExternalForce   = default;
		}

		public static void SetKinematicVelocity(KCCContext context, ref KCCData data, FP speed, FP groundAcceleration, FP airAcceleration, FP groundFriction, FP airFriction)
		{
			data.KinematicDirection = new FPVector3(data.InputDirection.X, default, data.InputDirection.Z);

			if (data.IsGrounded == true)
			{
				// The character is grounded.

				if (IsAlmostZero(data.KinematicDirection, FP.EN4) == false && KCCPhysicsUtility.ProjectOnGround(data.GroundNormal, data.KinematicDirection, out FPVector3 projectedMoveDirection) == true)
				{
					// Use projected kinematic direction on ground when possible.
					data.KinematicTangent = projectedMoveDirection.Normalized;
				}
				else
				{
					// Otherwise use ground tangent => steepest descent.
					data.KinematicTangent = data.GroundTangent;
				}
			}
			else
			{
				// The character is floating in the air.

				if (IsAlmostZero(data.KinematicDirection, FP.EN4) == false)
				{
					// Use kinematic direction directly.
					data.KinematicTangent = data.KinematicDirection.Normalized;
				}
				else
				{
					// No direction set, use character forward.
					data.KinematicTangent = data.TransformDirection;
				}
			}

			data.KinematicSpeed = speed;

			FP        deltaTime         = context.Frame.DeltaTime;
			FPVector3 kinematicVelocity = data.KinematicVelocity;

			if (data.IsGrounded == true)
			{
				// The character is grounded.

				if (IsAlmostZero(kinematicVelocity, FP.EN2) == false && KCCPhysicsUtility.ProjectOnGround(data.GroundNormal, kinematicVelocity, out FPVector3 projectedKinematicVelocity) == true)
				{
					// Project current velocity on ground.
					kinematicVelocity = projectedKinematicVelocity.Normalized * kinematicVelocity.Magnitude;
				}

				if (IsAlmostZero(data.KinematicDirection, FP.EN2) == true)
				{
					// No kinematic direction
					// Just apply friction (XYZ) and exit early.
					data.KinematicVelocity = kinematicVelocity + KCCPhysicsUtility.GetFriction(kinematicVelocity, kinematicVelocity, FPVector3.One, data.GroundNormal, data.KinematicSpeed, true, FP._0, FP._0, groundFriction, deltaTime);
					return;
				}
			}
			else
			{
				// The character is floating in the air.

				if (IsAlmostZero(data.KinematicDirection, FP.EN2) == true)
				{
					// No kinematic direction
					// Just apply friction (XZ) and exit early.
					data.KinematicVelocity = kinematicVelocity + KCCPhysicsUtility.GetFriction(kinematicVelocity, kinematicVelocity, new FPVector3(FP._1, FP._0, FP._1), data.KinematicSpeed, true, FP._0, FP._0, airFriction, deltaTime);
					return;
				}
			}

			// Following section calculates ground/air acceleration and friction relatively to move direction and kinematic tangent.
			// And combines them together with base kinematic velocity.

			FPVector3 moveDirection = kinematicVelocity;
			if (moveDirection == default)
			{
				moveDirection = data.KinematicTangent;
			}

			FPVector3 acceleration;
			FPVector3 friction;

			if (data.IsGrounded == true)
			{
				acceleration = KCCPhysicsUtility.GetAcceleration(kinematicVelocity, data.KinematicTangent, FPVector3.One, data.KinematicSpeed, false, data.KinematicDirection.Magnitude, FP._0, groundAcceleration, FP._0, deltaTime);
				friction     = KCCPhysicsUtility.GetFriction(kinematicVelocity, moveDirection, FPVector3.One, data.GroundNormal, data.KinematicSpeed, false, FP._0, FP._0, groundFriction, deltaTime);
			}
			else
			{
				acceleration = KCCPhysicsUtility.GetAcceleration(kinematicVelocity, data.KinematicTangent, FPVector3.One, data.KinematicSpeed, false, data.KinematicDirection.Magnitude, FP._0, airAcceleration, FP._0, deltaTime);
				friction     = KCCPhysicsUtility.GetFriction(kinematicVelocity, moveDirection, new FPVector3(FP._1, FP._0, FP._1), data.KinematicSpeed, false, FP._0, FP._0, airFriction, deltaTime);
			}

			kinematicVelocity = KCCPhysicsUtility.CombineAccelerationAndFriction(kinematicVelocity, acceleration, friction);

			// Clamp velocity.
			if (kinematicVelocity.SqrMagnitude > data.KinematicSpeed * data.KinematicSpeed)
			{
				kinematicVelocity = kinematicVelocity / kinematicVelocity.Magnitude * data.KinematicSpeed;
			}

			// Reset Y axis to get stable jump height results even if moving downwards.
			if (data.HasJumped == true && kinematicVelocity.Y < FP._0)
			{
				kinematicVelocity.Y = FP._0;
			}

			data.KinematicVelocity = kinematicVelocity;
		}

		private static bool IsAlmostZero(FP value, FP tolerance)
		{
			return value < tolerance && value > -tolerance;
		}

		private static bool IsAlmostZero(FPVector3 vector, FP tolerance)
		{
			return vector.X < tolerance && vector.X > -tolerance
				&& vector.Y < tolerance && vector.Y > -tolerance
				&& vector.Z < tolerance && vector.Z > -tolerance;
		}
	}
}
