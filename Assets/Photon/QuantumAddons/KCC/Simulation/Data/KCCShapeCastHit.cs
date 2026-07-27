namespace Quantum
{
	using Quantum.Physics3D;

	/// <summary>
	/// Container for storing shape-cast physics hit and metadata.
	/// </summary>
	public sealed class KCCShapeCastHit
	{
	    public Hit3D             PhysicsHit;
	    public EKCCCollisionType CollisionType;

		public void Set(Hit3D physicsHit)
		{
			PhysicsHit    = physicsHit;
			CollisionType = physicsHit.IsTrigger == true ? EKCCCollisionType.Trigger : EKCCCollisionType.None;
		}

		public void Reset()
		{
			PhysicsHit    = default;
			CollisionType = default;
		}
	}
}
