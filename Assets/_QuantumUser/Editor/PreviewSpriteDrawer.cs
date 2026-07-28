namespace Quantum.Editor
{
    using UnityEditor;
    using UnityEngine;

    // Draws a [PreviewSprite] Sprite field as the normal object field followed by its thumbnail -
    // same shape as PreviewPrefabDrawer, just for Sprite fields (e.g. SkillData.Icon) instead of
    // GameObject ones. GetAssetPreview renders asynchronously and returns null on the first few
    // calls - falling back to GetMiniThumbnail means something always shows immediately, and later
    // Inspector repaints (Unity polls fairly often on its own) pick up the real render once ready.
    [CustomPropertyDrawer(typeof(PreviewSpriteAttribute))]
    public class PreviewSpriteDrawer : PropertyDrawer
    {
        private const float PreviewSize = 64f;
        private const float Spacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(fieldRect, property, label);

            Sprite sprite = property.objectReferenceValue as Sprite;
            if (sprite != null)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(sprite);
                if (preview == null)
                    preview = AssetPreview.GetMiniThumbnail(sprite);

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

            if (property.objectReferenceValue is Sprite)
                height += PreviewSize + Spacing;

            return height;
        }
    }
}
