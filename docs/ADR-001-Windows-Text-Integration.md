# ADR-001: Windows Text Integration Strategy

## Status
Proposed

## Context
The previous TypeRight implementation suffered from unreliable Windows-wide text interaction, particularly around identifying completed words, accessing text context, and performing safe text replacements. This was the main weakness that caused rough typing interactions and unreliable suggestions.

## Decision
Use the Windows Text Services Framework (TSF) as the primary text integration mechanism, with UI Automation as a fallback for applications that do not fully support TSF.

## Considered Options

### Option A: Native TSF Only
- **Pros**: Deep integration, proper caret tracking, edit sessions, supports most modern Windows applications
- **Cons**: Requires native C++ COM programming, complex threading model, application compatibility varies
- **Evidence**: TSF is the documented Windows mechanism for text input integration; used by Word, Notepad, Chrome, Edge

### Option B: UI Automation Only
- **Pros**: Works across more applications via UI Automation patterns
- **Cons**: Less reliable text range access, no native edit-session support, harder to track completed words, performance concerns
- **Evidence**: UI Automation text patterns are more generic and less suited for real-time typing integration

### Option C: Keyboard Hooks Primary
- **Pros**: Works in any application that accepts keyboard input
- **Cons**: Cannot read existing text, cannot determine caret position, cannot safely replace text, blocks typing, records keystroke histories (privacy concern)
- **Evidence**: Previous implementation relied heavily on this approach and produced unreliable suggestions

## Selected Design
**Primary: Windows Text Services Framework (TSF)**
- Native C++ COM text service for applications that support TSF
- Provides proper text range access, caret tracking, and edit-session management
- Enables safe text replacement via ITextStoreACF/ITextSelection interfaces
- Thread-safe when implemented correctly

**Fallback: UI Automation**
- Used only where TSF is unavailable or unreliable
- TextPattern and ValuePattern for text access
- No automatic text replacement; suggestions only (no inline correction)
- User explicitly acknowledges when falling back to this mode

**Unsupported Applications**
- If neither TSF nor UI Automation provides reliable text access, TypeRight leaves the text untouched
- No compensation by blindly injecting replacements
- User sees indication that TypeRight is active but not modifying text

## Trade-offs
- **Complexity**: TSF requires native C++ COM programming with strict threading rules
- **Coverage**: TSF does not cover every Windows application; fallback needed
- **Maintenance**: Native code requires careful lifetime management
- **Reliability**: When TSF works, it's the most reliable approach; fallback reduces coverage but increases safety

## Consequences
- **Positive**: Safe text replacement, proper caret positioning, focus-preserving popups
- **Negative**: Requires native C++ compilation and TSF registration; some applications may not be supported
- **Risk**: TSF initialization failure must fail closed (no text modification)

## References
- Microsoft Text Services Framework documentation
- TSF IDL definitions (msctf.idl, ctf.h)
- Previous TypeRight failure analysis: unreliable word completion, text range corruption, focus stealing