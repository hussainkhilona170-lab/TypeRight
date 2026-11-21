using System.Linq;
using TypeRight.Engine.Tokenization;
using Xunit;

namespace TypeRight.Engine.Tests;

public class TokenizerTests
{
    private readonly DefaultTokenizer _tokenizer = new();

    [Fact]
    public void Tokenize_SeparatesWordsFromPunctuationAndWhitespace()
    {
        var tokens = _tokenizer.Tokenize("Hello, world!");

        Assert.Equal(5, tokens.Count);
        Assert.Equal(new[] { "Hello", "world" }, tokens.Where(t => t.IsWord).Select(t => t.Text));
        Assert.All(tokens.Where(t => !t.IsWord), t => Assert.Null(t.WordValue));
    }

    [Fact]
    public void Tokenize_KeepsContractionsTogether()
    {
        var tokens = _tokenizer.Tokenize("don't stop");

        Assert.Equal("don't", tokens[0].Text);
        Assert.True(tokens[0].IsWord);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, -1)]
    [InlineData(7, 1)]
    [InlineData(12, -1)]
    public void GetWordIndexAtOffset_ReturnsWordIndexOrMinusOne(int offset, int expected)
    {
        Assert.Equal(expected, _tokenizer.GetWordIndexAtOffset("Hello, world!", offset));
    }

    [Theory]
    [InlineData("Hello world", 5, true)]
    [InlineData("Hello world", 3, false)]
    [InlineData("Hello world", 11, true)]
    [InlineData("Hello, world", 5, true)]
    public void IsWordBoundary_DetectsBoundaries(string text, int offset, bool expected)
    {
        Assert.Equal(expected, _tokenizer.IsWordBoundary(text, offset));
    }

    [Fact]
    public void EmptyInput_ProducesNoTokens()
    {
        Assert.Empty(_tokenizer.Tokenize(string.Empty));
        Assert.Equal(-1, _tokenizer.GetWordIndexAtOffset(string.Empty, 0));
        Assert.False(_tokenizer.IsWordBoundary(string.Empty, 0));
    }
}

public class DamerauLevenshteinTests
{
    [Theory]
    [InlineData("world", "world", 0)]
    [InlineData("wrold", "world", 1)]
    [InlineData("wor", "world", 2)]
    [InlineData("wprld", "world", 1)]
    public void Distance_HandlesInsertsSubstitutionsAndTranspositions(string a, string b, int expected)
    {
        Assert.Equal(expected, DamerauLevenshtein.Distance(a, b));
    }

    [Theory]
    [InlineData("café", "cafe")]
    [InlineData("αβγδ", "αβδγ")]
    [InlineData("日本語", "日本")]
    public void Distance_SupportsCharactersOutsideLatin1(string a, string b)
    {
        Assert.True(DamerauLevenshtein.Distance(a, b) > 0);
        Assert.Equal(0, DamerauLevenshtein.Distance(a, a));
    }

    [Fact]
    public void NormalizedDistance_IsBetweenZeroAndOne()
    {
        Assert.Equal(0.0, DamerauLevenshtein.NormalizedDistance("abc", "abc"));
        Assert.Equal(1.0, DamerauLevenshtein.NormalizedDistance("abc", "xyz"));
        Assert.Equal(0.0, DamerauLevenshtein.NormalizedDistance(string.Empty, string.Empty));
        Assert.Equal(1.0, DamerauLevenshtein.NormalizedDistance(string.Empty, "abc"));
    }
}
