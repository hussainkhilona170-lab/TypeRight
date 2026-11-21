#nullable disable
using System;
using TypeRight.Domain;
using TypeRight.Engine;

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
            return TypeRight.Domain.Models.CorrectionDecision.Preserve(
                rationale: $"Word '{request.CompletedWord}' is valid {request.LanguageVariant} vocabulary.");
        }

        // Step 2: Generate candidate corrections
        var contextWords = new List<string>();
        contextWords.AddRange(request.AdjacentLeft);
        contextWords.AddRange(request.AdjacentRight);

        var candidates = _candidateGenerator.GenerateCandidates(
            request.CompletedWord,
            contextWords,
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

        // Step 4: Calculate confidence and determine suggested action
        var (confidence, suggestedAction) = _confidenceCalculator.CalculateConfidence(
            rankedCandidates,
            TypeRight.Domain.Models.ReasonCategory.AutoCorrect);

        // Step 5: Refine the reason category based on analysis
        TypeRight.Domain.Models.ReasonCategory reasonCategory = RefineReasonCategory(
            rankedCandidates,
            confidence,
            request.CompletedWord,
            request.LanguageVariant);

        // Step 6: Apply correction priority philosophy
        TypeRight.Domain.Models.CorrectionDecision decision;

        switch (suggestedAction)
        {
            case TypeRight.Domain.Models.CorrectionAction.AutoCorrect:
                string correctedWord = rankedCandidates[0].Candidate;
                decision = TypeRight.Domain.Models.CorrectionDecision.AutoCorrect(
                    correctedWord: correctedWord,
                    confidence: confidence,
                    rationale: $"Auto-corrected '{request.CompletedWord}' to '{correctedWord}' based on {confidence:P0} confidence.");
                break;

            case TypeRight.Domain.Models.CorrectionAction.Suggest:
                var alternatives = rankedCandidates
                    .Take(3)
                    .Select(c => c.Candidate)
                    .ToList();

                decision = TypeRight.Domain.Models.CorrectionDecision.Suggest(
                    alternatives: alternatives,
                    confidence: confidence,
                    rationale: $"Suggestion for '{request.CompletedWord}': top alternative '{rankedCandidates[0].Candidate}' with {confidence:P0} confidence.");
                break;

            case TypeRight.Domain.Models.CorrectionAction.Preserve:
            default:
                decision = TypeRight.Domain.Models.CorrectionDecision.LowConfidencePreserve(
                    rationale: $"Preserved '{request.CompletedWord}' - low confidence ({confidence:P0}) correction decision.");
                break;
        }

        return decision;
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

        float topScore = rankedCandidates[0].FinalScore;

        bool isAmbiguous = IsLikelyAmbiguousWord(completedWord);
        if (isAmbiguous && confidence < 0.9f)
            return TypeRight.Domain.Models.ReasonCategory.Ambiguous;

        bool isRegional = IsRegionalSpelling(completedWord, languageVariant);
        if (isRegional)
            return TypeRight.Domain.Models.ReasonCategory.RegionalVariant;

        if (IsLikelyProperNameOrTechnical(completedWord))
            return TypeRight.Domain.Models.ReasonCategory.ProperNameOrTechnical;

        if (confidence >= 0.85f)
            return TypeRight.Domain.Models.ReasonCategory.AutoCorrect;
        if (confidence >= 0.65f)
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

        var britishToAmerican = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["colour"] = "color",
            ["organise"] = "organize",
            ["centre"] = "center",
            ["favourite"] = "favorite",
            ["travelling"] = "traveling",
            ["recognised"] = "recognized",
            ["analyse"] = "analyze",
            ["realise"] = "realize"
        };

        var americanToBritish = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["color"] = "colour",
            ["organize"] = "organise",
            ["center"] = "centre",
            ["favorite"] = "favourite",
            ["traveling"] = "travelling",
            ["recognized"] = "recognised",
            ["analyze"] = "analyse",
            ["realize"] = "realise"
        };

        bool isBritish = britishToAmerican.ContainsKey(word);
        bool isAmerican = americanToBritish.ContainsKey(word);

        if (languageVariant == TypeRight.Domain.Models.LanguageVariant.EnUs)
            return isBritish;
        if (languageVariant == TypeRight.Domain.Models.LanguageVariant.EnGb)
            return isAmerican;

        return false;
    }

    private bool IsLikelyProperNameOrTechnical(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;

        bool hasInternalCapital = word.Any(c => char.IsUpper(c)) &&
                                  word.Any(c => char.IsLower(c));

        bool isAllCaps = word.Equals(word, StringComparison.OrdinalIgnoreCase) &&
                         word.Any(c => char.IsLetter(c));

        bool isShortCapitalized = word.Length >= 2 && word.Length <= 3 &&
                                  char.IsUpper(word[0]);

        return hasInternalCapital || (isShortCapitalized && !isAllCaps);
    }
}