#nullable disable
using System;
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
}

/// <summary>
/// Provides vocabulary validation using an embedded word list.
/// </summary>
public class EmbeddedDictionaryValidator : IDictionaryValidator
{
    private readonly HashSet<string> _baseVocabulary;
    private readonly HashSet<string> _britishVariants;
    private readonly HashSet<string> _americanVariants;
    private readonly bool _considerVariants;

    /// <summary>
    /// Constructs the validator with base vocabulary.
    /// </summary>
    /// <param name="baseVocabulary">Set of base words (American English defaults).</param>
    /// <param name="britishVariants">Set of words with British spellings (e.g., "colour").</param>
    /// <param name="americanVariants">Set of words with American spellings (e.g., "color").</param>
    /// <param name="considerVariants">Whether to accept regional variants.</param>
    public EmbeddedDictionaryValidator(
        HashSet<string> baseVocabulary,
        HashSet<string> britishVariants,
        HashSet<string> americanVariants,
        bool considerVariants = true)
    {
        _baseVocabulary = baseVocabulary ?? throw new ArgumentNullException(nameof(baseVocabulary));
        _britishVariants = britishVariants ?? throw new ArgumentNullException(nameof(britishVariants));
        _americanVariants = americanVariants ?? throw new ArgumentNullException(nameof(americanVariants));
        _considerVariants = considerVariants;
    }

    /// <summary>
    /// Checks if a word is in the base vocabulary or approved variants.
    /// </summary>
    public bool IsValid(string word, LanguageVariant languageVariant)
        => IsValidWithVariants(word, languageVariant, considerVariants: true);

    /// <summary>
    /// Checks word validity considering regional spelling variants.
    /// </summary>
    public bool IsValidWithVariants(string word, LanguageVariant languageVariant, bool considerVariants = true)
    {
        if (string.IsNullOrEmpty(word)) return false;

        word = word.Trim();

        // Check base vocabulary (case-insensitive)
        if (_baseVocabulary.Contains(word, StringComparer.OrdinalIgnoreCase))
            return true;

        // If not in base, check with variants consideration
        if (!considerVariants) return false;

        // Check regional spelling variants based on language variant
        switch (languageVariant)
        {
            case LanguageVariant.Auto:
                // Accept both British and American variants
                if (_britishVariants.Contains(word, StringComparer.OrdinalIgnoreCase))
                    return true;
                if (_americanVariants.Contains(word, StringComparer.OrdinalIgnoreCase))
                    return true;
                break;

            case LanguageVariant.EnUs:
                // Accept American variants and base vocab
                if (_americanVariants.Contains(word, StringComparer.OrdinalIgnoreCase))
                    return true;
                break;

            case LanguageVariant.EnGb:
                // Accept British variants and base vocab
                if (_britishVariants.Contains(word, StringComparer.OrdinalIgnoreCase))
                    return true;
                break;
        }

        return false;
    }
}