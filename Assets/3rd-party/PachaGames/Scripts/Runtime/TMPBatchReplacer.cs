// TMPBatchReplacer.cs
// One-file tool for batch replacing TextMeshPro (TMP_Text) font assets and material presets
// in the CURRENTLY OPEN SCENE. Safe in editor: uses fontSharedMaterial (not per-instance).

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NaughtyAttributes;
using UnityEngine;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;
#endif

public class TMPBatchReplacer : MonoBehaviour
{
    [Header("Match (what to search for)")]
    public TMP_FontAsset matchFont;        // leave null to ignore font matching
    public Material matchMaterial;         // leave null to ignore material matching (TMP preset asset)

    [Header("Replace With (what to apply)")]
    public TMP_FontAsset newFont;          // leave null to keep existing font
    public Material newMaterial;           // leave null to keep existing material (preset asset)

    [ContextMenu("Replace In Open Scene")]
    [Button]
    public void ReplaceInScene()
    {
        TMP_Text[] allTMPTexts = GameObject.FindObjectsOfType<TMP_Text>(true);
        int i = 0;
        Dictionary<string,int> dontMaterials = new Dictionary<string,int>();
        foreach (var text in allTMPTexts)
        {
            if (text.font == matchFont && matchMaterial.name == text.fontSharedMaterial.name)
            {
                text.font = newFont;
                text.fontSharedMaterial = newMaterial;
                i++;
            }
        }

        foreach (var d in dontMaterials)
        {
            Debug.Log(d.Key);
        }
        Debug.Log($"$Found {i} Fonts that needs replacement");
    }

}
