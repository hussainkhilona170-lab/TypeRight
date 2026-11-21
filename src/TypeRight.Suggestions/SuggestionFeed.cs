using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Documents;
using TypeRight.Domain.Models;

namespace TypeRight.Suggestions
{
    /// <summary>
    /// Local suggestion feed. Groups alerts by category, supports accept,
    /// dismiss and acknowledge, and rebases on document changes. No network,
    /// no persistence, no user text stored.
    /// </summary>
    public sealed class SuggestionFeed
    {
        private readonly Dictionary<Guid, SuggestionAlert> _alerts =
            new Dictionary<Guid, SuggestionAlert>();
        private readonly List<FeedbackEvent> _feedback = new List<FeedbackEvent>();

        public IReadOnlyCollection<SuggestionAlert> Alerts => _alerts.Values.ToArray();
        public IReadOnlyList<FeedbackEvent> Feedback => _feedback.ToArray();
        public int Count => _alerts.Count;

        public void Clear()
        {
            _alerts.Clear();
            _feedback.Clear();
        }

        public void Add(SuggestionAlert alert)
        {
            if (alert == null) return;
            _alerts[alert.Id] = alert;
            _feedback.Add(new FeedbackEvent(FeedbackKind.AlertRendered, alert.Id, alert.Category));
        }

        /// <summary>Returns the highest-priority alert in a category.</summary>
        public SuggestionAlert Top(SuggestionCategory category)
        {
            return _alerts.Values
                .Where(a => a.Category == category)
                .OrderByDescending(a => a.Priority.Layer)
                .ThenByDescending(a => a.Priority.Value)
                .ThenByDescending(a => a.Confidence)
                .FirstOrDefault();
        }

        public IReadOnlyList<SuggestionAlert> ByCategory(SuggestionCategory category)
        {
            return _alerts.Values
                .Where(a => a.Category == category)
                .OrderByDescending(a => a.Priority.Value)
                .ToArray();
        }

        public IReadOnlyDictionary<SuggestionCategory, int> CountsByCategory()
        {
            return _alerts.Values
                .GroupBy(a => a.Category)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Rebases every alert across a revision. Alerts whose ranges were
        /// deleted are removed and recorded as acknowledged.
        /// </summary>
        public int Rebase(DocumentDelta delta)
        {
            if (delta == null) return 0;

            var next = new Dictionary<Guid, SuggestionAlert>();
            foreach (var alert in _alerts.Values)
            {
                var (rebased, status) = AlertRebase.Rebase(alert, delta);
                if (status == AlertRebaseStatus.Rebased && rebased != null)
                    next[rebased.Id] = rebased;
                else
                    _feedback.Add(new FeedbackEvent(FeedbackKind.AcknowledgeAlert, alert.Id, alert.Category));
            }
            _alerts.Clear();
            foreach (var pair in next) _alerts.Add(pair.Key, pair.Value);
            return _alerts.Count;
        }

        /// <summary>
        /// Accepts an alert and returns the document delta for the chosen
        /// alternative. Returns null when the alert or alternative is missing
        /// or the context length does not match the alert range.
        /// </summary>
        public DocumentDelta Accept(Guid alertId, int alternativeIndex, RevisionedDocument document)
        {
            if (!_alerts.TryGetValue(alertId, out var alert)) return null;
            if (alert.Transform == null || document == null) return null;
            if (alternativeIndex < 0 || alternativeIndex >= alert.Transform.Alternatives.Count) return null;
            if (alert.Range.Length != alert.Transform.Context.Length) return null;

            var alternative = alert.Transform.Alternatives[alternativeIndex];

            var builder = new DeltaBuilder().Retain(alert.Range.Start);
            foreach (var op in alternative.Ops) builder.Add(op);
            builder.Retain(document.Length - alert.Range.End);

            var delta = builder.Build(alert.Range.Revision);
            if (delta.BaseLength != document.Length) return null;

            Record(alertId, FeedbackKind.AcceptAlert);
            Record(alertId, FeedbackKind.SuggestionApplied);
            _alerts.Remove(alertId);
            return delta;
        }

        public void Dismiss(Guid alertId)
        {
            if (!_alerts.TryGetValue(alertId, out var alert)) return;
            Record(alertId, FeedbackKind.DismissAlert);
            _alerts.Remove(alertId);
        }

        public void Skip(Guid alertId)
        {
            if (!_alerts.TryGetValue(alertId, out var alert)) return;
            Record(alertId, FeedbackKind.SkipAlert);
        }

        public void AddToDictionary(Guid alertId)
        {
            if (!_alerts.TryGetValue(alertId, out var alert)) return;
            if (!alert.HasAddToDictionary) return;
            Record(alertId, FeedbackKind.AlertAddToDictionary);
        }

        private void Record(Guid alertId, FeedbackKind kind)
        {
            _alerts.TryGetValue(alertId, out var alert);
            _feedback.Add(new FeedbackEvent(kind, alertId,
                alert == null ? SuggestionCategory.Correctness : alert.Category));
        }

        /// <summary>Feedback counts by kind. Never includes text.</summary>
        public IReadOnlyDictionary<FeedbackKind, int> FeedbackCounts()
        {
            return _feedback.GroupBy(f => f.Kind).ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
