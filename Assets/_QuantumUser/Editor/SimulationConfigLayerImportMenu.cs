namespace QuantumUser.Editor
{
    using Quantum;
    using UnityEditor;

    // SimulationConfig.ImportLayersFromUnity is public but the SDK only wires it up inside the
    // destructive Reset() (which also wipes Physics/Navigation tuning back to defaults). This adds
    // a right-click context menu entry on the SimulationConfig asset that re-syncs just
    // Physics.Layers/LayerMatrix from Unity's Project Settings > Tags and Layers, leaving
    // everything else untouched - use this after adding/renaming a physics layer in Unity so
    // f.Layers.GetLayerMask("YourLayer") can actually find it.
    public static class SimulationConfigLayerImportMenu
    {
        [MenuItem("CONTEXT/SimulationConfig/Import Layers From Unity (3D)")]
        private static void ImportLayers3D(MenuCommand command)
        {
            var config = (SimulationConfig)command.context;
            config.ImportLayersFromUnity(SimulationConfig.PhysicsType.Physics3D);
            EditorUtility.SetDirty(config);
        }
    }
}
