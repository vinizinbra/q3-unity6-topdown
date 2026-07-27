namespace Quantum
{
	using Photon.Deterministic;

	public static unsafe partial class KCCMathUtility
	{
		private static readonly FP P90  = 90;
		private static readonly FP N90  = -90;
		private static readonly FP P180 = 180;
		private static readonly FP N180 = -180;
		private static readonly FP P360 = 360;

		public static void ClampLookRotationAngles(ref FP pitch, ref FP yaw)
		{
			pitch = FPMath.Clamp(pitch, N90, P90);

			while (yaw > P180) { yaw -= P360; }
			while (yaw < N180) { yaw += P360; }
		}

		public static void ClampLookRotationAngles(ref FP pitch, ref FP yaw, FP minPitch, FP maxPitch)
		{
			if (minPitch < N90) { minPitch = N90; }
			if (maxPitch > P90) { maxPitch = P90; }

			if (maxPitch < minPitch) { maxPitch = minPitch; }

			pitch = FPMath.Clamp(pitch, minPitch, maxPitch);

			while (yaw > P180) { yaw -= P360; }
			while (yaw < N180) { yaw += P360; }
		}

		public static FPVector2 ClampLookRotationAngles(FPVector2 lookRotation)
		{
			return ClampLookRotationAngles(lookRotation, N90, P90);
		}

		public static FPVector2 ClampLookRotationAngles(FPVector2 lookRotation, FP minPitch, FP maxPitch)
		{
			if (minPitch < N90) { minPitch = N90; }
			if (maxPitch > P90) { maxPitch = P90; }

			if (maxPitch < minPitch) { maxPitch = minPitch; }

			lookRotation.X = FPMath.Clamp(lookRotation.X, minPitch, maxPitch);

			while (lookRotation.Y > P180) { lookRotation.Y -= P360; }
			while (lookRotation.Y < N180) { lookRotation.Y += P360; }

			return lookRotation;
		}

		public static void GetClampedLookRotationAngles(FPQuaternion lookRotation, out FP pitch, out FP yaw)
		{
			FPVector3 eulerAngles = lookRotation.AsEuler;

			if (eulerAngles.X > P180) { eulerAngles.X -= P360; }
			if (eulerAngles.Y > P180) { eulerAngles.Y -= P360; }

			pitch = FPMath.Clamp(eulerAngles.X,  N90,  P90);
			yaw   = FPMath.Clamp(eulerAngles.Y, N180, P180);
		}

		public static FPVector2 GetClampedEulerLookRotation(FPQuaternion lookRotation)
		{
			FPVector2 eulerAngles = lookRotation.AsEuler.XY;

			if (eulerAngles.X > P180) { eulerAngles.X -= P360; }
			if (eulerAngles.Y > P180) { eulerAngles.Y -= P360; }

			eulerAngles.X = FPMath.Clamp(eulerAngles.X,  N90,  P90);
			eulerAngles.Y = FPMath.Clamp(eulerAngles.Y, N180, P180);

			return eulerAngles;
		}

		public static FPVector2 GetClampedEulerLookRotation(FPVector3 direction)
		{
			return GetClampedEulerLookRotation(FPQuaternion.LookRotation(direction));
		}

		public static FPVector2 GetClampedEulerLookRotation(FPVector2 lookRotation, FPVector2 lookRotationDelta, FP minPitch, FP maxPitch)
		{
			return ClampLookRotationAngles(lookRotation + lookRotationDelta, minPitch, maxPitch);
		}

		public static FPVector2 GetClampedEulerLookRotationDelta(FPVector2 lookRotation, FPVector2 lookRotationDelta, FP minPitch, FP maxPitch)
		{
			FPVector2 clampedlookRotationDelta = lookRotationDelta;
			lookRotationDelta.X = FPMath.Clamp(lookRotation.X + lookRotationDelta.X, minPitch, maxPitch) - lookRotation.X;
			return lookRotationDelta;
		}
	}
}
