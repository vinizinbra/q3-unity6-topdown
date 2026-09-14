// Solo-only "First Break" tutorial popup, shown once the enemies are actually cleared (Global.
// BreathingAreaSecured flips true) during the run's FIRST Breathing Break (BreathingIndex == 0) -
// see InMatchTutorialManager.QUpdate, which edge-detects that moment. No content/logic of its own -
// see TutorialPopup, which owns the shared "unpause on close" behavior every tutorial popup needs.
public class FirstBreakPopup : TutorialPopup
{
}
