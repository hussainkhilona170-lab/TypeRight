# ADR-002: Correction Engine Placement

## Status
Proposed

## Context
The correction engine is the intellectual core of TypeRight. It must be independent of UI frameworks (WPF) and Windows integration (TSF/keyboard hooks) to ensure testability, maintainability, and correct separation of concerns.

## Decision
The correction engine resides in the TypeRight.Engine project as a pure .NET 10 class library with no direct dependencies on WPF, TSF, or keyboard APIs. All input/output is via explicit interfaces.

## Considered Options

### Option A: Engine embedded in WPF UI project
- **Pros**: Simple to start, no cross-project referencing
- **Cons**: Violates separation of concerns; UI thread blocking possible; impossible to test without UI; correction logic tangled with presentation
- **Evidence**: Previous TypeRight implementation had correction logic embedded in view models, making unit testing extremely difficult and causing UI freezes during correction processing

### Option B: Engine as standalone .NET class library with interface contracts
- **Pros**: Complete separation; can be tested with xUnit; independent deployment; clear input/output contracts
- **Cons**: Requires defined interfaces for input/output; slightly more initial setup
- **Evidence**: Clean architecture patterns (Hexagonal/Clean Architecture) enable independent testing and replacement of components

### Option C: Engine as native C++ component
- **Pros**: Same language as TSF integration; potential performance benefits
- **Cons**: .NET 10 is the primary framework; mixing managed/unmanaged introduces COM marshalling complexity; harder to iterate rapidly
- **Evidence**: While the TSF integration will be native C++, the correction engine logic is algorithmic and fits naturally in .NET

## Selected Design
**TypeRight.Engine project contains:**
- `CorrectionRequest` - immutable input record (completed word, context, capitalization, language version, request ID)
- `CorrectionDecision` - immutable output record (action: Preserve/AutoCorrect/Suggest, alternatives, confidence, reason category)
- `ITokenizer` - interface for word tokenization
- `IDictionaryValidator` - interface for dictionary validation
- `ICandidateGenerator` - interface for candidate generation
- `ICandidateRanker` - interface for ranking candidates
- `IConfidenceCalculator` - interface for confidence scoring
- `ICorrectionDecisionMaker` - interface for final decision logic

**Dependency rule**: The engine may not reference TypeRight.Desktop, TypeRight.Windows, or any WPF/TSF projects. It may only reference standard .NET 10 APIs and TypeRight.Domain.

## Trade-offs
- **Positive**: Engine can be unit tested independently; UI can be swapped without engine changes; engine can be potentially ported to other .NET platforms
- **Negative**: Requires disciplined interface design; native TSF integration must translate to/from engine interfaces
- **Risk**: Interface bloat if too many details leak through; must guard against anemic domain model anti-pattern

## Consequences
- **Positive**: Correction algorithms are deterministic and locally testable; same input + same vocabulary state = same decision; engine can be verified in isolation before Windows integration
- **Negative**: Engine-to-TSF adaptation layer required; must ensure no circular dependencies

## Next Steps
1. Define CorrectionRequest and CorrectionDecision interfaces
2. Implement tokenizer, dictionary validator, candidate generator, ranker, confidence calculator
3. Implement correction decision logic with priority: High confidence → Auto-correct, Medium confidence → Suggest, Low confidence → Preserve