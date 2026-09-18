namespace Quantum
{
    using UnityEngine;

    // View-only addition to the sim-owned ChallengeDefinition (see ChallengeDefinition.cs) - split
    // into its own file the same way CharacterData/PassiveData/SkillData keep their own Sprite-
    // carrying fields in a sibling .View.cs, since Rules has no bearing on TeamChallengeUtility's
    // simulation logic (Sprite/string are never read by sim code, only by InteractionPromptWidget).
    public partial class ChallengeDefinition
    {
        [Tooltip("Icon+text rows describing this specific challenge's rules/objective breakdown - e.g. a skull icon + \"Kill 20 enemies\", a shield icon + \"Don't lose any HP\". Shown on the POI's own Interaction Area prompt (InteractionPromptWidget's rules area) while deciding whether to Ready up, resolved dynamically off the live TeamChallenge.SelectedChallenge roll - see docs/optional-team-challenge.md. Left empty, the rules area simply doesn't show for this challenge (Description above still shows as the one-line blurb).")]
        public ChallengeRuleEntry[] Rules;
    }

    [System.Serializable]
    public struct ChallengeRuleEntry
    {
        public Sprite Icon;
        [TextArea] public string Text;
        [Tooltip("When on, Text is a format string with {0} as a placeholder for the LIVE co-op-scaled kill target - e.g. \"Kill {0} enemies before the timer runs out\" - resolved every frame off the current connected-player count via TeamChallengeUtility.ResolveKillTarget, the exact same formula/value BeginChallengeActive will actually use once the challenge begins. Off by default: Text is shown verbatim.")]
        public bool ScaleTextWithKillTarget;
    }
}
