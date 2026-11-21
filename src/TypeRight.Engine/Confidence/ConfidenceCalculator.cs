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

            case ReasonCategory.LowConfidence:
                // Low-confidence decisions must never auto-correct, regardless of score.
                confidence = Math.Min(topScore, ConfidenceThresholds.Low);
                action = CorrectionAction.Preserve;
                break;

            case ReasonCategory.InsufficientData:
                confidence = 0.2f;
                action = CorrectionAction.Preserve;
                break;

            default:
                if (topScore >= ConfidenceThresholds.High)
                {
                    confidence = topScore;
                    action = CorrectionAction.AutoCorrect;
                }
                else if (topScore >= ConfidenceThresholds.Medium)
                {
                    confidence = topScore;
                    action = CorrectionAction.Suggest;
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