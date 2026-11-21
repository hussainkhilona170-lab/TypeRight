#nullable disable
using System;
using TypeRight.Domain;
using TypeRight.Engine;
using TypeRight.Engine.Confidence;
using TypeRight.Engine.Vocabulary;

namespace TypeRight.Engine.CorrectionDecision;

/// <summary>
/// Makes the final correction decision based on engine evaluation.
/// Orchestrates tokenization, dictionary validation, candidate generation,
/// ranking, confidence calculation, and priority-based decision logic.
/// </summary>
public interface ICorrectionDecisionMaker
{
    /// <summary>
    /// Processes a correction request and produces a decision.
    /// </summary>
    /// <param name="request">The correction request containing input context.</param>
    /// <returns>The correction decision.</returns>
    TypeRight.Domain.Models.CorrectionDecision MakeDecision(TypeRight.Domain.Models.CorrectionRequest request);
}

/// <summary>
/// Default implementation of the correction decision maker.
/// Implements the hybrid correction behaviour philosophy:
/// "Correct what I got wrong. Don't rewrite what I meant to say."
/// </summary>
public class CorrectionDecisionMaker : ICorrectionDecisionMaker
{
    private readonly TypeRight.Engine.Vocabulary.IDictionaryValidator _dictionary;
    private readonly TypeRight.Engine.CandidateGeneration.ICandidateGenerator _candidateGenerator;
    private readonly TypeRight.Engine.CandidateRanking.ICandidateRanker _candidateRanker;
    private readonly TypeRight.Engine.Confidence.IConfidenceCalculator _confidenceCalculator;

    /// <summary>
    /// Constructs the correction decision maker with required dependencies.
    /// </summary>
    public CorrectionDecisionMaker(
        TypeRight.Engine.Vocabulary.IDictionaryValidator dictionary,
        TypeRight.Engine.CandidateGeneration.ICandidateGenerator candidateGenerator,
        TypeRight.Engine.CandidateRanking.ICandidateRanker candidateRanker,
        TypeRight.Engine.Confidence.IConfidenceCalculator confidenceCalculator)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _candidateGenerator = candidateGenerator ?? throw new ArgumentNullException(nameof(candidateGenerator));
        _candidateRanker = candidateRanker ?? throw new ArgumentNullException(nameof(candidateRanker));
        _confidenceCalculator = confidenceCalculator ?? throw new ArgumentNullException(nameof(confidenceCalculator));
    }

    /// <summary>
    /// Processes a correction request and produces a deterministic decision.
    /// </summary>
    public TypeRight.Domain.Models.CorrectionDecision MakeDecision(TypeRight.Domain.Models.CorrectionRequest request)
    {
        // Step 1: Check if the completed word is valid vocabulary
        bool isValid = _dictionary.IsValid(request.CompletedWord, request.LanguageVariant);

        if (isValid)
        {
            return new TypeRight.Domain.Models.CorrectionDecision(
                TypeRight.Domain.Models.CorrectionAction.Preserve,
                correctedWord: null,
                alternatives: null,
                confidence: 1.0f,
                reason: TypeRight.Domain.Models.ReasonCategory.ValidWord,
                rationale: $"Word '{request.CompletedWord}' is valid {request.LanguageVariant} vocabulary.");
        }

        // Step 2: Generate candidate corrections
        var contextWords = new List<string>();
        contextWords.AddRange(request.AdjacentLeft);
        contextWords.AddRange(request.AdjacentRight);

        var candidates = _candidateGenerator.GenerateCandidates(
            request.CompletedWord,
            contextWords,
            request.LanguageVariant,
            maxCandidates: 10);

        if (candidates.Count == 0)
        {
            return TypeRight.Domain.Models.CorrectionDecision.LowConfidencePreserve(
                rationale: $"No valid corrections found for '{request.CompletedWord}'.");
        }

        // Step 3: Rank candidates based on context and frequency
        var rankedCandidates = _candidateRanker.Rank(
            candidates,
            request.CompletedWord,
            request.AdjacentLeft,
            request.AdjacentRight,
            request.LanguageVariant);

        // Step 4: Calculate a provisional confidence so that reason refinement
        // can distinguish high-confidence matches from ambiguous ones.
        var (provisionalConfidence, _) = _confidenceCalculator.CalculateConfidence(
            rankedCandidates,
            TypeRight.Domain.Models.ReasonCategory.AutoCorrect);

        // Step 5: Refine the reason category, then recalculate confidence with
        // it. Categories such as regional variants and proper names must drive
        // the action, not just be reported to callers.
        TypeRight.Domain.Models.ReasonCategory reasonCategory = RefineReasonCategory(
            rankedCandidates,
            provisionalConfidence,
            request.CompletedWord,
            request.LanguageVariant);

        var (confidence, suggestedAction) = _confidenceCalculator.CalculateConfidence(
            rankedCandidates,
            reasonCategory);

        // Step 6: Apply correction priority philosophy
        TypeRight.Domain.Models.CorrectionAction action;
        string correctedWord = null;
        IReadOnlyList<string> alternatives = null;
        string rationale;

        switch (suggestedAction)
        {
            case TypeRight.Domain.Models.CorrectionAction.AutoCorrect:
                action = TypeRight.Domain.Models.CorrectionAction.AutoCorrect;
                correctedWord = rankedCandidates[0].Candidate;
                rationale = $"Auto-corrected '{request.CompletedWord}' to '{correctedWord}' based on {confidence:P0} confidence.";
                break;

            case TypeRight.Domain.Models.CorrectionAction.Suggest:
                action = TypeRight.Domain.Models.CorrectionAction.Suggest;
                alternatives = rankedCandidates
                    .Take(3)
                    .Select(c => c.Candidate)
                    .ToList();
                rationale = $"Suggestion for '{request.CompletedWord}': top alternative '{rankedCandidates[0].Candidate}' with {confidence:P0} confidence.";
                break;

            case TypeRight.Domain.Models.CorrectionAction.Preserve:
            default:
                action = TypeRight.Domain.Models.CorrectionAction.Preserve;
                rationale = $"Preserved '{request.CompletedWord}' - {reasonCategory} decision ({confidence:P0} confidence).";
                break;
        }

        return new TypeRight.Domain.Models.CorrectionDecision(
            action, correctedWord, alternatives, confidence, reasonCategory, rationale);
    }

    /// <summary>
    /// Refines the reason category based on the correction analysis.
    /// </summary>
    private TypeRight.Domain.Models.ReasonCategory RefineReasonCategory(
        IReadOnlyList<TypeRight.Engine.CandidateRanking.RankedCandidate> rankedCandidates,
        float confidence,
        string completedWord,
        TypeRight.Domain.Models.LanguageVariant languageVariant)
    {
        if (rankedCandidates.Count == 0)
            return TypeRight.Domain.Models.ReasonCategory.InsufficientData;

        bool isAmbiguous = IsLikelyAmbiguousWord(completedWord);
        if (isAmbiguous && confidence < 0.9f)
            return TypeRight.Domain.Models.ReasonCategory.Ambiguous;

        bool isRegional = IsRegionalSpelling(completedWord, languageVariant);
        if (isRegional)
            return TypeRight.Domain.Models.ReasonCategory.RegionalVariant;

        if (IsLikelyProperNameOrTechnical(completedWord))
            return TypeRight.Domain.Models.ReasonCategory.ProperNameOrTechnical;

        if (confidence >= ConfidenceThresholds.High)
            return TypeRight.Domain.Models.ReasonCategory.AutoCorrect;
        if (confidence >= ConfidenceThresholds.Medium)
            return TypeRight.Domain.Models.ReasonCategory.Suggest;
        return TypeRight.Domain.Models.ReasonCategory.LowConfidence;
    }

    private bool IsLikelyAmbiguousWord(string word)
    {
        var ambiguousWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "your", "you're", "their", "there", "its", "it's",
            "form", "from", "by", "buy"
        };

        return ambiguousWords.Contains(word);
    }

    private bool IsRegionalSpelling(string word, TypeRight.Domain.Models.LanguageVariant languageVariant)
    {
        if (languageVariant == TypeRight.Domain.Models.LanguageVariant.Auto) return false;
        if (!RegionalSpelling.TryGetPair(word, out RegionalSpellingPair pair)) return false;
        if (!pair.PreserveWhenVariantDiffers) return false;

        bool isBritishSpelling = string.Equals(word, pair.British, StringComparison.OrdinalIgnoreCase);
        bool isAmericanSpelling = string.Equals(word, pair.American, StringComparison.OrdinalIgnoreCase);

        if (languageVariant == TypeRight.Domain.Models.LanguageVariant.EnUs)
            return isBritishSpelling;
        if (languageVariant == TypeRight.Domain.Models.LanguageVariant.EnGb)
            return isAmericanSpelling;

        return false;
    }

    private bool IsLikelyProperNameOrTechnical(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;

        bool hasInternalCapital = word.Any(c => char.IsUpper(c)) &&
                                  word.Any(c => char.IsLower(c));

        bool isAllCaps = word.Equals(word.ToUpperInvariant(), StringComparison.Ordinal) &&
                         word.Any(c => char.IsLetter(c));

        bool isShortCapitalized = word.Length >= 2 && word.Length <= 3 &&
                                  char.IsUpper(word[0]);

        return hasInternalCapital || (isShortCapitalized && !isAllCaps);
    }
}