using System;
using System.Linq;
using TypeRight.Domain.Models;
using TypeRight.Engine.CandidateGeneration;
using Xunit;

namespace TypeRight.Engine.Tests;

public class EngineCompositionTests
{
    [Theory]
    [InlineData("wrold", LanguageVariant.Auto, "world")]
    [InlineData("teh", LanguageVariant.Auto, "the")]
    [InlineData("defence", LanguageVariant.EnUs, "defense")]
    [InlineData("helo", LanguageVariant.Auto, "hello")]
    public void CreateDecisionMaker_CorrectsCommonTypos(string input, LanguageVariant variant, string expected)
    {
        var decision = EngineComposition.CreateDecisionMaker().MakeDecision(Request(input, variant));

        Assert.Equal(CorrectionAction.AutoCorrect, decision.Action);
        Assert.Equal(expected, decision.CorrectedWord);
    }

    [Fact]
    public void CreateDecisionMaker_IsDeterministic()
    {
        var maker = EngineComposition.CreateDecisionMaker();
        var request = Request("wrold", LanguageVariant.Auto);

        var first = maker.MakeDecision(request);
        var second = maker.MakeDecision(request);

        Assert.Equal(first.Action, second.Action);
        Assert.Equal(first.CorrectedWord, second.CorrectedWord);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.Reason, second.Reason);
    }

    [Fact]
    public void CreateDecisionMaker_PreservesValidAndWrongVariantWords()
    {
        var maker = EngineComposition.CreateDecisionMaker();

        var valid = maker.MakeDecision(Request("world", LanguageVariant.Auto));
        var regional = maker.MakeDecision(Request("colour", LanguageVariant.EnUs));

        Assert.Equal(ReasonCategory.ValidWord, valid.Reason);
        Assert.Equal(CorrectionAction.Preserve, valid.Action);
        Assert.Equal(CorrectionAction.Preserve, regional.Action);
        Assert.Equal(ReasonCategory.RegionalVariant, regional.Reason);
    }

    [Fact]
    public void CreateCandidateGenerator_ReturnsSharedConfiguredGenerator()
    {
        ICandidateGenerator generator = EngineComposition.CreateCandidateGenerator();

        var candidates = generator.GenerateCandidates(
            "wrold",
            Array.Empty<string>(),
            maxCandidates: EngineComposition.DefaultMaxCandidates);

        Assert.Contains(candidates, c => c.Candidate == "world");
        Assert.True(candidates.Count <= EngineComposition.DefaultMaxCandidates);
    }

    [Fact]
    public void CreateCandidateGenerator_SupportsPersonalWords()
    {
        ICandidateGenerator generator = EngineComposition.CreateCandidateGenerator(new[] { "qwerty" });

        var candidates = generator.GenerateCandidates("qwerti", Array.Empty<string>());

        Assert.Contains(candidates, c => c.Candidate == "qwerty");
    }

    [Fact]
    public void CreateDecisionMaker_SupportsPersonalWords()
    {
        var maker = EngineComposition.CreateDecisionMaker(new[] { "qwerty" });

        var decision = maker.MakeDecision(Request("qwerti", LanguageVariant.Auto));

        Assert.Equal(CorrectionAction.AutoCorrect, decision.Action);
        Assert.Equal("qwerty", decision.CorrectedWord);
    }

    private static CorrectionRequest Request(string word, LanguageVariant variant)
        => CorrectionRequest.Minimal(
            word,
            CapitalizationInfo.FromOriginal(word),
            variant,
            new VocabularyVersion(1, 0));
}
