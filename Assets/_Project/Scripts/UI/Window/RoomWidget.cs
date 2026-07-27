using TMPro;
using UnityEngine;

public class RoomWidget : MonoBehaviour
{
    public TMP_Text name;

    public void Setup(string playerName)
    {
        gameObject.SetActive(!string.IsNullOrEmpty(playerName));
        name.text = playerName;
    }
}