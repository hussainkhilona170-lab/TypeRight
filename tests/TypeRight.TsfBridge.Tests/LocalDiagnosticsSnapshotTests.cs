using System;
using TypeRight.Configuration;
using TypeRight.Privacy;
using TypeRight.Suggestions;
using TypeRight.TsfBridge;
using Xunit;

namespace TypeRight.TsfBridge.Tests
{
    public class LocalDiagnosticsSnapshotTests
    {
        [Fact]
        public void Capture_ExposesCountsOnly()
        {
            var bridge = new BridgeHealthSnapshot(
                TimeSpan.FromSeconds(3), 4, 1, 2, 1, 0, 0, 1, 1, 1, 0);
            var configuration = new ConfigurationSettings
            {
                ExcludedApplications = new[] { "one.exe", "two.exe" },
                PersonalLearningEnabled = false
            };
            var features = new LocalFeatureGates();
            var policy = new PolicyEvaluator(
                new EnterprisePolicy
                {
                    BlockedClients = "client-one,client-two",
                    BlockedDomains = "blocked.example"
                },
                IntegrationRuleset.Load("no-such-rules.json"));

            var snapshot = LocalDiagnosticsSnapshot.Capture(bridge, configuration, features, policy);

            Assert.Same(bridge, snapshot.Bridge);
            Assert.Equal(2, snapshot.ExcludedApplicationCount);
            Assert.Equal(0, snapshot.PersonalWordCount);
            Assert.Equal(2, snapshot.BlockedClientCount);
            Assert.Equal(1, snapshot.BlockedDomainCount);
            Assert.Equal(0, snapshot.IntegrationRuleCount);
            Assert.Equal(0, snapshot.SensitivityRuleCount);
            Assert.Equal(6, snapshot.ActiveFeatureCount);
            Assert.Equal(17, snapshot.TotalFeatureCount);
        }

        [Fact]
        public void Capture_WithMissingDependenciesFailsClosedToZeroCounts()
        {
            var snapshot = LocalDiagnosticsSnapshot.Capture(null, null, null, null);

            Assert.Same(BridgeHealthSnapshot.Empty, snapshot.Bridge);
            Assert.Equal(0, snapshot.ActiveFeatureCount);
            Assert.Equal(17, snapshot.TotalFeatureCount);
            Assert.Equal(0, snapshot.ExcludedApplicationCount);
            Assert.Equal(0, snapshot.PersonalWordCount);
            Assert.Equal(0, snapshot.BlockedClientCount);
            Assert.Equal(0, snapshot.BlockedDomainCount);
            Assert.Equal(0, snapshot.IntegrationRuleCount);
            Assert.Equal(0, snapshot.SensitivityRuleCount);
        }
    }
}
