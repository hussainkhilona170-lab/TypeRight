# ADR-007: Porting observed client features into TypeRight

**Status:** Accepted
**Date:** 2026-11-21
**Decision:** Adopt the locally implementable feature patterns observed in the Grammarly client artifacts, without adopting its cloud egress or telemetry model.

## Evidence boundary

This ADR is based on static analysis of official client artifacts under `C:\GrammarlyRE` and the accompanying research briefs. No logged-in session was used, no server-side code or model weights were available, and .NET assemblies were inspected via metadata and strings rather than full IL decompilation. Server-side behaviour is therefore not claimed here.

Evidence tiers match the source briefs: `[OBSERVED]`, `[INFERRED]`, `[UNKNOWN]`.

## What was copied, and where

| Observed feature | Evidence | TypeRight implementation | Local adaptation |
|---|---|---|---|
| 584-rule integration/sensitivity configuration (`IntegrationOptions.json`) | `[OBSERVED]` `desktop-payload/.../Resources/Configuration/IntegrationOptions.json` | `src/TypeRight.Privacy/IntegrationOptions.json`, `IntegrationRule.cs`, `IntegrationRuleset.cs` | Rule file copied verbatim (UTF-8). All 81 `IsSensitivityCheck` rules compiled; non-sensitivity records are browser/UIA rendering overrides and are not applied as native correction gates. DLP toggle in `EnterprisePolicy` controls evaluation. |
| Enterprise policy surface (enrolment token, blocked clients/domains, DLP, confidential mode, integration mode) | `[OBSERVED]` `extension/src/schema.json` | `src/TypeRight.Privacy/EnterprisePolicy.cs` (includes `PolicyEvaluator`) | Registry-backed under `HKLM/HKCU\SOFTWARE\Policies\TypeRight`. Every path fails closed. |
| Quill Delta insert/retain/delete document model | `[OBSERVED]` `Delta-*.js`, `Grammarly.Domain.QuillDelta.dll` | `src/TypeRight.Documents/DocumentDelta.cs` (includes `DeltaBuilder`) | Pure .NET; no UI dependency. Deltas carry the revision they were computed against. |
| diff-match-patch based local change computation and range rebasing | `[OBSERVED]` `Grammarly.Domain.QuillDelta.dll`; `[INFERRED]` used to rebase ranges while typing | `src/TypeRight.Documents/DocumentDelta.cs` (`TextDiffer`), `TextRange.Rebase` in `RevisionedDocument.cs` | Common-prefix/suffix differ is sufficient for word replacement and is deterministic. Stale revisions are rejected. |
| Apply safety checks (`IsAlertSafeToApplyTextOperations`, `IsSubAlertContextLengthPreserved`) | `[OBSERVED]` `Grammarly.Domain.TextChecking.dll` | `RevisionedDocument.TryApply`, `IsReplacementSafe`, `BuildReplacement` | Refuses stale revisions, wrong base length, empty/out-of-range replacements. |
| SuperAlert / SubAlert / transformJson alternatives / priority / detected language | `[OBSERVED]` `Grammarly.Domain.Models.Suggestions.dll`, `Grammarly.Domain.Models.Transforms.dll` | `src/TypeRight.Suggestions/SuggestionAlert.cs` | Alerts hold identifiers, ranges, alternatives and metadata. No text is persisted. |
| Alert range rebasing across revisions (`AlertRebase`, `SuperAlertRanges`) | `[OBSERVED]`/`[INFERRED]` | `src/TypeRight.Suggestions/AlertRebase.cs`, `SuggestionFeed.Rebase` | Alerts survive inserted text; deleted alerts are acknowledged and removed. |
| Feedback taxonomy (`AcceptAlert`, `DismissAlert`, `AlertAddToDictionary`, Vbar feedback, ...) | `[OBSERVED]` `Grammarly.Services.CoreApi.dll`, `TextChecking.dll` | `FeedbackKind`, `FeedbackEvent`, `SuggestionFeed.FeedbackCounts` | Recorded in memory only. No upload, no disk, no user text. |
| Suggestion categories (Correctness, Clarity, Engagement, Delivery, ...) | `[OBSERVED]` `Grammarly.SDUI.Core`, agent panels | `src/TypeRight.Domain/Models/SuggestionCategory.cs` (includes `SuggestionCategoryMapper`) | Maps engine reason categories to a user-facing feed taxonomy. |
| Agent/feature matrix with async checks | `[OBSERVED]` `18079.libs.chunk.js`, `agentPanel/*` keys | `src/TypeRight.Suggestions/LocalFeatureGates.cs` | Only offline-capable categories are active. Cloud-dependent agents (Plagiarism, AI Detector, Humanizer, GenerativeRewrite, Citations, Translate, Resume, Style Guide sync) are hard-gated off because `IsLocallyAvailable` returns false. |
| Authorship provenance (typed vs pasted vs remote/generated) | `[OBSERVED]` `Grammarly.Domain.TextChecking.Authorship.*` | `src/TypeRight.Documents/ProvenanceTracker.cs` | Metadata only (offset, length, origin, timestamp). Disabled by default; never writes text, disk or network. It is not wired into the TSF input path. |

## What was deliberately not copied

- The CAPI HTTP/WebSocket checking service (`/api/check`, `/api/check/aidetector`, `/submit_ot`) — TypeRight is local-first and sends no text off the machine.
- Server-driven UI, remote alert configuration (`AuthHook`), pushed content and rewrite caches (`TrustedRewrite`, `VBars`) — these require a server.
- Streaming generation (Cheetah), TextActions rewrite scopes, and generative insert telemetry — no local model exists and no keystroke or content logging is permitted.
- Site-specific content-script adapters — TypeRight integrates through native TSF, not browser DOM injection.
- Keystroke capture — the local provenance tracker is opt-in metadata only and off by default.

## Consequences

- The correction engine remains independent of WPF and Windows APIs; all new document and suggestion code targets `net10.0`.
- Runtime enforcement exists at two layers: `PolicyEvaluator`/DLP in the bridge and `LocalFeatureGates` for the proofreader switch. Both deny by default.
- The 584-rule file is shipped with the app; `IntegrationOptions.json` must remain next to `TypeRight.dll` in published output.
- Tests: `TypeRight.Domain.Tests` (17), `TypeRight.Engine.Tests` (66), `TypeRight.Documents.Tests` (46), `TypeRight.Privacy.Tests` (12), `TypeRight.Configuration.Tests` (5) and `TypeRight.TsfBridge.Tests` (23) — 169 total — cover rule loading, policy decisions, delta application, rebasing, safe apply, provenance privacy, suggestion lifecycle, feedback taxonomy, feature gates, capitalization detection, correction decisions, candidate generation/ranking, personal-word persistence, tokenised suggestions, APPLY/ACK resolution and pipe-protocol safety.
