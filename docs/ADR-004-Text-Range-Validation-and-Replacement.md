# ADR-004: Text Range Validation and Replacement

## Status
Proposed

## Context
Text modification is the highest-risk operation in TypeRight. A faulty replacement can corrupt the user's document, destroy surrounding text, or cause the application to crash. The previous TypeRight implementation had instances of incorrect range calculation and surrounding text corruption.

## Decision
All text replacement must go through valid TSF edit-session mechanisms. Never directly modify an application document from an unsafe callback or arbitrary background thread. Four validation checks must pass before any replacement is attempted.

## Considered Options

### Option A: Direct text assignment (e.g., `richTextBox.Text = newText`)
- **Pros**: Simple; immediate visual result
- **Cons**: Bypasses application undo stack; can corrupt rich text formatting; may trigger application-specific event handlers; not TSF-compliant; previous TypeRight had bugs with this approach
- **Evidence**: Previous implementation replaced text via direct document access, causing formatting loss in Word and unexpected insertion sequences in Notepad

### Option B: TSF ITextStoreACF edit session (SELECTED)
- **Pros**: Respects application undo stack; preserves formatting; proper caret management; application-aware; rollback if edit session fails
- **Cons**: Requires valid TSF edit-session interface; must verify interface availability; must revalidate text range before applying; slightly more code
- **Evidence**: TSF edit sessions are the documented way to modify text in a target application; Word, Notepad, Chrome all support this pattern

### Option C: UI Automation pattern editing
- **Pros**: Works in applications without TSF
- **Cons**: Not all applications support editable UI Automation patterns; harder to preserve caret position; no standardized undo integration
- **Evidence**: UI Automation editing support varies wildly by application; not reliable as primary mechanism

### Option D: Kernel-level direct memory modification
- **Pros**: Bypasses all API limitations
- **Cons**: Extremely dangerous; can crash system; violates Windows security model; not viable
- **Evidence**: N/A

## Selected Design
**Four-Point Validation Before Replacement**

### Check 1: Target Application Still Active
- The target window handle (HWND) is still valid and foreground
- The application is not in a state where text modification would be inappropriate (e.g., read-only dialog, password field)
- Verification: GetForegroundWindow() comparison; IsWindowEnabled(); check for password character masking

### Check 2: Relevant Context Still Exists
- The text surrounding the original word is still present and matches
- The caret position has not moved independently
- Verification: Re-read the text range from TSF `ITextRange`; compare surrounding characters

### Check 3: Original Word Still Matches
- The word being replaced is still the same text (user may have typed additional characters, backspaced, or the application may have modified it)
- Verification: Get the current `ITextRange` text and compare against the word that triggered the correction

### Check 4: Intended Text Range Remains Valid
- The start and end positions of the word to replace are still valid within the document
- The replacement text does not exceed document bounds
- Verification: Calculate replacement range from current text; verify start >= 0 && end <= text.Length

### Edit-Session Workflow
```
1. Acquire ITextEditSession2 from TSF
2. Call BeginEditSession() 
3. Verify all 4 checks against current text
4. If all pass: Call ReplaceRange(start, end, replacementText)
5. Call EndEditSession() with commit
6. If any check fails: Call AbortEditSession() - no changes made
7. If ITextEditSession2 unavailable: Do nothing (fail closed)
```

### Caret Position Preservation
- Before replacement: Record current caret position from TSF
- After replacement: Restore caret to the position corresponding to what would be after the replacement text
- If replacement is at word boundary: Caret moves past the replacement
- If replacement is inline: Caret stays at same visual position

### Surrounding Text Preservation
- **Delimiters**: Word boundaries (spaces, punctuation) are preserved
- **Whitespace**: Leading/trailing whitespace around the word is preserved
- **Punctuation**: Trailing punctuation (.,!?,;:) is preserved in place; not included in replacement
- **Capitalization**: First-letter capitalization of sentence start is preserved; all-caps preservation is analyzed

### Immediate Undo
- TSF edit sessions automatically participate in the application's undo stack
- No separate undo metadata needed; the edit session itself provides undo
- If the application does not support undo, TypeRight records the original text in a transient in-memory cache (not persisted) for one undo keystroke (Ctrl+Z)

## Trade-offs
- **Positive**: Safe replacement; respects application undo; preserves formatting; rollback on failure; no document corruption
- **Negative**: Requires valid TSF interfaces; four validation checks add slight latency; fails closed if any check fails (user may perceive "nothing happened")
- **Risk**: If all four checks pass but replacement still corrupts text, this indicates a TSF bug; document and fallback to "suggestion only, no auto-correct"

## Consequences
- **Positive**: Users can undo corrections with Ctrl+Z; surrounding text never corrupted; application compatibility via TSF; immediate undo built-in
- **Negative**: Some applications may not provide valid ITextEditSession2; in those cases, TypeRight only shows suggestions, not auto-corrections
- **Mitigation**: Fallback to suggestion mode (no auto-correct) when edit session not available

## References
- Microsoft ITextEditSession2 interface
- TSF edit session best practices
- Previous TypeRight corruption incidents: surrounding text deleted, caret position lost, undo stack broken