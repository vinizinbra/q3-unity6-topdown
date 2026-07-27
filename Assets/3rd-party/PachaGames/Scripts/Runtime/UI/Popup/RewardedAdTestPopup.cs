using UnityEngine;

public class RewardedAdTestPopup : PgUiPopup<RewardedAdTestPopup>
{
    private System.Action rewardCallback;
    
    public void Setup(System.Action reward)
    {
        rewardCallback = reward;
    }

    public override void Close(bool ignoreTweens = false)
    {
        base.Close(ignoreTweens);
        rewardCallback = null;
    }
    public void Success()
    {
        rewardCallback?.Invoke();
        Close();
    }
}
