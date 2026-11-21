using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Domain.Models;
using TypeRight.Engine.CandidateGeneration;
using TypeRight.Engine.Vocabulary;
using Xunit;

namespace TypeRight.Engine.Tests;

public class CandidateGeneratorTests
{
    [Fact]
    public void GenerateCandidates_DrawsFromDictionaryWordsInsteadOfEnumeratingStrings()
    {
        // "café" cannot be produced by the old a-z string enumeration, so seeing it
        // proves generation is driven by the dictionary word list.
        var dictionary = new StubDictionary(new[] { "café", "world", "word" });
        var generator = new EditDistanceCandidateGenerator(dictionary, 2, 10);

        var candidates = generator.GenerateCandidates("cafe", Array.Empty<string>());

        Assert.Contains(candidates, c => c.Candidate == "café");
        Assert.Equal(1, dictionary.Enumerations);
    }

    [Fact]
    public void GenerateCandidates_LongInputDoesNotBuildCombinatorialUniverse()
    {
        string longWord = new string('q', 40);
        var dictionary = new StubDictionary(new[] { "world", "word" });
        var generator = new EditDistanceCandidateGenerator(dictionary, 2, 10);

        var candidates = generator.GenerateCandidates(longWord, Array.Empty<string>());

        Assert.Empty(candidates);
        Assert.Equal(1, dictionary.Enumerations);
    }

    [Fact]
    public void GenerateCandidates_HonorsConfiguredAndMethodMaxCandidates()
    {
        string[] words = Enumerable.Range('a', 12).Select(c => "word" + (char)c).ToArray();
        var generator = new EditDistanceCandidateGenerator(new StubDictionary(words), 2, maxCandidates: 3);

        int methodLimit = generator.GenerateCandidates("word", Array.Empty<string>(), maxCandidates: 10).Count;
        int smallerMethodLimit = generator.GenerateCandidates("word", Array.Empty<string>(), maxCandidates: 2).Count;

        Assert.Equal(3, methodLimit);
        Assert.Equal(2, smallerMethodLimit);
    }

    [Fact]
    public void GenerateCandidates_UsesRequestedLanguageVariant()
    {
        var dictionary = new EmbeddedDictionaryValidator(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "word", "test", "hello", "the" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colour" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "color" });
        var generator = new EditDistanceCandidateGenerator(dictionary, 2, 10);

        var us = generator.GenerateCandidates("colur", Array.Empty<string>(), LanguageVariant.EnUs);
        var gb = generator.GenerateCandidates("colur", Array.Empty<string>(), LanguageVariant.EnGb);

        Assert.Equal("color", us[0].Candidate);
        Assert.Equal("colour", gb[0].Candidate);
    }

    [Fact]
    public void EmbeddedDictionaryValidator_HonorConsiderVariantsAndPersonalWords()
    {
        var validator = new EmbeddedDictionaryValidator(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colour" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "color" },
            considerVariants: false,
            personalWords: new[] { "Xyzzy" });

        Assert.True(validator.IsValid("XYZZY", LanguageVariant.Auto));
        Assert.False(validator.IsValid("colour", LanguageVariant.Auto));
        // The validator-level setting is a hard cap: even an explicit request to
        // consider variants cannot re-enable them.
        Assert.False(validator.IsValidWithVariants("colour", LanguageVariant.EnGb));
        Assert.Contains("color", validator.Words);
    }

    private sealed class StubDictionary : IDictionaryValidator
    {
        private readonly IReadOnlyList<string> _words;

        public StubDictionary(IEnumerable<string> words) => _words = words.ToArray();

        public int Enumerations { get; private set; }

        public IEnumerable<string> Words
        {
            get
            {
                Enumerations++;
                return _words;
            }
        }

        public bool IsValid(string word, LanguageVariant languageVariant)
            => _words.Contains(word, StringComparer.OrdinalIgnoreCase);

        public bool IsValidWithVariants(string word, LanguageVariant languageVariant, bool considerVariants = true)
            => IsValid(word, languageVariant);
    }
}
