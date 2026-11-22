using System;

namespace TypeRight.TsfBridge;

/// <summary>
/// Content-free counters for the local correction bridge. Every value is a
/// count, duration, fixed protocol name or closed-set state; no request text,
/// suggestion text, process name or exception text is ever included.
/// </summary>
public sealed class BridgeHealthSnapshot
{
    public static BridgeHealthSnapshot Empty { get; } = new BridgeHealthSnapshot(
        TimeSpan.Zero, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public string PipeName { get; } = BridgeProtocol.PipeName;
    public TimeSpan Uptime { get; }
    public long Evaluations { get; }
    public long Pings { get; }
    public long ApplyRequests { get; }
    public long Denied { get; }
    public long Errors { get; }
    public long EngineTimeouts { get; }
    public long SuggestionsOffered { get; }
    public long SuggestionsResolved { get; }
    public int ActiveClients { get; }
    public int PendingSuggestions { get; }

    public BridgeHealthSnapshot(TimeSpan uptime, long evaluations, long pings,
                                long applyRequests, long denied, long errors, long engineTimeouts,
                                long suggestionsOffered, long suggestionsResolved,
                                int activeClients, int pendingSuggestions)
    {
        Uptime = uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime;
        Evaluations = evaluations;
        Pings = pings;
        ApplyRequests = applyRequests;
        Denied = denied;
        Errors = errors;
        EngineTimeouts = engineTimeouts;
        SuggestionsOffered = suggestionsOffered;
        SuggestionsResolved = suggestionsResolved;
        ActiveClients = activeClients;
        PendingSuggestions = pendingSuggestions;
    }
}
