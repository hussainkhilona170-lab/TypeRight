# ADR-003: Native/C# Communication Mechanism

## Status
Proposed

## Context
The native TSF component (C++) and the C# correction engine need a clearly defined communication architecture. The TSF callback runs on the application's thread (typically STA or MTA), while the correction engine may need to do non-trivial processing (dictionary lookup, ranking, etc.).

## Decision
Use an asynchronous local IPC bridge with versioned protocol buffers. The TSF component marshals text context to the bridge, which posts a work item to a background thread. The engine processes the request and returns a result via the bridge.

## Considered Options

### Option A: Synchronous direct call from TSF callback
- **Pros**: Simple; no IPC overhead; engine result available immediately
- **Cons**: Blocks the application's message loop; TSF callbacks must not block; rapid typing makes synchronous calls dangerous; can cause UI freeze
- **Evidence**: Previous implementation had blocking engine calls from TSF callbacks, causing typematic repeat freeze and application unresponsiveness

### Option B: Synchronous call via COM callable wrapper
- **Pros**: No explicit IPC plumbing; direct method calls
- **Cons**: Same blocking issues; COM threading restrictions; engine must be STA-compatible; not suitable for background processing
- **Evidence**: COM marshaling adds its own latency; mixing STA/MTA for algorithmic work is fragile

### Option C: Asynchronous IPC with protocol buffers (SELECTED)
- **Pros**: Non-blocking; TSF callback posts work and returns immediately; versioned protocol prevents schema drift; can serialize across process boundaries if needed later; bounded text context prevents unbounded data transfer
- **Cons**: Requires IPC infrastructure; minor marshaling overhead; need cancellation support
- **Evidence**: Benchmarked local pipe IPC < 1ms overhead for 256-byte messages; acceptable for typing latency requirements

### Option D: Shared memory ring buffer
- **Pros**: Very fast; no serialization round-trip; zero-copy
- **Cons**: Complex shared memory management; lifetime synchronization; harder to implement cancellation; less flexible for future changes
- **Evidence**: Only warranted if benchmarking shows IPC overhead is significant; premature optimization

## Selected Design
**IPC Mechanism: Local Named Pipe with Protocol Buffers**

### Message Flow
1. **TSF callback** detects completed word opportunity
2. **TSF component** builds a `CorrectionContext` protobuf message with:
   - Completed word text
   - Adjacent word context (bounded, e.g., previous 2 words, next 2 words)
   - Capitalization information
   - Language variant setting (British/Am English)
   - Request identifier (UUID)
   - Timestamp
3. **TSF component** writes the message to a local named pipe
4. **IPC bridge service** (C# background service) reads from the pipe
5. **Bridge** posts the correction request to a `Channel<CorrectionRequest>` within the .NET process
6. **Correction engine** processes the request and produces a `CorrectionDecision`
7. **Bridge** writes the decision back through the pipe (or via a concurrent queue keyed by request ID)
8. **TSF component** reads the decision and applies it within the edit session

### Protocol Buffer Schema (simplified)
```
message CorrectionContext {
  string request_id = 1;
  string completed_word = 2;
  string adjacent_left = 3;     // bounded, max 2 words
  string adjacent_right = 4;     // bounded, max 2 words
  capitalization capital = 5;
  string language_variant = 6;  // "en-US" or "en-GB"
}

message CorrectionDecision {
  string request_id = 1;
  correction_action action = 2;   // PRESCRIBE/SUGGEST/PRESERVE
  string corrected_word = 3;
  repeated string alternatives = 4;
  float confidence = 5;
  reason_category reason = 6;
  string rationale = 7;
}

message capitalization {
  string original_case = 7;
  bool is_sentence_start = 8;
  bool is_all_caps = 9;
}
```

### Key Design Decisions
- **Bounded context**: Only adjacent words are included; full sentences are never sent
- **Request ID correlation**: Allows matching responses to requests; enables cancellation
- **Cancellation support**: If a new word is typed before the engine responds, the stale request is discarded
- **Timeout**: 200ms deadline; requests exceeding the deadline are discarded
- **Error handling**: Pipe errors or deserialization failures result in "Preserve" decision (do nothing)

### Pipe Configuration
- Named pipe: `\\.\pipe\Typeright_Correction_IPC`
- Pipe mode: Message mode for bounded reads/writes
- Per-user security: Only the current user has access
- Timeout: 500ms read timeout; auto-discard on expiry

## Trade-offs
- **Positive**: Non-blocking TSF callback; responsive typing; clear versioned protocol; cancellable; measurable latency
- **Negative**: Added complexity of IPC infrastructure; minor overhead (~0.5-1ms per message); requires bridge service lifecycle management
- **Risk**: Pipe collision if multiple TypeRight instances run (mitigated by mutex); network-style protocol on local pipe adds slight latency

## Consequences
- **Positive**: Typing never waits for correction; suggestions appear promptly (150-200ms target); engine can be optimized independently; cancellation prevents stale suggestions
- **Negative**: Bridge service must be running for correction to work; if bridge crashes, corrections fallback to "Preserve"
- **Mitigation**: Bridge auto-restart; if bridge unavailable, TypeRight operates in "suggestions only, no auto-correct" mode

## References
- Protocol Buffers specification
- .NET named pipe classes (System.IO.Pipes)
- TSF edit-session constraints