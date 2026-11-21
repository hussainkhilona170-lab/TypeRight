namespace TypeRight.Domain.Models
{
    /// <summary>
    /// Suggestion feed categories. The taxonomy mirrors the categories observed
    /// in the reference client's side panel; categories TypeRight does not
    /// implement locally are retained for wire compatibility but never emitted.
    /// </summary>
    public enum SuggestionCategory
    {
        Correctness,
        Clarity,
        Delivery,
        Engagement,
        StyleGuide,
        Plagiarism,
        WritingExpert,
        AiStudio
    }

    /// <summary>
    /// Maps the engine's internal decision reasons to user-facing feed
    /// categories. The engine itself stays independent of any UI framework.
    /// </summary>
    public static class SuggestionCategoryMapper
    {
        public static SuggestionCategory FromReason(ReasonCategory reason)
        {
            switch (reason)
            {
                case ReasonCategory.AutoCorrect:
                case ReasonCategory.ValidWord:
                    return SuggestionCategory.Correctness;

                case ReasonCategory.Ambiguous:
                    return SuggestionCategory.Clarity;

                case ReasonCategory.RegionalVariant:
                    return SuggestionCategory.Delivery;

                case ReasonCategory.ProperNameOrTechnical:
                    return SuggestionCategory.StyleGuide;

                case ReasonCategory.Suggest:
                    return SuggestionCategory.Correctness;

                default:
                    return SuggestionCategory.Correctness;
            }
        }

        public static string DisplayName(SuggestionCategory category)
        {
            switch (category)
            {
                case SuggestionCategory.Clarity: return "Clarity";
                case SuggestionCategory.Delivery: return "Delivery";
                case SuggestionCategory.Engagement: return "Engagement";
                case SuggestionCategory.StyleGuide: return "Style guide";
                case SuggestionCategory.Plagiarism: return "Plagiarism";
                case SuggestionCategory.WritingExpert: return "Writing expert";
                case SuggestionCategory.AiStudio: return "AI Studio";
                default: return "Correctness";
            }
        }
    }
}
