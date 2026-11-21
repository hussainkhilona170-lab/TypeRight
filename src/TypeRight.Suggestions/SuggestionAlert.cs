using System;
using System.Collections.Generic;
using TypeRight.Documents;
using TypeRight.Domain.Models;

namespace TypeRight.Suggestions
{
    /// <summary>
    /// A single alternative offered by a suggestion. The operation list has
    /// the same shape as the observed transformJson alternatives: a retain
    /// context plus insert/delete operations.
    /// </summary>
    public sealed class TransformAlternative
    {
        public IReadOnlyList<DeltaOp> Ops { get; }
        public string Label { get; }

        public TransformAlternative(IReadOnlyList<DeltaOp> ops, string label = null)
        {
            Ops = ops ?? Array.Empty<DeltaOp>();
            Label = label;
        }

        public string ApplyTo(string baseText)
        {
            return new DocumentDelta(Ops).Apply(baseText);
        }
    }

    /// <summary>
    /// The transform attached to an alert: the context range and the set of
    /// alternatives that can replace it.
    /// </summary>
    public sealed class AlertTransform
    {
        public AlertRange Context { get; }
        public IReadOnlyList<TransformAlternative> Alternatives { get; }

        public AlertTransform(AlertRange context, IReadOnlyList<TransformAlternative> alternatives)
        {
            Context = context;
            Alternatives = alternatives ?? Array.Empty<TransformAlternative>();
        }
    }

    /// <summary>
    /// A range captured at a document revision. Equivalent to the observed
    /// SuperAlertRanges / ReadonlyAlertRanges model.
    /// </summary>
    public readonly struct AlertRange
    {
        public int Start { get; }
        public int End { get; }
        public int Revision { get; }

        public AlertRange(int start, int end, int revision)
        {
            Start = start;
            End = end;
            Revision = revision;
        }

        public int Length => End - Start;

        public AlertRange? Rebase(DocumentDelta delta)
        {
            var range = new TextRange(Start, End, Revision).Rebase(delta);
            return range.HasValue
                ? new AlertRange(range.Value.Start, range.Value.End, range.Value.Revision)
                : (AlertRange?)null;
        }

        public override string ToString() => $"[{Start},{End})@r{Revision}";
    }

    /// <summary>
    /// Priority metadata: layer, tier name/rank and value, mirroring the
    /// observed priority object on server alerts.
    /// </summary>
    public readonly struct AlertPriority
    {
        public int Layer { get; }
        public string TierName { get; }
        public int TierRank { get; }
        public float Value { get; }

        public AlertPriority(int layer, string tierName, int tierRank, float value)
        {
            Layer = layer;
            TierName = tierName;
            TierRank = tierRank;
            Value = value;
        }

        public static AlertPriority Normal => new(0, "normal", 0, 1.0f);
    }

    /// <summary>
    /// A nested alert. Grammarly flattens subalerts recursively; TypeRight
    /// keeps them as a flat list with parent references preserved by ordering.
    /// </summary>
    public sealed class SubAlert
    {
        public Guid Id { get; }
        public SuggestionCategory Category { get; }
        public AlertRange Range { get; }
        public AlertTransform Transform { get; }
        public string Title { get; }
        public bool HasAddToDictionary { get; }

        public SubAlert(Guid id, SuggestionCategory category, AlertRange range,
                        AlertTransform transform, string title, bool hasAddToDictionary = false)
        {
            Id = id;
            Category = category;
            Range = range;
            Transform = transform;
            Title = title;
            HasAddToDictionary = hasAddToDictionary;
        }
    }

    /// <summary>
    /// The primary user-visible suggestion. Aggregates subalerts and carries
    /// the same enrichment fields observed on SuperAlert/RenderableAlert.
    /// </summary>
    public sealed class SuggestionAlert
    {
        public Guid Id { get; }
        public string Title { get; }
        public SuggestionCategory Category { get; }
        public AlertRange Range { get; }
        public AlertTransform Transform { get; }
        public float Confidence { get; }
        public string Rationale { get; }
        public AlertPriority Priority { get; }
        public bool HasAddToDictionary { get; }
        public string DetectedLanguage { get; }
        public IReadOnlyList<SubAlert> SubAlerts { get; }

        public SuggestionAlert(
            Guid id,
            string title,
            SuggestionCategory category,
            AlertRange range,
            AlertTransform transform,
            float confidence,
            string rationale,
            AlertPriority priority,
            bool hasAddToDictionary = false,
            string detectedLanguage = null,
            IReadOnlyList<SubAlert> subAlerts = null)
        {
            Id = id;
            Title = title;
            Category = category;
            Range = range;
            Transform = transform;
            Confidence = confidence;
            Rationale = rationale;
            Priority = priority;
            HasAddToDictionary = hasAddToDictionary;
            DetectedLanguage = detectedLanguage;
            SubAlerts = subAlerts ?? Array.Empty<SubAlert>();
        }
    }

    /// <summary>
    /// Feedback taxonomy observed in the client. TypeRight records these
    /// locally in memory only - there is no telemetry upload.
    /// </summary>
    public enum FeedbackKind
    {
        AcceptAlert,
        AcknowledgeAlert,
        DismissAlert,
        SkipAlert,
        AlertAddToDictionary,
        AlertRendered,
        AlertViewed,
        EmotionFeedback,
        VbarCopyFeedback,
        VbarGenerateFeedback,
        VbarInsertFeedback,
        VbarSatisfactionFeedback,
        WritingExpertFeedback,
        SuggestionApplied,
        GeneratedInsertApplied
    }

    /// <summary>
    /// A local feedback record. Contains identifiers and counts, not text.
    /// </summary>
    public readonly struct FeedbackEvent
    {
        public FeedbackKind Kind { get; }
        public Guid AlertId { get; }
        public SuggestionCategory Category { get; }
        public DateTimeOffset RecordedAt { get; }

        public FeedbackEvent(FeedbackKind kind, Guid alertId, SuggestionCategory category)
        {
            Kind = kind;
            AlertId = alertId;
            Category = category;
            RecordedAt = DateTimeOffset.UtcNow;
        }
    }
}
