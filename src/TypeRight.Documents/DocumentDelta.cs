using System;
using System.Collections.Generic;
using System.Text;

namespace TypeRight.Documents
{
    public enum DeltaOpKind
    {
        Insert,
        Retain,
        Delete
    }

    /// <summary>
    /// A single Quill-style document operation. Inserts produce new text;
    /// retains and deletes consume text in the base document.
    /// </summary>
    public readonly struct DeltaOp
    {
        public DeltaOpKind Kind { get; }
        public int Count { get; }
        public string Text { get; }

        private DeltaOp(DeltaOpKind kind, int count, string text)
        {
            Kind = kind;
            Count = count;
            Text = text;
        }

        public static DeltaOp Insert(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return new DeltaOp(DeltaOpKind.Insert, text.Length, text);
        }

        public static DeltaOp Retain(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            return new DeltaOp(DeltaOpKind.Retain, count, null);
        }

        public static DeltaOp Delete(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            return new DeltaOp(DeltaOpKind.Delete, count, null);
        }

        /// <summary>Characters consumed from the base document.</summary>
        public int BaseLength => Kind == DeltaOpKind.Insert ? 0 : Count;

        /// <summary>Characters produced in the resulting document.</summary>
        public int TargetLength => Kind == DeltaOpKind.Delete ? 0 : Count;

        public override string ToString()
        {
            switch (Kind)
            {
                case DeltaOpKind.Insert: return "insert(" + Count + ")";
                case DeltaOpKind.Retain: return "retain(" + Count + ")";
                default: return "delete(" + Count + ")";
            }
        }
    }

    /// <summary>
    /// An ordered set of operations describing a document revision. Modelled on
    /// the Quill Delta insert/retain/delete shape observed in the client.
    /// </summary>
    public sealed class DocumentDelta
    {
        public IReadOnlyList<DeltaOp> Ops { get; }

        /// <summary>Revision this delta was computed against.</summary>
        public int Revision { get; }

        public int BaseLength
        {
            get
            {
                int total = 0;
                foreach (var op in Ops) total += op.BaseLength;
                return total;
            }
        }

        public int TargetLength
        {
            get
            {
                int total = 0;
                foreach (var op in Ops) total += op.TargetLength;
                return total;
            }
        }

        public DocumentDelta(IReadOnlyList<DeltaOp> ops, int revision = 0)
        {
            if (ops == null) throw new ArgumentNullException(nameof(ops));
            Ops = ops;
            Revision = revision;
        }

        /// <summary>
        /// Applies the delta to the supplied base text. Returns null when the
        /// base text does not match the delta's expected length (a stale apply).
        /// </summary>
        public string Apply(string baseText)
        {
            if (baseText == null) throw new ArgumentNullException(nameof(baseText));
            if (baseText.Length != BaseLength) return null;

            var result = new StringBuilder(TargetLength);
            int position = 0;

            foreach (var op in Ops)
            {
                switch (op.Kind)
                {
                    case DeltaOpKind.Insert:
                        result.Append(op.Text);
                        break;

                    case DeltaOpKind.Retain:
                        result.Append(baseText, position, op.Count);
                        position += op.Count;
                        break;

                    case DeltaOpKind.Delete:
                        position += op.Count;
                        break;
                }
            }

            if (position != baseText.Length) return null;
            return result.ToString();
        }

        /// <summary>
        /// Maps an offset in the base document to the corresponding offset in
        /// the resulting document. Returns -1 if the text at that offset was
        /// deleted.
        /// </summary>
        public int RebaseOffset(int baseOffset)
        {
            if (baseOffset < 0 || baseOffset > BaseLength) return -1;

            int basePos = 0;
            int targetPos = 0;

            foreach (var op in Ops)
            {
                if (op.Kind == DeltaOpKind.Insert)
                {
                    // An insert at a boundary shifts a caret at that boundary.
                    targetPos += op.Count;
                    continue;
                }

                int opEnd = basePos + op.Count;

                if (baseOffset >= basePos && baseOffset <= opEnd)
                {
                    if (op.Kind == DeltaOpKind.Delete)
                    {
                        // The far edge of a deletion still maps to a valid
                        // position; only offsets strictly inside are lost.
                        return baseOffset == opEnd ? targetPos : -1;
                    }
                    return targetPos + (baseOffset - basePos);
                }

                basePos = opEnd;
                if (op.Kind == DeltaOpKind.Retain) targetPos += op.Count;
            }

            return targetPos;
        }

        /// <summary>
        /// True when the delta leaves the retained region length unchanged -
        /// the local equivalent of IsSubAlertContextLengthPreserved.
        /// </summary>
        public bool PreservesRetainedLength(int otherBaseLength)
        {
            int retained = 0;
            foreach (var op in Ops)
                if (op.Kind == DeltaOpKind.Retain) retained += op.Count;
            return retained == otherBaseLength;
        }
    }

    /// <summary>
    /// Fluent builder for document deltas.
    /// </summary>
    public sealed class DeltaBuilder
    {
        private readonly List<DeltaOp> _ops = new List<DeltaOp>();

        public DeltaBuilder Retain(int count)
        {
            if (count > 0) _ops.Add(DeltaOp.Retain(count));
            return this;
        }

        public DeltaBuilder Delete(int count)
        {
            if (count > 0) _ops.Add(DeltaOp.Delete(count));
            return this;
        }

        public DeltaBuilder Insert(string text)
        {
            if (!string.IsNullOrEmpty(text)) _ops.Add(DeltaOp.Insert(text));
            return this;
        }

        public DeltaBuilder Add(DeltaOp op)
        {
            _ops.Add(op);
            return this;
        }

        public DocumentDelta Build(int revision = 0) => new DocumentDelta(_ops.ToArray(), revision);
    }

    /// <summary>
    /// Produces a minimal, predictable delta between two strings by isolating
    /// the common prefix and suffix. Correction operations are local word
    /// replacements, so this yields a compact insert/retain/delete sequence
    /// without the cost of a full diff-matrix.
    /// </summary>
    public static class TextDiffer
    {
        public static DocumentDelta Compute(string oldText, string newText)
        {
            if (oldText == null) throw new ArgumentNullException(nameof(oldText));
            if (newText == null) throw new ArgumentNullException(nameof(newText));

            int prefix = 0;
            int maxPrefix = Math.Min(oldText.Length, newText.Length);
            while (prefix < maxPrefix && oldText[prefix] == newText[prefix]) prefix++;

            int suffix = 0;
            int maxSuffix = maxPrefix - prefix;
            while (suffix < maxSuffix &&
                   oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
                suffix++;

            int oldMiddle = oldText.Length - prefix - suffix;
            int newMiddle = newText.Length - prefix - suffix;

            return new DeltaBuilder()
                .Retain(prefix)
                .Delete(oldMiddle)
                .Insert(newText.Substring(prefix, newMiddle))
                .Retain(suffix)
                .Build();
        }
    }
}
