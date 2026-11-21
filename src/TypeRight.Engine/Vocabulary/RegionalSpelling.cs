#nullable disable
using System;
using System.Collections.Generic;
using TypeRight.Domain.Models;

namespace TypeRight.Engine.Vocabulary;

/// <summary>
/// A single British/American spelling pair.
/// </summary>
public sealed class RegionalSpellingPair
{
    /// <summary>British spelling.</summary>
    public string British { get; }

    /// <summary>American spelling.</summary>
    public string American { get; }

    /// <summary>
    /// True when a correctly spelled but wrong-variant word should be preserved
    /// (for example "colour" in en-US). False when the wrong-variant spelling is
    /// treated as a typo to be corrected (for example "defence" in en-US).
    /// </summary>
    public bool PreserveWhenVariantDiffers { get; }

    public RegionalSpellingPair(string british, string american, bool preserveWhenVariantDiffers = true)
    {
        British = british ?? throw new ArgumentNullException(nameof(british));
        American = american ?? throw new ArgumentNullException(nameof(american));
        PreserveWhenVariantDiffers = preserveWhenVariantDiffers;
    }
}

/// <summary>
/// One canonical map of regional spellings shared by dictionary setup and
/// correction reasoning.
/// </summary>
public static class RegionalSpelling
{
    private static readonly RegionalSpellingPair[] Pairs =
    {
        new("colour", "color"),
        new("organise", "organize"),
        new("centre", "center"),
        new("favourite", "favorite"),
        new("travelling", "traveling"),
        new("recognised", "recognized"),
        new("analyse", "analyze"),
        new("realise", "realize"),
        new("programme", "program"),
        // These two are unambiguously misspelled in en-US and are corrected there.
        new("defence", "defense", preserveWhenVariantDiffers: false),
        new("offence", "offense", preserveWhenVariantDiffers: false)
    };

    private static readonly Dictionary<string, string> BritishToAmericanMap = BuildMap(britishToAmerican: true);
    private static readonly Dictionary<string, string> AmericanToBritishMap = BuildMap(britishToAmerican: false);

    /// <summary>All canonical pairs.</summary>
    public static IReadOnlyList<RegionalSpellingPair> All => Pairs;

    /// <summary>British spelling -&gt; American spelling.</summary>
    public static IReadOnlyDictionary<string, string> BritishToAmerican => BritishToAmericanMap;

    /// <summary>American spelling -&gt; British spelling.</summary>
    public static IReadOnlyDictionary<string, string> AmericanToBritish => AmericanToBritishMap;

    /// <summary>Finds the canonical pair containing the given spelling.</summary>
    public static bool TryGetPair(string word, out RegionalSpellingPair pair)
    {
        if (!string.IsNullOrEmpty(word))
        {
            foreach (RegionalSpellingPair candidate in Pairs)
            {
                if (string.Equals(candidate.British, word, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.American, word, StringComparison.OrdinalIgnoreCase))
                {
                    pair = candidate;
                    return true;
                }
            }
        }

        pair = null;
        return false;
    }

    private static Dictionary<string, string> BuildMap(bool britishToAmerican)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (RegionalSpellingPair pair in Pairs)
        {
            map[britishToAmerican ? pair.British : pair.American] =
                britishToAmerican ? pair.American : pair.British;
        }

        return map;
    }
}
