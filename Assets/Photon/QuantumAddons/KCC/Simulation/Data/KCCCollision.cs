namespace Quantum
{
	using System;

	/// <summary>
	/// Defines collision type between KCC and overlapping collider surface calculated from depenetration or a trigger.
    /// <list type="bullet">
    /// <item><description>None - Default.</description></item>
    /// <item><description>Ground - Angle between Up and normalized depenetration vector is between 0 and KCCData.MaxGroundAngle.</description></item>
    /// <item><description>Slope - Angle between Up and normalized depenetration vector is between KCCData.MaxGroundAngle and (90 - KCCData.MaxWallAngle).</description></item>
    /// <item><description>Wall - Angle between Back and normalized depenetration vector is between -KCCData.MaxWallAngle and KCCData.MaxWallAngle.</description></item>
    /// <item><description>Hang - Angle between Back and normalized depenetration vector is between -30 and -KCCData.MaxWallAngle.</description></item>
    /// <item><description>Top - Angle between Back and normalized depenetration vector is lower than -30.</description></item>
    /// <item><description>Trigger - Overlapping collider - trigger. Penetration is unknown.</description></item>
    /// </list>
	/// </summary>
	public enum EKCCCollisionType : Byte
	{
	    None    = 0,
	    Ground  = 1,
	    Slope   = 2,
	    Wall    = 4,
	    Hang    = 8,
	    Top     = 16,
	    Trigger = 32,
	}

	/// <summary>
	/// Internal data structure for tracking collisions. Do not use directly.
	/// </summary>
	public partial struct KCCCollision : IEquatable<KCCCollision>
	{
		/// <summary>
		/// Returns valid EntityRef if the KCC collides with an entity with a collider.
		/// </summary>
		public EntityRef GetEntity()
		{
			return Source == EKCCCollisionSource.Entity ? Reference : EntityRef.None;
		}

		/// <summary>
		/// Returns valid static collider index if the KCC collides a static collider.
		/// </summary>
		public int GetColliderIndex()
		{
			return Source == EKCCCollisionSource.Collider ? Reference.Index : -1;
		}

		/// <summary>
		/// Returns info for processor callbacks based on this collision.
		/// </summary>
		public KCCProcessorInfo GetProcessorInfo()
		{
			switch (Source)
			{
				case EKCCCollisionSource.None:     return new KCCProcessorInfo(EKCCProcessorSource.None,           EntityRef.None, -1);
				case EKCCCollisionSource.Entity:   return new KCCProcessorInfo(EKCCProcessorSource.EntityCollider, Reference,      -1);
				case EKCCCollisionSource.Collider: return new KCCProcessorInfo(EKCCProcessorSource.StaticCollider, EntityRef.None, Reference.Index);
				default:
				{
					throw new NotImplementedException(Source.ToString());
				}
			}
		}

		public bool Equals(KCCCollision other)
		{
			return Source == other.Source && Reference.Equals(other.Reference) && Processor.Equals(other.Processor);
		}
	}
}


