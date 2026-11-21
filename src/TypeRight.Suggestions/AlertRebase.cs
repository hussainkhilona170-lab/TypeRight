using System;
using System.Collections.Generic;
using TypeRight.Documents;

namespace TypeRight.Suggestions
{
    public enum AlertRebaseStatus
    {
        Rebased,
        Deleted,
        StaleRevision
    }

    /// <summary>
    /// Rebases alerts and their nested subalerts across document revisions so
    /// a suggestion survives continued typing. Mirrors AlertRebase and
    /// TransformAlertRanges in the reference client.
    /// </summary>
    public static class AlertRebase
    {
        public static (SuggestionAlert Alert, AlertRebaseStatus Status) Rebase(
            SuggestionAlert alert, DocumentDelta delta)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));
            if (delta == null) throw new ArgumentNullException(nameof(delta));
            if (alert.Range.Revision != delta.Revision)
                return (null, AlertRebaseStatus.StaleRevision);

            var newRange = alert.Range.Rebase(delta);
            if (!newRange.HasValue)
                return (null, AlertRebaseStatus.Deleted);

            var newContext = alert.Transform?.Context.Rebase(delta);
            var transform = alert.Transform == null
                ? null
                : new AlertTransform(
                    newContext ?? newRange.Value,
                    alert.Transform.Alternatives);

            var subAlerts = new List<SubAlert>();
            foreach (var sub in alert.SubAlerts)
            {
                if (sub.Range.Revision != delta.Revision) continue;
                var subRange = sub.Range.Rebase(delta);
                if (!subRange.HasValue) continue;

                var subContext = sub.Transform?.Context.Rebase(delta);
                var subTransform = sub.Transform == null
                    ? null
                    : new AlertTransform(
                        subContext ?? subRange.Value,
                        sub.Transform.Alternatives);

                subAlerts.Add(new SubAlert(sub.Id, sub.Category, subRange.Value,
                    subTransform, sub.Title, sub.HasAddToDictionary));
            }

            var rebased = new SuggestionAlert(
                alert.Id,
                alert.Title,
                alert.Category,
                newRange.Value,
                transform,
                alert.Confidence,
                alert.Rationale,
                alert.Priority,
                alert.HasAddToDictionary,
                alert.DetectedLanguage,
                subAlerts);

            return (rebased, AlertRebaseStatus.Rebased);
        }
    }
}
