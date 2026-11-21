#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TypeRight.Engine.Vocabulary;

/// <summary>
/// Loads the embedded common-English word list synchronously and deterministically.
/// </summary>
public static class EmbeddedWordList
{
    private const string ResourceSuffix = "EnglishWords.txt";

    // Minimal fallback so construction still works if the resource is unavailable.
    private static readonly string[] FallbackWords =
    {
        "the", "and", "or", "but", "if", "then", "else", "for", "while", "do",
        "is", "are", "was", "were", "be", "been", "have", "has", "had",
        "will", "would", "can", "could", "may", "might", "must", "not", "no",
        "a", "an", "my", "your", "his", "her", "its", "our", "their",
        "i", "me", "we", "us", "you", "he", "she", "it", "to", "of", "in", "on",
        "at", "by", "from", "up", "down", "out", "over", "under", "there", "here",
        "what", "which", "who", "this", "that", "these", "those", "one", "two",
        "world", "word", "hello", "test", "the", "color", "defense", "offense",
        "center", "favorite", "organize", "traveling", "recognized", "analyze", "realize"
    };

    /// <summary>
    /// Returns the words from the embedded resource, or the built-in fallback list.
    /// </summary>
    public static IReadOnlyList<string> Load()
    {
        Assembly assembly = typeof(EmbeddedWordList).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));

        if (resourceName == null)
            return FallbackWords;

        using Stream stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return FallbackWords;

        var words = new List<string>();
        using var reader = new StreamReader(stream);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;

            foreach (string word in trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                words.Add(word);
        }

        return words.Count == 0 ? FallbackWords : words;
    }
}
