namespace Quantum
{
	using Photon.Deterministic;
	using Quantum.Physics3D;

	/// <summary>
	/// Container for storing physics hit and metadata. Used mainly for multi-pass depenetration algorithm.
	/// </summary>
	public sealed class KCCOverlapHit
	{
	    public Hit3D             PhysicsHit;
	    public EKCCCollisionType CollisionType;
	    public bool              HasPenetration;
	    public FP                Penetration;
	    public FP                UpDirectionDot;

		public void Set(Hit3D physicsHit)
		{
			PhysicsHit     = physicsHit;
			CollisionType  = physicsHit.IsTrigger == true ? EKCCCollisionType.Trigger : EKCCCollisionType.None;
			HasPenetration = default;
			Penetration    = default;
			UpDirectionDot = default;
		}

		public void Reset()
		{
			PhysicsHit     = default;
			CollisionType  = default;
			HasPenetration = default;
			Penetration    = default;
			UpDirectionDot = default;
		}
	}
}
