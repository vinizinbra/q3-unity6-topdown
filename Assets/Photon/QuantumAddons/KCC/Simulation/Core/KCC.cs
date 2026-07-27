namespace Quantum
{
	using System;
	using System.Collections.Generic;
	using Photon.Deterministic;
	using Quantum.Core;

	unsafe partial struct KCC
	{
		/// <summary>
		/// Controls execution of the KCC.
		/// </summary>
		public bool IsActive => Data.IsActive;

		/// <summary>
		/// Flag that indicates the KCC is touching a collider with normal angle lower than <c>KCCData.MaxGroundAngle</c>.
		/// </summary>
		public bool IsGrounded => Data.IsGrounded;

		/// <summary>
		/// Indicates the KCC is stepping up.
		/// </summary>
		public bool IsSteppingUp => Data.IsSteppingUp;

		/// <summary>
		/// Indicates the KCC temporarily lost grounded state and is snapping to ground.
		/// </summary>
		public bool IsSnappingToGround => Data.IsSnappingToGround;

		/// <summary>
		/// Flag that indicates the KCC has jumped in current tick.
		/// </summary>
		public bool HasJumped => Data.HasJumped;

		/// <summary>
		/// Calculated position which is propagated to <c>Transform</c> on the end of KCC.Update().
		/// </summary>
		public FPVector3 Position => Data.TargetPosition;

		/// <summary>
		/// Speed calculated from real position change.
		/// </summary>
		public FP RealSpeed => Data.RealSpeed;

		/// <summary>
		/// Velocity calculated from real position change.
		/// </summary>
		public FPVector3 RealVelocity => Data.RealVelocity;

		/// <summary>
		/// Combined normal of all touching colliders. Normals less distant from up direction have bigger impacton final normal.
		/// </summary>
		public FPVector3 GroundNormal => Data.GroundNormal;

		/// <summary>
		/// Controls execution of the KCC. No movement/callbacks are executed if the KCC is inactive.
		/// Setting KCC inactive also forces all tracked collisions being removed in following Update().
		/// </summary>
		public void SetActive(bool isActive)
		{
			Data.IsActive = isActive;
		}

		/// <summary>
		/// Set gravity vector.
		/// </summary>
		/// <param name="gravity">Gravity vector.</param>
		public void SetGravity(FPVector3 gravity)
		{
			Data.Gravity = gravity;
		}

		/// <summary>
		/// Set input direction of the KCC.
		/// </summary>
		/// <param name="direction">Input direction in world space.</param>
		/// <param name="clampToNormalized">Vector with magnitude greater than 1.0 is normalized.</param>
		public void SetInputDirection(FPVector3 direction, bool clampToNormalized = true)
		{
			if (clampToNormalized == true)
			{
				direction = FPVector3.ClampMagnitude(direction, FP._1);
			}

			Data.InputDirection = direction;
		}

		/// <summary>
		/// Returns current look rotation.
		/// </summary>
		public FPVector2 GetLookRotation(bool pitch = true, bool yaw = true)
		{
			return Data.GetLookRotation(pitch, yaw);
		}

		/// <summary>
		/// Add pitch and yaw look rotation. Resulting values are clamped to &lt;-90, 90&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void AddLookRotation(FP pitchDelta, FP yawDelta)
		{
			Data.AddLookRotation(pitchDelta, yawDelta);
		}

		/// <summary>
		/// Add pitch and yaw look rotation. Resulting values are clamped to &lt;minPitch, maxPitch&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void AddLookRotation(FP pitchDelta, FP yawDelta, FP minPitch, FP maxPitch)
		{
			Data.AddLookRotation(pitchDelta, yawDelta, minPitch, maxPitch);
		}

		/// <summary>
		/// Add pitch (x) and yaw (y) look rotation. Resulting values are clamped to &lt;-90, 90&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void AddLookRotation(FPVector2 lookRotationDelta)
		{
			AddLookRotation(lookRotationDelta.X, lookRotationDelta.Y);
		}

		/// <summary>
		/// Add pitch (x) and yaw (y) look rotation. Resulting values are clamped to &lt;minPitch, maxPitch&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void AddLookRotation(FPVector2 lookRotationDelta, FP minPitch, FP maxPitch)
		{
			AddLookRotation(lookRotationDelta.X, lookRotationDelta.Y, minPitch, maxPitch);
		}

		/// <summary>
		/// Set pitch and yaw look rotation. Values are clamped to &lt;-90, 90&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void SetLookRotation(FP pitch, FP yaw)
		{
			Data.SetLookRotation(pitch, yaw);
		}

		/// <summary>
		/// Set pitch and yaw look rotation. Values are clamped to &lt;minPitch, maxPitch&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void SetLookRotation(FP pitch, FP yaw, FP minPitch, FP maxPitch)
		{
			Data.SetLookRotation(pitch, yaw, minPitch, maxPitch);
		}

		/// <summary>
		/// Set pitch and yaw look rotation. Values are clamped to &lt;-90, 90&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void SetLookRotation(FPVector2 lookRotation)
		{
			SetLookRotation(lookRotation.X, lookRotation.Y);
		}

		/// <summary>
		/// Set pitch and yaw look rotation. Values are clamped to &lt;minPitch, maxPitch&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void SetLookRotation(FPVector2 lookRotation, FP minPitch, FP maxPitch)
		{
			SetLookRotation(lookRotation.X, lookRotation.Y, minPitch, maxPitch);
		}

		/// <summary>
		/// Set pitch and yaw look rotation. Roll is ignored (not supported). Values are clamped to &lt;-90, 90&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void SetLookRotation(FPQuaternion lookRotation, bool preservePitch = false, bool preserveYaw = false)
		{
			Data.SetLookRotation(lookRotation, preservePitch, preserveYaw);
		}

		/// <summary>
		/// Add jump impulse, which should be propagated by processors to <c>KCCData.DynamicVelocity</c>.
		/// The impulse will be processed in following KCC update.
		/// </summary>
		public void Jump(FPVector3 impulse)
		{
			Data.JumpImpulse += impulse;
		}

		/// <summary>
		/// Add impulse from external sources. Should propagate in processors to <c>KCCData.DynamicVelocity</c>.
		/// </summary>
		public void AddExternalImpulse(FPVector3 impulse)
		{
			Data.ExternalImpulse += impulse;
		}

		/// <summary>
		/// Set impulse from external sources. Should propagate in processors to <c>KCCData.DynamicVelocity</c>.
		/// </summary>
		public void SetExternalImpulse(FPVector3 impulse)
		{
			Data.ExternalImpulse = impulse;
		}

		/// <summary>
		/// Add force from external sources. Should propagate in processors to <c>KCCData.DynamicVelocity</c>.
		/// </summary>
		public void AddExternalForce(FPVector3 force)
		{
			Data.ExternalForce += force;
		}

		/// <summary>
		/// Set force from external sources. Should propagate in processors to <c>KCCData.DynamicVelocity</c>.
		/// </summary>
		public void SetExternalForce(FPVector3 force)
		{
			Data.ExternalForce = force;
		}

		/// <summary>
		/// Add position delta from external sources. Will be consumed by following KCC update.
		/// </summary>
		public void AddExternalDelta(FPVector3 delta)
		{
			Data.ExternalDelta += delta;
		}

		/// <summary>
		/// Set position delta from external sources. Will be consumed by following KCC update.
		/// </summary>
		public void SetExternalDelta(FPVector3 delta)
		{
			Data.ExternalDelta = delta;
		}

		/// <summary>
		/// Set <c>KCCData.DynamicVelocity</c>.
		/// </summary>
		public void SetDynamicVelocity(FPVector3 velocity)
		{
			Data.DynamicVelocity = velocity;
		}

		/// <summary>
		/// Set <c>KCCData.KinematicDirection</c>.
		/// </summary>
		public void SetKinematicDirection(FPVector3 direction)
		{
			Data.KinematicDirection = direction;
		}

		/// <summary>
		/// Set <c>KCCData.KinematicSpeed</c>.
		/// </summary>
		public void SetKinematicSpeed(FP speed)
		{
			Data.KinematicSpeed = speed;
		}

		/// <summary>
		/// Set <c>KCCData.KinematicVelocity</c>.
		/// </summary>
		public void SetKinematicVelocity(FPVector3 velocity)
		{
			Data.KinematicVelocity = velocity;
		}

		/// <summary>
		/// Sets <c>KCCData.DesiredPosition</c>, <c>KCCData.TargetPosition</c>.
		/// Also sets <c>KCCData.HasTeleported</c> flag to <c>true</c> and clears <c>KCCData.IsSteppingUp</c> and <c>KCCData.IsSnappingToGround</c>.
		/// Calling this from within a processor stage effectively stops any pending move steps and forces KCC to update hits with new overlap query.
		/// Calling this from outside of the KCC.Update() has no effect, the change is not propagated to Transform component.
		/// </summary>
		[Obsolete("Use Teleport(Frame, FPVector3) which also propagates to Transform3D.")]
		public void Teleport(FPVector3 position)
		{
			Data.DesiredPosition    = position;
			Data.TargetPosition     = position;
			Data.HasTeleported      = true;
			Data.IsSteppingUp       = false;
			Data.IsSnappingToGround = false;
		}

		/// <summary>
		/// Sets <c>KCCData.DesiredPosition</c>, <c>KCCData.TargetPosition</c> and calls Transform3D.Teleport().
		/// Also sets <c>KCCData.HasTeleported</c> flag to <c>true</c> and clears <c>KCCData.IsSteppingUp</c> and <c>KCCData.IsSnappingToGround</c>.
		/// Calling this from within a processor stage effectively stops any pending move steps and forces KCC to update hits with new overlap query.
		/// </summary>
		public void Teleport(FrameBase frame, FPVector3 position)
		{
			Data.DesiredPosition    = position;
			Data.TargetPosition     = position;
			Data.HasTeleported      = true;
			Data.IsSteppingUp       = false;
			Data.IsSnappingToGround = false;

			if (frame.Unsafe.TryGetPointer(Entity, out Transform3D* transform3D) == true)
			{
				transform3D->Teleport(frame, position);
			}
		}

		/// <summary>
		/// Resets most of the <c>KCCData</c> properties to default.
		/// This is a soft reset and won't remove registered modifier processors.
		/// Active collisions will be removed in following <c>KCC.Update()</c> call.
		/// For immediate, full cleanup including <c>OnExit()</c> callback on all processors use <c>Deinitialize()</c>.
		/// </summary>
		public void ResetData()
		{
			KCCData data = default;

			// These properties should remain untouched to prevent glitches if the method is called from within <c>KCCProcessor.OnEnter()</c> callback.
			data.IsActive        = Data.IsActive;
			data.LookPitch       = Data.LookPitch;
			data.LookYaw         = Data.LookYaw;
			data.BasePosition    = Data.BasePosition;
			data.DesiredPosition = Data.DesiredPosition;
			data.TargetPosition  = Data.TargetPosition;
			data.DeltaTime       = Data.DeltaTime;

			ResetUserData(ref data);

			Data = data;
		}

		/// <summary>
		/// Initialize KCC component. Called when the entity is created.
		/// </summary>
		public void Initialize(KCCContext context)
		{
			if (IsInitialized == true)
				return;

			Entity = context.Entity;

			Ignores    = context.Frame.AllocateHashSet<KCCIgnore>();
			Modifiers  = context.Frame.AllocateList<KCCModifier>();
			Collisions = context.Frame.AllocateList<KCCCollision>();

			Data.IsActive = true;

			if (context.Frame.Unsafe.TryGetPointer<Transform3D>(context.Entity, out Transform3D* transform) == true)
			{
				Data.BasePosition    = transform->Position;
				Data.DesiredPosition = transform->Position;
				Data.TargetPosition  = transform->Position;

				Data.SetLookRotation(transform->Rotation, true, false);
			}

			IsInitialized = true;
			InitializeUser(context);

			List<KCCProcessor> runtimeProcessors = context.Settings.RuntimeProcessors;
			for (int i = 0, count = runtimeProcessors.Count; i < count; ++i)
			{
				runtimeProcessors[i].OnEnter(context, KCCProcessorInfo.Default, default);
			}
		}

		/// <summary>
		/// Deinitialize KCC component. Called when the entity is destroyed.
		/// </summary>
		public void Deinitialize(KCCContext context)
		{
			if (IsInitialized == false)
				return;

			ForceRemoveAllModifiers(context);
			ForceRemoveAllCollisions(context);

			List<KCCProcessor> runtimeProcessors = context.Settings.RuntimeProcessors;
			for (int i = runtimeProcessors.Count - 1; i >= 0; --i)
			{
				runtimeProcessors[i].OnExit(context, KCCProcessorInfo.Default);
			}

			ResetData();
			Data.IsActive = false;

			DeinitializeUser(context);
			IsInitialized = false;

			context.Frame.FreeHashSet(ref Ignores);
			context.Frame.FreeList(ref Modifiers);
			context.Frame.FreeList(ref Collisions);
		}

		public void Update(KCCContext context)
		{
			if (IsInitialized == false)
				return;

			FP        baseDeltaTime       = context.Frame.DeltaTime;
			FPVector3 basePosition        = Data.BasePosition;
			bool      wasGrounded         = Data.IsGrounded;
			bool      wasSteppingUp       = Data.IsSteppingUp;
			bool      wasSnappingToGround = Data.IsSnappingToGround;

			Transform3D* transform;

			bool hasTransform = context.Frame.Unsafe.TryGetPointer<Transform3D>(context.Entity, out transform);
			if (hasTransform == true)
			{
				basePosition = transform->Position;
			}

			Data.DeltaTime       = baseDeltaTime;
			Data.HasJumped       = default;
			Data.HasTeleported   = default;
			Data.BasePosition    = basePosition;
			Data.DesiredPosition = basePosition;
			Data.TargetPosition  = basePosition;

			if (Data.IsActive == false)
			{
				ForceRemoveAllCollisions(context);

				Data.RealVelocity = default;
				Data.RealSpeed    = default;

				return;
			}

			context.StageInfo.CacheProcessors(context);

			InvokeBeforeMove(context);

			FP        pendingDeltaTime     = FPMath.Clamp01(Data.DeltaTime);
			FPVector3 pendingDeltaPosition = Data.DesiredVelocity * pendingDeltaTime + Data.ExternalDelta;

			FPVector3 desiredPosition = Data.BasePosition + pendingDeltaPosition;

			Data.DesiredPosition = desiredPosition;
			Data.TargetPosition  = Data.BasePosition;
			Data.ExternalDelta   = default;

			bool      hasFinished           = false;
			FP        radiusMultiplier      = FPMath.Clamp(context.Settings.CCDRadiusMultiplier, FP._0_10, FP._1 - FP._0_10);
			FP        maxDeltaMagnitude     = context.Settings.Radius * (radiusMultiplier + FP._0_10);
			FP        optimalDeltaMagnitude = context.Settings.Radius * radiusMultiplier;
			FPVector3 nonTeleportedPosition = Data.TargetPosition;

			while (hasFinished == false && Data.HasTeleported == false)
			{
				Data.BasePosition = Data.TargetPosition;

				FP        consumeDeltaTime     = pendingDeltaTime;
				FPVector3 consumeDeltaPosition = pendingDeltaPosition;

				FP consumeDeltaPositionMagnitude = consumeDeltaPosition.Magnitude;
				if (consumeDeltaPositionMagnitude > maxDeltaMagnitude)
				{
					FP deltaRatio = optimalDeltaMagnitude / consumeDeltaPositionMagnitude;

					consumeDeltaTime     *= deltaRatio;
					consumeDeltaPosition *= deltaRatio;
				}
				else
				{
					hasFinished = true;
				}

				pendingDeltaTime     -= consumeDeltaTime;
				pendingDeltaPosition -= consumeDeltaPosition;

				if (pendingDeltaTime <= FP._0)
				{
					pendingDeltaTime = FP._0;
				}

				Data.DeltaTime           = consumeDeltaTime;
				Data.DesiredPosition     = Data.BasePosition + consumeDeltaPosition;
				Data.TargetPosition      = Data.DesiredPosition;
				Data.WasGrounded         = Data.IsGrounded;
				Data.WasSteppingUp       = Data.IsSteppingUp;
				Data.WasSnappingToGround = Data.IsSnappingToGround;

				ProcessMoveStep(context);

				if (Data.HasTeleported == false)
				{
					nonTeleportedPosition = Data.TargetPosition;
				}

				if (hasFinished == true && Data.ExternalDelta != default)
				{
					pendingDeltaPosition += Data.ExternalDelta;
					Data.ExternalDelta = default;
					hasFinished = false;
				}
			}

			Data.DeltaTime           = baseDeltaTime;
			Data.BasePosition        = basePosition;
			Data.DesiredPosition     = desiredPosition;
			Data.WasGrounded         = wasGrounded;
			Data.WasSteppingUp       = wasSteppingUp;
			Data.WasSnappingToGround = wasSnappingToGround;
			Data.RealVelocity        = (nonTeleportedPosition - basePosition) / baseDeltaTime;
			Data.RealSpeed           = Data.RealVelocity.Magnitude;

			InvokeAfterMove(context);

			if (hasTransform == true)
			{
				if (Data.HasTeleported == true)
				{
					transform->Teleport(context.Frame, Data.TargetPosition, Data.TransformRotation);
				}
				else
				{
					transform->Position = Data.TargetPosition;
					transform->Rotation = Data.TransformRotation;
				}
			}
		}

		private void ProcessMoveStep(KCCContext context)
		{
			Data.IsGrounded         = default;
			Data.IsSteppingUp       = default;
			Data.IsSnappingToGround = default;
			Data.GroundNormal       = default;
			Data.GroundTangent      = default;
			Data.GroundPosition     = default;
			Data.GroundDistance     = default;
			Data.GroundAngle        = default;

			KCCOverlapInfo overlapInfo = KCCOverlapInfo.Get();
			overlapInfo.Reset();

			if (context.Settings.CollisionLayerMask != 0)
			{
				ResolvePenetration(context, overlapInfo, ref Data, context.Settings.Radius, context.Settings.Height, context.Settings.Extent, context.Settings.CollisionLayerMask, context.Settings.MaxPenetrationSteps);
			}

			if (Data.HasJumped == true)
			{
				Data.IsGrounded = false;
			}

			InvokeAfterMoveStep(context, overlapInfo);

			UpdateCollisions(context, overlapInfo, Data.TargetPosition);

			KCCOverlapInfo.Return(overlapInfo);
		}

		/// <summary>
		/// Use this for custom KCC initialization logic.
		/// </summary>
		partial void InitializeUser(KCCContext context);

		/// <summary>
		/// Use this for custom KCC deinitialization logic.
		/// </summary>
		partial void DeinitializeUser(KCCContext context);

		/// <summary>
		/// Use this for custom KCCData reset logic.
		/// </summary>
		/// <param name="data">New data values that will be set.</param>
		partial void ResetUserData(ref KCCData data);
	}
}
