# TypeRight Plan

**Last updated:** 2026-11-22  
**Repository:** https://github.com/hussainkhilona170-lab/TypeRight  
**Status:** Active development on `master`

This file is the single source of truth for the current feature set, work in
progress, upcoming work, goals and long-term vision. It must be updated in
**every commit**. Each commit must be pushed to GitHub, and its commit link must
be recorded in the commit history below so decisions and code can be traced
back to a specific change.

---

## Vision

TypeRight is a **local-first Windows typing companion**. It should quietly help
people write better in any application through native Windows Text Services
Framework (TSF) integration, without sending text anywhere, logging keystrokes,
stealing focus, or weakening the user's control over their own document.

The long-term vision is a trustworthy writing layer that is:

- **Private by default** — no cloud checking service, no telemetry, no content upload.
- **Safe before clever** — never mutate text on stale, ambiguous or unauthorised context; fail closed.
- **Native and unobtrusive** — work inside the typing session via TSF, not by scraping text or hijacking windows.
- **Explainable and controllable** — clear correction reasons, visible pause/resume, local settings.
- **Testable and deterministic** — the engine and document model remain independent of WPF and Windows APIs.

TypeRight deliberately does **not** aim to copy the cloud/telemetry model of
commercial writing assistants. Features that require a server are out of scope
unless they can be implemented locally and privately.

---

## Product principles

1. **No text egress.** User text must never leave the machine. Diagnostics and logs contain opcodes, lengths and reasons only — never content.
2. **Fail closed.** Missing policy context, malformed requests, stale revisions or unknown feature gates deny correction rather than guessing.
3. **TSF-only replacement.** Text changes go through proper TSF edit sessions with validated ranges; no direct document mutation from background threads.
4. **User in control.** Pause/resume, exclusions, confidence threshold, suggestions toggle and personal learning are local and discoverable.
5. **Separation of concerns.** Domain, Engine, Documents, Suggestions and Privacy stay free of UI dependencies so they can be tested headlessly.
6. **Every commit is traceable.** `plan.md` is updated with the change summary and the pushed GitHub commit link.

---

## Current features

### Windows text integration

- Native TSF text service under `src/TypeRight.TSF.Native`.
- Local named-pipe bridge `TypeRight.Correction.3` connecting native TSF to the managed engine.
- Versioned 6-field `EVAL` request: language, word, process, domain and focused UI element; legacy 3-field requests are parsed but denied when policy requires context.
- Token-bearing `SUG` response plus native-initiated `APPLY`/`ACK` selection with closed-set reason codes and rechecked gates.
- Malformed, oversized, wrong-opcode and unknown-status inputs fail closed; ACK values use the full word byte budget so offered replacements are not truncated.
- Bounded UTF-8 framing, sanitised fields, per-request engine timeout and a bounded concurrent-client cap.
- Current-user-only pipe ACL; no user text in logs or diagnostics.
- Legacy `TypeRightEngine` wrapper is wired to the real engine pipeline with fail-closed fallback on fault.

### Correction engine

- Pure .NET 10 engine with no WPF/TSF dependency.
- Unicode-safe tokenizer and casing/all-caps detection.
- Embedded common-English vocabulary loaded synchronously and deterministically.
- Dictionary-driven candidate generation using Damerau-Levenshtein distance; no unbounded a–z edit enumeration.
- Context-aware candidate ranking using frequency, adjacent words, common pairs and QWERTY adjacency.
- Shared confidence thresholds and explicit low-confidence preservation.
- Regional British/American spelling handling with per-pair preservation/correction policy.
- `Auto`, `Suggest` and `Preserve` decisions with reason categories and rationale.
- Personal-word support through `EngineComposition.CreateDecisionMaker(personalWords)` and `CreateCandidateGenerator(personalWords)`.
- `ReloadableDecisionMaker` refreshes the live engine pipeline from the local dictionary without restarting the app or replacing the bridge's stable reference.

### Document and suggestion safety

- Quill-style insert/retain/delete deltas with revision-aware application.
- `RevisionedDocument` rejects stale revisions, malformed deltas, bad base lengths and unsafe replacements.
- Exact op-shape validation for replacements, not just matching aggregate lengths.
- Range rebasing across inserts/deletes; fully deleted ranges are removed.
- Opt-in authorship provenance metadata; off by default, never written to text/disk/network.
- Suggestion feed with super-alerts/sub-alerts, alternatives, priority ordering, feedback counts and safe range validation.
- Local feature gates: undefined keys fail closed and cloud-dependent agents are hard-disabled.

### Privacy and enterprise policy

- Registry-backed enterprise policy under `HKLM/HKCU\SOFTWARE\Policies\TypeRight`.
- Blocked clients, blocked domains, confidential mode, DLP and integration mode.
- 584-rule integration/sensitivity configuration with 81 sensitivity rules compiled locally.
- `PolicyEvaluator.RequiresContext` forces fail-closed behaviour for legacy context-free requests.
- Excluded-application, confidence-threshold and suggestions-visible settings enforced in the bridge.

### Desktop, tray and overlay

- WPF tray application with status, active-feature count, Open, Settings, Pause/Resume and Exit actions.
- Embedded/copied tray icon with safe fallback.
- Settings window for language variant, confidence threshold, personal learning, exclusions and suggestion visibility, including an “Add word” action for the local dictionary.
- Non-activating, non-focusable, single-instance suggestion overlay with work-area clamping, indexed selection, Dispatcher marshalling, auto-dismiss and no taskbar button.
- Overlay selection resolves through the bridge to a token-validated TSF replacement; stale, paused or unoffered values are rejected before any edit.
- Generic startup error messages; exception text and paths are never shown to the user.

### Configuration, persistence and tooling

- JSON settings load/save with safe defaults for malformed or missing files.
- Optional SQLite personal-vocabulary schema, created only when personal learning is enabled.
- Case-insensitive personal-word round-trip: canonical storage, frequency updates, corrupt-file fallback and startup load into the engine.
- WiX installer scripts, license page, uninstall registry cleanup and path-independent PowerShell tooling.
- Toolchain diagnostic script and local engine host.
- Removed the flagged/unreadable `tools/screenshot.ps1`; use built-in Windows screenshot tools instead.

### Current verification

| Check | Result |
|---|---|
| `dotnet build TypeRight.slnx --nologo -v minimal` | 0 warnings, 0 errors |
| `dotnet test TypeRight.slnx --nologo -v minimal` | 186 passed, 0 failed, 0 skipped |
| Test projects | Domain 17, Engine 66, Documents 46, Privacy 12, Configuration 5, TsfBridge 40 |
| Native C++ build | Not run locally — MSVC/Windows SDK are not installed in this environment |

---

## Work in progress

These are implemented or partially implemented but still need end-to-end
validation or wiring before being considered done.

1. **Native TSF toolchain validation.** The v3 protocol, token correlation and TSF lifetime handling are written, but the C++ projects cannot be compiled locally without MSVC and the Windows SDK. A real-app smoke test is still required.
2. **Overlay polish.** Mouse selection, auto-dismiss, pause dismissal and single-instance reuse are wired; keyboard navigation, host-deactivation dismissal and accessibility polish are still open.
3. **Installer/release validation.** Scripts parse and the WiX document is well-formed, but a clean machine install/upgrade/uninstall run has not yet been performed. Signing and auto-update are not implemented.
4. **Local feature matrix expansion.** Cloud-dependent agents are correctly gated off; additional offline-correctness and local-style checks need careful privacy review before activation.
5. **Native diagnostics without content.** Event-only diagnostics exist; a local, content-free health view would improve supportability without introducing telemetry.

---

## Upcoming features and roadmap

### Next

- [ ] Compile and smoke-test the native TSF components with MSVC/Windows SDK on a Windows desktop session.
- [x] Wire `SUG` responses through the desktop app into the overlay and apply the selected replacement via a validated TSF edit.
- [x] Load personal SQLite words into the engine at startup and add words from the local settings UI.
- [x] Add protocol fuzz/integration tests for truncated, oversized and malformed pipe input.
- [ ] Run a full installer install → upgrade → uninstall verification on a clean VM.

### Later

- [ ] Expand the embedded vocabulary and frequency data with curated, versioned, offline resources.
- [ ] Add local grammar/style rules behind `LocalFeatureGates`, each with an explicit privacy review.
- [ ] Add keyboard navigation and accessibility polish to the suggestion overlay.
- [ ] Add a local diagnostics panel showing policy/rule counts and bridge health — no text, no network.
- [ ] Add release signing, deterministic versioning and a documented update path.
- [ ] Publish architecture/API documentation and ADR statuses for implemented features.

---

## Goals

### Correctness and safety

- No stale, malformed or unverified replacement may modify user text.
- Every policy path that lacks required context denies correction.
- All text mutations go through TSF edit sessions with validated ranges and revisions.
- Maintain zero confirmed document-corruption defects.

### Privacy

- Zero network calls from the correction path.
- Zero keystroke logging.
- Zero user text in logs, diagnostics, crash messages or persisted feedback.
- Provenance and personal learning remain opt-in and local.

### Reliability and performance

- Bridge engine evaluation timeout remains bounded (currently 200 ms) and concurrent clients remain capped (currently 8).
- A slow or crashed engine must never block the TSF callback indefinitely.
- Overlay must never activate, steal focus or appear off-screen.

### Engineering quality

- Managed solution builds with 0 warnings and 0 errors.
- Full managed test suite stays green on every commit; current baseline is 186 tests.
- New behaviour requires tests; safety-critical behaviour requires negative tests.
- `plan.md`, `README.md` and relevant ADRs are kept consistent with the code.

### Delivery discipline

- No commit is left local-only: every commit is pushed to `origin/master`.
- Every commit updates this file with the change summary and GitHub link.
- Each commit message states what changed and how it was verified.

---

## Architecture boundaries

| Layer | Responsibility | Must not do |
|---|---|---|
| `TypeRight.Domain` | Request/decision models, suggestion taxonomy | Depend on UI, IO or Windows APIs |
| `TypeRight.Engine` | Tokenisation, candidates, ranking, confidence, decisions | Read user files, log text, call the network |
| `TypeRight.Configuration` | Local settings and opt-in personal vocabulary storage | Log settings values or expose user text |
| `TypeRight.Documents` | Deltas, revisions, rebasing, provenance | Mutate live application documents |
| `TypeRight.Suggestions` | Alert feed, feedback, feature gates | Persist user text or upload feedback |
| `TypeRight.Privacy` | Enterprise policy and integration rules | Allow correction on unknown context |
| `TypeRight.TsfBridge` | Named-pipe protocol and enforcement | Trust untrusted input or surface exception text |
| `TypeRight.Desktop` / `TypeRight.WPFOverlay` | Tray UI, settings, overlay | Perform unsafe document edits directly |
| `TypeRight.TSF.Native` | TSF integration and bounded pipe client | Log or transmit user content |

---

## Commit history

Rules for this table:

1. Add a row **in the same commit** that changes product behaviour or project plans.
2. Push the commit immediately after committing.
3. Replace the `Link` cell with the exact GitHub commit URL once pushed.
4. Never rewrite the history table to hide a change; append a follow-up row instead.

| Date | Commit | Summary | Verification | Link |
|---|---|---|---|---|
| 2026-11-21 | `15810cc` | Port local Grammarly feature patterns: sensitivity rules, policy gates, revisioned deltas, alert feed and feature matrix | Baseline build/tests | [15810cc](https://github.com/hussainkhilona170-lab/TypeRight/commit/15810cc) |
| 2026-11-21 | `e09d3e4` | Harden correction pipeline: real engine wiring, embedded vocabulary/ranking/rebasing safety, 6-field pipe protocol with fail-closed policy, tenant-safe tooling/installer, overlay/tray fixes, TsfBridge tests, and establish `plan.md` as the project source of truth | Build 0 warnings/0 errors; 153/153 tests passed | [e09d3e4](https://github.com/hussainkhilona170-lab/TypeRight/commit/e09d3e4490fc9e30d600090e186a3dfecd1ed372) |
| 2026-11-22 | `952b4af` | Complete the opt-in personal dictionary round-trip: safe SQLite load/store with canonical case-insensitive words, reloadable engine, settings UI “Add word” action, Configuration test project and plan/test-count sync | Build 0 warnings/0 errors; 160/160 tests passed | [952b4af](https://github.com/hussainkhilona170-lab/TypeRight/commit/952b4afbd9b621a978818e8ea43e986a943ba095) |
| 2026-11-22 | `986e64c` | Wire the overlay-to-replacement loop end to end: protocol v3 with token-bearing `SUG`, native `APPLY`/`ACK`, Dispatcher-marshalled indexed overlay selection, gate rechecks, TSF reference lifetime/invalidation fixes and 9 new protocol tests | Build 0 warnings/0 errors; 169/169 tests passed; native C++ smoke test still pending | [986e64c](https://github.com/hussainkhilona170-lab/TypeRight/commit/986e64ce5005a75b21e1bb2aac299f8a77ae9ddf) |
| 2026-11-22 | Current | Harden protocol parsing with 17 fuzz/edge-case tests for malformed, oversized, wrong-opcode and unknown-status lines; reject unknown ACK statuses, preserve full word-byte budgets for replacements and verify fail-closed UI/pause/cancellation paths | Build 0 warnings/0 errors; 186/186 tests passed | [Repository history](https://github.com/hussainkhilona170-lab/TypeRight/commits/master) |

> At push time, the exact commit URL for the current row is reported in the push output and final handoff. Future commits must replace the history link with their own exact GitHub commit URL.

---

## Definition of done

A feature is done only when:

- [ ] The behaviour is implemented behind the correct layer boundary.
- [ ] Privacy and fail-closed rules are verified.
- [ ] Tests are added/updated and the full suite passes.
- [ ] `plan.md` reflects the new status and includes this change.
- [ ] The commit is pushed and its GitHub link is recorded.
