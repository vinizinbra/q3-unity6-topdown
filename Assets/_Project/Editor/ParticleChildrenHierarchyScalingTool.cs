using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RiftRaiders.Editor
{
    // TEMPORARY / throwaway tool - not meant to stick around. Select one or more particle prefab
    // ASSETS in the Project window, run this, and every CHILD ParticleSystem inside each prefab
    // (the root's own ParticleSystem, if it has one, is left untouched - only descendants) gets
    // switched to ParticleSystemScalingMode.Hierarchy.
    //
    // Why the adaptation: under Local scaling mode a particle system's rendered size only reacts to
    // ITS OWN transform.localScale, never a parent's. Under Hierarchy mode it reacts to the full
    // lossyScale (its own scale times every ancestor's, up to the prefab root). Flipping the mode
    // therefore only changes anything when that ratio isn't 1 - typically because some ancestor in
    // the prefab (e.g. a scaled root the whole effect was resized on) was being silently ignored
    // under Local mode and would suddenly apply under Hierarchy mode. When that's the case, this
    // divides the child's authored Start Size by the newly-introduced scale so it renders at the
    // same size immediately after the switch, while now correctly following any future scale
    // changes on its parents at runtime.
    public static class ParticleChildrenHierarchyScalingTool
    {
        private const float Epsilon = 0.0001f;

        [MenuItem("Tools/RiftRaiders/Test/Particles - Set Children To Hierarchy Scaling")]
        private static void Run()
        {
            GameObject[] prefabAssets = Selection.gameObjects
                .Where(go => PrefabUtility.IsPartOfPrefabAsset(go))
                .ToArray();

            if (prefabAssets.Length == 0)
            {
                Debug.LogWarning("[ParticleChildrenHierarchyScalingTool] Select one or more particle prefab assets in the Project window first.");
                return;
            }

            int prefabsChanged = 0;
            int particlesChanged = 0;

            foreach (GameObject selected in prefabAssets)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                GameObject root = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    ParticleSystem[] children = root.GetComponentsInChildren<ParticleSystem>(true)
                        .Where(ps => ps.gameObject != root)
                        .ToArray();

                    bool prefabDirty = false;

                    foreach (ParticleSystem ps in children)
                    {
                        if (ConvertToHierarchyScaling(ps))
                        {
                            particlesChanged++;
                            prefabDirty = true;
                        }
                    }

                    if (prefabDirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        prefabsChanged++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Debug.Log($"[ParticleChildrenHierarchyScalingTool] Done - {particlesChanged} child particle system(s) across {prefabsChanged} prefab(s) switched to Hierarchy scaling.");
        }

        // Returns true if the particle system was actually touched (mode changed and/or size
        // compensated) - false for one already on Hierarchy mode with nothing to do.
        private static bool ConvertToHierarchyScaling(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;

            if (main.scalingMode == ParticleSystemScalingMode.Hierarchy)
                return false;

            // Effective scale the particle's size currently reacts to, under its CURRENT mode -
            // Local uses only this transform's own local scale, Shape doesn't scale rendered size
            // at all. Hierarchy (the mode we're switching to) uses the full lossyScale instead.
            Vector3 oldScale = main.scalingMode == ParticleSystemScalingMode.Local
                ? ps.transform.localScale
                : Vector3.one;
            Vector3 newScale = ps.transform.lossyScale;

            bool needsAdaptation = Mathf.Abs(oldScale.x - newScale.x) > Epsilon
                || Mathf.Abs(oldScale.y - newScale.y) > Epsilon
                || Mathf.Abs(oldScale.z - newScale.z) > Epsilon;

            if (needsAdaptation)
            {
                Vector3 factor = new Vector3(
                    SafeDivide(oldScale.x, newScale.x),
                    SafeDivide(oldScale.y, newScale.y),
                    SafeDivide(oldScale.z, newScale.z));

                if (main.startSize3D)
                {
                    main.startSizeX = ScaleCurve(main.startSizeX, factor.x);
                    main.startSizeY = ScaleCurve(main.startSizeY, factor.y);
                    main.startSizeZ = ScaleCurve(main.startSizeZ, factor.z);
                }
                else
                {
                    float uniformFactor = (factor.x + factor.y + factor.z) / 3f;
                    if (Mathf.Abs(factor.x - factor.y) > Epsilon || Mathf.Abs(factor.y - factor.z) > Epsilon)
                        Debug.LogWarning($"[ParticleChildrenHierarchyScalingTool] '{ps.name}' has non-uniform scale but Start Size isn't 3D - used the average compensation factor ({uniformFactor:0.###}), double check it in-Editor.", ps);

                    main.startSize = ScaleCurve(main.startSize, uniformFactor);
                }
            }

            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            EditorUtility.SetDirty(ps);
            return true;
        }

        private static float SafeDivide(float a, float b) => Mathf.Abs(b) > Epsilon ? a / b : a;

        private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float factor)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    curve.constant *= factor;
                    break;
                case ParticleSystemCurveMode.TwoConstants:
                    curve.constantMin *= factor;
                    curve.constantMax *= factor;
                    break;
                case ParticleSystemCurveMode.Curve:
                case ParticleSystemCurveMode.TwoCurves:
                    curve.curveMultiplier *= factor;
                    break;
            }

            return curve;
        }
    }
}
