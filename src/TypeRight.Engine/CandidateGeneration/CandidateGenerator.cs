#nullable disable
using System;
using System.Collections.Generic;
using TypeRight.Domain.Models;
using TypeRight.Engine;
using TypeRight.Engine.Vocabulary;
using TypeRight.Engine.Tokenization;

namespace TypeRight.Engine.CandidateGeneration;

/// <summary>
/// Generates candidate corrections for a misspelled word.
/// Uses multiple strategies: edit distance, keyboard adjacency, n-gram similarity.
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
/// Generates candidates using Damerau-Levenshtein edit distance.
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
    /// Generates candidate corrections using edit distance, filtered by dictionary validity.
    /// </summary>
    public IReadOnlyList<CandidateResult> GenerateCandidates(
        string misspelled,
        IReadOnlyList<string> contextWords,
        int maxCandidates = 10)
    {
        if (string.IsNullOrEmpty(misspelled)) return Array.Empty<CandidateResult>();

        misspelled = misspelled.Trim();
        var candidates = new List<CandidateResult>();

        // Generate all strings within max edit distance
        var generated = GenerateWithinEditDistance(misspelled, _maxEditDistance);

        // Filter and score
        foreach (var candidate in generated)
        {
            // Skip the original word itself
            if (string.Equals(candidate, misspelled, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check dictionary validity
            if (!_dictionary.IsValid(candidate, LanguageVariant.Auto))
                continue;

            // Calculate edit distance score (lower distance = higher score)
            int distance = DamerauLevenshtein.Distance(misspelled, candidate);
            float score = CalculateEditDistanceScore(distance, misspelled.Length, candidate.Length);

            candidates.Add(new CandidateResult(candidate, score, CandidateGenerationStrategy.EditDistance));
        }

        // Sort by score descending, then alphabetically for ties
        candidates.Sort((a, b) =>
        {
            int scoreCompare = b.InitialScore.CompareTo(a.InitialScore);
            if (scoreCompare != 0) return scoreCompare;
            return string.Compare(a.Candidate, b.Candidate, StringComparison.OrdinalIgnoreCase);
        });

        // Return top maxCandidates
        return candidates.Count > maxCandidates
            ? candidates.Take(maxCandidates).ToList()
            : candidates;
    }

    /// <summary>
    /// Generates all strings within the maximum edit distance from the source.
    /// Uses a breadth-first approach.
    /// </summary>
    private HashSet<string> GenerateWithinEditDistance(string source, int maxDistance)
    {
        if (string.IsNullOrEmpty(source)) return new HashSet<string>();

        var results = new HashSet<string>();
        var queue = new Queue<(string current, int distance)>();
        var visited = new HashSet<string>();

        queue.Enqueue((source, 0));
        visited.Add(source);

        while (queue.Count > 0)
        {
            var (current, currentDistance) = queue.Dequeue();

            if (currentDistance < maxDistance)
            {
                // Generate all possible edits at current distance + 1
                var edits = GenerateEdits(current);

                foreach (var edit in edits)
                {
                    if (!visited.Contains(edit))
                    {
                        visited.Add(edit);
                        queue.Enqueue((edit, currentDistance + 1));

                        // Add to results if it's different from the original
                        if (edit != source)
                        {
                            results.Add(edit);
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Generates all possible single-edit variations of a string.
    /// Insertions, deletions, substitutions, and transpositions.
    /// </summary>
    private HashSet<string> GenerateEdits(string word)
    {
        var edits = new HashSet<string>();
        char[] chars = word.ToCharArray();
        int length = chars.Length;

        // Substitutions
        for (int i = 0; i < length; i++)
        {
            for (char c = 'a'; c <= 'z'; c++)
            {
                if (c == chars[i]) continue;
                char[] newChars = (char[])chars.Clone();
                newChars[i] = c;
                edits.Add(new string(newChars));
            }
        }

        // Insertions
        for (int i = 0; i <= length; i++)
        {
            for (char c = 'a'; c <= 'z'; c++)
            {
                char[] newChars = new char[length + 1];
                for (int j = 0; j < i; j++)
                    newChars[j] = chars[j];
                newChars[i] = c;
                for (int j = i; j < length; j++)
                    newChars[j + 1] = chars[j];
                edits.Add(new string(newChars));
            }
        }

        // Deletions
        for (int i = 0; i < length; i++)
        {
            char[] newChars = new char[length - 1];
            for (int j = 0, k = 0; j < length; j++)
            {
                if (j == i) continue;
                newChars[k++] = chars[j];
            }
            edits.Add(new string(newChars));
        }

        // Transpositions (adjacent swap)
        for (int i = 0; i < length - 1; i++)
        {
            char[] newChars = (char[])chars.Clone();
            char temp = newChars[i];
            newChars[i] = newChars[i + 1];
            newChars[i + 1] = temp;
            edits.Add(new string(newChars));
        }

        return edits;
    }

    /// <summary>
    /// Calculates an edit distance-based score.
    /// Lower distance = higher score (max 1.0).
    /// </summary>
    private float CalculateEditDistanceScore(int distance, int originalLength, int candidateLength)
    {
        // Normalized score: exponential decay with distance
        // Distance 0 = score 1.0 (but we skip the original word)
        // Distance 1 = score ~0.8
        // Distance 2 = score ~0.5
        // Distance >= 3 = score ~0.2

        float baseScore = 1.0f;
        float distancePenalty = distance * 0.3f;
        float lengthFactor = Math.Max(1, Math.Min(originalLength, candidateLength)) / (float)Math.Max(originalLength, candidateLength);

        float score = baseScore - distancePenalty;
        score = Math.Max(0.1f, Math.Min(1.0f, score * lengthFactor));

        return score;
    }
}