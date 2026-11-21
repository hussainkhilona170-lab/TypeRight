#nullable disable
using System;
using TypeRight.Domain.Models;
using TypeRight.Engine;
using TypeRight.Engine.CorrectionDecision;
using TypeRight.Engine.CandidateGeneration;
using TypeRight.Engine.CandidateRanking;
using TypeRight.Engine.Confidence;
using TypeRight.Engine.Vocabulary;

namespace TypeRight.Engine;

/// <summary>
/// Composes the correction engine with all required components.
/// Configures dictionary, tokenizer, generator, ranker, confidence calculator,
/// and decision maker with default implementations.
/// </summary>
public static class EngineComposition
{
    /// <summary>
    /// Creates a fully configured correction decision maker.
    /// </summary>
    public static ICorrectionDecisionMaker CreateDecisionMaker()
    {
        // 1. Build base vocabulary from embedded resources
        var baseVocabulary = LoadBaseVocabulary();
        var britishVariants = LoadBritishVariants();
        var americanVariants = LoadAmericanVariants();

        // 2. Configure dictionary validator
        var dictionary = new EmbeddedDictionaryValidator(
            baseVocabulary,
            britishVariants,
            americanVariants,
            considerVariants: true);

        // 3. Configure candidate generator
        var candidateGenerator = new EditDistanceCandidateGenerator(
            dictionary,
            maxEditDistance: 2,
            maxCandidates: 15);

        // 4. Configure candidate ranker
        var frequencyProvider = new SimpleFrequencyProvider();
        var candidateRanker = new ContextAwareRanker(frequencyProvider);

        // 5. Configure confidence calculator
        var confidenceCalculator = new ConfidenceCalculator();

        // 6. Create the decision maker
        var decisionMaker = new CorrectionDecisionMaker(
            dictionary,
            candidateGenerator,
            candidateRanker,
            confidenceCalculator);

        return decisionMaker;
    }

    /// <summary>
    /// Loads the base English vocabulary.
    /// In production, this would load from an embedded resource or file.
    /// For now, contains a core set of common words.
    /// </summary>
    private static HashSet<string> LoadBaseVocabulary()
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "or", "but", "if", "then", "else", "for", "while", "do",
            "does", "did", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "having", "will", "would", "shall", "should",
            "can", "could", "may", "might", "must", "here", "there", "where",
            "what", "which", "who", "whom", "this", "that", "these", "those",
            "a", "an", "my", "your", "his", "her", "its", "our", "their",
            "i", "me", "we", "us", "you", "he", "she", "it",
            "to", "of", "in", "on", "at", "by", "for", "from", "up", "down",
            "out", "over", "under", "again", "further", "then", "once",
            "not", "no", "none", "nothing", "something", "any", "some", "such"
        };

        return words;
    }

    /// <summary>
    /// Loads British spelling variant words.
    /// </summary>
    private static HashSet<string> LoadBritishVariants()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "colour", "organise", "centre", "favourite",
            "travelling", "recognised", "analyse", "realise",
            "defence", "offence", "premise", "programme"
        };
    }

    /// <summary>
    /// Loads American spelling variant words.
    /// </summary>
    private static HashSet<string> LoadAmericanVariants()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "color", "organize", "center", "favorite",
            "traveling", "recognized", "analyze", "realize",
            "defense", "offense", "program", "programming"
        };
    }

    /// <summary>
    /// Simple frequency provider for word frequency scoring.
    /// </summary>
    private class SimpleFrequencyProvider : IFrequencyProvider
    {
        private readonly Dictionary<string, float> _frequencyCache = new();

        public SimpleFrequencyProvider()
        {
            InitializeFrequencies();
        }

        private void InitializeFrequencies()
        {
            foreach (var word in new[] { "the", "be", "to", "of", "and", "a", "in", "that", "have", "i" })
            {
                _frequencyCache[word] = 1.0f;
            }

            foreach (var word in new[] { "you", "that", "it", "he", "was", "for", "on", "are", "as", "with" })
            {
                _frequencyCache[word] = 0.6f;
            }

            foreach (var word in new[] { "but", "not", "what", "all", "we", "when", "your", "can", "said",
                "there", "use", "an", "each", "which", "she", "do", "how", "their",
                "if", "will", "up", "other", "about", "out", "many", "then",
                "them", "these", "so", "some", "her", "would", "make", "like",
                "into", "time", "has", "look", "two", "more", "go", "see", "number",
                "no", "way", "could", "people", "my", "than", "first", "water",
                "been", "call", "who", "oil", "its", "now", "find", "long", "down",
                "day", "did", "get", "come", "made" })
            {
                _frequencyCache[word] = 0.3f;
            }
        }

        public float GetFrequencyScore(string word)
        {
            if (_frequencyCache.TryGetValue(word.ToLower(), out float score))
                return score;

            return 0.5f;
        }
    }
}