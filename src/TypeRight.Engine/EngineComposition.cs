#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Domain.Models;
using TypeRight.Engine.CandidateGeneration;
using TypeRight.Engine.CandidateRanking;
using TypeRight.Engine.Confidence;
using TypeRight.Engine.CorrectionDecision;
using TypeRight.Engine.Vocabulary;

namespace TypeRight.Engine;

/// <summary>
/// Composes the correction engine with all required components.
/// Configures dictionary, tokenizer, generator, ranker, confidence calculator,
/// and decision maker with default implementations.
/// </summary>
public static class EngineComposition
{
    /// <summary>Default maximum edit distance used by the shared generator.</summary>
    public const int DefaultMaxEditDistance = 2;

    /// <summary>Default maximum candidate count used by the shared generator.</summary>
    public const int DefaultMaxCandidates = 15;

    /// <summary>
    /// Creates a fully configured correction decision maker.
    /// </summary>
    public static ICorrectionDecisionMaker CreateDecisionMaker()
        => CreateDecisionMaker(personalWords: null);

    /// <summary>
    /// Creates a fully configured correction decision maker with extra personal words.
    /// </summary>
    /// <param name="personalWords">Optional user-specific accepted words.</param>
    public static ICorrectionDecisionMaker CreateDecisionMaker(IEnumerable<string> personalWords)
    {
        IDictionaryValidator dictionary = CreateDictionary(personalWords);
        ICandidateGenerator candidateGenerator = CreateCandidateGenerator(dictionary);

        var frequencyProvider = new SimpleFrequencyProvider();
        var candidateRanker = new ContextAwareRanker(frequencyProvider);
        var confidenceCalculator = new ConfidenceCalculator();

        return new CorrectionDecisionMaker(
            dictionary,
            candidateGenerator,
            candidateRanker,
            confidenceCalculator);
    }

    /// <summary>
    /// Creates the same candidate generator configuration used by
    /// <see cref="CreateDecisionMaker()"/>.
    /// </summary>
    public static ICandidateGenerator CreateCandidateGenerator()
        => CreateCandidateGenerator(personalWords: null);

    /// <summary>
    /// Creates the shared candidate generator with extra personal words.
    /// </summary>
    /// <param name="personalWords">Optional user-specific accepted words.</param>
    public static ICandidateGenerator CreateCandidateGenerator(IEnumerable<string> personalWords)
        => CreateCandidateGenerator(CreateDictionary(personalWords));

    private static ICandidateGenerator CreateCandidateGenerator(IDictionaryValidator dictionary)
        => new EditDistanceCandidateGenerator(
            dictionary,
            maxEditDistance: DefaultMaxEditDistance,
            maxCandidates: DefaultMaxCandidates);

    private static IDictionaryValidator CreateDictionary(IEnumerable<string> personalWords)
    {
        var baseVocabulary = LoadBaseVocabulary();
        return new EmbeddedDictionaryValidator(
            baseVocabulary,
            new HashSet<string>(RegionalSpelling.BritishToAmerican.Keys, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(RegionalSpelling.AmericanToBritish.Keys, StringComparer.OrdinalIgnoreCase),
            considerVariants: true,
            personalWords: personalWords);
    }

    /// <summary>
    /// Loads the embedded base English vocabulary (American spellings).
    /// </summary>
    private static HashSet<string> LoadBaseVocabulary()
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string word in EmbeddedWordList.Load())
        {
            if (!string.IsNullOrWhiteSpace(word))
                words.Add(word.Trim());
        }

        // "programming" is valid in both variants and is deliberately not a
        // regional pair; keep it in the base vocabulary.
        words.Add("programming");

        return words;
    }

    /// <summary>
    /// Simple frequency provider for word frequency scoring.
    /// </summary>
    private sealed class SimpleFrequencyProvider : IFrequencyProvider
    {
        private readonly Dictionary<string, float> _frequencyCache = new(StringComparer.OrdinalIgnoreCase);

        public SimpleFrequencyProvider()
        {
            InitializeFrequencies();
        }

        private void InitializeFrequencies()
        {
            foreach (string word in new[] { "the", "be", "to", "of", "and", "a", "in", "have", "i", "world" })
                _frequencyCache[word] = 1.0f;

            foreach (string word in new[] { "you", "that", "it", "he", "was", "for", "on", "are", "as", "with" })
                _frequencyCache[word] = 0.6f;

            foreach (string word in new[]
            {
                "but", "not", "what", "all", "we", "when", "your", "can", "said",
                "there", "use", "an", "each", "which", "she", "do", "how", "their",
                "if", "will", "up", "other", "about", "out", "many", "then",
                "them", "these", "so", "some", "her", "would", "make", "like",
                "into", "time", "has", "look", "two", "more", "go", "see", "number",
                "no", "way", "could", "people", "my", "than", "first", "water",
                "been", "call", "who", "oil", "its", "now", "find", "long", "down",
                "day", "did", "get", "come", "made", "hello", "word",
                "test", "defense", "offense"
            })
            {
                _frequencyCache[word] = 0.6f;
            }
        }

        public float GetFrequencyScore(string word)
        {
            if (string.IsNullOrEmpty(word)) return 0.5f;
            return _frequencyCache.TryGetValue(word, out float score) ? score : 0.5f;
        }
    }
}
