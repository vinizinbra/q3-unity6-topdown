namespace Quantum
{
	using UnityEngine;

	public unsafe partial class QPrototypeKCC
	{
#if UNITY_EDITOR
		private void OnDrawGizmosSelected()
		{
			if (Prototype == null)
				return;
			if (Prototype.Settings.IsValid == false)
				return;

			KCCSettings settings = QuantumUnityDB.Global.GetAssetEditorInstance<KCCSettings>(Prototype.Settings);
			if (settings == null)
				return;

			float radius = Mathf.Max(0.01f, settings.Radius.AsFloat);
			float height = Mathf.Max(radius * 2.0f, settings.Height.AsFloat);
			float extent = settings.Extent.AsFloat;

			Vector3 basePosition = transform.position;

			Color gizmosColor = Gizmos.color;

			Vector3 baseLow     = basePosition + Vector3.up * radius;
			Vector3 baseHigh    = basePosition + Vector3.up * (height - radius);
			Vector3 offsetFront = Vector3.forward * radius;
			Vector3 offsetBack  = Vector3.back    * radius;
			Vector3 offsetLeft  = Vector3.left    * radius;
			Vector3 offsetRight = Vector3.right   * radius;

			Gizmos.color = Color.green;

			Gizmos.DrawWireSphere(baseLow, radius);
			Gizmos.DrawWireSphere(baseHigh, radius);

			Gizmos.DrawLine(baseLow + offsetFront, baseHigh + offsetFront);
			Gizmos.DrawLine(baseLow + offsetBack,  baseHigh + offsetBack);
			Gizmos.DrawLine(baseLow + offsetLeft,  baseHigh + offsetLeft);
			Gizmos.DrawLine(baseLow + offsetRight, baseHigh + offsetRight);

			if (extent > 0.0f)
			{
				float extendedRadius = radius + extent;

				Gizmos.color = Color.yellow;

				Gizmos.DrawWireSphere(baseLow, extendedRadius);
				Gizmos.DrawWireSphere(baseHigh, extendedRadius);
			}

			Gizmos.color = gizmosColor;
		}
#endif
	}
}
