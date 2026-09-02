namespace Quantum.Editor {
  using UnityEditor;
  using UnityEngine;

  public static class ShutdownQuantumRunners {
    [MenuItem("Tools/RiftRaiders/Utilities/Shutdown Quantum Runners")]
    private static void Shutdown() {
      if (!Application.isPlaying) {
        Debug.LogWarning("[ShutdownQuantumRunners] Not in Play Mode - nothing to shut down.");
        return;
      }

      var count = 0;
      foreach (var _ in QuantumRunner.ActiveRunners) {
        count++;
      }

      if (count == 0) {
        Debug.Log("[ShutdownQuantumRunners] No active QuantumRunner instances.");
        return;
      }

      QuantumRunner.ShutdownAll();
      Debug.Log($"[ShutdownQuantumRunners] Shut down {count} QuantumRunner instance(s) - their native heap is now released.");
    }
  }
}
