using System;

namespace TypeRight.Documents
{
    /// <summary>
    /// A half-open text range with the revision it was captured against.
    /// </summary>
    public readonly struct TextRange
    {
        public int Start { get; }
        public int End { get; }
        public int Revision { get; }

        public int Length => End - Start;

        public TextRange(int start, int end, int revision)
        {
            Start = start;
            End = end;
            Revision = revision;
        }

        public bool Contains(int offset) => offset >= Start && offset < End;

        /// <summary>
        /// Rebases the range across a delta. Returns null when the whole range
        /// was deleted or the delta came from a different revision.
        /// </summary>
        public TextRange? Rebase(DocumentDelta delta)
        {
            if (delta == null) return null;
            if (Revision != delta.Revision) return null;

            int start = delta.RebaseOffsetClamped(Start);
            int end = delta.RebaseOffsetClamped(End);
            if (start < 0 || end < 0 || end <= start) return null;
            if (!delta.RetainsAnyInRange(Start, End)) return null;
            return new TextRange(start, end, Revision + 1);
        }

        public override string ToString() => $"[{Start},{End})@r{Revision}";
    }

    public enum ApplyOutcome
    {
        Applied,
        StaleRevision,
        DeltaTooShort,
        CorruptDelta
    }

    /// <summary>
    /// A local revisioned document. Carries the same concepts observed in the
    /// reference client (revision numbers, deltas, range rebasing, apply safety
    /// checks) but never leaves the process and stores no history beyond the
    /// current text.
    /// </summary>
    public sealed class RevisionedDocument
    {
        private readonly int _revision;
        private readonly string _text;

        public int Revision => _revision;
        public string Text => _text;
        public int Length => _text.Length;

        public RevisionedDocument(string text) : this(text, 0)
        {
        }

        private RevisionedDocument(string text, int revision)
        {
            _text = text ?? string.Empty;
            _revision = revision;
        }

        /// <summary>
        /// Applies a delta only if the caller's expected revision still matches
        /// and the delta is structurally valid for this document.
        /// </summary>
        public ApplyOutcome TryApply(DocumentDelta delta, int expectedRevision, out RevisionedDocument result)
        {
            result = null;
            if (delta == null) return ApplyOutcome.CorruptDelta;
            if (expectedRevision != _revision) return ApplyOutcome.StaleRevision;
            if (delta.Revision != expectedRevision) return ApplyOutcome.StaleRevision;
            if (!IsStructurallyValid(delta, _text.Length)) return ApplyOutcome.CorruptDelta;

            string next = delta.Apply(_text);
            if (next == null) return ApplyOutcome.DeltaTooShort;

            result = new RevisionedDocument(next, _revision + 1);
            return ApplyOutcome.Applied;
        }

        private static bool IsStructurallyValid(DocumentDelta delta, int documentLength)
        {
            int position = 0;
            foreach (var op in delta.Ops)
            {
                if (op.Count < 0) return false;
                switch (op.Kind)
                {
                    case DeltaOpKind.Insert:
                        if (op.Text == null || op.Text.Length != op.Count) return false;
                        break;

                    case DeltaOpKind.Retain:
                    case DeltaOpKind.Delete:
                        if (position + op.Count > documentLength) return false;
                        position += op.Count;
                        break;

                    default:
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Builds an alert-safe replace delta for a word range. The retained
        /// prefix and suffix are preserved exactly; refusal happens when the
        /// range is outside the document or the replacement is empty.
        /// </summary>
        public DocumentDelta BuildReplacement(TextRange range, string replacement, int expectedRevision)
        {
            if (range.Revision != expectedRevision || expectedRevision != _revision) return null;
            if (range.Start < 0 || range.End > _text.Length || range.Length <= 0) return null;
            if (string.IsNullOrEmpty(replacement)) return null;

            return new DeltaBuilder()
                .Retain(range.Start)
                .Delete(range.Length)
                .Insert(replacement)
                .Retain(_text.Length - range.End)
                .Build(expectedRevision);
        }

        /// <summary>
        /// Safety check: the delta must retain the exact prefix and suffix around
        /// the range and consume exactly the range length between them. Aggregate
        /// lengths alone are not enough - edits outside the range are rejected.
        /// </summary>
        public bool IsReplacementSafe(DocumentDelta delta, TextRange range)
        {
            if (delta == null) return false;
            if (delta.Revision != _revision || range.Revision != _revision) return false;
            if (range.Start < 0 || range.End > _text.Length || range.Length <= 0) return false;
            if (delta.BaseLength != _text.Length) return false;

            int index = 0;
            if (range.Start > 0)
            {
                if (index >= delta.Ops.Count) return false;
                var prefix = delta.Ops[index];
                if (prefix.Kind != DeltaOpKind.Retain || prefix.Count != range.Start) return false;
                index++;
            }

            int consumed = 0;
            while (index < delta.Ops.Count)
            {
                var op = delta.Ops[index];
                if (op.Kind == DeltaOpKind.Retain) break;
                if (op.Count < 0) return false;
                if (op.Kind == DeltaOpKind.Delete) consumed += op.Count;
                else if (op.Kind != DeltaOpKind.Insert) return false;
                if (consumed > range.Length) return false;
                index++;
            }
            if (consumed != range.Length) return false;

            int trailing = _text.Length - range.End;
            if (trailing > 0)
            {
                if (index >= delta.Ops.Count) return false;
                var suffix = delta.Ops[index];
                if (suffix.Kind != DeltaOpKind.Retain || suffix.Count != trailing) return false;
                index++;
            }

            return index == delta.Ops.Count;
        }
    }
}
