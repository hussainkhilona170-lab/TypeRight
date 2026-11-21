using System;
using TypeRight.Configuration;
using TypeRight.Domain.Models;
using TypeRight.Engine.CorrectionDecision;
using TypeRight.Privacy;
using TypeRight.TsfBridge;
using Xunit;
using Decision = TypeRight.Domain.Models.CorrectionDecision;

namespace TypeRight.TsfBridge.Tests
{
    public class CorrectionPipeServerTests
    {
        private sealed class StubDecisionMaker : ICorrectionDecisionMaker
        {
            public Decision Result;
            public int CallCount;

            public StubDecisionMaker(Decision result)
            {
                Result = result;
            }

            public CorrectionDecision MakeDecision(CorrectionRequest request)
            {
                CallCount++;
                return Result;
            }
        }

        private static PolicyEvaluator ContextPolicy()
        {
            return new PolicyEvaluator(
                new EnterprisePolicy { BlockedClients = "blocked.exe" },
                IntegrationRuleset.Load("no-such-rules.json"));
        }

        private static string Eval(string process = "notepad.exe")
        {
            return BridgeProtocol.FormatEval("enus", "wrold", process, string.Empty, string.Empty);
        }

        [Fact]
        public void Ping_DoesNotCallEngine()
        {
            var maker = new StubDecisionMaker(Decision.Preserve());
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings()))
            {
                Assert.Equal(BridgeProtocol.OkPong, server.ProcessRequest(BridgeProtocol.Ping));
                Assert.Equal(0, maker.CallCount);
            }
        }

        [Fact]
        public void LowConfidenceAutoCorrect_BecomesSuggestion()
        {
            var configuration = new ConfigurationSettings
            {
                ConfidenceThreshold = 0.9f,
                ShowSuggestions = true
            };
            var maker = new StubDecisionMaker(Decision.AutoCorrect("world", 0.7f));
            using (var server = new CorrectionPipeServer(maker, configuration))
            {
                Assert.Equal("SUG\tworld", server.ProcessRequest(Eval()));
            }
        }

        [Fact]
        public void LowConfidenceAutoCorrect_WithSuggestionsDisabled_ReturnsNone()
        {
            var configuration = new ConfigurationSettings
            {
                ConfidenceThreshold = 0.9f,
                ShowSuggestions = false
            };
            var maker = new StubDecisionMaker(Decision.AutoCorrect("world", 0.7f));
            using (var server = new CorrectionPipeServer(maker, configuration))
            {
                Assert.Equal(BridgeProtocol.OkNone, server.ProcessRequest(Eval()));
            }
        }

        [Fact]
        public void Suggestion_WithSuggestionsDisabled_ReturnsNone()
        {
            var configuration = new ConfigurationSettings { ShowSuggestions = false };
            var maker = new StubDecisionMaker(Decision.Suggest(new[] { "world" }, 0.9f));
            using (var server = new CorrectionPipeServer(maker, configuration))
            {
                Assert.Equal(BridgeProtocol.OkNone, server.ProcessRequest(Eval()));
            }
        }

        [Fact]
        public void ExcludedApplication_IsCaseInsensitive()
        {
            var configuration = new ConfigurationSettings
            {
                ExcludedApplications = new[] { "NOTEPAD.EXE" }
            };
            var maker = new StubDecisionMaker(Decision.AutoCorrect("world", 0.99f));
            using (var server = new CorrectionPipeServer(maker, configuration))
            {
                Assert.Equal(BridgeProtocol.Denied, server.ProcessRequest(Eval("notePad.exe")));
                Assert.Equal(0, maker.CallCount);
            }
        }

        [Fact]
        public void PausedService_DeniesCorrections()
        {
            var maker = new StubDecisionMaker(Decision.AutoCorrect("world", 0.99f));
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings(), null, null, () => false))
            {
                Assert.Equal(BridgeProtocol.Denied, server.ProcessRequest(Eval()));
                Assert.Equal(0, maker.CallCount);
            }
        }

        [Fact]
        public void LegacyRequestWithoutContext_DeniedWhenPolicyRequiresContext()
        {
            var maker = new StubDecisionMaker(Decision.AutoCorrect("world", 0.99f));
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings(), ContextPolicy()))
            {
                var legacy = BridgeProtocol.FormatEval("enus", "wrold");
                Assert.Equal(BridgeProtocol.Denied, server.ProcessRequest(legacy));
                Assert.Equal(0, maker.CallCount);
            }
        }

        [Fact]
        public void SixFieldRequestWithProcessContext_ReachesEngine()
        {
            var maker = new StubDecisionMaker(Decision.AutoCorrect("world", 0.99f));
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings(), ContextPolicy()))
            {
                Assert.Equal("AUTO\tworld", server.ProcessRequest(Eval()));
                Assert.Equal(1, maker.CallCount);
            }
        }
    }
}