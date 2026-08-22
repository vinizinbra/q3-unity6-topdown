using Quantum;
using UnityEngine;

// Lux's Scrap counter - the base Scrap Collector passive's own payoff: every pickup is a stack, and
// reaching StacksRequired makes her next Hero Skill cast free (SkillSystem.GrantFreeCast). Stacks
// hold at the threshold rather than overflowing, and only reset once that free cast is actually
// spent (ScrapUtility.OnFreeCastConsumed), so the glow reads as a banked free cast, not a moment.
// Hidden entirely when LuxScrapCollector is absent, i.e. the passive wasn't taken.
//
// Replaces the old ScrapUiWidget.
public class LuxHudWidget : HeroHudWidget
{
    [SerializeField, Tooltip("Scrap stacks toward the next free Hero Skill cast - ScrapStacks against StacksRequired. Glows once the threshold is reached - the free cast is banked until it's spent.")]
    private Section scrap;

    protected override bool TryRefresh(Frame frame, EntityRef entity)
    {
        if (frame.TryGet<LuxScrapCollector>(entity, out var collector) == false)
            return false;

        int required = Mathf.Max(1, collector.StacksRequired);
        bool ready = collector.ScrapStacks >= collector.StacksRequired;

        scrap.Show(collector.ScrapStacks / (float)required, $"{collector.ScrapStacks}/{collector.StacksRequired}", ready);
        return true;
    }

    protected override void HideSections()
    {
        scrap.Hide();
    }
}
