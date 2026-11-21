# TypeRight

Local-first Windows typing-correction tool. TypeRight evaluates a single completed
word, offers auto-correction or suggestions, and keeps all text on the machine —
no cloud checking service, telemetry or keystroke logging.

See `docs/ADR-*.md` for the architecture decisions behind the Windows text
integration, correction engine placement, native/C# bridge, range validation,
suggestion overlay, persistence and locally ported client features.

## Build and test

Requires the .NET 10 SDK (`net10.0` and `net10.0-windows` targets).

```powershell
dotnet build TypeRight.slnx
dotnet test TypeRight.slnx
```

`tools/check-toolchain.ps1` reports the available compilers, SDKs and Windows
Kits. The C++ TSF components under `src/TypeRight.TSF.Native` and
`src/TypeRight.TSFWrapper/TSF` additionally require MSVC and the Windows SDK;
the managed solution builds without them.

## Layout

| Path | Purpose |
|---|---|
| `src/TypeRight.Domain` | Request/decision models and suggestion taxonomy |
| `src/TypeRight.Engine` | Tokenization, candidate generation/ranking, confidence and decisions |
| `src/TypeRight.Documents` | Delta-based document edits, revisioning, authorship metadata |
| `src/TypeRight.Suggestions` | Local alert feed, feature gates and range rebasing |
| `src/TypeRight.Privacy` | Enterprise policy and 584-rule integration/sensitivity configuration |
| `src/TypeRight.TsfBridge` | Named-pipe server connecting the native TSF text service to the engine |
| `src/TypeRight.Desktop` | WPF tray application and settings UI |
| `src/TypeRight.WPFOverlay` | Suggestion overlay window |
| `installer`, `tools` | Installer scripts, diagnostics and the engine host |

## Tests

`dotnet test TypeRight.slnx` runs the Domain (17), Engine (64), Documents (46),
Privacy (12) and TsfBridge (14) test projects — 153 tests total.
