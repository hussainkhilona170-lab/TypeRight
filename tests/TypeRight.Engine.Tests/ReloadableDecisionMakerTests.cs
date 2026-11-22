using System;
using System.Collections.Generic;
using TypeRight.Domain.Models;
using TypeRight.Engine.CorrectionDecision;
using Xunit;

namespace TypeRight.Engine.Tests;

public class ReloadableDecisionMakerTests
{
    [Fact]
    public void ReloadableDecisionMaker_UsesLatestPersonalWords()
    {
        var words = new List<string>();
        var maker = new ReloadableDecisionMaker(() => words);

        var before = maker.MakeDecision(Request("qwerti"));
        Assert.NotEqual("qwerty", before.CorrectedWord);

        words.Add("qwerty");
        maker.Reload();

        var after = maker.MakeDecision(Request("qwerti"));
        Assert.Equal(CorrectionAction.AutoCorrect, after.Action);
        Assert.Equal("qwerty", after.CorrectedWord);
    }

    [Fact]
    public void ReloadableDecisionMaker_FallsBackToBaseVocabularyWhenProviderFaults()
    {
        var maker = new ReloadableDecisionMaker(() => throw new InvalidOperationException());

        var decision = maker.MakeDecision(Request("wrold"));

        Assert.Equal(CorrectionAction.AutoCorrect, decision.Action);
        Assert.Equal("world", decision.CorrectedWord);
    }

    private static CorrectionRequest Request(string word)
        => CorrectionRequest.Minimal(
            word,
            CapitalizationInfo.FromOriginal(word),
            LanguageVariant.Auto,
            new VocabularyVersion(1, 0));
}
