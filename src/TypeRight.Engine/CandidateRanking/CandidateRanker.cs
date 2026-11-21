#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Domain.Models;
using TypeRight.Engine;
using TypeRight.Engine.CandidateGeneration;
using TypeRight.Engine.Tokenization;

namespace TypeRight.Engine.CandidateRanking;

/// <summary>
/// Ranks and re-ranks candidates based on context, frequency, and confidence factors.
/// </summary>
public interface ICandidateRanker
{
    /// <summary>
    /// Re-ranks a list of candidates based on context analysis and frequency.
    /// </summary>
    /// <param name="candidates">The candidates to rank.</param>
    /// <param name="completedWord">The original completed word.</param>
    /// <param name="adjacentLeft">Words to the left of the completed word.</param>
    /// <param name="adjacentRight">Words to the right of the completed word.</param>
    /// <param name="languageVariant">The English variant setting.</param>
    /// <returns>Re-ranked candidates with updated scores.</returns>
    IReadOnlyList<RankedCandidate> Rank(
        IReadOnlyList<CandidateResult> candidates,
        string completedWord,
        IReadOnlyList<string> adjacentLeft,
        IReadOnlyList<string> adjacentRight,
        LanguageVariant languageVariant);
}

/// <summary>
/// Provides frequency scores for words.
/// </summary>
public interface IFrequencyProvider
{
    float GetFrequencyScore(string word);
}

/// <summary>
/// A ranked candidate with a final confidence-eligible score.
/// </summary>
public class RankedCandidate
{
    /// <summary>
    /// The candidate word.
    /// </summary>
    public string Candidate { get; }

    /// <summary>
    /// The final ranking score (0.0-1.0, higher is better).
    /// </summary>
    public float FinalScore { get; }

    /// <summary>
    /// The contribution factors that influenced this score.
    /// </summary>
    public RankedCandidate.Factor Factors { get; }

    /// <summary>
    /// Creates a new RankedCandidate.
    /// </summary>
    public RankedCandidate(string candidate, float finalScore, RankedCandidate.Factor factors)
    {
        Candidate = candidate;
        FinalScore = finalScore;
        Factors = factors;
    }

    /// <summary>
    /// Factors that influenced the ranking score.
    /// </summary>
    public struct Factor
    {
        /// <summary>
        /// Edit distance contribution.
        /// </summary>
        public float EditDistance;

        /// <summary>
        /// Context match contribution.
        /// </summary>
        public float ContextMatch;

        /// <summary>
        /// Frequency/language model contribution.
        /// </summary>
        public float Frequency;

        /// <summary>
        /// Keyboard adjacency contribution.
        /// </summary>
        public float KeyboardAdjacency;

        /// <summary>
        /// n-gram similarity contribution.
        /// </summary>
        public float NgramSimilarity;
    }
}

/// <summary>
/// Ranks candidates using context, frequency, keyboard adjacency, and n-gram analysis.
/// </summary>
public class ContextAwareRanker : ICandidateRanker
{
    private readonly IFrequencyProvider _frequencyProvider;

    /// <summary>
    /// Constructs the context-aware ranker.
    /// </summary>
    public ContextAwareRanker(IFrequencyProvider frequencyProvider)
    {
        _frequencyProvider = frequencyProvider ?? throw new ArgumentNullException(nameof(frequencyProvider));
    }

    /// <summary>
    /// Re-ranks candidates based on multiple factors.
    /// </summary>
    public IReadOnlyList<RankedCandidate> Rank(
        IReadOnlyList<CandidateResult> candidates,
        string completedWord,
        IReadOnlyList<string> adjacentLeft,
        IReadOnlyList<string> adjacentRight,
        LanguageVariant languageVariant)
    {
        var ranked = new List<RankedCandidate>();

        foreach (var candidate in candidates)
        {
            // 1. Edit distance factor
            int editDist = DamerauLevenshtein.Distance(completedWord, candidate.Candidate);
            float editScore = 1.0f - (editDist / (float)Math.Max(completedWord.Length, 1));
            float editDistanceClamped = Math.Max(0, editScore);

            // 2. Context match factor
            float contextScore = ComputeContextMatch(candidate.Candidate, adjacentLeft, adjacentRight);

            // 3. Frequency factor
            float freqScore = _frequencyProvider.GetFrequencyScore(candidate.Candidate);

            // 4. Keyboard adjacency factor
            float keyboardScore = ComputeKeyboardAdjacency(completedWord, candidate.Candidate);

            // 5. n-gram similarity factor
            float ngramScore = ComputeNgramSimilarity(completedWord, candidate.Candidate);

            // Weighted final score
            float finalScore =
                (editDistanceClamped * 0.35f) +
                (contextScore * 0.30f) +
                (freqScore * 0.20f) +
                (keyboardScore * 0.10f) +
                (ngramScore * 0.05f);

            var factors = new RankedCandidate.Factor
            {
                EditDistance = editDistanceClamped,
                ContextMatch = contextScore,
                Frequency = freqScore,
                KeyboardAdjacency = keyboardScore,
                NgramSimilarity = ngramScore
            };

            ranked.Add(new RankedCandidate(candidate.Candidate, finalScore, factors));
        }

        // Sort by final score descending
        ranked.Sort((a, b) => b.FinalScore.CompareTo(a.FinalScore));

        return ranked;
    }

    /// <summary>
    /// Computes context match based on adjacent words.
    /// </summary>
    private float ComputeContextMatch(string candidate, IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 && right.Count == 0) return 0.5f;

        float score = 0.5f;

        foreach (var word in left.Take(2))
        {
            score += 0.05f;
        }

        foreach (var word in right.Take(2))
        {
            score += 0.05f;
        }

        return Math.Min(1.0f, score);
    }

    /// <summary>
    /// Computes keyboard adjacency scoring.
    /// </summary>
    private float ComputeKeyboardAdjacency(string original, string candidate)
    {
        if (original == candidate) return 1.0f;

        float score = 0.0f;
        int matches = 0;
        int minLen = Math.Min(original.Length, candidate.Length);

        for (int i = 0; i < minLen; i++)
        {
            if (original[i] == candidate[i])
                matches++;
        }

        if (minLen > 0)
            score = (float)matches / minLen;
        else
            score = 0.5f;

        return score;
    }

    /// <summary>
    /// Computes n-gram similarity.
    /// </summary>
    private float ComputeNgramSimilarity(string a, string b)
    {
        if (a == b) return 1.0f;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0f;

        var gramsA = GetBigrams(a);
        var gramsB = GetBigrams(b);

        if (gramsA.Count == 0 || gramsB.Count == 0) return 0.5f;

        int intersection = gramsA.Intersect(gramsB).Count();
        float similarity = (float)intersection / Math.Max(gramsA.Count, gramsB.Count);

        return Math.Max(0, similarity);
    }

    /// <summary>
    /// Gets character bigrams from a string.
    /// </summary>
    private HashSet<string> GetBigrams(string text)
    {
        var grams = new HashSet<string>();
        if (string.IsNullOrEmpty(text) || text.Length < 2) return grams;

        for (int i = 0; i < text.Length - 1; i++)
        {
            grams.Add(text.Substring(i, 2));
        }
        return grams;
    }
}