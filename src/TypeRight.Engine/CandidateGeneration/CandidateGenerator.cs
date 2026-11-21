#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Domain.Models;
using TypeRight.Engine.Tokenization;
using TypeRight.Engine.Vocabulary;

namespace TypeRight.Engine.CandidateGeneration;

/// <summary>
/// Generates candidate corrections for a misspelled word.
/// Uses dictionary-driven edit distance matching, with keyboard adjacency and
/// n-gram similarity applied during ranking.
/// </summary>
public interface ICandidateGenerator
{
    /// <summary>
    /// Generates candidate corrections for a misspelled word.
    /// </summary>
    /// <param name="misspelled">The word that may be misspelled.</param>
    /// <param name="contextWords">Adjacent words for context-aware generation.</param>
    /// <param name="maxCandidates">Maximum number of candidates to generate.</param>
    /// <returns>A list of (candidate, score) tuples, sorted by relevance.</returns>
    IReadOnlyList<CandidateResult> GenerateCandidates(
        string misspelled,
        IReadOnlyList<string> contextWords,
        int maxCandidates = 10);

    /// <summary>
    /// Generates candidate corrections for a misspelled word, honoring the
    /// requested language variant when validating candidates.
    /// </summary>
    /// <param name="misspelled">The word that may be misspelled.</param>
    /// <param name="contextWords">Adjacent words for context-aware generation.</param>
    /// <param name="languageVariant">The English variant to validate against.</param>
    /// <param name="maxCandidates">Maximum number of candidates to generate.</param>
    /// <returns>A list of (candidate, score) tuples, sorted by relevance.</returns>
    IReadOnlyList<CandidateResult> GenerateCandidates(
        string misspelled,
        IReadOnlyList<string> contextWords,
        LanguageVariant languageVariant,
        int maxCandidates = 10)
        => GenerateCandidates(misspelled, contextWords, maxCandidates);
}

/// <summary>
/// A candidate correction result with an initial score.
/// </summary>
public readonly struct CandidateResult
{
    /// <summary>
    /// The candidate word.
    /// </summary>
    public string Candidate { get; }

    /// <summary>
    /// The initial relevance score (higher is better).
    /// </summary>
    public float InitialScore { get; }

    /// <summary>
    /// The generation strategy used (for debugging/tracing).
    /// </summary>
    public CandidateGenerationStrategy Strategy { get; }

    /// <summary>
    /// Creates a new CandidateResult.
    /// </summary>
    public CandidateResult(string candidate, float initialScore, CandidateGenerationStrategy strategy)
    {
        Candidate = candidate;
        InitialScore = initialScore;
        Strategy = strategy;
    }
}

/// <summary>
/// The strategy used to generate a candidate.
/// </summary>
public enum CandidateGenerationStrategy
{
    EditDistance,
    KeyboardAdjacency,
    NgramSimilarity,
    DictionaryLookup
}

/// <summary>
/// Generates candidates using dictionary-driven Damerau-Levenshtein matching.
/// </summary>
public class EditDistanceCandidateGenerator : ICandidateGenerator
{
    private readonly IDictionaryValidator _dictionary;
    private readonly int _maxEditDistance;
    private readonly int _maxCandidates;

    /// <summary>
    /// Constructs the edit distance candidate generator.
    /// </summary>
    /// <param name="dictionary">Validator for filtering valid candidates.</param>
    /// <param name="maxEditDistance">Maximum edit distance to consider.</param>
    /// <param name="maxCandidates">Maximum candidates to return.</param>
    public EditDistanceCandidateGenerator(
        IDictionaryValidator dictionary,
        int maxEditDistance = 2,
        int maxCandidates = 10)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _maxEditDistance = maxEditDistance;
        _maxCandidates = maxCandidates;
    }

    /// <summary>
    /// Generates candidate corrections using the dictionary word list.
    /// </summary>
    public IReadOnlyList<CandidateResult> GenerateCandidates(
        string misspelled,
        IReadOnlyList<string> contextWords,
        int maxCandidates = 10)
        => GenerateCandidates(misspelled, contextWords, LanguageVariant.Auto, maxCandidates);

    /// <summary>
    /// Generates candidate corrections using the dictionary word list and the
    /// requested language variant.
    /// </summary>
    public IReadOnlyList<CandidateResult> GenerateCandidates(
        string misspelled,
        IReadOnlyList<string> contextWords,
        LanguageVariant languageVariant,
        int maxCandidates = 10)
    {
        if (string.IsNullOrWhiteSpace(misspelled)) return Array.Empty<CandidateResult>();

        // The configured cap always wins over the per-call request.
        int limit = Math.Min(maxCandidates, _maxCandidates);
        if (limit <= 0 || _maxEditDistance <= 0) return Array.Empty<CandidateResult>();

        string source = misspelled.Trim();
        string normalizedSource = source.ToLowerInvariant();
        var candidates = new List<CandidateResult>();

        // Iterate the dictionary word list rather than enumerating all possible
        // strings within edit distance N (which explodes combinatorially).
        foreach (string dictionaryWord in _dictionary.Words)
        {
            if (string.IsNullOrWhiteSpace(dictionaryWord)) continue;

            string candidate = dictionaryWord.Trim();
            if (string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase))
                continue;

            if (Math.Abs(candidate.Length - normalizedSource.Length) > _maxEditDistance)
                continue;

            int distance = DamerauLevenshtein.Distance(normalizedSource, candidate.ToLowerInvariant());
            if (distance <= 0 || distance > _maxEditDistance)
                continue;

            if (!_dictionary.IsValid(candidate, languageVariant))
                continue;

            float score = CalculateEditDistanceScore(distance, normalizedSource.Length, candidate.Length);
            candidates.Add(new CandidateResult(candidate, score, CandidateGenerationStrategy.EditDistance));
        }

        // Sort by score descending, then by candidate text for deterministic ties.
        candidates.Sort((a, b) =>
        {
            int scoreCompare = b.InitialScore.CompareTo(a.InitialScore);
            if (scoreCompare != 0) return scoreCompare;

            int ignoreCase = string.Compare(a.Candidate, b.Candidate, StringComparison.OrdinalIgnoreCase);
            return ignoreCase != 0
                ? ignoreCase
                : string.Compare(a.Candidate, b.Candidate, StringComparison.Ordinal);
        });

        return candidates.Count > limit ? candidates.Take(limit).ToList() : candidates;
    }

    /// <summary>
    /// Calculates an edit distance-based score.
    /// Lower distance = higher score (max 1.0).
    /// </summary>
    private float CalculateEditDistanceScore(int distance, int originalLength, int candidateLength)
    {
        // Distance 1 = ~0.7, distance 2 = ~0.4, with a mild length penalty.
        float baseScore = 1.0f;
        float distancePenalty = distance * 0.3f;
        int shorter = Math.Max(1, Math.Min(originalLength, candidateLength));
        int longer = Math.Max(1, Math.Max(originalLength, candidateLength));
        float lengthFactor = shorter / (float)longer;

        float score = Math.Max(0.1f, Math.Min(1.0f, (baseScore - distancePenalty) * lengthFactor));
        return score;
    }
}
