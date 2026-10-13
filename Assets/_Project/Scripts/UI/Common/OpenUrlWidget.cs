using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opens a web page when the Button on this object is pressed (click, tap or gamepad Submit) - e.g. the main menu's
/// "Wishlist on Steam" button. The page opens in the system browser, or in the Steam client for a steam:// URL.
/// </summary>
[RequireComponent(typeof(Button))]
public class OpenUrlWidget : MonoBehaviour
{
    [SerializeField, Tooltip("The page to open. Placeholder until the game's Steam store page exists - then use its https://store.steampowered.com/app/<AppId>/ URL.")]
    private string url = "https://store.steampowered.com/";

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(Open);
    }

    public void Open()
    {
        if (!string.IsNullOrEmpty(url))
            Application.OpenURL(url);
    }
}
