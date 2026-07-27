namespace Quantum
{
	using System;
	using Photon.Deterministic;
	using Quantum.Collections;
	using Quantum.Physics3D;

	unsafe partial struct KCC
	{
		/// <summary>
		/// Returns position depenetrated from all overlapping colliders and updates ground related data in KCCData.
		/// </summary>
		public void ResolvePenetration(KCCContext context, KCCOverlapInfo overlapInfo, ref KCCData data, int maxSteps = 1)
		{
			ResolvePenetration(context, overlapInfo, ref data, context.Settings.Radius, context.Settings.Height, FP._0, context.Settings.CollisionLayerMask, maxSteps);
		}

		/// <summary>
		/// Depenetrates KCCData.TargetPosition from all overlapping colliders and updates ground related data in KCCData.
		/// </summary>
		public void ResolvePenetration(KCCContext context, KCCOverlapInfo overlapInfo, ref KCCData data, FP radius, FP height, FP extent, LayerMask layerMask, int maxSteps)
		{
			overlapInfo.Reset();

			FPVector3    capsuleOffset = FPVector3.Up * (height / 2);
			Shape3D      capsuleShape  = Shape3D.CreateCapsule(radius + extent, height / 2 - radius);
			QueryOptions queryOptions  = QueryOptions.HitAll | QueryOptions.ComputeDetailedInfo;

			// Reset to initial values

			data.IsGrounded     = false;
			data.GroundNormal   = FPVector3.Up;
			data.GroundAngle    = FP._0;
			data.GroundPosition = data.TargetPosition;
			data.GroundDistance = FP._0;
			data.GroundTangent  = data.TransformDirection;

			// First-pass

			HitCollection3D hits = context.Frame.Physics3D.OverlapShape(data.TargetPosition + capsuleOffset, FPQuaternion.Identity, capsuleShape, layerMask, queryOptions);
			if (hits.Count == 0)
				return;
			if (hits.Count == 1 && hits[0].Entity == context.Entity)
				return;

			QHashSet<KCCIgnore>  ignores                  = context.Frame.ResolveHashSet(context.KCC->Ignores);
			FP                   maxGroundDot             = default;
			FPVector3            maxGroundNormal          = FPVector3.Up;
			FP                   minSurfaceDot            = FPMath.Cos(FPMath.Clamp(90 - data.MaxGroundAngle, FP._0, 90) * FP.Deg2Rad);
			FP                   minGroundDot             = FPMath.Cos(FPMath.Clamp(data.MaxGroundAngle, FP._0, 90) * FP.Deg2Rad);
			FP                   minWallDot               = -FPMath.Cos(FPMath.Clamp(90 - data.MaxWallAngle, FP._0, 90) * FP.Deg2Rad);
			FP                   minHangDot               = -FPMath.Cos(FPMath.Clamp(90 - data.MaxHangAngle, FP._0, 90) * FP.Deg2Rad);
			FPVector3            positionDelta            = data.TargetPosition - data.BasePosition;
			bool                 projectPenetration       = data.DynamicVelocity.Y <= FP._0;
			KCCPenetrationSolver overlapPenetrationSolver = KCCThreadStaticCache.Get<KCCPenetrationSolver>();

			int extraSteps = Math.Max(0, maxSteps - 1);
			if (extraSteps > 0)
			{
				FP minStepDistance = FP.EN3;

				FP targetDistance = FPVector3.Distance(data.BasePosition, data.TargetPosition);
				if (targetDistance < extraSteps * minStepDistance)
				{
					extraSteps = Math.Clamp((targetDistance / minStepDistance).AsInt, 0, extraSteps);
				}
			}

			overlapPenetrationSolver.Reset();

			for (int i = 0, count = hits.Count; i < count; ++i)
			{
				Hit3D* hit3D = hits.HitsBuffer + i;

				int staticColliderIndex = hit3D->StaticColliderIndex;
				if (staticColliderIndex >= 0)
				{
					if (ignores.Contains(new KCCIgnore(staticColliderIndex)) == true)
						continue;
				}
				else if (hit3D->Entity != EntityRef.None)
				{
					if (hit3D->Entity == context.Entity)
						continue;
					if (ignores.Contains(new KCCIgnore(hit3D->Entity)) == true)
						continue;
				}
				else
				{
					continue;
				}

				if (context.ResolveCollision != null && context.ResolveCollision(context, *hit3D) == false)
					continue;

				if (staticColliderIndex >= 0 && hit3D->TriangleIndex >= 0)
				{
					bool forceProjectPenetration = projectPenetration == true && KCCPhysicsUtility.ShouldForceProjectPenetration(hits, staticColliderIndex, minSurfaceDot);
					if (KCCPhysicsUtility.ComputeMeshPenetration(hit3D, data.TargetPosition, radius, height, projectPenetration, forceProjectPenetration, minGroundDot, positionDelta) == false)
						continue;

					KCCPenetrationSolver meshPenetrationSolver = KCCThreadStaticCache.Get<KCCPenetrationSolver>();

					meshPenetrationSolver.Reset();
					meshPenetrationSolver.AddCorrection(hit3D->Normal, hit3D->OverlapPenetration);

					for (int j = i + 1; j < hits.Count; ++j)
					{
						Hit3D* otherHit = hits.HitsBuffer + j;
						if (otherHit->StaticColliderIndex == staticColliderIndex)
						{
							if (KCCPhysicsUtility.ComputeMeshPenetration(otherHit, data.TargetPosition, radius, height, projectPenetration, forceProjectPenetration, minGroundDot, positionDelta) == true)
							{
								meshPenetrationSolver.AddCorrection(otherHit->Normal, otherHit->OverlapPenetration);
							}

							otherHit->SetHitEntity(EntityRef.None);
						}
					}

					if (meshPenetrationSolver.Count > 1)
					{
						FPVector3 meshCorrection = meshPenetrationSolver.CalculateBest(8, FP.EN3);

						hit3D->OverlapPenetration = meshCorrection.Magnitude;
						if (hit3D->OverlapPenetration >= FP.EN4)
						{
							hit3D->Normal = meshCorrection / hit3D->OverlapPenetration;
						}

						hit3D->TriangleIndex = -999;
					}

					meshPenetrationSolver.Reset();
					KCCThreadStaticCache.Return(meshPenetrationSolver);
				}

				KCCOverlapHit overlapHit = overlapInfo.AddHit(*hit3D);

				FP distance = hit3D->OverlapPenetration - extent;
				if (distance >= FP._0)
				{
					overlapHit.HasPenetration = true;
					overlapHit.Penetration    = distance;
				}
				else
				{
					overlapHit.HasPenetration = false;
					overlapHit.Penetration    = -distance;
				}

				if (overlapHit.CollisionType == EKCCCollisionType.Trigger)
					continue;

				FPVector3 direction = hit3D->Normal;

				overlapHit.UpDirectionDot = FPVector3.Dot(direction, FPVector3.Up);

				if (overlapHit.UpDirectionDot >= minGroundDot)
				{
					overlapHit.CollisionType = EKCCCollisionType.Ground;

					data.IsGrounded = true;

					if (overlapHit.UpDirectionDot >= maxGroundDot)
					{
						maxGroundDot    = overlapHit.UpDirectionDot;
						maxGroundNormal = direction;
					}
				}
				else if (overlapHit.UpDirectionDot > -minWallDot)
				{
					overlapHit.CollisionType = EKCCCollisionType.Slope;
				}
				else if (overlapHit.UpDirectionDot >= minWallDot)
				{
					overlapHit.CollisionType = EKCCCollisionType.Wall;
				}
				else if (overlapHit.UpDirectionDot>= minHangDot)
				{
					overlapHit.CollisionType = EKCCCollisionType.Hang;
				}
				else
				{
					overlapHit.CollisionType = EKCCCollisionType.Top;
				}

				if (distance <= FP._0)
					continue;

				if (overlapHit.UpDirectionDot > FP._0 && overlapHit.UpDirectionDot < minGroundDot)
				{
					if (distance >= FP.EN4 && projectPenetration == true)
					{
						FP movementDot = FPVector2.Dot(positionDelta.XZ, direction.XZ);
						if (movementDot < FP._0)
						{
							KCCPhysicsUtility.ProjectVerticalPenetration(ref direction, ref distance);
						}
					}
				}

				overlapHit.Penetration = distance;

				overlapPenetrationSolver.AddCorrection(direction, distance);
			}

			if (overlapPenetrationSolver.Count > 0)
			{
				int solverInterations = 8;
				FP  solverMaxError    = FP.EN3;

				if (extraSteps == 0)
				{
					solverInterations = 12;
					solverMaxError    = FP.EN4;
				}

				FPVector3 correction = overlapPenetrationSolver.CalculateBest(solverInterations, solverMaxError);
				correction = FPVector3.ClampMagnitude(correction, radius);

				FP correctionMultiplier = FPMath.Max(FP._0_25, FP._1 - extraSteps * FP._0_25);

				data.TargetPosition += correction * correctionMultiplier;
			}
			else
			{
				extraSteps = 0;
			}

			// Multi-pass

			while (extraSteps > 0)
			{
				--extraSteps;

				capsuleShape = Shape3D.CreateCapsule(radius, height / 2 - radius);

				hits = context.Frame.Physics3D.OverlapShape(data.TargetPosition + capsuleOffset, FPQuaternion.Identity, capsuleShape, layerMask, queryOptions);
				if (hits.Count == 0)
					break;
				if (hits.Count == 1 && hits[0].Entity == context.Entity)
					break;

				overlapPenetrationSolver.Reset();

				for (int i = 0, count = hits.Count; i < count; ++i)
				{
					Hit3D* hit3D = hits.HitsBuffer + i;

					int staticColliderIndex = hit3D->StaticColliderIndex;
					if (staticColliderIndex >= 0)
					{
						if (ignores.Contains(new KCCIgnore(staticColliderIndex)) == true)
							continue;
					}
					else if (hit3D->Entity != EntityRef.None)
					{
						if (hit3D->Entity == context.Entity)
							continue;
						if (ignores.Contains(new KCCIgnore(hit3D->Entity)) == true)
							continue;
					}
					else
					{
						continue;
					}

					if (context.ResolveCollision != null && context.ResolveCollision(context, *hit3D) == false)
						continue;

					if (staticColliderIndex >= 0 && hit3D->TriangleIndex >= 0)
					{
						bool forceProjectPenetration = projectPenetration == true && KCCPhysicsUtility.ShouldForceProjectPenetration(hits, staticColliderIndex, minSurfaceDot);
						if (KCCPhysicsUtility.ComputeMeshPenetration(hit3D, data.TargetPosition, radius, height, projectPenetration, forceProjectPenetration, minGroundDot, positionDelta) == false)
							continue;

						KCCPenetrationSolver meshPenetrationSolver = KCCThreadStaticCache.Get<KCCPenetrationSolver>();

						meshPenetrationSolver.Reset();
						meshPenetrationSolver.AddCorrection(hit3D->Normal, hit3D->OverlapPenetration);

						for (int j = i + 1; j < hits.Count; ++j)
						{
							Hit3D* otherHit = hits.HitsBuffer + j;
							if (otherHit->StaticColliderIndex == staticColliderIndex)
							{
								if (KCCPhysicsUtility.ComputeMeshPenetration(otherHit, data.TargetPosition, radius, height, projectPenetration, forceProjectPenetration, minGroundDot, positionDelta) == true)
								{
									meshPenetrationSolver.AddCorrection(otherHit->Normal, otherHit->OverlapPenetration);
								}

								otherHit->SetHitEntity(EntityRef.None);
							}
						}

						if (meshPenetrationSolver.Count > 1)
						{
							FPVector3 meshCorrection = meshPenetrationSolver.CalculateBest(8, FP.EN3);

							hit3D->OverlapPenetration = meshCorrection.Magnitude;
							if (hit3D->OverlapPenetration >= FP.EN4)
							{
								hit3D->Normal = meshCorrection / hit3D->OverlapPenetration;
							}

							hit3D->TriangleIndex = -999;
						}

						meshPenetrationSolver.Reset();
						KCCThreadStaticCache.Return(meshPenetrationSolver);
					}

					KCCOverlapHit overlapHit = overlapInfo.GetOrAddHit(*hit3D);

					FP   distance       = hit3D->OverlapPenetration;
					bool hadPenetration = overlapHit.HasPenetration;

					overlapHit.HasPenetration = true;

					if (overlapHit.CollisionType == EKCCCollisionType.Trigger)
					{
						overlapHit.Penetration = hadPenetration == true ? FPMath.Min(overlapHit.Penetration, distance) : distance;
						continue;
					}

					FPVector3 direction      = hit3D->Normal;
					FP        upDirectionDot = FPVector3.Dot(direction, FPVector3.Up);

					if (hadPenetration == false || upDirectionDot > overlapHit.UpDirectionDot)
					{
						if (upDirectionDot >= minGroundDot)
						{
							overlapHit.CollisionType = EKCCCollisionType.Ground;

							data.IsGrounded = true;

							if (upDirectionDot >= maxGroundDot)
							{
								maxGroundDot    = upDirectionDot;
								maxGroundNormal = direction;
							}
						}
						else if (upDirectionDot > -minWallDot)
						{
							overlapHit.CollisionType = EKCCCollisionType.Slope;
						}
						else if (upDirectionDot >= minWallDot)
						{
							overlapHit.CollisionType = EKCCCollisionType.Wall;
						}
						else if (upDirectionDot>= minHangDot)
						{
							overlapHit.CollisionType = EKCCCollisionType.Hang;
						}
						else
						{
							overlapHit.CollisionType = EKCCCollisionType.Top;
						}
					}

					if (upDirectionDot > FP._0 && upDirectionDot < minGroundDot)
					{
						if (distance >= FP.EN4 && projectPenetration == true)
						{
							FP movementDot = FPVector2.Dot(positionDelta.XZ, direction.XZ);
							if (movementDot < FP._0)
							{
								KCCPhysicsUtility.ProjectVerticalPenetration(ref direction, ref distance);
							}
						}
					}

					if (hadPenetration == true)
					{
						overlapHit.Penetration    = FPMath.Min(overlapHit.Penetration, distance);
						overlapHit.UpDirectionDot = FPMath.Max(overlapHit.UpDirectionDot, upDirectionDot);
					}
					else
					{
						overlapHit.Penetration    = distance;
						overlapHit.UpDirectionDot = upDirectionDot;
					}

					overlapPenetrationSolver.AddCorrection(direction, distance);
				}

				if (overlapPenetrationSolver.Count <= 0)
					break;

				int solverInterations = 8;
				FP  solverMaxError    = FP.EN3;

				if (extraSteps == 0)
				{
					solverInterations = 12;
					solverMaxError    = FP.EN4;
				}

				FPVector3 correction = overlapPenetrationSolver.CalculateBest(solverInterations, solverMaxError);
				correction = FPVector3.ClampMagnitude(correction, radius);

				FP correctionMultiplier = FPMath.Max(FP._0_25, FP._1 - extraSteps * FP._0_25);

				data.TargetPosition += correction * correctionMultiplier;
			}

			// Post-processing

			if (data.IsGrounded == true)
			{
				data.GroundNormal   = maxGroundNormal;
				data.GroundAngle    = FPVector3.Angle(maxGroundNormal, FPVector3.Up);
				data.GroundPosition = data.TargetPosition + new FPVector3(FP._0, radius, FP._0) - data.GroundNormal * radius;
				data.GroundDistance = FP._0;

				if (KCCPhysicsUtility.ProjectOnGround(data.GroundNormal, data.GroundNormal.XZ.XOY, out FPVector3 projectedGroundNormal) == true)
				{
					data.GroundTangent = projectedGroundNormal.Normalized;
				}
				else if (KCCPhysicsUtility.ProjectOnGround(data.GroundNormal, data.DesiredVelocity.XZ.XOY, out FPVector3 projectedDesiredVelocity) == true)
				{
					data.GroundTangent = projectedDesiredVelocity.Normalized;
				}
				else
				{
					data.GroundTangent = data.TransformDirection;
				}
			}

			overlapPenetrationSolver.Reset();
			KCCThreadStaticCache.Return(overlapPenetrationSolver);
		}
	}
}
