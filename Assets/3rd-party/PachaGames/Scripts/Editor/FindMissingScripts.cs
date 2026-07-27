using UnityEngine;
using UnityEditor;

public class FindMissingScripts : MonoBehaviour
{
    #if UNITY_EDITOR
    [MenuItem("Tools/Find Missing Scripts in Active Scene (Including Inactive)")]
        private static void FindMissingScriptsInScene()
        {
            int missingCount = 0;
    
            // Get the active scene
            GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
    
            foreach (GameObject rootObj in allObjects)
            {
                // Recursively check all children, including inactive objects
                CheckForMissingScripts(rootObj, ref missingCount);
            }
    
            if (missingCount == 0)
            {
                Debug.Log("No missing scripts found in the active scene (including inactive objects).");
            }
            else
            {
                Debug.Log($"Total missing scripts found in the active scene (including inactive objects): {missingCount}");
            }
        }
    
        private static void CheckForMissingScripts(GameObject obj, ref int missingCount)
        {
            Component[] components = obj.GetComponents<Component>();
    
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    string path = GetFullPath(obj);
                    Debug.LogWarning($"Missing script found in GameObject: {path}", obj);
                    missingCount++;
                }
            }
    
            // Recursively check child objects
            foreach (Transform child in obj.transform)
            {
                CheckForMissingScripts(child.gameObject, ref missingCount);
            }
        }
    
        private static string GetFullPath(GameObject obj)
        {
            string path = obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = obj.name + "/" + path;
            }
            return path;
        }
        #endif
}