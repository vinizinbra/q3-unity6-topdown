namespace Quantum
{
    using System;

    // Shared "resolve a designer-authored template against this asset's own live values" helper -
    // used by both SkillData.Description and SkillActionData.Description (see DescriptionArgs on
    // each) so a retuned number never requires manually updating the sentence describing it, the
    // way a fully static string would. A template with no {0}/{1}/... placeholders is returned
    // unchanged, so plain non-templated text still works with zero overhead.
    public static class DescriptionUtility
    {
        public static string Format(string template, object[] args)
        {
            if (string.IsNullOrEmpty(template) || args == null || args.Length == 0)
                return template;

            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                // A placeholder index with no matching arg (template edited out of sync with
                // DescriptionArgs) - fall back to the raw template rather than throwing at
                // Inspector-draw or UI-display time.
                return template;
            }
        }
    }
}
