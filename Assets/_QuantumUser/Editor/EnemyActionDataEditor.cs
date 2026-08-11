namespace Quantum.Editor
{
    using System;
    using UnityEditor;

    // See FoldoutGroupEditor - renders EnemyActionData's [FoldoutGroup("Base"/"Animation")] runs
    // (EnemyActionData.cs/.View.cs) as collapsible boxed sections. Adds one extra check on top: a
    // ProjectileDeliveryData resolves its own hit through ProjectileHitData.Effects (see that
    // class's own comment) rather than this asset's Effects list, so an author who fills in
    // Effects here anyway would see it silently never fire - flag that combination instead of
    // letting it fail silently.
    [CustomEditor(typeof(EnemyActionData))]
    public class EnemyActionDataEditor : FoldoutGroupEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            SerializedProperty effects = serializedObject.FindProperty(nameof(EnemyActionData.Effects));
            if (effects == null || effects.arraySize == 0)
                return;

            SerializedProperty delivery = serializedObject.FindProperty(nameof(EnemyActionData.Delivery));
            if (delivery == null || IsProjectileDelivery(delivery) == false)
                return;

            EditorGUILayout.HelpBox(
                "Delivery is a ProjectileDeliveryData - Effects below won't run. That delivery hands hit " +
                "resolution off to the spawned projectile entirely; author these effects on its " +
                "ProjectileDataAsset's Hit (ProjectileHitData.Effects) instead, or leave Effects empty here.",
                MessageType.Warning);
        }

        private static bool IsProjectileDelivery(SerializedProperty deliveryProperty)
        {
            SerializedProperty guidProperty = deliveryProperty.FindPropertyRelativeOrThrow(AssetRefDrawer.RawValuePath);
            if (guidProperty.hasMultipleDifferentValues)
                return false;

            AssetGuid guid = (AssetGuid)guidProperty.longValue;
            if (guid.IsValid == false || guid.IsDynamic)
                return false;

            try
            {
                return QuantumUnityDB.GetGlobalAssetEditorInstance(guid) is ProjectileDeliveryData;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
