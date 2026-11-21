#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Domain.Models;

namespace TypeRight.Engine.Vocabulary;

/// <summary>
/// Validates whether a word is in the dictionary (base vocabulary + personal dictionary).
/// Supports both British and American English variants.
/// </summary>
public interface IDictionaryValidator
{
    /// <summary>
    /// Determines if a word is considered valid in the current vocabulary.
    /// </summary>
    /// <param name="word">The word to validate.</param>
    /// <param name="languageVariant">The English variant to check against.</param>
    /// <returns>true if the word is valid; false if it's a genuine misspelling.</returns>
    bool IsValid(string word, LanguageVariant languageVariant);

    /// <summary>
    /// Determines if a word is valid, considering regional spelling variants.
    /// For example, "color" is valid in en-US, "colour" is valid in en-GB,
    /// and both are accepted when language variant is Auto.
    /// </summary>
    /// <param name="word">The word to validate.</param>
    /// <param name="languageVariant">The English variant to check against.</param>
    /// <param name="considerVariants">Whether to consider regional spelling variants.</param>
    /// <returns>true if the word is valid or is a valid regional variant.</returns>
    bool IsValidWithVariants(string word, LanguageVariant languageVariant, bool considerVariants = true);

    /// <summary>
    /// All words known to this dictionary. Candidate generation iterates this
    /// list instead of enumerating an artificial "all strings" universe.
    /// </summary>
    IEnumerable<string> Words => Array.Empty<string>();
}

/// <summary>
/// Provides vocabulary validation using an embedded word list.
/// </summary>
public class EmbeddedDictionaryValidator : IDictionaryValidator
{
    private readonly HashSet<string> _baseVocabulary;
    private readonly HashSet<string> _britishVariants;
    private readonly HashSet<string> _americanVariants;
    private readonly HashSet<string> _personalWords;
    private readonly bool _considerVariants;

    /// <summary>
    /// Constructs the validator with base vocabulary.
    /// </summary>
    /// <param name="baseVocabulary">Set of base words (American English defaults).</param>
    /// <param name="britishVariants">Set of words with British spellings (e.g., "colour").</param>
    /// <param name="americanVariants">Set of words with American spellings (e.g., "color").</param>
    /// <param name="considerVariants">Whether to accept regional variants.</param>
    /// <param name="personalWords">Optional user-specific words to accept.</param>
    public EmbeddedDictionaryValidator(
        HashSet<string> baseVocabulary,
        HashSet<string> britishVariants,
        HashSet<string> americanVariants,
        bool considerVariants = true,
        IEnumerable<string> personalWords = null)
    {
        // Copy into case-insensitive sets so lookups never depend on the caller's comparer.
        _baseVocabulary = new HashSet<string>(
            baseVocabulary ?? throw new ArgumentNullException(nameof(baseVocabulary)),
            StringComparer.OrdinalIgnoreCase);
        _britishVariants = new HashSet<string>(
            britishVariants ?? throw new ArgumentNullException(nameof(britishVariants)),
            StringComparer.OrdinalIgnoreCase);
        _americanVariants = new HashSet<string>(
            americanVariants ?? throw new ArgumentNullException(nameof(americanVariants)),
            StringComparer.OrdinalIgnoreCase);
        _personalWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (personalWords != null)
        {
            foreach (string word in personalWords)
            {
                if (!string.IsNullOrWhiteSpace(word))
                    _personalWords.Add(word.Trim());
            }
        }

        _considerVariants = considerVariants;
    }

    /// <summary>
    /// All known words: base, personal and enabled regional variants.
    /// </summary>
    public IEnumerable<string> Words
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> all = _baseVocabulary
                .Concat(_personalWords)
                .Concat(_britishVariants)
                .Concat(_americanVariants);

            foreach (string word in all)
            {
                if (seen.Add(word))
                    yield return word;
            }
        }
    }

    /// <summary>
    /// Checks if a word is in the base/personal vocabulary or approved variants.
    /// </summary>
    public bool IsValid(string word, LanguageVariant languageVariant)
        => IsValidWithVariants(word, languageVariant, _considerVariants);

    /// <summary>
    /// Checks word validity considering regional spelling variants.
    /// </summary>
    public bool IsValidWithVariants(string word, LanguageVariant languageVariant, bool considerVariants = true)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;

        word = word.Trim();

        // Base vocabulary and personal dictionary are always accepted (case-insensitive).
        if (_baseVocabulary.Contains(word) || _personalWords.Contains(word))
            return true;

        // The validator-level setting is a hard cap on variant acceptance.
        if (!considerVariants || !_considerVariants) return false;

        switch (languageVariant)
        {
            case LanguageVariant.Auto:
                // Accept both British and American variants.
                return _britishVariants.Contains(word) || _americanVariants.Contains(word);

            case LanguageVariant.EnUs:
                // Accept American variants in addition to the base vocabulary.
                return _americanVariants.Contains(word);

            case LanguageVariant.EnGb:
                // Accept British variants in addition to the base vocabulary.
                return _britishVariants.Contains(word);

            default:
                return false;
        }
    }
}
