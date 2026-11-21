using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Domain.Models;
using TypeRight.Engine.CandidateGeneration;
using TypeRight.Engine.CandidateRanking;
using Xunit;

namespace TypeRight.Engine.Tests;

public class CandidateRankerTests
{
    private static CandidateResult Candidate(string word, float initialScore = 0.8f)
        => new(word, initialScore, CandidateGenerationStrategy.EditDistance);

    [Fact]
    public void Rank_EmptyContextIsNeutral()
    {
        var ranker = new ContextAwareRanker(new FrequencyStub());

        var ranked = ranker.Rank(
            new[] { Candidate("world") },
            "wrold",
            Array.Empty<string>(),
            Array.Empty<string>(),
            LanguageVariant.Auto);

        Assert.Equal(1.0f, ranked[0].Factors.ContextMatch);
    }

    [Fact]
    public void Rank_UsesCandidateSpecificFrequencyAndContext()
    {
        var provider = new FrequencyStub { Scores = { ["world"] = 1.0f, ["word"] = 0.1f } };
        var ranker = new ContextAwareRanker(provider);
        var candidates = new[] { Candidate("word"), Candidate("world") };

        var ranked = ranker.Rank(
            candidates,
            "wordl",
            new[] { "hello" },
            Array.Empty<string>(),
            LanguageVariant.Auto);

        Assert.Equal("world", ranked[0].Candidate);
        Assert.True(ranked[0].Factors.ContextMatch > ranked[1].Factors.ContextMatch);
        Assert.True(ranked[0].Factors.Frequency > ranked[1].Factors.Frequency);
    }

    [Fact]
    public void Rank_ConsumesInitialScore()
    {
        var ranker = new ContextAwareRanker(new FrequencyStub());
        var candidates = new[]
        {
            Candidate("abd", initialScore: 0.1f),
            Candidate("abe", initialScore: 0.9f)
        };

        var ranked = ranker.Rank(
            candidates,
            "abc",
            Array.Empty<string>(),
            Array.Empty<string>(),
            LanguageVariant.Auto);

        Assert.Equal("abe", ranked[0].Candidate);
        Assert.True(ranked[0].Factors.EditDistance > ranked[1].Factors.EditDistance);
    }

    [Fact]
    public void Rank_UsesQwertyNeighborsForKeyboardAdjacency()
    {
        var ranker = new ContextAwareRanker(new FrequencyStub());

        // 'p' and 'o' are adjacent on a QWERTY keyboard; 'h' and 'e' are not.
        var keyboardTypo = ranker.Rank(
            new[] { Candidate("world") },
            "wprld",
            Array.Empty<string>(),
            Array.Empty<string>(),
            LanguageVariant.Auto);
        var nonAdjacent = ranker.Rank(
            new[] { Candidate("the") },
            "teh",
            Array.Empty<string>(),
            Array.Empty<string>(),
            LanguageVariant.Auto);

        Assert.Equal(1.0f, keyboardTypo[0].Factors.KeyboardAdjacency);
        Assert.Equal(0.0f, nonAdjacent[0].Factors.KeyboardAdjacency);
    }

    [Fact]
    public void Rank_EqualScoresUseDeterministicCandidateTieBreak()
    {
        var ranker = new ContextAwareRanker(new FrequencyStub());
        var candidates = new[] { Candidate("abe"), Candidate("abd") };

        var first = ranker.Rank(
            candidates,
            "abc",
            Array.Empty<string>(),
            Array.Empty<string>(),
            LanguageVariant.Auto);
        var reversed = ranker.Rank(
            candidates.Reverse().ToArray(),
            "abc",
            Array.Empty<string>(),
            Array.Empty<string>(),
            LanguageVariant.Auto);

        Assert.Equal(new[] { "abd", "abe" }, first.Select(r => r.Candidate));
        Assert.Equal(first.Select(r => r.Candidate), reversed.Select(r => r.Candidate));
    }

    private sealed class FrequencyStub : IFrequencyProvider
    {
        public Dictionary<string, float> Scores { get; } = new(StringComparer.OrdinalIgnoreCase);

        public float GetFrequencyScore(string word)
            => Scores.TryGetValue(word ?? string.Empty, out float score) ? score : 0.5f;
    }
}
