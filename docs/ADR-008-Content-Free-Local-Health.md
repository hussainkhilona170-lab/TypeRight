# ADR-008: Content-Free Local Health Diagnostics

## Status

Accepted — implemented in the managed bridge, settings UI and local policy/configuration layers.

## Context

TypeRight needs a local support view to distinguish "no correction happened"
from "the bridge is unavailable", "policy denied", "the engine timed out" or
"a suggestion is still waiting for a click". The original client exposed rich
diagnostics that could include document text, element names and exception
details. That would violate TypeRight's local-first privacy guarantees if copied
into a health panel, log file or crash report.

## Decision

Health data is count-only and stays on the machine.

- `CorrectionPipeServer` maintains `Interlocked` counters for evaluations,
  pings, apply requests, denials, errors, engine timeouts, suggestions offered,
  suggestions resolved, active clients and pending suggestions. It also exposes
  a fixed pipe name and uptime duration.
- `ConfigurationSettings.GetPersonalWordCount()` uses `SELECT COUNT(*)` so the
  opt-in vocabulary is never read back into UI memory for diagnostics.
- `PolicyEvaluator` exposes only counts of blocked clients, blocked domains,
  integration rules and sensitivity rules. No policy value or rule text is
  exposed.
- `LocalFeatureGates.Snapshot()` contributes an active/total feature count.
- `LocalDiagnosticsSnapshot.Capture()` composes the counts and fails closed to
  zero local counts if any dependency is missing or throws.
- The WPF settings window renders these counts in a read-only card with a manual
  refresh button. It never displays a word, process name, domain, UI element,
  rule, setting value, path or exception text.

## Privacy invariants

- No user text, keystroke, document content, suggestion text, context field or
  exception message enters a diagnostic snapshot.
- No diagnostic data is sent over the network or written to a file by this
  feature.
- Missing or corrupt local state yields count `0`, never an error string.
- The pipe name is a fixed protocol identifier, not user data.

## Consequences

- Support can quickly see bridge liveness, fail-closed denials and timeout
  pressure without gaining access to what the user typed.
- New diagnostics must be counts, durations, fixed opcodes or closed-set states;
  any free-form diagnostic value requires a new ADR and privacy review.
- Native C++ diagnostics remain event-only until the MSVC/Windows SDK smoke test
  is available; they must follow the same content-free rule.
