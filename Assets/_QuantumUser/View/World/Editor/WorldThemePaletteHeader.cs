namespace Quantum.Editor
{
    using UnityEditor;
    using UnityEngine;

    // Palette preview for WorldTheme, drawn above its normal Inspector fields - a 2x2 "surface"
    // square with a 3-dot blood splash in the middle (Enemy.BloodColor, the death VFX/decal tint -
    // see EffectsManager.OnEnemyExploded), a 2x1 "wall" band underneath, fading from Walls color
    // into Sky color at the bottom (mirrors the level shader's actual Height Fog blend - see
    // EnvironmentManager - which gets strongest at low world height).
    //
    // Lives under View/World/Editor (no covering asmdef/asmref) rather than QuantumUser/Editor -
    // WorldTheme.cs itself has no asmref either, so it compiles into the default Assembly-CSharp
    // (confirmed by its .asset files' own m_EditorClassIdentifier: Assembly-CSharp::Quantum.
    // WorldTheme), which the named Quantum.Unity.Editor assembly does NOT reference. An uncovered
    // Editor/ folder compiles into the default Assembly-CSharp-Editor instead, which always sees
    // Assembly-CSharp automatically - no asmdef edits required.
    //
    // Hooks Editor.finishedDefaultHeaderGUI instead of registering a [CustomEditor(typeof(
    // WorldTheme))] - this project's copy of NaughtyAttributes.Editor isn't referenced by every
    // assembly, so there's no guaranteed NaughtyInspector base type to inherit; a plain CustomEditor
    // override would silently replace whatever normally draws WorldTheme's Inspector (fields + the
    // "Apply To Scene (Debug)" [Button]) instead of adding to it. The header-GUI hook sidesteps that
    // - it fires for every Editor regardless of which CustomEditor is actually driving the rest of
    // the Inspector.
    [InitializeOnLoad]
    public static class WorldThemePaletteHeader
    {
        private const float PreviewWidth = 140f;
        private const float SurfaceHeight = PreviewWidth; // 2x2 - square
        private const float WallsHeight = PreviewWidth * 0.5f; // 2x1 - half the surface's height
        private const int GradientSlices = 20;
        private const float SplashRadius = 9f;

        static WorldThemePaletteHeader()
        {
            Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
        }

        private static void OnHeaderGUI(Editor editor)
        {
            if (editor.target is not WorldTheme theme)
                return;

            DrawPalettePreview(theme);
        }

        private static void DrawPalettePreview(WorldTheme theme)
        {
            WorldEnvironmentTheme environment = theme.Environment;
            Color bloodColor = theme.Enemy.BloodColor;

            Rect area = GUILayoutUtility.GetRect(PreviewWidth, SurfaceHeight + WallsHeight, GUILayout.ExpandWidth(true));
            float x = area.x + (area.width - PreviewWidth) * 0.5f;

            Rect surfaceRect = new Rect(x, area.y, PreviewWidth, SurfaceHeight);
            EditorGUI.DrawRect(surfaceRect, environment.Surface);
            DrawDeathSplash(surfaceRect.center, bloodColor);

            Rect wallsRect = new Rect(x, surfaceRect.yMax, PreviewWidth, WallsHeight);
            DrawVerticalGradient(wallsRect, environment.Walls, environment.Sky);
        }

        // Three uneven circles clustered off-center - reads more like an actual splash than three
        // identical dots in a neat row.
        private static void DrawDeathSplash(Vector2 center, Color bloodColor)
        {
            DrawCircle(center + new Vector2(-SplashRadius * 0.7f, SplashRadius * 0.4f), SplashRadius, bloodColor);
            DrawCircle(center + new Vector2(SplashRadius * 0.8f, SplashRadius * 0.6f), SplashRadius * 0.75f, bloodColor);
            DrawCircle(center + new Vector2(SplashRadius * 0.1f, -SplashRadius * 0.9f), SplashRadius * 0.55f, bloodColor);
        }

        private static void DrawCircle(Vector2 center, float radius, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, radius);
            Handles.EndGUI();
        }

        private static void DrawVerticalGradient(Rect rect, Color top, Color bottom)
        {
            float sliceHeight = rect.height / GradientSlices;
            for (int i = 0; i < GradientSlices; i++)
            {
                float t = i / (float)(GradientSlices - 1);
                Rect slice = new Rect(rect.x, rect.y + i * sliceHeight, rect.width, sliceHeight + 1f);
                EditorGUI.DrawRect(slice, Color.Lerp(top, bottom, t));
            }
        }
    }
}
