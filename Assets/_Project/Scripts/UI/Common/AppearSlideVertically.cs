// Appear/hide banner that slides VERTICALLY: appears sliding up into place while fading in, hides by
// continuing up while fading out. Same shape as the AREA SECURED banner, just on the Y axis. All
// tuning (distances, durations, eases, playOnEnable, autoHideAfter) lives on the AppearSlide base.
public class AppearSlideVertically : AppearSlide
{
    protected override bool IsVertical => true;
}
