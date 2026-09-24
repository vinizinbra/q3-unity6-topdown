using Quantum;
using QuantumUser.View;

// Shared base for the solo-only tutorial popups (HowToPlayPopup/FirstBreakPopup/HeroIntroPopup)
// opened via InMatchPopupManager.Open<T> - see InMatchTutorialManager, which decides WHEN each one
// opens by reacting locally to EventGameStateChanged (pure View-side decision, the sim has no notion
// of "tutorial"). Closing ANY tutorial popup always means the same thing - unpause
// (SetTutorialPauseCommand, Paused = false) - so that send lives here once instead of duplicated
// per popup. Neither HowToPlayPopup nor FirstBreakPopup has any content/logic of its own beyond
// this: a single button wired to UiPopup.Close() in the Inspector is all either one needs.
public abstract class TutorialPopup : UiPopup
{
    public override void Close()
    {
        base.Close();

        // HeroIntroPopup can be queued together with HowToPlayPopup (both fire off the same
        // Lobby->Survival moment - see InMatchTutorialManager.OnGameStateChanged) - base.Close()
        // above already ran InMatchPopupManager.OnPopupClose synchronously (onClose), so by now the
        // queue already reflects whether another popup is still waiting behind this one. Only
        // unpause once nothing tutorial-related is left showing/queued, so the sim doesn't briefly
        // resume in the gap between two queued tutorial popups.
        if (InMatchPopupManager.instance == null || InMatchPopupManager.instance.HasPendingPopups == false)
            SendPause(false);
    }

    // Also called by InMatchTutorialManager to send the OPEN-side pause (Paused = true) - kept
    // here, not duplicated, since it's the exact same "every set local slot" send either direction.
    internal static void SendPause(bool paused)
    {
        if (MyLocalPlayer.Instance == null)
            return;

        var slots = MyLocalPlayer.Instance.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet)
                QuantumRunner.Default.Game.SendCommand(i, new SetTutorialPauseCommand { Paused = paused });
        }
    }
}
