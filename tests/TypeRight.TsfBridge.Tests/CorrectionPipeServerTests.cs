using System;
using System.Threading.Tasks;
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
                var response = server.ProcessRequest(Eval());
                var parts = response.Split('\t');

                Assert.Equal(BridgeProtocol.OkSuggest, parts[0]);
                Assert.True(BridgeProtocol.IsValidToken(parts[1]));
                Assert.Equal("world", parts[2]);
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

        [Fact]
        public void SuggestionOffered_IncludesTokenAndContext()
        {
            var maker = new StubDecisionMaker(Decision.Suggest(new[] { "world" }, 0.9f));
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings()))
            {
                SuggestionOfferedEventArgs offered = null;
                server.SuggestionOffered += (_, args) => offered = args;

                var response = server.ProcessRequest(Eval());
                string token = response.Split('\t')[1];

                Assert.NotNull(offered);
                Assert.Equal(token, offered.Token);
                Assert.Equal("wrold", offered.Word);
                Assert.Equal("notepad.exe", offered.Process);
                Assert.Single(offered.Suggestions);
                Assert.Equal("world", offered.Suggestions[0]);
            }
        }

        [Fact]
        public async Task Apply_ResolvesSelectedAlternative()
        {
            var maker = new StubDecisionMaker(Decision.Suggest(new[] { "world", "word" }, 0.9f));
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings()))
            {
                var response = server.ProcessRequest(Eval());
                string token = response.Split('\t')[1];
                var apply = server.ProcessApplyAsync(
                    BridgeProtocol.FormatApply(token, "notepad.exe", string.Empty, string.Empty));

                Assert.True(server.TryResolveSuggestion(token, 1));
                var ack = await apply;

                Assert.True(BridgeProtocol.TryParseAck(ack, out _, out var status, out var value));
                Assert.Equal(BridgeProtocol.AckOk, status);
                Assert.Equal("word", value);
            }
        }

        [Fact]
        public async Task Apply_DismissedSuggestionReturnsCancelled()
        {
            var maker = new StubDecisionMaker(Decision.Suggest(new[] { "world" }, 0.9f));
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings()))
            {
                var response = server.ProcessRequest(Eval());
                string token = response.Split('\t')[1];
                var apply = server.ProcessApplyAsync(
                    BridgeProtocol.FormatApply(token, "notepad.exe", string.Empty, string.Empty));

                server.DismissSuggestion(token);
                var ack = await apply;

                Assert.True(BridgeProtocol.TryParseAck(ack, out _, out var status, out var value));
                Assert.Equal(BridgeProtocol.AckNone, status);
                Assert.Equal(BridgeProtocol.ReasonCancelled, value);
            }
        }

        [Fact]
        public async Task Apply_PausedAfterOfferIsDenied()
        {
            bool paused = false;
            var maker = new StubDecisionMaker(Decision.Suggest(new[] { "world" }, 0.9f));
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings(), null, null, () => !paused))
            {
                var response = server.ProcessRequest(Eval());
                string token = response.Split('\t')[1];
                var apply = server.ProcessApplyAsync(
                    BridgeProtocol.FormatApply(token, "notepad.exe", string.Empty, string.Empty));

                paused = true;
                Assert.True(server.TryResolveSuggestion(token, 0));
                var ack = await apply;

                Assert.True(BridgeProtocol.TryParseAck(ack, out _, out var status, out var value));
                Assert.Equal(BridgeProtocol.AckDeny, status);
                Assert.Equal(BridgeProtocol.ReasonServicePaused, value);
            }
        }

        [Fact]
        public async Task Apply_UnknownTokenReturnsNotFound()
        {
            var maker = new StubDecisionMaker(Decision.Preserve());
            using (var server = new CorrectionPipeServer(maker, new ConfigurationSettings()))
            {
                var ack = await server.ProcessApplyAsync(BridgeProtocol.FormatApply(
                    Guid.NewGuid().ToString("N"), "notepad.exe", string.Empty, string.Empty));

                Assert.True(BridgeProtocol.TryParseAck(ack, out _, out var status, out var value));
                Assert.Equal(BridgeProtocol.AckNone, status);
                Assert.Equal(BridgeProtocol.ReasonNotFound, value);
            }
        }
    }
}