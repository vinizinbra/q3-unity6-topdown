namespace Quantum
{
	using Photon.Deterministic;
	using Quantum.Collections;
	using Physics3D;

	unsafe partial struct KCC
	{
		/// <summary>
		/// Sphere overlap using same filtering as for KCC physics query.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC udpate.</param>
		/// <param name="overlapInfo">Contains results of the overlap.</param>
		/// <param name="position">Center position of the sphere.</param>
		/// <param name="radius">Radius of the sphere.</param>
		/// <param name="queryOptions">Use for custom query filtering.</param>
		public bool SphereOverlap(KCCContext context, KCCOverlapInfo overlapInfo, FPVector3 position, FP radius, QueryOptions queryOptions)
		{
			return SphereOverlap(context, overlapInfo, position, radius, default, context.Settings.CollisionLayerMask, queryOptions);
		}

		/// <summary>
		/// Capsule overlap using same filtering as for KCC physics query.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC udpate.</param>
		/// <param name="overlapInfo">Contains results of the overlap.</param>
		/// <param name="position">Bottom position of the capsule.</param>
		/// <param name="radius">Radius of the capsule.</param>
		/// <param name="height">Height of the capsule.</param>
		/// <param name="queryOptions">Use for custom query filtering.</param>
		public bool CapsuleOverlap(KCCContext context, KCCOverlapInfo overlapInfo, FPVector3 position, FP radius, FP height, QueryOptions queryOptions)
		{
			return CapsuleOverlap(context, overlapInfo, position, radius, height, default, context.Settings.CollisionLayerMask, queryOptions);
		}

		/// <summary>
		/// Ray cast using same filtering as for KCC physics query.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC udpate.</param>
		/// <param name="shapeCastInfo">Contains results of the cast sorted by distance.</param>
		/// <param name="position">Origin position of the cast.</param>
		/// <param name="direction">Direction of the cast.</param>
		/// <param name="maxDistance">Distance of the cast.</param>
		/// <param name="queryOptions">Use for custom query filtering.</param>
		public bool RayCast(KCCContext context, KCCShapeCastInfo shapeCastInfo, FPVector3 position, FPVector3 direction, FP maxDistance, QueryOptions queryOptions)
		{
			return RayCast(context, shapeCastInfo, position, direction, maxDistance, context.Settings.CollisionLayerMask, queryOptions);
		}

		/// <summary>
		/// Sphere cast using same filtering as for KCC physics query.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC udpate.</param>
		/// <param name="shapeCastInfo">Contains results of the cast sorted by distance.</param>
		/// <param name="position">Center position of the sphere.</param>
		/// <param name="radius">Radius of the sphere.</param>
		/// <param name="direction">Direction of the cast.</param>
		/// <param name="maxDistance">Distance of the cast.</param>
		/// <param name="queryOptions">Use for custom query filtering.</param>
		public bool SphereCast(KCCContext context, KCCShapeCastInfo shapeCastInfo, FPVector3 position, FP radius, FPVector3 direction, FP maxDistance, QueryOptions queryOptions)
		{
			return SphereCast(context, shapeCastInfo, position, radius, default, direction, maxDistance, context.Settings.CollisionLayerMask, queryOptions);
		}

		/// <summary>
		/// Capsule cast using same filtering as for KCC physics query.
		/// </summary>
		/// <param name="context">Reference to KCCContext from within KCC udpate.</param>
		/// <param name="shapeCastInfo">Contains results of the cast sorted by distance.</param>
		/// <param name="position">Bottom position of the capsule.</param>
		/// <param name="radius">Radius of the capsule.</param>
		/// <param name="height">Height of the capsule.</param>
		/// <param name="direction">Direction of the cast.</param>
		/// <param name="maxDistance">Distance of the cast.</param>
		/// <param name="queryOptions">Use for custom query filtering.</param>
		public bool CapsuleCast(KCCContext context, KCCShapeCastInfo shapeCastInfo, FPVector3 position, FP radius, FP height, FPVector3 direction, FP maxDistance, QueryOptions queryOptions)
		{
			return CapsuleCast(context, shapeCastInfo, position, radius, height, default, direction, maxDistance, context.Settings.CollisionLayerMask, queryOptions);
		}

		private bool SphereOverlap(KCCContext context, KCCOverlapInfo overlapInfo, FPVector3 position, FP radius, FP extent, LayerMask layerMask, QueryOptions queryOptions)
		{
			overlapInfo.Reset();

			overlapInfo.Position     = position;
			overlapInfo.Radius       = radius;
			overlapInfo.Height       = default;
			overlapInfo.Extent       = extent;
			overlapInfo.LayerMask    = layerMask;
			overlapInfo.QueryOptions = queryOptions;

			Shape3D sphereShape = Shape3D.CreateSphere(radius + extent);

			HitCollection3D hits = context.Frame.Physics3D.OverlapShape(position, FPQuaternion.Identity, sphereShape, layerMask, queryOptions);

			return ProcessOverlapHits(context, overlapInfo, hits);
		}

		private bool CapsuleOverlap(KCCContext context, KCCOverlapInfo overlapInfo, FPVector3 position, FP radius, FP height, FP extent, LayerMask layerMask, QueryOptions queryOptions)
		{
			overlapInfo.Reset();

			overlapInfo.Position     = position;
			overlapInfo.Radius       = radius;
			overlapInfo.Height       = height;
			overlapInfo.Extent       = extent;
			overlapInfo.LayerMask    = layerMask;
			overlapInfo.QueryOptions = queryOptions;

			FPVector3 capsulePosition = position + FPVector3.Up * (height / 2);
			Shape3D   capsuleShape    = Shape3D.CreateCapsule(radius + extent, height / 2 - radius);

			HitCollection3D hits = context.Frame.Physics3D.OverlapShape(capsulePosition, FPQuaternion.Identity, capsuleShape, layerMask, queryOptions);

			return ProcessOverlapHits(context, overlapInfo, hits);
		}

		private bool RayCast(KCCContext context, KCCShapeCastInfo shapeCastInfo, FPVector3 position, FPVector3 direction, FP maxDistance, LayerMask layerMask, QueryOptions queryOptions)
		{
			shapeCastInfo.Reset();

			shapeCastInfo.Position     = position;
			shapeCastInfo.Direction    = direction;
			shapeCastInfo.MaxDistance  = maxDistance;
			shapeCastInfo.LayerMask    = layerMask;
			shapeCastInfo.QueryOptions = queryOptions;

			HitCollection3D hits = context.Frame.Physics3D.RaycastAll(position, direction, maxDistance, layerMask, queryOptions);

			return ProcessShapeCastHits(context, shapeCastInfo, hits);
		}

		private bool SphereCast(KCCContext context, KCCShapeCastInfo shapeCastInfo, FPVector3 position, FP radius, FP extent, FPVector3 direction, FP maxDistance, LayerMask layerMask, QueryOptions queryOptions)
		{
			shapeCastInfo.Reset();

			shapeCastInfo.Position     = position;
			shapeCastInfo.Radius       = radius;
			shapeCastInfo.Extent       = extent;
			shapeCastInfo.Direction    = direction;
			shapeCastInfo.MaxDistance  = maxDistance;
			shapeCastInfo.LayerMask    = layerMask;
			shapeCastInfo.QueryOptions = queryOptions;

			Shape3D sphereShape = Shape3D.CreateSphere(radius + extent);

			HitCollection3D hits = context.Frame.Physics3D.ShapeCastAll(position, FPQuaternion.Identity, sphereShape, direction * maxDistance, layerMask, queryOptions);

			return ProcessShapeCastHits(context, shapeCastInfo, hits);
		}

		private bool CapsuleCast(KCCContext context, KCCShapeCastInfo shapeCastInfo, FPVector3 position, FP radius, FP height, FP extent, FPVector3 direction, FP maxDistance, LayerMask layerMask, QueryOptions queryOptions)
		{
			shapeCastInfo.Reset();

			shapeCastInfo.Position     = position;
			shapeCastInfo.Radius       = radius;
			shapeCastInfo.Height       = height;
			shapeCastInfo.Extent       = extent;
			shapeCastInfo.Direction    = direction;
			shapeCastInfo.MaxDistance  = maxDistance;
			shapeCastInfo.LayerMask    = layerMask;
			shapeCastInfo.QueryOptions = queryOptions;

			FPVector3 capsulePosition = position + FPVector3.Up * (height / 2);
			Shape3D   capsuleShape    = Shape3D.CreateCapsule(radius + extent, height / 2 - radius);

			HitCollection3D hits = context.Frame.Physics3D.ShapeCastAll(capsulePosition, FPQuaternion.Identity, capsuleShape, direction * maxDistance, layerMask, queryOptions);

			return ProcessShapeCastHits(context, shapeCastInfo, hits);
		}

		private static bool ProcessOverlapHits(KCCContext context, KCCOverlapInfo overlapInfo, HitCollection3D hits)
		{
			if (hits.Count <= 0)
				return false;

			QHashSet<KCCIgnore> ignores = context.Frame.ResolveHashSet(context.KCC->Ignores);

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
					KCCPenetrationSolver meshPenetrationSolver = KCCThreadStaticCache.Get<KCCPenetrationSolver>();

					meshPenetrationSolver.Reset();
					meshPenetrationSolver.AddCorrection(hit3D->Normal, hit3D->OverlapPenetration);

					for (int j = i + 1; j < hits.Count; ++j)
					{
						Hit3D* otherHit = hits.HitsBuffer + j;
						if (otherHit->StaticColliderIndex == staticColliderIndex)
						{
							meshPenetrationSolver.AddCorrection(otherHit->Normal, otherHit->OverlapPenetration);
							otherHit->SetHitEntity(EntityRef.None);
						}
					}

					if (meshPenetrationSolver.Count > 1)
					{
						FPVector3 correction = meshPenetrationSolver.CalculateBest(10, FP.EN4);

						hit3D->OverlapPenetration = correction.Magnitude;
						if (hit3D->OverlapPenetration >= FP.EN4)
						{
							hit3D->Normal = correction / hit3D->OverlapPenetration;
						}

						hit3D->TriangleIndex = -999;
					}

					meshPenetrationSolver.Reset();
					KCCThreadStaticCache.Return(meshPenetrationSolver);
				}

				overlapInfo.AddHit(*hit3D);
			}

			return overlapInfo.AllHits.Count > 0;
		}

		private static bool ProcessShapeCastHits(KCCContext context, KCCShapeCastInfo shapeCastInfo, HitCollection3D hits)
		{
			if (hits.Count <= 0)
				return false;

			QHashSet<KCCIgnore> ignores = context.Frame.ResolveHashSet(context.KCC->Ignores);

			for (int i = 0, count = hits.Count; i < count; ++i)
			{
				Hit3D hit3D = hits[i];

				int staticColliderIndex = hit3D.StaticColliderIndex;
				if (staticColliderIndex >= 0)
				{
					if (ignores.Contains(new KCCIgnore(staticColliderIndex)) == true)
						continue;
				}
				else if (hit3D.Entity != EntityRef.None)
				{
					if (hit3D.Entity == context.Entity)
						continue;
					if (ignores.Contains(new KCCIgnore(hit3D.Entity)) == true)
						continue;
				}
				else
				{
					continue;
				}

				if (context.ResolveCollision != null && context.ResolveCollision(context, hit3D) == false)
					continue;

				if (staticColliderIndex >= 0 && hit3D.TriangleIndex >= 0)
				{
					for (int j = i + 1; j < hits.Count; ++j)
					{
						Hit3D* otherHit = hits.HitsBuffer + j;
						if (otherHit->StaticColliderIndex == staticColliderIndex)
						{
							if (otherHit->OverlapPenetration < hit3D.OverlapPenetration)
							{
								hit3D = *otherHit;
								otherHit->SetHitEntity(EntityRef.None);
							}
						}
					}
				}

				shapeCastInfo.AddHit(hit3D);
			}

			shapeCastInfo.Sort();

			return shapeCastInfo.AllHits.Count > 0;
		}
	}
}


