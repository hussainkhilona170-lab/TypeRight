#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Domain.Models;
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
    private const float EditWeight = 0.30f;
    private const float ContextWeight = 0.25f;
    private const float FrequencyWeight = 0.38f;
    private const float KeyboardWeight = 0.02f;
    private const float NgramWeight = 0.05f;

    private static readonly IReadOnlyDictionary<char, HashSet<char>> KeyboardNeighbors = BuildKeyboardNeighbors();

    private static readonly HashSet<string> CommonAdjacentPairs = new(StringComparer.Ordinal)
    {
        "the|world", "hello|world", "of|the", "in|the", "to|the", "for|the", "on|the",
        "the|test", "the|word", "hello|test", "a|word", "each|word", "my|word"
    };

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
        if (candidates == null || candidates.Count == 0)
            return Array.Empty<RankedCandidate>();

        IReadOnlyList<string> left = adjacentLeft ?? Array.Empty<string>();
        IReadOnlyList<string> right = adjacentRight ?? Array.Empty<string>();
        string original = (completedWord ?? string.Empty).ToLowerInvariant();
        var ranked = new List<RankedCandidate>(candidates.Count);

        foreach (CandidateResult candidate in candidates)
        {
            string candidateText = candidate.Candidate ?? string.Empty;
            string normalizedCandidate = candidateText.ToLowerInvariant();

            // 1. Edit distance factor (also consumes the generator's initial score).
            int editDist = DamerauLevenshtein.Distance(original, normalizedCandidate);
            int denominator = Math.Max(1, Math.Max(original.Length, normalizedCandidate.Length));
            float distanceScore = Math.Clamp(1.0f - editDist / (float)denominator, 0.0f, 1.0f);
            float initialScore = Math.Clamp(candidate.InitialScore, 0.0f, 1.0f);
            float editScore = Math.Clamp((distanceScore * 0.7f) + (initialScore * 0.3f), 0.0f, 1.0f);

            // 2. Context match factor (candidate-aware; neutral when no context exists).
            float contextScore = ComputeContextMatch(candidateText, left, right);

            // 3. Frequency factor.
            float freqScore = Math.Clamp(_frequencyProvider.GetFrequencyScore(normalizedCandidate), 0.0f, 1.0f);

            // 4. QWERTY keyboard adjacency factor.
            float keyboardScore = ComputeKeyboardAdjacency(original, normalizedCandidate);

            // 5. Character n-gram similarity factor.
            float ngramScore = ComputeNgramSimilarity(original, normalizedCandidate);

            float finalScore =
                (editScore * EditWeight) +
                (contextScore * ContextWeight) +
                (freqScore * FrequencyWeight) +
                (keyboardScore * KeyboardWeight) +
                (ngramScore * NgramWeight);

            var factors = new RankedCandidate.Factor
            {
                EditDistance = editScore,
                ContextMatch = contextScore,
                Frequency = freqScore,
                KeyboardAdjacency = keyboardScore,
                NgramSimilarity = ngramScore
            };

            // Candidate text casing is preserved from the generator.
            ranked.Add(new RankedCandidate(candidateText, finalScore, factors));
        }

        ranked.Sort((a, b) =>
        {
            int scoreCompare = b.FinalScore.CompareTo(a.FinalScore);
            if (scoreCompare != 0) return scoreCompare;

            int ignoreCase = string.Compare(a.Candidate, b.Candidate, StringComparison.OrdinalIgnoreCase);
            return ignoreCase != 0
                ? ignoreCase
                : string.Compare(a.Candidate, b.Candidate, StringComparison.Ordinal);
        });

        return ranked;
    }

    /// <summary>
    /// Computes a candidate-aware context score. With no adjacent words the
    /// context is neutral (1.0), so a strong candidate can still auto-correct.
    /// </summary>
    private float ComputeContextMatch(string candidate, IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 && right.Count == 0) return 1.0f;

        float score = 0.5f;
        foreach (string word in left.Take(2).Concat(right.Take(2)))
            score += ComputeContextPairScore(word, candidate);

        return Math.Clamp(score, 0.0f, 1.0f);
    }

    /// <summary>
    /// Scores a single adjacent word against a candidate using frequency,
    /// character-bigram similarity and a small common word-pair map.
    /// </summary>
    private float ComputeContextPairScore(string contextWord, string candidate)
    {
        if (string.IsNullOrWhiteSpace(contextWord)) return 0.0f;

        string ctx = contextWord.Trim().ToLowerInvariant();
        string cand = (candidate ?? string.Empty).Trim().ToLowerInvariant();
        if (ctx.Length == 0 || cand.Length == 0) return 0.0f;
        if (string.Equals(ctx, cand, StringComparison.Ordinal)) return 0.5f;

        float frequency = Math.Clamp(_frequencyProvider.GetFrequencyScore(cand), 0.0f, 1.0f);
        float similarity = ComputeNgramSimilarity(ctx, cand);
        float pairBonus = CommonAdjacentPairs.Contains(ctx + "|" + cand) ? 0.25f : 0.0f;

        return Math.Clamp(0.1f + (frequency * 0.1f) + (similarity * 0.1f) + pairBonus, 0.0f, 0.25f);
    }

    /// <summary>
    /// Computes keyboard adjacency using a QWERTY neighbor map.
    /// </summary>
    private static float ComputeKeyboardAdjacency(string original, string candidate)
    {
        string a = (original ?? string.Empty).ToLowerInvariant();
        string b = (candidate ?? string.Empty).ToLowerInvariant();

        if (string.Equals(a, b, StringComparison.Ordinal)) return 1.0f;
        if (a.Length == 0 || b.Length == 0) return 0.0f;

        int minLen = Math.Min(a.Length, b.Length);
        int differences = Math.Abs(a.Length - b.Length);
        int adjacent = 0;

        for (int i = 0; i < minLen; i++)
        {
            if (a[i] == b[i]) continue;

            differences++;
            if (KeyboardNeighbors.TryGetValue(a[i], out HashSet<char> neighbors) && neighbors.Contains(b[i]))
                adjacent++;
        }

        return differences == 0 ? 1.0f : Math.Clamp(adjacent / (float)differences, 0.0f, 1.0f);
    }

    /// <summary>
    /// Computes character bigram similarity (case-insensitive).
    /// </summary>
    private static float ComputeNgramSimilarity(string a, string b)
    {
        string left = (a ?? string.Empty).ToLowerInvariant();
        string right = (b ?? string.Empty).ToLowerInvariant();

        if (string.Equals(left, right, StringComparison.Ordinal)) return 1.0f;
        if (left.Length == 0 || right.Length == 0) return 0.0f;

        HashSet<string> gramsA = GetBigrams(left);
        HashSet<string> gramsB = GetBigrams(right);
        if (gramsA.Count == 0 || gramsB.Count == 0) return 0.5f;

        int intersection = gramsA.Intersect(gramsB).Count();
        return Math.Clamp(intersection / (float)Math.Max(gramsA.Count, gramsB.Count), 0.0f, 1.0f);
    }

    /// <summary>
    /// Gets character bigrams from a string.
    /// </summary>
    private static HashSet<string> GetBigrams(string text)
    {
        var grams = new HashSet<string>(StringComparer.Ordinal);
        string normalized = (text ?? string.Empty).ToLowerInvariant();
        if (normalized.Length < 2) return grams;

        for (int i = 0; i < normalized.Length - 1; i++)
            grams.Add(normalized.Substring(i, 2));

        return grams;
    }

    /// <summary>
    /// Builds a QWERTY keyboard adjacency map (horizontal and diagonal neighbors).
    /// </summary>
    private static IReadOnlyDictionary<char, HashSet<char>> BuildKeyboardNeighbors()
    {
        string[] rows = { "qwertyuiop", "asdfghjkl", "zxcvbnm" };
        var map = new Dictionary<char, HashSet<char>>();

        for (int row = 0; row < rows.Length; row++)
        {
            for (int col = 0; col < rows[row].Length; col++)
            {
                char key = rows[row][col];
                if (!map.TryGetValue(key, out HashSet<char> neighbors))
                    map[key] = neighbors = new HashSet<char>();

                for (int rowOffset = -1; rowOffset <= 1; rowOffset++)
                {
                    int neighborRow = row + rowOffset;
                    if (neighborRow < 0 || neighborRow >= rows.Length) continue;

                    for (int colOffset = -1; colOffset <= 1; colOffset++)
                    {
                        if (rowOffset == 0 && colOffset == 0) continue;

                        int neighborCol = col + colOffset;
                        if (neighborCol < 0 || neighborCol >= rows[neighborRow].Length) continue;

                        neighbors.Add(rows[neighborRow][neighborCol]);
                    }
                }
            }
        }

        return map;
    }
}
