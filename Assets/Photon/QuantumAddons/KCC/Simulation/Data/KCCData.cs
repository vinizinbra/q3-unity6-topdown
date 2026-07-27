namespace Quantum
{
	using Photon.Deterministic;

	partial struct KCCData
	{
		/// <summary>
		/// Look rotation based on <c>LookPitch</c> and <c>LookYaw</c>.
		/// </summary>
		public FPQuaternion LookRotation => FPQuaternion.Euler(LookPitch, LookYaw, 0);

		/// <summary>
		/// Look direction based on <c>LookRotation</c> (<c>LookPitch</c> and <c>LookYaw</c>).
		/// </summary>
		public FPVector3 LookDirection => LookRotation * FPVector3.Forward;

		/// <summary>
		/// Transform rotation based on <c>LookYaw</c>.
		/// </summary>
		public FPQuaternion TransformRotation => FPQuaternion.Euler(0, LookYaw, 0);

		/// <summary>
		/// Transform direction based on <c>TransformRotation</c> (<c>LookYaw</c>).
		/// </summary>
		public FPVector3 TransformDirection => TransformRotation * FPVector3.Forward;

		/// <summary>
		/// Indicates the KCC temporarily or permanently lost grounded state.
		/// </summary>
		public bool IsOnEdge => IsGrounded == false && WasGrounded == true;

		/// <summary>
		/// Velocity used for KCC movement (<c>KinematicVelocity</c> + <c>DynamicVelocity</c>).
		/// </summary>
		public FPVector3 DesiredVelocity => DynamicVelocity + KinematicVelocity;

		private static readonly FP P90  = 90;
		private static readonly FP N90  = -90;
		private static readonly FP P180 = 180;
		private static readonly FP N180 = -180;
		private static readonly FP P360 = 360;

		/// <summary>
		/// Returns the look rotation vector with selected axes.
		/// </summary>
		public FPVector2 GetLookRotation(bool pitch = true, bool yaw = true)
		{
			FPVector2 lookRotation = default;

			if (pitch == true) { lookRotation.X = LookPitch; }
			if (yaw   == true) { lookRotation.Y = LookYaw;   }

			return lookRotation;
		}

		/// <summary>
		/// Add pitch and yaw look rotation. Resulting values are clamped to &lt;-90, 90&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void AddLookRotation(FP pitchDelta, FP yawDelta)
		{
			if (pitchDelta != FP._0)
			{
				LookPitch = FPMath.Clamp(LookPitch + pitchDelta, N90, P90);
			}

			if (yawDelta != FP._0)
			{
				FP lookYaw = LookYaw + yawDelta;

				while (lookYaw > P180) { lookYaw -= P360; }
				while (lookYaw < N180) { lookYaw += P360; }

				LookYaw = lookYaw;
			}
		}

		/// <summary>
		/// Add pitch and yaw look rotation. Resulting values are clamped to &lt;minPitch, maxPitch&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void AddLookRotation(FP pitchDelta, FP yawDelta, FP minPitch, FP maxPitch)
		{
			if (pitchDelta != FP._0)
			{
				if (minPitch < N90) { minPitch = N90; }
				if (maxPitch > P90) { maxPitch = P90; }

				if (maxPitch < minPitch) { maxPitch = minPitch; }

				LookPitch = FPMath.Clamp(LookPitch + pitchDelta, minPitch, maxPitch);
			}

			if (yawDelta != FP._0)
			{
				FP lookYaw = LookYaw + yawDelta;

				while (lookYaw > P180) { lookYaw -= P360; }
				while (lookYaw < N180) { lookYaw += P360; }

				LookYaw = lookYaw;
			}
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
			KCCMathUtility.ClampLookRotationAngles(ref pitch, ref yaw);

			LookPitch = pitch;
			LookYaw   = yaw;
		}

		/// <summary>
		/// Set pitch and yaw look rotation. Values are clamped to &lt;minPitch, maxPitch&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void SetLookRotation(FP pitch, FP yaw, FP minPitch, FP maxPitch)
		{
			KCCMathUtility.ClampLookRotationAngles(ref pitch, ref yaw, minPitch, maxPitch);

			LookPitch = pitch;
			LookYaw   = yaw;
		}

		/// <summary>
		/// Set pitch (x) and yaw (y) look rotation. Values are clamped to &lt;-90, 90&gt; (pitch) and &lt;-180, 180&gt; (yaw).
		/// Changes are propagated to Transform component in following KCC update.
		/// </summary>
		public void SetLookRotation(FPVector2 lookRotation)
		{
			SetLookRotation(lookRotation.X, lookRotation.Y);
		}

		/// <summary>
		/// Set pitch (x) and yaw (y) look rotation. Values are clamped to &lt;minPitch, maxPitch&gt; (pitch) and &lt;-180, 180&gt; (yaw).
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
			KCCMathUtility.GetClampedLookRotationAngles(lookRotation, out FP pitch, out FP yaw);

			if (preservePitch == false) { LookPitch = pitch; }
			if (preserveYaw   == false) { LookYaw   = yaw;   }
		}
	}
}


