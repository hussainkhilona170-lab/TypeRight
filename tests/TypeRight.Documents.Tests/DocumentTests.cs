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
    }
}
