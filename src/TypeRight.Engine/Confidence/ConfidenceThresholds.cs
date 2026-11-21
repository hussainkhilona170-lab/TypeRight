namespace TypeRight.Engine.Confidence;

/// <summary>
/// Shared confidence thresholds used by both the confidence calculator and the
/// decision maker's reason refinement.
/// </summary>
public static class ConfidenceThresholds
{
    /// <summary>Score at or above which automatic correction is allowed.</summary>
    public const float High = 0.71f;

    /// <summary>Score at or above which suggestions are shown.</summary>
    public const float Medium = 0.65f;

    /// <summary>Score at or above which a correction is at least considered.</summary>
    public const float Low = 0.40f;
}
