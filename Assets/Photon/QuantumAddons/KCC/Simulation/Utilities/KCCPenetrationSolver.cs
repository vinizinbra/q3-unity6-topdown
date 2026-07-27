namespace Quantum
{
	using System;
	using Photon.Deterministic;

	public sealed class KCCPenetrationSolver
	{
		// PUBLIC MEMBERS

		/// <summary>Count of input corrections.</summary>
		public int Count => _count;

		// PRIVATE MEMBERS

		private int          _count;
		private Correction[] _corrections;

		// CONSTRUCTORS

		public KCCPenetrationSolver() : this(64)
		{
		}

		public KCCPenetrationSolver(int capacity)
		{
			_corrections = new Correction[capacity];
			for (int i = 0; i < capacity; ++i)
			{
				_corrections[i] = new Correction();
			}
		}

		// PUBLIC METHODS

		/// <summary>
		/// Resets resolver. Call this before adding corrections.
		/// </summary>
		public void Reset()
		{
			_count = default;
		}

		/// <summary>
		/// Adds single correction vector.
		/// </summary>
		public bool AddCorrection(FPVector3 direction, FP distance)
		{
			if (_count >= _corrections.Length)
				return false;

			Correction correction = _corrections[_count];

			correction.Amount    = direction * distance;
			correction.Direction = direction;
			correction.Distance  = distance;
			correction.Error     = default;

			++_count;

			return true;
		}

		/// <summary>
		/// Returns correction at specific index.
		/// </summary>
		public FPVector3 GetCorrection(int index)
		{
			return _corrections[index].Amount;
		}

		/// <summary>
		/// Returns correction amount and direction at specific index.
		/// </summary>
		public FPVector3 GetCorrection(int index, out FPVector3 direction)
		{
			Correction correction = _corrections[index];
			direction = correction.Direction;
			return correction.Amount;
		}

		/// <summary>
		/// Returns correction amount, direction and distance at specific index.
		/// </summary>
		public FPVector3 GetCorrection(int index, out FPVector3 direction, out FP distance)
		{
			Correction correction = _corrections[index];
			direction = correction.Direction;
			distance  = correction.Distance;
			return correction.Amount;
		}

		/// <summary>
		/// Calculates target correction vector based on added corrections.
		/// </summary>
		public FPVector3 CalculateBest(int maxIterations, FP maxError)
		{
			if (_count <= 0)
				return default;
			if (_count == 1)
				return _corrections[0].Amount;

			if (_count == 2)
			{
				Correction correction0 = _corrections[0];
				Correction correction1 = _corrections[1];

				if (FPVector3.Dot(correction0.Direction, correction1.Direction) < FP._0)
				{
					return CalculateBinary(correction0, correction1);
				}
			}

			if (_count == 3)
			{
				Correction correction0 = _corrections[0];
				Correction correction1 = _corrections[1];
				Correction correction2 = _corrections[2];

				FP absUpDot0 = FPMath.Abs(FPVector3.Dot(correction0.Direction, FPVector3.Up));
				FP absUpDot1 = FPMath.Abs(FPVector3.Dot(correction1.Direction, FPVector3.Up));
				FP absUpDot2 = FPMath.Abs(FPVector3.Dot(correction2.Direction, FPVector3.Up));

				FP wallThreshold  = FP._0_05 * FP._0_10;
				FP floorThreshold = FP._1 - FP._0_05 * FP._0_01;

				if (absUpDot0 > floorThreshold)
				{
					if (absUpDot1 < wallThreshold && absUpDot2 < wallThreshold && FPVector3.Dot(correction1.Direction, correction2.Direction) < FP._0)
					{
						return CalculateMinMax(correction0.Amount, CalculateBinary(correction1, correction2));
					}
				}
				else if (absUpDot1 > floorThreshold)
				{
					if (absUpDot0 < wallThreshold && absUpDot2 < wallThreshold && FPVector3.Dot(correction0.Direction, correction2.Direction) < FP._0)
					{
						return CalculateMinMax(correction1.Amount, CalculateBinary(correction0, correction2));
					}
				}
				else if (absUpDot2 > floorThreshold)
				{
					if (absUpDot0 < wallThreshold && absUpDot1 < wallThreshold && FPVector3.Dot(correction0.Direction, correction1.Direction) < FP._0)
					{
						return CalculateMinMax(correction2.Amount, CalculateBinary(correction0, correction1));
					}
				}
			}

			return CalculateErrorDescent(maxIterations, maxError);
		}

		/// <summary>
		/// Calculates target correction vector based on added corrections.
		/// </summary>
		public FPVector3 CalculateMinMax()
		{
			if (_count <= 0)
				return default;
			if (_count == 1)
				return _corrections[0].Amount;

			FPVector3 minCorrection = default;
			FPVector3 maxCorrection = default;

			for (int i = 0; i < _count; ++i)
			{
				Correction correction = _corrections[i];

				minCorrection = FPVector3.Min(minCorrection, correction.Amount);
				maxCorrection = FPVector3.Max(maxCorrection, correction.Amount);
			}

			return minCorrection + maxCorrection;
		}

		/// <summary>
		/// Calculates target correction vector based on added corrections.
		/// </summary>
		public FPVector3 CalculateSum()
		{
			if (_count <= 0)
				return default;
			if (_count == 1)
				return _corrections[0].Amount;

			FPVector3 targetCorrection = default;

			for (int i = 0; i < _count; ++i)
			{
				targetCorrection += _corrections[i].Amount;
			}

			return targetCorrection;
		}

		/// <summary>
		/// Calculates target correction vector based on added corrections.
		/// </summary>
		public FPVector3 CalculateAverage()
		{
			if (_count <= 0)
				return default;
			if (_count == 1)
				return _corrections[0].Amount;

			return CalculateSum() / _count;
		}

		/// <summary>
		/// Calculates target correction vector based on added corrections.
		/// </summary>
		public FPVector3 CalculateBinary()
		{
			if (_count <= 0)
				return default;
			if (_count == 1)
				return _corrections[0].Amount;
			if (_count > 2)
				throw new InvalidOperationException("Count of corrections must be 2 at max!");

			return CalculateBinary(_corrections[0], _corrections[1]);
		}

		/// <summary>
		/// Calculates target correction vector based on added corrections.
		/// </summary>
		public FPVector3 CalculateErrorDescent(int maxIterations, FP maxError)
		{
			if (_count <= 0)
				return default;
			if (_count == 1)
				return _corrections[0].Amount;

			int       iterations       = default;
			FPVector3 targetCorrection = default;

			while (iterations < maxIterations)
			{
				++iterations;

				FP accumulatedError = default;

				for (int i = 0; i < _count; ++i)
				{
					Correction correction = _corrections[i];

					FP error = correction.Distance - FPVector3.Dot(targetCorrection, correction.Direction);
					if (error > FP._0)
					{
						accumulatedError += error;
						targetCorrection += error * correction.Direction;
					}
				}

				if(accumulatedError < maxError)
					break;
			}

			return targetCorrection;
		}

		/// <summary>
		/// Calculates target correction vector based on added corrections.
		/// </summary>
		public FPVector3 CalculateGradientDescent(int maxIterations, FP maxError)
		{
			if (_count <= 0)
				return default;
			if (_count == 1)
				return _corrections[0].Amount;

			FPVector3    error;
			FP           errorDot;
			FP           errorCorrection;
			FP           errorCorrectionSize;
			FPVector3    targetCorrection = default;
			int          iterations = default;

			while (iterations < maxIterations)
			{
				++iterations;

				error               = default;
				errorCorrection     = default;
				errorCorrectionSize = default;

				for (int i = 0, count = _count; i < count; ++i)
				{
					Correction correction = _corrections[i];

					// Calculate error of desired correction relative to single correction.
					correction.Error = correction.Direction.X * targetCorrection.X + correction.Direction.Y * targetCorrection.Y + correction.Direction.Z * targetCorrection.Z - correction.Distance;

					// Accumulate error of all corrections.
					error += correction.Direction * correction.Error;
				}

				// The accumulated error is almost zero which means we hit a local minimum.
				if (error.Magnitude <= maxError)
					break;

				// Normalize the error => now we know what is the wrong direction => desired correction needs to move in opposite direction to lower the error.
				error = error.Normalized;

				for (int i = 0; i < _count; ++i)
				{
					Correction correction = _corrections[i];

					// Compare single correction direction with the accumulated error direction.
					errorDot = correction.Direction.X * error.X + correction.Direction.Y * error.Y + correction.Direction.Z * error.Z;

					// Accumulate error correction based on relative correction errors.
					// Corrections with direction aligned to accumulated error have more impact.
					errorCorrection += correction.Error * errorDot;

					if (errorDot >= 0)
					{
						errorCorrectionSize += errorDot;
					}
					else
					{
						errorCorrectionSize -= errorDot;
					}
				}

				if (errorCorrectionSize < FP.EN4)
					break;

				// The error correction is almost zero and desired correction won't change.
				errorCorrection /= errorCorrectionSize;
				if (error.Magnitude <= maxError)
					break;

				// Move desired correction in opposite way of the accumulated error.
				targetCorrection -= error * errorCorrection;
			}

			return targetCorrection;
		}

		// PRIVATE METHODS

		private static FPVector3 CalculateMinMax(params FPVector3[] corrections)
		{
			FPVector3 minCorrection = default;
			FPVector3 maxCorrection = default;

			for (int i = 0; i < corrections.Length; ++i)
			{
				FPVector3 correction = corrections[i];
				minCorrection = FPVector3.Min(minCorrection, correction);
				maxCorrection = FPVector3.Max(maxCorrection, correction);
			}

			return minCorrection + maxCorrection;
		}

		private static FPVector3 CalculateBinary(Correction correction0, Correction correction1)
		{
			FP threshold = FP._1 - FP.EN4;

			FP correctionDot = FPVector3.Dot(correction0.Direction, correction1.Direction);
			if (correctionDot > threshold || correctionDot < -threshold)
			{
				FPVector3 minCorrection = FPVector3.Min(FPVector3.Min(default, correction0.Amount), correction1.Amount);
				FPVector3 maxCorrection = FPVector3.Max(FPVector3.Max(default, correction0.Amount), correction1.Amount);
				return minCorrection + maxCorrection;
			}

			FPVector3 deltaCorrectionDirection = FPVector3.Cross(FPVector3.Cross(correction0.Direction, correction1.Direction), correction0.Direction).Normalized;
			FP        deltaCorrectionDistance  = (correction1.Distance - correction0.Distance * correctionDot) / FPMath.Sqrt(FP._1 - correctionDot * correctionDot);

			return correction0.Amount + deltaCorrectionDirection * deltaCorrectionDistance;
		}

		// DATA STRUCTURES

		private sealed class Correction
		{
			public FPVector3 Amount;
			public FPVector3 Direction;
			public FP        Distance;
			public FP        Error;
		}
	}
}


