using UnityEngine;

/// <summary>
/// The player-facing tips shown on the menu (TipsWidget) and the loading screen (LoadingWindow): short, one
/// mechanic each, meant to teach. Rich text is allowed - highlight the key word with
/// &lt;color=#FD3971&gt;...&lt;/color&gt; like the upgrade cards do.
/// </summary>
[CreateAssetMenu(fileName = "Tips", menuName = "RiftRaiders/Tips")]
public class TipsData : ScriptableObject
{
    [TextArea(1, 3)] public string[] tips = new string[0];
}
