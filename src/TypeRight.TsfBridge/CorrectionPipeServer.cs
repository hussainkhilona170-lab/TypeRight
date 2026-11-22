using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const int DefaultMaxConcurrentClients = 8;
        private static readonly TimeSpan DefaultEngineTimeout = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DefaultSelectionTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Raised when suggestions are ready for a UI to display.</summary>
        public event EventHandler<SuggestionOfferedEventArgs> SuggestionOffered;

        /// <summary>Raised when a pending selection resolves so a UI can dismiss.</summary>
        public event EventHandler<SuggestionResolvedEventArgs> SuggestionResolved;

        private readonly ConcurrentDictionary<string, PendingSuggestion> _pendingSuggestions =
            new ConcurrentDictionary<string, PendingSuggestion>();

        private readonly ICorrectionDecisionMaker _decisionMaker;
        private readonly ConfigurationSettings _configuration;
        private readonly PolicyEvaluator _policyEvaluator;
        private readonly LocalFeatureGates _features;
        private readonly Func<bool> _isCorrectionEnabled;
        private readonly TimeSpan _engineTimeout;
        private readonly SemaphoreSlim _clientSlots;
        private readonly object _clientTasksLock = new object();
        private readonly List<Task> _clientTasks = new List<Task>();
        private CancellationTokenSource _cts;
        private Task _loop;
        private bool _disposed;

        private sealed class PendingSuggestion
        {
            public string[] Suggestions { get; set; } = Array.Empty<string>();
            public string Process { get; set; }
            public string Domain { get; set; }
            public string Element { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public TaskCompletionSource<SuggestionSelection> Completion { get; } =
                new TaskCompletionSource<SuggestionSelection>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class SuggestionSelection
        {
            public string Status { get; }
            public string Value { get; }

            public SuggestionSelection(string status, string value)
            {
                Status = status;
                Value = value;
            }
        }

        public CorrectionPipeServer(ICorrectionDecisionMaker decisionMaker, ConfigurationSettings configuration)
            : this(decisionMaker, configuration, null, null, null)
        {
        }

        public CorrectionPipeServer(ICorrectionDecisionMaker decisionMaker,
                                    ConfigurationSettings configuration,
                                    PolicyEvaluator policyEvaluator)
            : this(decisionMaker, configuration, policyEvaluator, null, null)
        {
        }

        public CorrectionPipeServer(ICorrectionDecisionMaker decisionMaker,
                                    ConfigurationSettings configuration,
                                    PolicyEvaluator policyEvaluator,
                                    LocalFeatureGates features)
            : this(decisionMaker, configuration, policyEvaluator, features, null)
        {
        }

        /// <summary>
        /// Creates a bridge with a runtime gate (used by the tray "Pause Service" action).
        /// A null gate means corrections are enabled.
        /// </summary>
        public CorrectionPipeServer(ICorrectionDecisionMaker decisionMaker,
                                    ConfigurationSettings configuration,
                                    PolicyEvaluator policyEvaluator,
                                    LocalFeatureGates features,
                                    Func<bool> isCorrectionEnabled)
            : this(decisionMaker, configuration, policyEvaluator, features, isCorrectionEnabled,
                   DefaultEngineTimeout, DefaultMaxConcurrentClients)
        {
        }

        public CorrectionPipeServer(ICorrectionDecisionMaker decisionMaker,
                                    ConfigurationSettings configuration,
                                    PolicyEvaluator policyEvaluator,
                                    LocalFeatureGates features,
                                    Func<bool> isCorrectionEnabled,
                                    TimeSpan engineTimeout,
                                    int maxConcurrentClients)
        {
            _decisionMaker = decisionMaker ?? throw new ArgumentNullException(nameof(decisionMaker));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _policyEvaluator = policyEvaluator;
            _features = features;
            _isCorrectionEnabled = isCorrectionEnabled ?? (() => true);
            _engineTimeout = engineTimeout;
            if (maxConcurrentClients < 1)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentClients));
            _clientSlots = new SemaphoreSlim(maxConcurrentClients, maxConcurrentClients);
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
                bool slotHeld = false;
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

                    // Deny extra connections instead of queueing them behind a slow client.
                    if (!await _clientSlots.WaitAsync(0, token).ConfigureAwait(false))
                        continue;

                    slotHeld = true;
                    var connected = server;
                    server = null; // ownership transferred to the client task

                    // Capture a stable local instance before scheduling; never schedule
                    // a closure over a variable that may be cleared by the loop.
                    TrackClientTask(HandleClientAsync(connected, token));
                    slotHeld = false;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break; // bridge is shutting down
                }
                catch (Exception)
                {
                    // Never record exception text - it may contain user data.
                    if (!token.IsCancellationRequested)
                    {
                        try { await Task.Delay(250, token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { break; }
                    }
                }
                finally
                {
                    if (slotHeld)
                    {
                        try { _clientSlots.Release(); } catch { }
                    }
                    try { server?.Dispose(); } catch { }
                }
            }
        }

        private void TrackClientTask(Task task)
        {
            lock (_clientTasksLock)
            {
                _clientTasks.Add(task);
            }
            task.ContinueWith(t =>
            {
                lock (_clientTasksLock)
                {
                    _clientTasks.Remove(t);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken outerToken)
        {
            try
            {
                using (var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, leaveOpen: true))
                using (var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true))
                {
                    writer.AutoFlush = true;
                    while (!outerToken.IsCancellationRequested && server.IsConnected)
                    {
                        var readTask = reader.ReadLineAsync();
                        var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, outerToken)).ConfigureAwait(false);
                        if (completed != readTask)
                        {
                            ObserveFault(readTask);
                            break;
                        }

                        string line = await readTask.ConfigureAwait(false);
                        if (line == null) break; // client closed

                        string response;
                        if (BridgeProtocol.TryParseApply(line, out _, out _, out _, out _))
                        {
                            // Selection waits are user-driven, not engine work. They
                            // use their own bounded timeout in HandleApplyAsync.
                            response = await ProcessApplyAsync(line, outerToken).ConfigureAwait(false);
                        }
                        else
                        {
                            // Time-box the whole engine call, not just the pipe read.
                            var engineTask = Task.Run(() => ProcessRequest(line));
                            try
                            {
                                var engineCompleted = await Task.WhenAny(
                                    engineTask, Task.Delay(_engineTimeout, outerToken)).ConfigureAwait(false);
                                if (engineCompleted != engineTask)
                                {
                                    ObserveFault(engineTask);
                                    response = BridgeProtocol.FormatError("engine_timeout");
                                }
                                else
                                {
                                    response = await engineTask.ConfigureAwait(false);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                ObserveFault(engineTask);
                                break;
                            }
                        }

                        await writer.WriteLineAsync(response).ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                // Connection dropped or bridge shutting down. No logging of content.
            }
            finally
            {
                try { server.Dispose(); } catch { }
                try { _clientSlots.Release(); } catch { }
            }
        }

        private static void ObserveFault(Task task)
        {
            // The abandoned engine call may still fault after a timeout; observe it
            // without ever reading or recording exception text.
            task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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

            string denyReason = IsCorrectionAllowed(process, domain, element);
            if (denyReason != null)
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
                bool belowThreshold = float.IsNaN(decision.Confidence) ||
                                      decision.Confidence < _configuration.ConfidenceThreshold;

                switch (decision.Action)
                {
                    case CorrectionAction.AutoCorrect:
                        if (string.IsNullOrEmpty(decision.CorrectedWord))
                            return BridgeProtocol.FormatNone();
                        if (belowThreshold)
                            return OfferSuggestions(language, word, process, domain, element,
                                new[] { decision.CorrectedWord });
                        return BridgeProtocol.FormatAuto(decision.CorrectedWord);

                    case CorrectionAction.Suggest:
                        return OfferSuggestions(language, word, process, domain, element,
                            decision.Alternatives);

                    default:
                        return BridgeProtocol.FormatNone();
                }
            }
            catch (Exception)
            {
                return BridgeProtocol.FormatError("engine_fault");
            }
        }

        /// <summary>
        /// Processes a native APPLY line. Public for headless protocol tests.
        /// </summary>
        public async Task<string> ProcessApplyAsync(string line, CancellationToken cancellationToken = default)
        {
            if (!BridgeProtocol.TryParseApply(line, out var token, out _, out _, out _))
                return BridgeProtocol.FormatError("bad_request");
            return await HandleApplyAsync(token, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> HandleApplyAsync(string token, CancellationToken outerToken)
        {
            if (!_pendingSuggestions.TryGetValue(token, out var pending))
                return BridgeProtocol.FormatAck(token, BridgeProtocol.AckNone, BridgeProtocol.ReasonNotFound);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
                var remaining = pending.ExpiresAt - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    CompletePending(token, pending, BridgeProtocol.AckNone, BridgeProtocol.ReasonExpired);
                    return BridgeProtocol.FormatAck(token, BridgeProtocol.AckNone, BridgeProtocol.ReasonExpired);
                }

                cts.CancelAfter(remaining);
                var selection = await pending.Completion.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                return BridgeProtocol.FormatAck(token, selection.Status, selection.Value);
            }
            catch (OperationCanceledException)
            {
                CompletePending(token, pending, BridgeProtocol.AckNone, BridgeProtocol.ReasonExpired);
                return BridgeProtocol.FormatAck(token, BridgeProtocol.AckNone, BridgeProtocol.ReasonExpired);
            }
            catch (Exception)
            {
                return BridgeProtocol.FormatAck(token, BridgeProtocol.AckNone, BridgeProtocol.ReasonNotFound);
            }
        }

        private string OfferSuggestions(string language, string word, string process, string domain,
                                        string element, IEnumerable<string> suggestions)
        {
            if (!_configuration.ShowSuggestions)
                return BridgeProtocol.FormatNone();

            var values = (suggestions ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrEmpty(s))
                .Take(5)
                .ToArray();
            if (values.Length == 0)
                return BridgeProtocol.FormatNone();

            string token = Guid.NewGuid().ToString("N");
            var pending = new PendingSuggestion
            {
                Suggestions = values,
                Process = process,
                Domain = domain,
                Element = element,
                ExpiresAt = DateTimeOffset.UtcNow.Add(DefaultSelectionTimeout)
            };
            _pendingSuggestions[token] = pending;

            try
            {
                SuggestionOffered?.Invoke(this, new SuggestionOfferedEventArgs(
                    token, language, word, process, domain, element, values));
            }
            catch (Exception)
            {
                // A UI fault must not leave a long-poll waiting on the native side.
                CompletePending(token, pending, BridgeProtocol.AckNone, BridgeProtocol.ReasonNotFound);
                return BridgeProtocol.FormatNone();
            }

            return BridgeProtocol.FormatSuggest(token, values);
        }

        /// <summary>
        /// Resolves a pending suggestion by index. Rechecks gates before returning
        /// the replacement so a pause or policy change cannot be bypassed by a click.
        /// </summary>
        public bool TryResolveSuggestion(string token, int alternativeIndex)
        {
            if (!BridgeProtocol.IsValidToken(token) ||
                !_pendingSuggestions.TryGetValue(token, out var pending))
                return false;

            if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                CompletePending(token, pending, BridgeProtocol.AckNone, BridgeProtocol.ReasonExpired);
                return false;
            }

            if (alternativeIndex < 0 || alternativeIndex >= pending.Suggestions.Length)
                return false;

            string denyReason = !_configuration.ShowSuggestions
                ? BridgeProtocol.ReasonSuggestionsOff
                : IsCorrectionAllowed(pending.Process, pending.Domain, pending.Element);

            if (denyReason != null)
            {
                CompletePending(token, pending, BridgeProtocol.AckDeny, denyReason);
                return true;
            }

            CompletePending(token, pending, BridgeProtocol.AckOk, pending.Suggestions[alternativeIndex]);
            return true;
        }

        /// <summary>Dismisses a pending suggestion without applying anything.</summary>
        public void DismissSuggestion(string token)
        {
            if (!BridgeProtocol.IsValidToken(token) ||
                !_pendingSuggestions.TryGetValue(token, out var pending))
                return;

            CompletePending(token, pending, BridgeProtocol.AckNone, BridgeProtocol.ReasonCancelled);
        }

        private void CompletePending(string token, PendingSuggestion pending, string status, string value)
        {
            if (!_pendingSuggestions.TryRemove(token, out var removed) || !ReferenceEquals(removed, pending))
                return;
            if (!pending.Completion.TrySetResult(new SuggestionSelection(status, value)))
                return;

            try
            {
                SuggestionResolved?.Invoke(this, new SuggestionResolvedEventArgs(token, status));
            }
            catch (Exception)
            {
                // UI observers are optional; never let a handler fault escape.
            }
        }

        /// <summary>
        /// Shared enforcement for EVAL and selection resolution. Returns a fixed
        /// reason code when correction is not allowed, or null when it is.
        /// </summary>
        private string IsCorrectionAllowed(string process, string domain, string element)
        {
            if (!_isCorrectionEnabled())
                return BridgeProtocol.ReasonServicePaused;
            if (IsApplicationExcluded(process))
                return BridgeProtocol.ReasonExcludedApp;

            if (_policyEvaluator != null)
            {
                bool hasContextFrame = process != null && domain != null && element != null;
                if (_policyEvaluator.RequiresContext &&
                    (string.IsNullOrEmpty(process) || !hasContextFrame))
                    return BridgeProtocol.ReasonPolicyDenied;

                var policy = _policyEvaluator.Evaluate(process, domain, element);
                if (!policy.AllowCorrection)
                    return BridgeProtocol.ReasonPolicyDenied;
            }

            if (_features != null && !_features.IsActive(FeatureKey.Proofreader))
                return BridgeProtocol.ReasonFeatureOff;

            return null;
        }

        /// <summary>
        /// Checks the local configuration's exclusion list. The native service also
        /// has a registry-based check; this bridge check is authoritative for
        /// requests that reach it and does not duplicate registry access.
        /// </summary>
        private bool IsApplicationExcluded(string process)
        {
            string[] excluded = _configuration.ExcludedApplications;
            if (excluded == null || excluded.Length == 0)
                return false;

            if (string.IsNullOrEmpty(process))
                return true; // exclusions are configured but no process context was supplied

            string processName = FileName(process);
            foreach (string entry in excluded)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                string excludedName = FileName(entry.Trim());
                if (string.Equals(excludedName, processName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string FileName(string value)
        {
            int separator = value.LastIndexOfAny(new[] { '\\', '/' });
            return separator < 0 ? value : value.Substring(separator + 1);
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

            foreach (var pending in _pendingSuggestions.ToArray())
                CompletePending(pending.Key, pending.Value, BridgeProtocol.AckNone, BridgeProtocol.ReasonCancelled);

            try { _cts?.Cancel(); } catch { }

            var tasks = new List<Task>();
            if (_loop != null) tasks.Add(_loop);
            lock (_clientTasksLock)
            {
                tasks.AddRange(_clientTasks);
            }

            try
            {
                if (tasks.Count > 0)
                    Task.WaitAll(tasks.ToArray(), DisposeWaitTimeout);
            }
            catch (Exception)
            {
                // Shutdown is best-effort; cancellation has already been signalled.
            }

            try { _cts?.Dispose(); } catch { }
            try { _clientSlots?.Dispose(); } catch { }
        }
    }
}