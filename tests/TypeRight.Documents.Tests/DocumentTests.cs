using System;
using TypeRight.Documents;
using Xunit;

namespace TypeRight.Documents.Tests
{
    public class DocumentDeltaTests
    {
        [Fact]
        public void AppliesInsertRetainDelete()
        {
            var delta = new DeltaBuilder()
                .Retain(6)
                .Delete(2)
                .Insert("XYZ")
                .Retain(6)
                .Build();

            Assert.Equal(14, delta.BaseLength);
            Assert.Equal(15, delta.TargetLength);
            Assert.Equal("hello XYZ world", delta.Apply("hello ab world"));
        }

        [Fact]
        public void RejectsStaleBaseText()
        {
            var delta = new DeltaBuilder().Retain(5).Delete(2).Insert("X").Build();
            Assert.Null(delta.Apply("short"));
        }

        [Fact]
        public void TextDifferProducesCompactReplacement()
        {
            var delta = TextDiffer.Compute("hello wrold", "hello world");
            Assert.Equal("hello world", delta.Apply("hello wrold"));
            Assert.Equal(7, delta.Ops[0].Count);
            Assert.Equal(DeltaOpKind.Delete, delta.Ops[1].Kind);
            Assert.Equal(DeltaOpKind.Insert, delta.Ops[2].Kind);
        }

        [Fact]
        public void RebaseOffsetMapsAcrossInsertAndDelete()
        {
            var delta = new DeltaBuilder()
                .Retain(5)
                .Delete(2)
                .Insert("XYZ")
                .Retain(4)
                .Build();

            Assert.Equal(0, delta.RebaseOffset(0));
            Assert.Equal(5, delta.RebaseOffset(5));
            Assert.Equal(-1, delta.RebaseOffset(6));
            Assert.Equal(10, delta.RebaseOffset(9));
            Assert.Equal(12, delta.RebaseOffset(11));
        }

        [Fact]
        public void RangeRebaseDropsDeletedRange()
        {
            var delta = new DeltaBuilder().Retain(2).Delete(3).Retain(5).Build(0);
            var range = new TextRange(3, 5, 0);
            Assert.Null(range.Rebase(delta));

            var survivor = new TextRange(6, 8, 0).Rebase(delta);
            Assert.True(survivor.HasValue);
            Assert.Equal(3, survivor.Value.Start);
            Assert.Equal(5, survivor.Value.End);
            Assert.Equal(1, survivor.Value.Revision);
        }

        [Fact]
        public void RebaseOffsetIncludesInsertAtRetainBoundary()
        {
            var delta = new DeltaBuilder().Retain(5).Insert("XYZ").Retain(4).Build();

            Assert.Equal(0, delta.RebaseOffset(0));
            Assert.Equal(8, delta.RebaseOffset(5));
            Assert.Equal(12, delta.RebaseOffset(9));
        }

        [Fact]
        public void RebaseOffsetIncludesInsertAfterDeletionFarEdge()
        {
            var delta = new DeltaBuilder().Retain(2).Delete(3).Insert("XY").Retain(4).Build();

            Assert.Equal(-1, delta.RebaseOffset(4));
            Assert.Equal(4, delta.RebaseOffset(5));
            Assert.Equal(5, delta.RebaseOffset(6));
        }

        [Fact]
        public void RebaseOffsetIgnoresInsertBeforeQueriedOffset()
        {
            var delta = new DeltaBuilder().Retain(3).Insert("ab").Retain(8).Build();

            Assert.Equal(0, delta.RebaseOffset(0));
            Assert.Equal(5, delta.RebaseOffset(3));
            Assert.Equal(7, delta.RebaseOffset(5));
        }

        [Fact]
        public void TextDifferCarriesRevision()
        {
            Assert.Equal(0, TextDiffer.Compute("a", "b").Revision);
            var delta = TextDiffer.Compute("hello wrold", "hello world", 7);
            Assert.Equal(7, delta.Revision);
            Assert.Equal("hello world", delta.Apply("hello wrold"));
        }

        [Fact]
        public void RangeRebaseShrinksPartialDeletion()
        {
            var delta = new DeltaBuilder().Retain(2).Delete(3).Retain(5).Build(0);

            var leading = new TextRange(1, 5, 0).Rebase(delta);
            Assert.True(leading.HasValue);
            Assert.Equal(1, leading.Value.Start);
            Assert.Equal(2, leading.Value.End);
            Assert.Equal(1, leading.Value.Revision);

            var trailing = new TextRange(2, 7, 0).Rebase(delta);
            Assert.True(trailing.HasValue);
            Assert.Equal(2, trailing.Value.Start);
            Assert.Equal(4, trailing.Value.End);
        }

        [Fact]
        public void RangeRebaseDropsWholeDeletionWithFollowingInsert()
        {
            var delta = new DeltaBuilder().Retain(2).Delete(3).Insert("XY").Retain(4).Build(0);
            Assert.Null(new TextRange(2, 5, 0).Rebase(delta));
        }
    }

    public class RevisionedDocumentTests
    {
        [Fact]
        public void AppliesOnlyAtExpectedRevision()
        {
            var doc = new RevisionedDocument("hello world");
            var delta = new DeltaBuilder().Retain(6).Delete(5).Insert("there").Build(0);

            Assert.Equal(ApplyOutcome.Applied, doc.TryApply(delta, 0, out var next));
            Assert.Equal("hello there", next.Text);
            Assert.Equal(1, next.Revision);
            Assert.Equal(ApplyOutcome.StaleRevision, doc.TryApply(delta, 4, out _));
        }

        [Fact]
        public void BuildReplacementPreservesContext()
        {
            var doc = new RevisionedDocument("hello wrold today");
            var range = new TextRange(6, 11, 0);
            var delta = doc.BuildReplacement(range, "world", 0);

            Assert.NotNull(delta);
            Assert.True(doc.IsReplacementSafe(delta, range));
            Assert.Equal("hello world today", delta.Apply(doc.Text));
        }

        [Fact]
        public void RefusesOutOfRangeAndEmptyReplacement()
        {
            var doc = new RevisionedDocument("hello");
            Assert.Null(doc.BuildReplacement(new TextRange(2, 99, 0), "x", 0));
            Assert.Null(doc.BuildReplacement(new TextRange(1, 3, 0), "", 0));
            Assert.Null(doc.BuildReplacement(new TextRange(1, 3, 1), "x", 0));
        }

        [Fact]
        public void TryApplyRejectsDeltaFromAnotherRevision()
        {
            var doc = new RevisionedDocument("hello");
            var delta = new DeltaBuilder().Retain(5).Build(3);
            Assert.Equal(ApplyOutcome.StaleRevision, doc.TryApply(delta, 0, out _));
        }

        [Fact]
        public void TryApplyDistinguishesCorruptDeltasFromShortDeltas()
        {
            var doc = new RevisionedDocument("hello");

            var malformed = new DocumentDelta(new[] { default(DeltaOp) }, 0);
            Assert.Equal(ApplyOutcome.CorruptDelta, doc.TryApply(malformed, 0, out _));

            var runsPastEnd = new DeltaBuilder().Retain(3).Delete(3).Build(0);
            Assert.Equal(ApplyOutcome.CorruptDelta, doc.TryApply(runsPastEnd, 0, out _));

            var shortDelta = new DeltaBuilder().Retain(3).Insert("x").Build(0);
            Assert.Equal(ApplyOutcome.DeltaTooShort, doc.TryApply(shortDelta, 0, out _));
        }

        [Fact]
        public void IsReplacementSafeRejectsEditsOutsideRange()
        {
            var doc = new RevisionedDocument("abcdef");
            var range = new TextRange(2, 4, 0);

            // Same aggregate lengths as a safe replacement, but edits land at 1 and 5.
            var outside = new DeltaBuilder()
                .Retain(1)
                .Delete(1)
                .Insert("X")
                .Retain(3)
                .Delete(1)
                .Insert("Y")
                .Build(0);

            Assert.Equal(doc.Length, outside.BaseLength);
            Assert.Equal(doc.Length - range.Length + 2, outside.TargetLength);
            Assert.False(doc.IsReplacementSafe(outside, range));
            Assert.True(doc.IsReplacementSafe(doc.BuildReplacement(range, "XY", 0), range));
        }

        [Fact]
        public void IsReplacementSafeValidatesRevisionAndBounds()
        {
            var doc = new RevisionedDocument("hello world");
            var range = new TextRange(6, 11, 0);
            var safe = doc.BuildReplacement(range, "world", 0);

            Assert.True(doc.IsReplacementSafe(safe, range));
            Assert.False(doc.IsReplacementSafe(new DocumentDelta(safe.Ops, 1), range));
            Assert.False(doc.IsReplacementSafe(safe, new TextRange(6, 11, 1)));
            Assert.False(doc.IsReplacementSafe(safe, new TextRange(-1, 11, 0)));
            Assert.False(doc.IsReplacementSafe(safe, new TextRange(6, 12, 0)));
            Assert.False(doc.IsReplacementSafe(null, range));
        }
    }

    public class ProvenanceTrackerTests
    {
        [Fact]
        public void DisabledByDefaultRecordsNothing()
        {
            var tracker = new ProvenanceTracker();
            tracker.Record(0, 5, AuthorshipKind.Typed);
            Assert.Equal(0, tracker.SpanCount);
            Assert.Equal(0, tracker.TypedLength);
        }

        [Fact]
        public void RecordsOnlyMetadataAndRebases()
        {
            var tracker = new ProvenanceTracker { IsEnabled = true };
            tracker.Record(0, 5, AuthorshipKind.Typed);
            tracker.Record(6, 5, AuthorshipKind.Pasted);

            Assert.Equal(5, tracker.TypedLength);
            Assert.Equal(5, tracker.NonTypedLength);

            var delta = new DeltaBuilder().Retain(3).Insert("ab").Retain(8).Build();
            tracker.Rebase(delta);
            Assert.Equal(2, tracker.SpanCount);
            Assert.Equal(7, tracker.TypedLength);
            Assert.Equal(5, tracker.NonTypedLength);

            var summary = tracker.Summary();
            Assert.Equal(7, summary[AuthorshipKind.Typed]);
            Assert.Equal(5, summary[AuthorshipKind.Pasted]);
        }

        [Fact]
        public void RejectsMismatchedDeltaRevision()
        {
            var tracker = new ProvenanceTracker(1) { IsEnabled = true };
            tracker.Record(0, 5, AuthorshipKind.Typed);

            tracker.Rebase(new DeltaBuilder().Retain(5).Build(0));

            Assert.Equal(1, tracker.Revision);
            Assert.Equal(1, tracker.SpanCount);
            Assert.Equal(5, tracker.TypedLength);
        }

        [Fact]
        public void RebaseAdvancesRevision()
        {
            var tracker = new ProvenanceTracker();
            tracker.Rebase(new DeltaBuilder().Insert("x").Build(0));
            Assert.Equal(1, tracker.Revision);
        }

        [Fact]
        public void IgnoresInvalidRecordInputs()
        {
            var tracker = new ProvenanceTracker { IsEnabled = true };
            tracker.Record(-1, 5, AuthorshipKind.Typed);
            tracker.Record(0, 0, AuthorshipKind.Typed);
            tracker.Record(0, -2, AuthorshipKind.Typed);
            tracker.Record(0, 5, (AuthorshipKind)99);

            Assert.Equal(0, tracker.SpanCount);
        }

        [Fact]
        public void RebaseShrinksPartiallyDeletedSpan()
        {
            var tracker = new ProvenanceTracker { IsEnabled = true };
            tracker.Record(0, 5, AuthorshipKind.Typed);

            tracker.Rebase(new DeltaBuilder().Retain(1).Delete(2).Retain(7).Build(0));

            Assert.Equal(1, tracker.SpanCount);
            Assert.Equal(3, tracker.TypedLength);
            Assert.Equal(1, tracker.Revision);
        }

        [Fact]
        public void RebaseDropsFullyDeletedSpan()
        {
            var tracker = new ProvenanceTracker { IsEnabled = true };
            tracker.Record(1, 2, AuthorshipKind.Typed);

            tracker.Rebase(new DeltaBuilder().Retain(1).Delete(2).Retain(7).Build(0));

            Assert.Equal(0, tracker.SpanCount);
        }
    }
}
