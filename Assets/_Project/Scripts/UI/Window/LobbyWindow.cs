using Quantum.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class LobbyWindow : UiWindow
{
    public TMP_InputField roomNameInput;

    public override void Show()
    {
        base.Show();
    }

    public override void Hide()
    {
        base.Hide();
    }

    public void TryCreateRoom()
    {
        var roomCode = UnityEngine.Random.Range(0, 99999).ToString("00000");
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.CUSTOM;
        MatchMakingConfig.Instance.Quickplay(roomCode);
    }

    public void TryJoinRoom()
    {
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.CUSTOM;
        MatchMakingConfig.Instance.Quickplay(roomNameInput.text.Trim());
    }
}


