using System;
using TypeRight.Domain.Models;
using Xunit;

namespace TypeRight.Domain.Tests;

public class CorrectionRequestTests
{
    private static readonly VocabularyVersion Version = new(1, 0);

    [Fact]
    public void Minimal_HasNoContext_AndUniqueIdentity()
    {
        var request = CorrectionRequest.Minimal(
            "wrold",
            CapitalizationInfo.FromOriginal("wrold"),
            LanguageVariant.Auto,
            Version);

        Assert.Equal("wrold", request.CompletedWord);
        Assert.Empty(request.AdjacentLeft);
        Assert.Empty(request.AdjacentRight);
        Assert.NotEqual(Guid.Empty, request.RequestId);
        Assert.InRange(request.RequestTime, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Constructor_RejectsNullInputs()
    {
        var capitalization = CapitalizationInfo.FromOriginal("word");

        Assert.Throws<ArgumentNullException>(() => new CorrectionRequest(
            null!, Array.Empty<string>(), Array.Empty<string>(), capitalization, LanguageVariant.Auto, Version));
        Assert.Throws<ArgumentNullException>(() => new CorrectionRequest(
            "word", null!, Array.Empty<string>(), capitalization, LanguageVariant.Auto, Version));
        Assert.Throws<ArgumentNullException>(() => new CorrectionRequest(
            "word", Array.Empty<string>(), null!, capitalization, LanguageVariant.Auto, Version));
    }

    [Theory]
    [InlineData("hello", false, false)]
    [InlineData("Hello", true, false)]
    [InlineData("HELLO", true, true)]
    [InlineData("123", false, false)]
    [InlineData("iPhone", false, false)]
    public void FromOriginal_DetectsCapitalization(string text, bool sentenceStart, bool allCaps)
    {
        var info = CapitalizationInfo.FromOriginal(text);

        Assert.Equal(text, info.OriginalCase);
        Assert.Equal(sentenceStart, info.IsSentenceStart);
        Assert.Equal(allCaps, info.IsAllCaps);
    }

    [Fact]
    public void FromOriginal_EmptyText_IsNotCapitalized()
    {
        var info = CapitalizationInfo.FromOriginal(string.Empty);

        Assert.False(info.IsSentenceStart);
        Assert.False(info.IsAllCaps);
    }

    [Fact]
    public void VocabularyVersion_FormatsAsString()
    {
        Assert.Equal("v2.3", new VocabularyVersion(2, 3).ToString());
    }
}

public class CorrectionDecisionTests
{
    [Fact]
    public void Factories_SetExpectedActionReasonAndPayload()
    {
        var preserve = CorrectionDecision.Preserve("valid");
        Assert.Equal(CorrectionAction.Preserve, preserve.Action);
        Assert.Equal(ReasonCategory.LowConfidence, preserve.Reason);
        Assert.Null(preserve.CorrectedWord);
        Assert.Null(preserve.Alternatives);

        var autoCorrect = CorrectionDecision.AutoCorrect("world", 0.95f);
        Assert.Equal(CorrectionAction.AutoCorrect, autoCorrect.Action);
        Assert.Equal(ReasonCategory.AutoCorrect, autoCorrect.Reason);
        Assert.Equal("world", autoCorrect.CorrectedWord);
        Assert.Equal(0.95f, autoCorrect.Confidence);

        var suggest = CorrectionDecision.Suggest(new[] { "world", "word" }, 0.7f);
        Assert.Equal(CorrectionAction.Suggest, suggest.Action);
        Assert.Equal(ReasonCategory.Suggest, suggest.Reason);
        Assert.Equal(new[] { "world", "word" }, suggest.Alternatives);
        Assert.Null(suggest.CorrectedWord);

        var lowConfidence = CorrectionDecision.LowConfidencePreserve();
        Assert.Equal(CorrectionAction.Preserve, lowConfidence.Action);
        Assert.Equal(ReasonCategory.LowConfidence, lowConfidence.Reason);
        Assert.Equal(0.1f, lowConfidence.Confidence);
    }
}

public class SuggestionCategoryTests
{
    [Theory]
    [InlineData(ReasonCategory.AutoCorrect, SuggestionCategory.Correctness)]
    [InlineData(ReasonCategory.ValidWord, SuggestionCategory.Correctness)]
    [InlineData(ReasonCategory.Ambiguous, SuggestionCategory.Clarity)]
    [InlineData(ReasonCategory.RegionalVariant, SuggestionCategory.Delivery)]
    [InlineData(ReasonCategory.ProperNameOrTechnical, SuggestionCategory.StyleGuide)]
    [InlineData(ReasonCategory.InsufficientData, SuggestionCategory.Correctness)]
    public void FromReason_MapsEngineCategories(ReasonCategory reason, SuggestionCategory expected)
    {
        Assert.Equal(expected, SuggestionCategoryMapper.FromReason(reason));
    }

    [Fact]
    public void DisplayName_UsesUserFacingLabels()
    {
        Assert.Equal("Correctness", SuggestionCategoryMapper.DisplayName(SuggestionCategory.Correctness));
        Assert.Equal("Clarity", SuggestionCategoryMapper.DisplayName(SuggestionCategory.Clarity));
        Assert.Equal("Delivery", SuggestionCategoryMapper.DisplayName(SuggestionCategory.Delivery));
        Assert.Equal("Style guide", SuggestionCategoryMapper.DisplayName(SuggestionCategory.StyleGuide));
        Assert.Equal("AI Studio", SuggestionCategoryMapper.DisplayName(SuggestionCategory.AiStudio));
    }
}
