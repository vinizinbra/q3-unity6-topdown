namespace Quantum
{
	using System;

	/// <summary>
	/// Internal data structure for tracking ignored colliders. Do not use directly.
	/// </summary>
	public partial struct KCCIgnore : IEquatable<KCCIgnore>
	{
		public KCCIgnore(EntityRef entity)
		{
			Source    = EKCCIgnoreSource.Entity;
			Reference = entity;
		}

		public KCCIgnore(int colliderIndex)
		{
			Source    = EKCCIgnoreSource.Collider;
			Reference = default;

			Reference.Index = colliderIndex;
		}

		public EntityRef GetEntity()
		{
			return Source == EKCCIgnoreSource.Entity ? Reference : EntityRef.None;
		}

		public bool Equals(KCCIgnore other)
		{
			return Source == other.Source && Reference.Equals(other.Reference);
		}
	}
}


