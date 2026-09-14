// Solo-only "How To Play" tutorial popup, shown once the instant the run starts (Lobby->Survival -
// see InMatchTutorialManager, which reacts to EventGameStateChanged). No content/logic of its own -
// see TutorialPopup, which owns the shared "unpause on close" behavior every tutorial popup needs.
public class HowToPlayPopup : TutorialPopup
{
}
