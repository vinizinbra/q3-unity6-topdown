// Appear/hide banner that slides HORIZONTALLY: appears sliding in from the left while fading in, hides
// by continuing right while fading out - the exact shape the AREA SECURED banner uses. All tuning
// (distances, durations, eases, playOnEnable, autoHideAfter) lives on the AppearSlide base.
public class AppearSlideHorizontally : AppearSlide
{
    protected override bool IsVertical => false;
}
