#nullable disable
using System;
using TypeRight.Domain.Models;
using TypeRight.Engine.CandidateRanking;

namespace TypeRight.Engine.Confidence;

/// <summary>
/// Calculates confidence score for a correction decision.
/// Determines whether to auto-correct, suggest, or preserve.
/// </summary>
public interface IConfidenceCalculator
{
    /// <summary>
    /// Calculates the confidence level for a given correction decision.
    /// </summary>
    /// <param name="rankedCandidates">The ranked candidates from the ranker.</param>
    /// <param name="decisionReason">The reason category informing the confidence.</param>
    /// <returns>A confidence value 0.0-1.0, and the suggested action.</returns>
    (float confidence, CorrectionAction suggestedAction) CalculateConfidence(
        IReadOnlyList<RankedCandidate> rankedCandidates,
        ReasonCategory decisionReason);
}

/// <summary>
/// Calculates confidence based on candidate scores, dictionary validation,
/// and contextual factors to determine correction priority.
/// </summary>
public class ConfidenceCalculator : IConfidenceCalculator
{
    private const float HighConfidenceThreshold = 0.85f;
    private const float MediumConfidenceThreshold = 0.65f;
    private const float LowConfidenceThreshold = 0.40f;

    /// <summary>
    /// Calculates confidence and suggests an action based on ranked candidates.
    /// </summary>
    public (float confidence, CorrectionAction suggestedAction) CalculateConfidence(
        IReadOnlyList<RankedCandidate> rankedCandidates,
        ReasonCategory decisionReason)
    {
        if (rankedCandidates.Count == 0)
        {
            return (0.1f, CorrectionAction.Preserve);
        }

        float topScore = rankedCandidates[0].FinalScore;

        CorrectionAction action;
        float confidence;

        switch (decisionReason)
        {
            case ReasonCategory.ValidWord:
                confidence = 1.0f;
                action = CorrectionAction.Preserve;
                break;

            case ReasonCategory.Ambiguous:
                confidence = topScore;
                action = CorrectionAction.Suggest;
                break;

            case ReasonCategory.RegionalVariant:
                confidence = 0.9f;
                action = CorrectionAction.Preserve;
                break;

            case ReasonCategory.ProperNameOrTechnical:
                confidence = 0.95f;
                action = CorrectionAction.Preserve;
                break;

            case ReasonCategory.InsufficientData:
                confidence = 0.2f;
                action = CorrectionAction.Preserve;
                break;

            default:
                if (topScore >= HighConfidenceThreshold)
                {
                    confidence = topScore;
                    action = CorrectionAction.AutoCorrect;
                }
                else if (topScore >= MediumConfidenceThreshold)
                {
                    confidence = topScore;
                    action = CorrectionAction.Suggest;
                }
                else if (topScore >= LowConfidenceThreshold)
                {
                    confidence = topScore;
                    action = CorrectionAction.Preserve;
                }
                else
                {
                    confidence = topScore;
                    action = CorrectionAction.Preserve;
                }
                break;
        }

        confidence = Math.Clamp(confidence, 0.0f, 1.0f);

        return (confidence, action);
    }
}