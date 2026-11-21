namespace TypeRight.Domain.Models;

/// <summary>
/// Input to the correction engine. Contains only the information needed for evaluation.
/// Does not send an entire sentence or document.
/// </summary>
public readonly struct CorrectionRequest
{
    /// <summary>
    /// The completed word that needs evaluation.
    /// </summary>
    public string CompletedWord { get; }

    /// <summary>
    /// Bounded adjacent-word context when necessary (max 2 words left/right).
    /// </summary>
    public IReadOnlyList<string> AdjacentLeft { get; }
    public IReadOnlyList<string> AdjacentRight { get; }

    /// <summary>
    /// Capitalization information about the completed word.
    /// </summary>
    public CapitalizationInfo Capitalization { get; }

    /// <summary>
    /// Language acceptance settings.
    /// </summary>
    public LanguageVariant LanguageVariant { get; }

    /// <summary>
    /// Vocabulary and rule version for deterministic processing.
    /// </summary>
    public VocabularyVersion VocabularyVersion { get; }

    /// <summary>
    /// Unique request identifier for cancellation tracking.
    /// </summary>
    public Guid RequestId { get; }

    /// <summary>
    /// Timestamp when the request was created.
    /// </summary>
    public DateTime RequestTime { get; }

    /// <summary>
    /// Creates a new CorrectionRequest.
    /// </summary>
    public CorrectionRequest(
        string completedWord,
        IReadOnlyList<string> adjacentLeft,
        IReadOnlyList<string> adjacentRight,
        CapitalizationInfo capitalization,
        LanguageVariant languageVariant,
        VocabularyVersion vocabularyVersion)
    {
        CompletedWord = completedWord ?? throw new ArgumentNullException(nameof(completedWord));
        AdjacentLeft = adjacentLeft ?? throw new ArgumentNullException(nameof(adjacentLeft));
        AdjacentRight = adjacentRight ?? throw new ArgumentNullException(nameof(adjacentRight));
        Capitalization = capitalization;
        LanguageVariant = languageVariant;
        VocabularyVersion = vocabularyVersion;
        RequestId = Guid.NewGuid();
        RequestTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a minimal request with no adjacent context.
    /// </summary>
    public static CorrectionRequest Minimal(string completedWord, CapitalizationInfo capitalization, LanguageVariant languageVariant, VocabularyVersion vocabularyVersion)
        => new(
            completedWord,
            Array.Empty<string>(),
            Array.Empty<string>(),
            capitalization,
            languageVariant,
            vocabularyVersion);
}

/// <summary>
/// Capitalization information about a word.
/// </summary>
public readonly struct CapitalizationInfo
{
    /// <summary>
    /// The original case of the word as typed.
    /// </summary>
    public string OriginalCase { get; }

    /// <summary>
    /// Whether the word is at the start of a sentence.
    /// </summary>
    public bool IsSentenceStart { get; }

    /// <summary>
    /// Whether the word is typed in all caps.
    /// </summary>
    public bool IsAllCaps { get; }

    /// <summary>
    /// Creates new CapitalizationInfo.
    /// </summary>
    public CapitalizationInfo(string originalCase, bool isSentenceStart, bool isAllCaps)
    {
        OriginalCase = originalCase;
        IsSentenceStart = isSentenceStart;
        IsAllCaps = isAllCaps;
    }

    /// <summary>
    /// Creates CapitalizationInfo from the original text.
    /// </summary>
    public static CapitalizationInfo FromOriginal(string originalText)
    {
        if (string.IsNullOrEmpty(originalText))
            return new CapitalizationInfo(originalText, false, false);

        bool isAllCaps = originalText.Equals(originalText.ToUpperInvariant(), StringComparison.Ordinal) &&
                         originalText.Any(c => char.IsLetter(c));
        bool isSentenceStart = originalText.Length > 0 && char.IsUpper(originalText[0]);

        return new CapitalizationInfo(originalText, isSentenceStart, isAllCaps);
    }
}

/// <summary>
/// Language variant acceptance settings.
/// </summary>
public enum LanguageVariant
{
    /// <summary>
    /// Auto-detect from user settings; accept both British and American variants.
    /// </summary>
    Auto,

    /// <summary>
    /// American English only.
    /// </summary>
    EnUs,

    /// <summary>
    /// British English only.
    /// </summary>
    EnGb
}

/// <summary>
/// Versioned vocabulary state for deterministic processing.
/// </summary>
public readonly struct VocabularyVersion
{
    /// <summary>
    /// Major version number.
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// Minor version number.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// Creates a new VocabularyVersion.
    /// </summary>
    public VocabularyVersion(int major, int minor)
    {
        Major = major;
        Minor = minor;
    }

    public override string ToString() => $"v{Major}.{Minor}";
}

/// <summary>
/// The action to take based on correction evaluation.
/// </summary>
public enum CorrectionAction
{
    /// <summary>
    /// Auto-correct the word.
    /// </summary>
    AutoCorrect,

    /// <summary>
    /// Show suggestions to the user.
    /// </summary>
    Suggest,

    /// <summary>
    /// Leave the word as-is.
    /// </summary>
    Preserve
}

/// <summary>
/// The result of a correction engine evaluation.
/// </summary>
public readonly struct CorrectionDecision
{
    /// <summary>
    /// The action to take.
    /// </summary>
    public CorrectionAction Action { get; }

    /// <summary>
    /// The corrected word, if action is AutoCorrect.
    /// </summary>
    public string? CorrectedWord { get; }

    /// <summary>
    /// Additional alternative suggestions, if action is Suggest.
    /// </summary>
    public IReadOnlyList<string>? Alternatives { get; }

    /// <summary>
    /// Confidence score 0.0-1.0.
    /// </summary>
    public float Confidence { get; }

    /// <summary>
    /// The reason category for the decision.
    /// </summary>
    public ReasonCategory Reason { get; }

    /// <summary>
    /// Rationale text explaining the decision.
    /// </summary>
    public string? Rationale { get; }

    /// <summary>
    /// Creates a new CorrectionDecision.
    /// </summary>
    public CorrectionDecision(
        CorrectionAction action,
        string? correctedWord,
        IReadOnlyList<string>? alternatives,
        float confidence,
        ReasonCategory reason,
        string? rationale)
    {
        Action = action;
        CorrectedWord = correctedWord;
        Alternatives = alternatives;
        Confidence = confidence;
        Reason = reason;
        Rationale = rationale;
    }

    /// <summary>
    /// Preserve the word as-is (no correction needed).
    /// </summary>
    public static CorrectionDecision Preserve(string? rationale = null)
        => new(CorrectionAction.Preserve, null, null, 0.0f, ReasonCategory.LowConfidence, rationale);

    /// <summary>
    /// Auto-correct with high confidence.
    /// </summary>
    public static CorrectionDecision AutoCorrect(string correctedWord, float confidence, string? rationale = null)
        => new(CorrectionAction.AutoCorrect, correctedWord, null, confidence, ReasonCategory.AutoCorrect, rationale);

    /// <summary>
    /// Show suggestions with medium confidence.
    /// </summary>
    public static CorrectionDecision Suggest(IReadOnlyList<string> alternatives, float confidence, string? rationale = null)
        => new(CorrectionAction.Suggest, null, alternatives, confidence, ReasonCategory.Suggest, rationale);

    /// <summary>
    /// Low confidence - leave the word alone.
    /// </summary>
    public static CorrectionDecision LowConfidencePreserve(string? rationale = null)
        => new(CorrectionAction.Preserve, null, null, 0.1f, ReasonCategory.LowConfidence, rationale);
}

/// <summary>
/// Category of reason for the correction decision.
/// </summary>
public enum ReasonCategory
{
    /// <summary>
    /// Word is valid vocabulary.
    /// </summary>
    ValidWord,

    /// <summary>
    /// High confidence automatic correction.
    /// </summary>
    AutoCorrect,

    /// <summary>
    /// Medium confidence - suggestions shown.
    /// </summary>
    Suggest,

    /// <summary>
    /// Low confidence - word preserved.
    /// </summary>
    LowConfidence,

    /// <summary>
    /// Word is ambiguous (e.g., form/from, their/there).
    /// </summary>
    Ambiguous,

    /// <summary>
    /// Regional spelling variant (british vs american).
    /// </summary>
    RegionalVariant,

    /// <summary>
    /// Personal name or technical term.
    /// </summary>
    ProperNameOrTechnical,

    /// <summary>
    /// Insufficient confidence to decide.
    /// </summary>
    InsufficientData
}