namespace Quantum.Editor
{
    using UnityEditor;
    using UnityEngine;

    // Draws a [PreviewPrefab] GameObject field as the normal object field followed by Unity's own
    // generated prefab thumbnail (same rendered icon shown in the Project window) - so swapping
    // e.g. EnemyDataAsset.ViewPrefab shows what the enemy looks like without opening the prefab.
    // GetAssetPreview renders asynchronously and returns null on the first few calls - falling
    // back to GetMiniThumbnail means something always shows immediately, and later Inspector
    // repaints (Unity polls fairly often on its own) pick up the real render once ready.
    [CustomPropertyDrawer(typeof(PreviewPrefabAttribute))]
    public class PreviewPrefabDrawer : PropertyDrawer
    {
        private const float PreviewSize = 64f;
        private const float Spacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(fieldRect, property, label);

            GameObject prefab = property.objectReferenceValue as GameObject;
            if (prefab != null)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(prefab);
                if (preview == null)
                    preview = AssetPreview.GetMiniThumbnail(prefab);

                if (preview != null)
                {
                    Rect previewRect = new Rect(position.x, fieldRect.yMax + Spacing, PreviewSize, PreviewSize);
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;

            if (property.objectReferenceValue is GameObject)
                height += PreviewSize + Spacing;

            return height;
        }
    }
}
