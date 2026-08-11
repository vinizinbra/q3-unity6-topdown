namespace QuantumUser.Editor
{
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Retunes ExperienceConfig.RequiredExperience (the level -> cumulative-XP curve). This field is
    // an FPAnimationCurve, which stores BOTH authored Keys (for the Inspector's own CurveField
    // round-trip) and a separately pre-baked Samples array (33 points, what ExperienceUtility.Grant
    // actually evaluates at runtime) - hand-editing the .asset YAML directly would desync those two
    // representations, since only the Inspector's own FixedCurveDrawer (see
    // Assets/Photon/Quantum/Editor/QuantumUnityEditor.cs) re-bakes Samples from Keys on change. This
    // script drives that exact same bake logic (verbatim from FixedCurveDrawer.OnGUI's
    // EndChangeCheck block) via SerializedProperty instead of a GUI event, so the result is
    // byte-for-byte what dragging the curve in the Inspector would produce.
    //
    // Same level shape throughout (keyframes always at level 1/10/25/50) - only the cumulative XP
    // values move as this gets retuned from playtesting feedback. History, in cumulative XP at
    // level 10/25/50: original authored 90/400/2000 (felt too slow) -> halved to 45/200/1000 (felt
    // too easy) -> current 68/300/1500 (75% of original, splitting the difference). Adjust the
    // Keyframe values below and re-run the menu item to retune again.
    public static class ExperienceCurveTuner
    {
        private const string ConfigPath = "Assets/_QuantumUser/Resources/Configs/ExperienceConfig.asset";

        [MenuItem("Tools/RiftRaiders/Tune Experience Curve")]
        internal static void ApplyCurve()
        {
            var config = AssetDatabase.LoadAssetAtPath<ExperienceConfig>(ConfigPath);

            if (config == null)
            {
                LogHelper.Error("ExperienceCurveTuner", $"No ExperienceConfig asset at {ConfigPath}");
                return;
            }

            var curve = new AnimationCurve();
            curve.AddKey(new Keyframe(1, 0));
            curve.AddKey(new Keyframe(10, 68));
            curve.AddKey(new Keyframe(25, 300));
            curve.AddKey(new Keyframe(50, 1500));

            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }

            curve.preWrapMode = WrapMode.Clamp;
            curve.postWrapMode = WrapMode.Clamp;

            var so = new SerializedObject(config);
            BakeIntoFPAnimationCurve(so.FindProperty("RequiredExperience"), curve);
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            LogHelper.Log("ExperienceCurveTuner", $"RequiredExperience rebaked - level 10/25/50 now cost {68}/{300}/{1500} cumulative XP.");
        }

        // Verbatim mirror of FixedCurveDrawer.OnGUI's EndChangeCheck block (see
        // Assets/Photon/Quantum/Editor/QuantumUnityEditor.cs:6153-6212) - same field paths, same
        // FP.FromFloat_UNSAFE conversions, same resolution+1 even sampling across [startTime,
        // endTime]. Kept as a faithful copy rather than calling into the drawer directly since that
        // class is internal and its bake step is only reachable from an OnGUI change event.
        private static void BakeIntoFPAnimationCurve(SerializedProperty prop, AnimationCurve animationCurve)
        {
            var resolutionProperty = prop.FindPropertyRelative("Resolution");
            var samplesProperty = prop.FindPropertyRelative("Samples");
            var startTimeProperty = GetPropertyNext(prop, "StartTime");
            var endTimeProperty = GetPropertyNext(prop, "EndTime");
            var preWrapModeProperty = prop.FindPropertyRelative("PreWrapMode");
            var postWrapModeProperty = prop.FindPropertyRelative("PostWrapMode");
            var preWrapModeOriginalProperty = prop.FindPropertyRelative("OriginalPreWrapMode");
            var postWrapModeOriginalProperty = prop.FindPropertyRelative("OriginalPostWrapMode");
            var keysProperty = prop.FindPropertyRelative("Keys");

            if (resolutionProperty.intValue <= 1)
            {
                resolutionProperty.intValue = 32;
            }

            keysProperty.ClearArray();
            keysProperty.arraySize = animationCurve.keys.Length;

            for (int i = 0; i < animationCurve.keys.Length; i++)
            {
                var key = animationCurve.keys[i];
                var keyProperty = keysProperty.GetArrayElementAtIndex(i);
                GetPropertyNext(keyProperty, "Time").longValue = FP.FromFloat_UNSAFE(key.time).RawValue;
                GetPropertyNext(keyProperty, "Value").longValue = FP.FromFloat_UNSAFE(key.value).RawValue;
                GetPropertyNext(keyProperty, "InTangent").longValue = FP.FromFloat_UNSAFE(key.inTangent).RawValue;
                GetPropertyNext(keyProperty, "OutTangent").longValue = FP.FromFloat_UNSAFE(key.outTangent).RawValue;

                keyProperty.FindPropertyRelative("TangentModeLeft").intValue = (int)AnimationUtility.GetKeyLeftTangentMode(animationCurve, i);
                keyProperty.FindPropertyRelative("TangentModeRight").intValue = (int)AnimationUtility.GetKeyRightTangentMode(animationCurve, i);
                keyProperty.FindPropertyRelative("TangentMode").intValue = 0;
                keyProperty.FindPropertyRelative("WeightedMode").intValue = (byte)key.weightedMode;
            }

            preWrapModeProperty.intValue = (int)GetWrapMode(animationCurve.preWrapMode);
            postWrapModeProperty.intValue = (int)GetWrapMode(animationCurve.postWrapMode);
            preWrapModeOriginalProperty.intValue = (int)animationCurve.preWrapMode;
            postWrapModeOriginalProperty.intValue = (int)animationCurve.postWrapMode;

            float startTime = animationCurve.keys.Length == 0 ? 0f : float.MaxValue;
            float endTime = animationCurve.keys.Length == 0 ? 1f : float.MinValue;

            for (int i = 0; i < animationCurve.keys.Length; i++)
            {
                startTime = Mathf.Min(startTime, animationCurve[i].time);
                endTime = Mathf.Max(endTime, animationCurve[i].time);
            }

            startTimeProperty.longValue = FP.FromFloat_UNSAFE(startTime).RawValue;
            endTimeProperty.longValue = FP.FromFloat_UNSAFE(endTime).RawValue;

            var resolution = resolutionProperty.intValue;

            if (resolution <= 0)
            {
                return;
            }

            samplesProperty.ClearArray();
            samplesProperty.arraySize = resolution + 1;
            var deltaTime = (endTime - startTime) / resolution;

            for (int i = 0; i < resolution + 1; i++)
            {
                var time = startTime + deltaTime * i;
                var fp = FP.FromFloat_UNSAFE(animationCurve.Evaluate(time));
                GetArrayElementNext(samplesProperty, i).longValue = fp.RawValue;
            }
        }

        private static SerializedProperty GetPropertyNext(SerializedProperty prop, string name)
        {
            var result = prop.FindPropertyRelative(name);

            if (result != null)
            {
                result.Next(true);
            }

            return result;
        }

        private static SerializedProperty GetArrayElementNext(SerializedProperty prop, int index)
        {
            var result = prop.GetArrayElementAtIndex(index);
            result.Next(true);
            return result;
        }

        private static FPAnimationCurve.WrapMode GetWrapMode(WrapMode wrapMode)
        {
            switch (wrapMode)
            {
                case WrapMode.Loop:
                    return FPAnimationCurve.WrapMode.Loop;
                case WrapMode.PingPong:
                    return FPAnimationCurve.WrapMode.PingPong;
                default:
                    return FPAnimationCurve.WrapMode.Clamp;
            }
        }
    }
}
