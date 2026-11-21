using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TypeRight.Configuration;
using TypeRight.Domain.Models;
using TypeRight.Engine.CorrectionDecision;
using TypeRight.Privacy;
using TypeRight.Suggestions;

namespace TypeRight.TsfBridge
{
    /// <summary>
    /// Hosts a local named pipe that the native TSF text service uses to request
    /// correction decisions from the managed engine. The pipe is local-only and
    /// restricted to the current user. Requests are bounded and time-boxed so a
    /// slow engine can never block a TSF callback indefinitely.
    /// </summary>
    public sealed class CorrectionPipeServer : IDisposable
    {
        private readonly ICorrectionDecisionMaker _decisionMaker;
        private readonly ConfigurationSettings _configuration;
        private readonly PolicyEvaluator _policyEvaluator;
        private readonly LocalFeatureGates _features;
        private CancellationTokenSource _cts;
        private Task _loop;
        private bool _disposed;

        public CorrectionPipeServer(ICorrectionDecisionMaker decisionMaker, ConfigurationSettings configuration)
            : this(decisionMaker, configuration, null, null)
        {
        }

        public CorrectionPipeServer(ICorrectionDecisionMaker decisionMaker,
                                    ConfigurationSettings configuration,
                                    PolicyEvaluator policyEvaluator)
            : this(decisionMaker, configuration, policyEvaluator, null)
        {
        }

        public CorrectionPipeServer(ICorrectionDecisionMaker decisionMaker,
                                    ConfigurationSettings configuration,
                                    PolicyEvaluator policyEvaluator,
                                    LocalFeatureGates features)
        {
            _decisionMaker = decisionMaker ?? throw new ArgumentNullException(nameof(decisionMaker));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _policyEvaluator = policyEvaluator;
            _features = features;
        }

        /// <summary>
        /// Starts accepting connections in the background.
        /// </summary>
        public void Start()
        {
            if (_loop != null) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    // Current-user-only security: the native TSF DLL runs in the same user session.
                    var pipeSecurity = new PipeSecurity();
                    var identity = WindowsIdentity.GetCurrent();
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        identity.User,
                        PipeAccessRights.ReadWrite,
                        System.Security.AccessControl.AccessControlType.Allow));

                    server = new NamedPipeServerStream(
                        BridgeProtocol.PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        4096, 4096);
                    server.SetAccessControl(pipeSecurity);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) break;

                    // Each connection is handled independently; a malformed client cannot
                    // take down the bridge.
                    _ = Task.Run(() => HandleClientAsync(server, token));
                    server = null; // ownership transferred
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Never record exception text - it may contain user data.
                    try { server?.Dispose(); } catch { }
                    if (!token.IsCancellationRequested)
                    {
                        try { Task.Delay(250, token).Wait(token); } catch { break; }
                    }
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken outerToken)
        {
            using (server)
            {
                try
                {
                    using (var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, leaveOpen: true))
                    using (var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true))
                    {
                        writer.AutoFlush = true;
                        string line;
                        while (!outerToken.IsCancellationRequested && server.IsConnected)
                        {
                            var readTask = reader.ReadLineAsync();
                            var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, outerToken)).ConfigureAwait(false);
                            if (completed != readTask) break;

                            line = await readTask.ConfigureAwait(false);
                            if (line == null) break; // client closed

                            string response;
                            try
                            {
                                response = ProcessRequest(line);
                            }
                            catch (Exception)
                            {
                                // Sanitized error - never include exception messages or input.
                                response = BridgeProtocol.FormatError("engine_fault");
                            }

                            await writer.WriteLineAsync(response).ConfigureAwait(false);
                            await writer.FlushAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    // Connection dropped - expected when the TSF DLL unloads. No logging of content.
                }
            }
        }

        /// <summary>
        /// Processes a single protocol line and returns the response line.
        /// Public so it can be unit-tested without a pipe.
        /// </summary>
        public string ProcessRequest(string line)
        {
            if (!BridgeProtocol.TryParseRequest(line, out var opcode, out var language, out var word,
                                                out var process, out var domain, out var element))
                return BridgeProtocol.FormatError("bad_request");

            if (opcode == BridgeProtocol.Ping)
                return BridgeProtocol.FormatPong();

            if (_policyEvaluator != null)
            {
                var policy = _policyEvaluator.Evaluate(process, domain, element);
                if (!policy.AllowCorrection)
                    return BridgeProtocol.FormatDeny();
            }

            // The local feature gate can disable proofreading at runtime without
            // tearing down the native TSF service.
            if (_features != null && !_features.IsActive(FeatureKey.Proofreader))
                return BridgeProtocol.FormatDeny();

            LanguageVariant variant = ParseLanguage(language);

            try
            {
                var request = new CorrectionRequest(
                    completedWord: word,
                    adjacentLeft: Array.Empty<string>(),
                    adjacentRight: Array.Empty<string>(),
                    capitalization: CapitalizationInfo.FromOriginal(word),
                    languageVariant: variant,
                    vocabularyVersion: new VocabularyVersion(2, 0));

                var decision = _decisionMaker.MakeDecision(request);

                switch (decision.Action)
                {
                    case CorrectionAction.AutoCorrect:
                        if (!string.IsNullOrEmpty(decision.CorrectedWord))
                            return BridgeProtocol.FormatAuto(decision.CorrectedWord);
                        return BridgeProtocol.FormatNone();

                    case CorrectionAction.Suggest:
                        return BridgeProtocol.FormatSuggest(decision.Alternatives);

                    default:
                        return BridgeProtocol.FormatNone();
                }
            }
            catch (Exception)
            {
                return BridgeProtocol.FormatError("engine_fault");
            }
        }

        private LanguageVariant ParseLanguage(string value)
        {
            switch ((value ?? string.Empty).ToLowerInvariant())
            {
                case "enus":
                case "en-us":
                    return LanguageVariant.EnUs;
                case "engb":
                case "en-gb":
                    return LanguageVariant.EnGb;
                default:
                    return _configuration.LanguageVariant;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
        }
    }
}
