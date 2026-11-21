using System;
using System.Collections.Generic;
using TypeRight.Documents;
using TypeRight.Domain.Models;
using TypeRight.Suggestions;
using Xunit;

namespace TypeRight.Documents.Tests
{
    public class SuggestionTests
    {
        private static SuggestionAlert MakeAlertWithRange(AlertRange range, Guid? id = null)
        {
            var transform = new AlertTransform(range, new[]
            {
                new TransformAlternative(new[] { DeltaOp.Delete(1), DeltaOp.Insert("x") }, "x")
            });
            return new SuggestionAlert(id ?? Guid.NewGuid(), "title", SuggestionCategory.Correctness,
                range, transform, 0.9f, null, AlertPriority.Normal);
        }

        private static SuggestionAlert MakeAlertWithPriority(int layer, float value, float confidence, Guid id)
        {
            var alert = MakeAlert();
            return new SuggestionAlert(id, alert.Title, SuggestionCategory.Correctness, alert.Range,
                alert.Transform, confidence, alert.Rationale, new AlertPriority(layer, "tier", 0, value));
        }

        private static SuggestionAlert MakeAlert(string word = "wrold", int revision = 0)
        {
            int start = 6;
            int end = start + word.Length;
            var range = new AlertRange(start, end, revision);
            var transform = new AlertTransform(range, new[]
            {
                new TransformAlternative(new[]
                {
                    DeltaOp.Delete(word.Length),
                    DeltaOp.Insert("world")
                }, "world")
            });

            return new SuggestionAlert(
                Guid.NewGuid(),
                "Possible spelling error",
                SuggestionCategory.Correctness,
                range,
                transform,
                0.9f,
                "Low-frequency word",
                AlertPriority.Normal,
                hasAddToDictionary: true,
                detectedLanguage: "en-US",
                subAlerts: new[]
                {
                    new SubAlert(Guid.NewGuid(), SuggestionCategory.Clarity,
                        range, transform, "Check spelling")
                });
        }

        [Fact]
        public void AcceptBuildsSafeReplacementDelta()
        {
            var doc = new RevisionedDocument("hello wrold today");
            var alert = MakeAlert();
            var feed = new SuggestionFeed();
            feed.Add(alert);

            var delta = feed.Accept(alert.Id, 0, doc);

            Assert.NotNull(delta);
            Assert.Equal("hello world today", delta.Apply(doc.Text));
            Assert.Equal(0, feed.Count);
            Assert.Contains(FeedbackKind.AcceptAlert, feed.FeedbackCounts().Keys);
            Assert.Contains(FeedbackKind.SuggestionApplied, feed.FeedbackCounts().Keys);
        }

        [Fact]
        public void AcceptRejectsStaleAlertRevision()
        {
            var doc = new RevisionedDocument("hello wrold today");
            var alert = MakeAlert(revision: 1);
            var feed = new SuggestionFeed();
            feed.Add(alert);

            Assert.Null(feed.Accept(alert.Id, 0, doc));
            Assert.Equal(1, feed.Count);
            Assert.DoesNotContain(FeedbackKind.AcceptAlert, feed.FeedbackCounts().Keys);
        }

        [Fact]
        public void AcceptRejectsOutOfBoundsRangeWithoutThrowing()
        {
            var doc = new RevisionedDocument("hello");
            var alert = MakeAlert();
            var feed = new SuggestionFeed();
            feed.Add(alert);

            Assert.Null(feed.Accept(alert.Id, 0, doc));
            Assert.Equal(1, feed.Count);
        }

        [Theory]
        [InlineData(-1, 3)]
        [InlineData(3, 3)]
        [InlineData(4, 3)]
        public void AcceptRejectsInvalidRangeBounds(int start, int end)
        {
            var doc = new RevisionedDocument("hello world");
            var alert = MakeAlertWithRange(new AlertRange(start, end, 0));
            var feed = new SuggestionFeed();
            feed.Add(alert);

            Assert.Null(feed.Accept(alert.Id, 0, doc));
            Assert.Equal(1, feed.Count);
        }

        [Fact]
        public void RebaseMovesAlertAndSubAlert()
        {
            var alert = MakeAlert();
            var feed = new SuggestionFeed();
            feed.Add(alert);

            var delta = new DeltaBuilder().Insert("Hi ").Retain(17).Build(0);
            Assert.Equal(1, feed.Rebase(delta));

            var rebased = Assert.Single(feed.Alerts);
            Assert.Equal(9, rebased.Range.Start);
            Assert.Equal(1, rebased.Range.Revision);
            Assert.Single(rebased.SubAlerts);
            Assert.Equal(9, rebased.SubAlerts[0].Range.Start);
        }

        [Fact]
        public void RebaseDropsDeletedAlert()
        {
            var alert = MakeAlert();
            var feed = new SuggestionFeed();
            feed.Add(alert);

            var delta = new DeltaBuilder().Delete(17).Build(0);
            Assert.Equal(0, feed.Rebase(delta));
            Assert.Contains(FeedbackKind.AcknowledgeAlert, feed.FeedbackCounts().Keys);
        }

        [Fact]
        public void StaleRevisionIsRejected()
        {
            var alert = MakeAlert(revision: 3);
            var feed = new SuggestionFeed();
            feed.Add(alert);
            var delta = new DeltaBuilder().Insert("x").Build(0);
            Assert.Equal(0, feed.Rebase(delta));
        }

        [Fact]
        public void StaleRevisionRebaseIsNotAcknowledged()
        {
            var alert = MakeAlert(revision: 3);
            var feed = new SuggestionFeed();
            feed.Add(alert);

            feed.Rebase(new DeltaBuilder().Insert("x").Build(0));

            Assert.DoesNotContain(FeedbackKind.AcknowledgeAlert, feed.FeedbackCounts().Keys);
        }

        [Fact]
        public void MismatchedSubAlertRevisionRejectsWholeAlert()
        {
            var parent = MakeAlert();
            var staleSub = new SubAlert(Guid.NewGuid(), SuggestionCategory.Clarity,
                new AlertRange(parent.Range.Start, parent.Range.End, 1), parent.Transform, "stale");
            var alert = new SuggestionAlert(parent.Id, parent.Title, parent.Category, parent.Range,
                parent.Transform, parent.Confidence, parent.Rationale, parent.Priority,
                parent.HasAddToDictionary, parent.DetectedLanguage, new[] { staleSub });

            var delta = new DeltaBuilder().Insert("x").Build(0);
            var (rebased, status) = AlertRebase.Rebase(alert, delta);
            Assert.Equal(AlertRebaseStatus.StaleRevision, status);
            Assert.Null(rebased);

            var feed = new SuggestionFeed();
            feed.Add(alert);
            Assert.Equal(0, feed.Rebase(delta));
            Assert.DoesNotContain(FeedbackKind.AcknowledgeAlert, feed.FeedbackCounts().Keys);
        }

        [Fact]
        public void TopAndByCategorySharePriorityOrder()
        {
            var feed = new SuggestionFeed();
            var lowLayerHighValue = MakeAlertWithPriority(0, 9.0f, 0.9f, Guid.NewGuid());
            var highLayerLowValue = MakeAlertWithPriority(1, 1.0f, 0.1f, Guid.NewGuid());
            feed.Add(lowLayerHighValue);
            feed.Add(highLayerLowValue);

            var ordered = feed.ByCategory(SuggestionCategory.Correctness);
            Assert.Same(highLayerLowValue, ordered[0]);
            Assert.Same(lowLayerHighValue, ordered[1]);
            Assert.Same(ordered[0], feed.Top(SuggestionCategory.Correctness));
        }

        [Fact]
        public void TopAndByCategoryBreakTiesByStableId()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var expectedFirst = first.CompareTo(second) < 0 ? first : second;
            var feed = new SuggestionFeed();
            feed.Add(MakeAlertWithPriority(1, 1.0f, 0.5f, second));
            feed.Add(MakeAlertWithPriority(1, 1.0f, 0.5f, first));

            var ordered = feed.ByCategory(SuggestionCategory.Correctness);
            Assert.Equal(expectedFirst, ordered[0].Id);
            Assert.Equal(expectedFirst, feed.Top(SuggestionCategory.Correctness).Id);
        }

        [Fact]
        public void ReplacingAlertDoesNotDoubleRecordRendered()
        {
            var original = MakeAlertWithRange(new AlertRange(1, 2, 0));
            var feed = new SuggestionFeed();
            feed.Add(original);

            var replacement = MakeAlertWithRange(new AlertRange(3, 4, 0), original.Id);
            feed.Add(replacement);

            Assert.Equal(1, feed.FeedbackCounts()[FeedbackKind.AlertRendered]);
            Assert.Same(replacement, Assert.Single(feed.Alerts));
        }

        [Fact]
        public void DismissSkipAndAddToDictionaryRecordFeedbackWithoutText()
        {
            var alert = MakeAlert();
            var feed = new SuggestionFeed();
            feed.Add(alert);

            feed.Skip(alert.Id);
            feed.AddToDictionary(alert.Id);
            feed.Dismiss(alert.Id);

            var counts = feed.FeedbackCounts();
            Assert.Equal(1, counts[FeedbackKind.SkipAlert]);
            Assert.Equal(1, counts[FeedbackKind.AlertAddToDictionary]);
            Assert.Equal(1, counts[FeedbackKind.DismissAlert]);
            Assert.Equal(0, feed.Count);

            foreach (var item in feed.Feedback)
            {
                Assert.NotEqual(Guid.Empty, item.AlertId);
                Assert.True(Enum.IsDefined(typeof(FeedbackKind), item.Kind));
            }
        }

        [Fact]
        public void CountsByCategoryUsesObservedTaxonomy()
        {
            var feed = new SuggestionFeed();
            feed.Add(MakeAlert());
            var clarity = MakeAlert();
            feed.Add(new SuggestionAlert(clarity.Id, "Clarity", SuggestionCategory.Clarity,
                clarity.Range, clarity.Transform, 0.5f, null, AlertPriority.Normal));

            var counts = feed.CountsByCategory();
            Assert.Equal(1, counts[SuggestionCategory.Correctness]);
            Assert.Equal(1, counts[SuggestionCategory.Clarity]);
        }
    }

    public class FeatureGateTests
    {
        [Fact]
        public void OfflineFeaturesOnByDefault()
        {
            var gates = new LocalFeatureGates();
            Assert.True(gates.IsActive(FeatureKey.Proofreader));
            Assert.True(gates.IsActive(FeatureKey.Clarity));
            Assert.True(gates.IsActive(FeatureKey.SensitivityGate));
            Assert.True(gates.IsActive(FeatureKey.PersonalDictionary));
        }

        [Fact]
        public void CloudFeaturesCannotBeEnabledLocally()
        {
            var gates = new LocalFeatureGates();
            gates.Set(FeatureKey.GenerativeRewrite, true);
            gates.Set(FeatureKey.Plagiarism, true);
            gates.Set(FeatureKey.AiDetector, true);

            Assert.False(gates.IsActive(FeatureKey.GenerativeRewrite));
            Assert.False(gates.IsActive(FeatureKey.Plagiarism));
            Assert.False(gates.IsActive(FeatureKey.AiDetector));
            Assert.False(gates.IsLocallyAvailable(FeatureKey.Humanizer));
        }

        [Fact]
        public void AuthorshipMetadataIsOffByDefault()
        {
            var gates = new LocalFeatureGates();
            Assert.False(gates.IsActive(FeatureKey.AuthorshipMetadata));
        }

        [Fact]
        public void SnapshotCoversWholeInventory()
        {
            var snapshot = new LocalFeatureGates().Snapshot();
            Assert.Equal(Enum.GetValues(typeof(FeatureKey)).Length, snapshot.Count);
        }

        [Fact]
        public void UndefinedKeysFailClosed()
        {
            var gates = new LocalFeatureGates();
            var undefined = (FeatureKey)999;

            Assert.False(gates.IsEnabled(undefined));
            Assert.False(gates.IsActive(undefined));
            Assert.Throws<ArgumentOutOfRangeException>(() => gates.Set(undefined, true));
        }
    }
}
