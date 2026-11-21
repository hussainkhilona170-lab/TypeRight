using System;
using System.Collections.Generic;
using TypeRight.Privacy;
using Xunit;

namespace TypeRight.Privacy.Tests
{
    public class IntegrationRulesetTests
    {
        [Fact]
        public void LoadsShippedRuleSet()
        {
            var ruleset = IntegrationRuleset.LoadDefault();
            Assert.Equal(584, ruleset.RuleCount);
            Assert.Equal(81, ruleset.SensitivityRuleCount);
        }

        [Theory]
        [InlineData("bcc")]
        [InlineData("cc")]
        [InlineData("To:")]
        [InlineData("BCC")]
        public void EmailRecipientFields_AreSensitive(string element)
        {
            var ruleset = IntegrationRuleset.LoadDefault();
            var decision = ruleset.Evaluate(element);
            Assert.False(decision.AllowCorrection, $"Expected '{element}' to be suppressed");
            Assert.True(decision.IsSensitive);
        }

        [Fact]
        public void NeutralElement_IsAllowed()
        {
            var ruleset = IntegrationRuleset.LoadDefault();
            var decision = ruleset.Evaluate("DocumentBody");
            Assert.True(decision.AllowCorrection);
        }

        [Fact]
        public void MissingRuleFile_FailsClosedAtPolicyLayer()
        {
            var ruleset = IntegrationRuleset.Load("does-not-exist.json");
            Assert.Equal(0, ruleset.RuleCount);
            Assert.True(ruleset.Evaluate("bcc").AllowCorrection);
        }
    }

    public class PolicyEvaluatorTests
    {
        private static PolicyEvaluator Evaluator(bool dlp, string blockedClients = null)
        {
            var policy = new EnterprisePolicy
            {
                DlpEnabled = dlp,
                BlockedClients = blockedClients,
                IntegrationMode = "full"
            };
            return new PolicyEvaluator(policy, IntegrationRuleset.LoadDefault());
        }

        [Fact]
        public void DlpDisabled_AllowsCorrection()
        {
            var evaluator = Evaluator(false);
            var decision = evaluator.Evaluate("notepad.exe", null, "bcc");
            Assert.True(decision.AllowCorrection);
        }

        [Fact]
        public void DlpEnabled_SensitiveField_Denies()
        {
            var evaluator = Evaluator(true);
            var decision = evaluator.Evaluate("notepad.exe", null, "bcc");
            Assert.False(decision.AllowCorrection);
            Assert.Contains("sensitive_field", decision.Reasons);
        }

        [Fact]
        public void BlockedClient_DeniesRegardlessOfDlp()
        {
            var evaluator = Evaluator(false, "notepad.exe, cmd.exe");
            var decision = evaluator.Evaluate("notepad.exe", null, null);
            Assert.False(decision.AllowCorrection);
            Assert.Contains("client_blocked_by_policy", decision.Reasons);
        }

        [Fact]
        public void LimitedIntegrationMode_DeniesWithDomain()
        {
            var policy = new EnterprisePolicy { DlpEnabled = false, IntegrationMode = "limited" };
            var evaluator = new PolicyEvaluator(policy, IntegrationRuleset.LoadDefault());
            var decision = evaluator.Evaluate("chrome.exe", "mail.google.com", null);
            Assert.False(decision.AllowCorrection);
            Assert.Contains("limited_integration_mode", decision.Reasons);
        }

        [Fact]
        public void DescribeRules_DoesNotContainUserText()
        {
            var description = string.Join(" ", Evaluator(true).DescribeRules());
            Assert.DoesNotContain("bcc", description);
            Assert.Contains("dlp=on", description);
        }
    }
}
