# ADR-003: Native/C# Communication Mechanism

## Status

Accepted — implemented as the line-oriented named-pipe bridge.

## Context

The native TSF component (C++) and the C# correction engine need a communication
architecture that does not block the application's input thread. TSF callbacks
run on the thread that owns the document, while the engine performs dictionary
lookups, candidate generation and ranking that must never stall typing.

## Decision

The TSF text service captures a completed word and posts a bounded request to a
managed background service over a local named pipe. The engine evaluates the
request on a worker thread, and the result is applied inside a fresh TSF edit
session. Corrections are only applied after the recorded text range is verified
again, so stale requests are discarded.

### Why not a synchronous call

- Direct calls or COM callable wrappers from a TSF callback block the host
  application's message loop and can cause typematic-repeat freezes.
- COM apartment rules make mixed STA/MTA processing fragile.

### Why not shared memory

- A ring buffer would add lifetime and cancellation complexity for a message
  size of a few hundred bytes; local pipe overhead is well below the typing
  latency budget.

## Implemented protocol

- Pipe: `\\.\pipe\TypeRight.Correction.3`, byte transmission mode, current-user
  access control only. Version 3 is required because `SUG` now carries a token
  and selection uses a second native-initiated `APPLY` request.
- Framing: one UTF-8 line per request and one UTF-8 line per response.
- Evaluation request: `EVAL\t<language>\t<word>\t<process>\t<domain>\t<element>`;
  a 3-field legacy form is accepted but fails closed when policy needs context.
- Auto response: `AUTO\t<word>`. Suggestion response:
  `SUG\t<token>\t<alternatives...>`. Terminal responses: `NONE`, `DENY`, `ERR`.
- Selection request: `APPLY\t<token>\t<process>\t<domain>\t<element>`. The bridge
  long-polls until the user clicks, the 5 s selection window expires, or policy
  changes, then returns `ACK\t<token>\t<status>\t<value>`. `status` is `OK`,
  `DENY` or `NONE`; `value` is either the exact offered replacement or a
  closed-set reason code. Tokens are opaque GUIDs, never user text.
- Timeouts: evaluation uses 250 ms native / 200 ms engine bounds; selection uses
  a 6 s native bound around the bridge's 5 s bound. The bridge always answers
  with a bounded line and never includes exception text or user text in logs.
- Policy and feature gates are enforced in the managed bridge before any
  decision is returned and again before a selection is acknowledged; `DENY`
  means no correction and no suggestion.

Protobuf was considered as a versioned schema but is not used: the line
protocol is simpler, bounded and sufficient for one completed word at a time.

## Consequences

- Typing never waits for the correction engine; a missing or slow bridge
  degrades to "preserve", never to a blocked input thread.
- Only the completed word and a language tag cross the pipe — no full document
  text, keystroke stream, telemetry or cloud egress.
- Both ends must agree on the pipe name and opcode set; changes require a
  version bump of the pipe name (`TypeRight.Correction.N`).
