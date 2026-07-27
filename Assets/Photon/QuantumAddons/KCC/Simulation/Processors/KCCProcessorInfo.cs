namespace Quantum
{
	/// <summary>
	/// Data structure which is passed in processor callbacks.
	/// Contains information about the processor registration source and a collider/entity that is referencing currently executed processor.
	/// </summary>
	public record KCCProcessorInfo
	{
		/// <summary>
		/// Defines source of the processor (manually registered modifier processor, a processor linked to static collider, a processor linked to KCCProcessorSource component on an entity).
		/// </summary>
		public readonly EKCCProcessorSource Source;

		/// <summary>
		/// Reference to an entity which references the currently executed processor.
		/// </summary>
		public readonly EntityRef Entity;

		/// <summary>
		/// Index of a static collider which references the currently executed processor.
		/// </summary>
		public readonly int ColliderIndex;

		public static readonly KCCProcessorInfo Default = new KCCProcessorInfo(EKCCProcessorSource.None, EntityRef.None, -1);

		public bool HasEntity        => Entity.IsValid;
		public bool IsModifier       => Source == EKCCProcessorSource.Modifier;
		public bool IsCollider       => Source == EKCCProcessorSource.StaticCollider || Source == EKCCProcessorSource.EntityCollider;
		public bool IsStaticCollider => Source == EKCCProcessorSource.StaticCollider;
		public bool IsEntityCollider => Source == EKCCProcessorSource.EntityCollider;

		public KCCProcessorInfo(EKCCProcessorSource source, EntityRef entity, int colliderIndex)
		{
			Source        = source;
			Entity        = entity;
			ColliderIndex = colliderIndex;
		}

		/// <summary>
		/// Returns MapStaticCollider3D if the KCC processor source is a collision with a static collider.
		/// </summary>
		public bool GetStaticCollider(Frame frame, out MapStaticCollider3D collider)
		{
			if (Source == EKCCProcessorSource.StaticCollider)
			{
				collider = frame.Map.StaticColliders3D[ColliderIndex];
				return true;
			}

			collider = default;
			return false;
		}

		/// <summary>
		/// Returns StaticColliderData if the KCC processor source is a collision with a static collider.
		/// </summary>
		public bool GetStaticColliderData(Frame frame, out StaticColliderData colliderData)
		{
			if (Source == EKCCProcessorSource.StaticCollider)
			{
				colliderData = frame.Map.StaticColliders3D[ColliderIndex].StaticData;
				return true;
			}

			colliderData = default;
			return false;
		}
	}
}
