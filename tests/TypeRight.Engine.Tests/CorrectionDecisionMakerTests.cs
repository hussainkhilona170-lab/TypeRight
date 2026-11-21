using System;
using System.Collections.Generic;
using TypeRight.Domain.Models;
using TypeRight.Engine.CandidateGeneration;
using TypeRight.Engine.CandidateRanking;
using TypeRight.Engine.Confidence;
using TypeRight.Engine.CorrectionDecision;
using TypeRight.Engine.Vocabulary;
using Xunit;
using Decision = TypeRight.Domain.Models.CorrectionDecision;

namespace TypeRight.Engine.Tests;

public class DictionaryValidatorTests
{
    private readonly EmbeddedDictionaryValidator _validator = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world", "validword" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colour" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "color" });

    [Theory]
    [InlineData("WORLD", LanguageVariant.Auto, true)]
    [InlineData("colour", LanguageVariant.Auto, true)]
    [InlineData("colour", LanguageVariant.EnGb, true)]
    [InlineData("colour", LanguageVariant.EnUs, false)]
    [InlineData("color", LanguageVariant.EnUs, true)]
    [InlineData("wrold", LanguageVariant.Auto, false)]
    [InlineData("", LanguageVariant.Auto, false)]
    public void IsValid_RespectsVocabularyAndVariant(string word, LanguageVariant variant, bool expected)
    {
        Assert.Equal(expected, _validator.IsValid(word, variant));
    }

    [Fact]
    public void IsValidWithVariants_CanIgnoreVariants()
    {
        Assert.False(_validator.IsValidWithVariants("colour", LanguageVariant.Auto, considerVariants: false));
        Assert.True(_validator.IsValidWithVariants("world", LanguageVariant.Auto, considerVariants: false));
    }
}

public class ConfidenceCalculatorTests
{
    private readonly ConfidenceCalculator _calculator = new();

    [Theory]
    [InlineData(0.95f, CorrectionAction.AutoCorrect)]
    [InlineData(0.7f, CorrectionAction.Suggest)]
    [InlineData(0.5f, CorrectionAction.Preserve)]
    public void CalculateConfidence_UsesScoreThresholds(float score, CorrectionAction expected)
    {
        var ranked = new[] { new RankedCandidate("world", score, default) };

        var (confidence, action) = _calculator.CalculateConfidence(ranked, ReasonCategory.AutoCorrect);

        Assert.Equal(expected, action);
        Assert.Equal(score, confidence);
    }

    [Fact]
    public void CalculateConfidence_EmptyCandidatesPreserveWithLowConfidence()
    {
        var (confidence, action) = _calculator.CalculateConfidence(
            Array.Empty<RankedCandidate>(), ReasonCategory.AutoCorrect);

        Assert.Equal(CorrectionAction.Preserve, action);
        Assert.Equal(0.1f, confidence);
    }

    [Fact]
    public void CalculateConfidence_LowConfidenceNeverAutoCorrects()
    {
        // Even an artificially high score must not trigger auto-correction when
        // the reason category says the evidence is weak.
        var ranked = new[] { new RankedCandidate("world", 0.99f, default) };

        var (confidence, action) = _calculator.CalculateConfidence(ranked, ReasonCategory.LowConfidence);

        Assert.Equal(CorrectionAction.Preserve, action);
        Assert.True(confidence <= 0.4f);
    }

    [Theory]
    [InlineData(ReasonCategory.RegionalVariant, CorrectionAction.Preserve, 0.9f)]
    [InlineData(ReasonCategory.ProperNameOrTechnical, CorrectionAction.Preserve, 0.95f)]
    [InlineData(ReasonCategory.Ambiguous, CorrectionAction.Suggest, 0.8f)]
    public void CalculateConfidence_RespectsSpecialReasons(
        ReasonCategory reason, CorrectionAction expectedAction, float expectedConfidence)
    {
        var ranked = new[] { new RankedCandidate("world", 0.8f, default) };

        var (confidence, action) = _calculator.CalculateConfidence(ranked, reason);

        Assert.Equal(expectedAction, action);
        Assert.Equal(expectedConfidence, confidence);
    }
}

public class CorrectionDecisionMakerTests
{
    private static Decision Decide(
        string word,
        IReadOnlyList<string> candidates,
        float score,
        LanguageVariant variant = LanguageVariant.Auto)
    {
        var dictionary = new StubDictionary();
        var generator = new StubGenerator(candidates);
        var ranker = new StubRanker(score);
        var maker = new CorrectionDecisionMaker(dictionary, generator, ranker, new ConfidenceCalculator());
        var request = CorrectionRequest.Minimal(
            word, CapitalizationInfo.FromOriginal(word), variant, new VocabularyVersion(1, 0));

        return maker.MakeDecision(request);
    }

    [Fact]
    public void ValidWord_IsPreservedWithValidWordReason()
    {
        var decision = Decide("validword", Array.Empty<string>(), 0f);

        Assert.Equal(CorrectionAction.Preserve, decision.Action);
        Assert.Equal(ReasonCategory.ValidWord, decision.Reason);
        Assert.Equal(1.0f, decision.Confidence);
    }

    [Fact]
    public void NoCandidates_IsPreserved()
    {
        var decision = Decide("wrold", Array.Empty<string>(), 0f);

        Assert.Equal(CorrectionAction.Preserve, decision.Action);
        Assert.Equal(ReasonCategory.LowConfidence, decision.Reason);
        Assert.Null(decision.CorrectedWord);
    }

    [Fact]
    public void HighConfidenceCandidate_IsAutoCorrected()
    {
        var decision = Decide("wrold", new[] { "world" }, 0.95f);

        Assert.Equal(CorrectionAction.AutoCorrect, decision.Action);
        Assert.Equal("world", decision.CorrectedWord);
        Assert.Equal(ReasonCategory.AutoCorrect, decision.Reason);
    }

    [Fact]
    public void MediumConfidenceCandidate_IsSuggested()
    {
        var decision = Decide("wrold", new[] { "world", "word" }, 0.7f);

        Assert.Equal(CorrectionAction.Suggest, decision.Action);
        Assert.Equal(new[] { "world", "word" }, decision.Alternatives);
        Assert.Equal(ReasonCategory.Suggest, decision.Reason);
    }

    [Fact]
    public void RegionalVariant_IsPreservedAndReasonIsReported()
    {
        var decision = Decide("colour", new[] { "color" }, 0.95f, LanguageVariant.EnUs);

        Assert.Equal(CorrectionAction.Preserve, decision.Action);
        Assert.Equal(ReasonCategory.RegionalVariant, decision.Reason);
        Assert.Null(decision.CorrectedWord);
    }

    [Fact]
    public void AmbiguousWord_IsSuggestedWithClarityReason()
    {
        var decision = Decide("form", new[] { "from" }, 0.8f);

        Assert.Equal(CorrectionAction.Suggest, decision.Action);
        Assert.Equal(ReasonCategory.Ambiguous, decision.Reason);
    }

    [Fact]
    public void ProperNameOrTechnicalWord_IsPreserved()
    {
        var decision = Decide("MicroSoft", new[] { "Microsoft" }, 0.95f);

        Assert.Equal(CorrectionAction.Preserve, decision.Action);
        Assert.Equal(ReasonCategory.ProperNameOrTechnical, decision.Reason);
    }

    [Fact]
    public void AllCapsWord_IsNotTreatedAsProperName()
    {
        var decision = Decide("WORLD", new[] { "World" }, 0.95f);

        Assert.Equal(CorrectionAction.AutoCorrect, decision.Action);
        Assert.Equal("World", decision.CorrectedWord);
    }

    [Fact]
    public void Generator_ReceivesRequestLanguageVariant()
    {
        var generator = new RecordingGenerator();
        var maker = new CorrectionDecisionMaker(
            new StubDictionary(), generator, new StubRanker(0.9f), new ConfidenceCalculator());
        var request = CorrectionRequest.Minimal(
            "wrold", CapitalizationInfo.FromOriginal("wrold"), LanguageVariant.EnGb, new VocabularyVersion(1, 0));

        maker.MakeDecision(request);

        Assert.Equal(LanguageVariant.EnGb, generator.LastVariant);
    }

    private sealed class RecordingGenerator : ICandidateGenerator
    {
        public LanguageVariant LastVariant { get; private set; }

        public IReadOnlyList<CandidateResult> GenerateCandidates(
            string misspelled, IReadOnlyList<string> contextWords, int maxCandidates = 10)
            => GenerateCandidates(misspelled, contextWords, LanguageVariant.Auto, maxCandidates);

        public IReadOnlyList<CandidateResult> GenerateCandidates(
            string misspelled,
            IReadOnlyList<string> contextWords,
            LanguageVariant languageVariant,
            int maxCandidates = 10)
        {
            LastVariant = languageVariant;
            return new[]
            {
                new CandidateResult("world", 0.9f, CandidateGenerationStrategy.EditDistance)
            };
        }
    }

    private sealed class StubDictionary : IDictionaryValidator
    {
        public bool IsValid(string word, LanguageVariant languageVariant)
            => string.Equals(word, "validword", StringComparison.OrdinalIgnoreCase);

        public bool IsValidWithVariants(string word, LanguageVariant languageVariant, bool considerVariants = true)
            => IsValid(word, languageVariant);
    }

    private sealed class StubGenerator : ICandidateGenerator
    {
        private readonly IReadOnlyList<string> _candidates;

        public StubGenerator(IReadOnlyList<string> candidates) => _candidates = candidates;

        public IReadOnlyList<CandidateResult> GenerateCandidates(
            string misspelled, IReadOnlyList<string> contextWords, int maxCandidates = 10)
        {
            var results = new List<CandidateResult>();
            foreach (var candidate in _candidates)
                results.Add(new CandidateResult(candidate, 0.9f, CandidateGenerationStrategy.EditDistance));
            return results;
        }
    }

    private sealed class StubRanker : ICandidateRanker
    {
        private readonly float _score;

        public StubRanker(float score) => _score = score;

        public IReadOnlyList<RankedCandidate> Rank(
            IReadOnlyList<CandidateResult> candidates,
            string completedWord,
            IReadOnlyList<string> adjacentLeft,
            IReadOnlyList<string> adjacentRight,
            LanguageVariant languageVariant)
        {
            var ranked = new List<RankedCandidate>();
            foreach (var candidate in candidates)
                ranked.Add(new RankedCandidate(candidate.Candidate, _score, default));
            return ranked;
        }
    }
}
