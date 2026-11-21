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
    }
}
