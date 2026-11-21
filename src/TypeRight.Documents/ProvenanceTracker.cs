using System;
using System.Collections.Generic;

namespace TypeRight.Documents
{
    /// <summary>
    /// Origin of a span of text. Modelled on the observed authorship
    /// distinction between typed keystrokes and pasted/remote/generated text.
    /// TypeRight records metadata only - never the text itself - and tracking
    /// is disabled by default to honour the no-keystroke-logging guarantee.
    /// </summary>
    public enum AuthorshipKind
    {
        Unknown,
        Typed,
        Pasted,
        Generated,
        Remote
    }

    public readonly struct ProvenanceSpan
    {
        public int Start { get; }
        public int Length { get; }
        public AuthorshipKind Kind { get; }
        public DateTimeOffset RecordedAt { get; }

        public ProvenanceSpan(int start, int length, AuthorshipKind kind, DateTimeOffset recordedAt)
        {
            Start = start;
            Length = length;
            Kind = kind;
            RecordedAt = recordedAt;
        }

        public override string ToString() =>
            $"{Kind}@{Start}+{Length}";
    }

    /// <summary>
    /// In-memory provenance metadata for the current document only. No text,
    /// no disk writes, no network. Spans are rebased when the document changes
    /// so offsets remain meaningful without recording content.
    /// </summary>
    public sealed class ProvenanceTracker
    {
        private readonly List<ProvenanceSpan> _spans = new List<ProvenanceSpan>();

        public bool IsEnabled { get; set; } = false;
        public int SpanCount => _spans.Count;

        public void Reset() => _spans.Clear();

        public void Record(int start, int length, AuthorshipKind kind)
        {
            if (!IsEnabled) return;
            if (start < 0 || length <= 0) return;
            _spans.Add(new ProvenanceSpan(start, length, kind, DateTimeOffset.UtcNow));
        }

        /// <summary>
        /// Rebases recorded spans across a document delta. Spans whose text was
        /// removed are dropped; their content is never inspected.
        /// </summary>
        public void Rebase(DocumentDelta delta)
        {
            if (delta == null || _spans.Count == 0) return;

            var next = new List<ProvenanceSpan>(_spans.Count);
            foreach (var span in _spans)
            {
                int start = delta.RebaseOffset(span.Start);
                int end = delta.RebaseOffset(span.Start + span.Length);
                if (start < 0 || end <= start) continue;
                next.Add(new ProvenanceSpan(start, end - start, span.Kind, span.RecordedAt));
            }
            _spans.Clear();
            _spans.AddRange(next);
        }

        /// <summary>
        /// Aggregates counts by origin. Contains no text and no offsets.
        /// </summary>
        public IReadOnlyDictionary<AuthorshipKind, int> Summary()
        {
            var counts = new Dictionary<AuthorshipKind, int>();
            foreach (var span in _spans)
            {
                if (!counts.ContainsKey(span.Kind)) counts[span.Kind] = 0;
                counts[span.Kind] += span.Length;
            }
            return counts;
        }

        public int TypedLength => LengthFor(AuthorshipKind.Typed);
        public int NonTypedLength
        {
            get
            {
                int total = 0;
                foreach (var span in _spans)
                    if (span.Kind != AuthorshipKind.Typed) total += span.Length;
                return total;
            }
        }

        private int LengthFor(AuthorshipKind kind)
        {
            int total = 0;
            foreach (var span in _spans)
                if (span.Kind == kind) total += span.Length;
            return total;
        }
    }
}
