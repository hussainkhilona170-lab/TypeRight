# ADR-005: Suggestion Overlay Implementation

## Status
Accepted — non-activating overlay is wired to the managed bridge and native TSF replacement.

## Context
The suggestion overlay is the primary user-facing feature of TypeRight. It must be unobtrusive, focus-preserving, and reliable. The previous TypeRight implementation had issues with focus stealing, flickering, duplicate windows, and inappropriate positioning.

## Decision
Use a lightweight, non-activating WPF popup that appears above the target application without stealing focus. The popup does not become the active window; it is merely visible. All interaction is through mouse or keyboard within the popup, then dismissal returns focus to the application.

## Considered Options

### Option A: Standard WPF Window activated (Activated=True)
- **Pros**: Simple to implement; WPF window infrastructure handles Z-order, focus, keyboard routing
- **Cons**: Steals focus from target application; taskbar button appears; Alt+Tab switches to it; user experience jarring; violates "work quietly in the background" philosophy
- **Evidence**: Previous TypeRight used activated windows; user complaints about focus disruption; accidental activation of popup when user was mid-typo

### Option B: WPF Popup with Focusable=True, AllowsTransparency=True
- **Pros**: Popup is part of WPF plumbing; can be positioned relative to caret; mouse interaction works
- **Cons**: Still creates a window that can receive focus; keyboard routing still possible; Z-order management complex across DPI boundaries; multi-monitor issues
- **Evidence**: WPF Popup is the common approach but requires careful property setting to avoid focus issues

### Option C: Non-activating overlay (SELECTED)
- **Pros**: Does not steal focus; no taskbar button; Alt+Tab does not switch to it; modeless; appears "in world" above the application; user can type around it; dismiss with Escape or click outside
- **Cons**: Requires custom window styling to remove caption/frame; must handle Z-order manually; DPI aware positioning; WM_PARENTCHAIN handling
- **Evidence**: This is the correct approach for a typing companion; the popup should be informational, not interactive in the window sense

## Selected Design
**WPF Transparent, Topmost, Non-Activating Popup Window**

### Window Style
- `AllowsTransparency="True"` - enables chrome-less window
- `WindowStyle="None"` - no caption, no border, no minimize/maximize
- `Topmost="True"` - stays above the target application
- `ShowInTaskbar="False"` - no taskbar button
- `Focusable="False"` - does not receive keyboard focus
- `IsHitTestVisible="True"` - mouse events pass through, but popup content receives mouse clicks
- `Opacity="0.95"` - near-invisible background; content is solid

### Window Construction
- **Root element**: `<Grid>` with `Background="Transparent"`
- **Content**: StackPanel with correction information
- **No Chrome**: Title bar, system menu, borders removed
- **DPI awareness**: `Usage="PerMonitorAwareness"` in manifest; visual DPI scaling handled by WPF

### Positioning Strategy
1. **Primary**: Caret-relative positioning
   - Get caret location from TSF `ITextRange` coordinates
   - Convert to screen coordinates
   - Position popup anchor at caret bottom-right corner
   - If insufficient horizontal space (right edge of screen): anchor at caret bottom-left
   - If insufficient vertical space (below screen): anchor at caret top-right or top-left

2. **Fallback**: UI Automation bounding rectangle
   - If TSF caret position is unavailable
   - Get bounding rectangle from UI Automation TextPattern
   - Use rectangle bottom as anchor point

3. **Secondary**: Screen center (last resort)
   - Only if neither TSF nor UI Automation provides position
   - Center of primary monitor; user can drag if needed

### Appearance
- **Maximum width**: 300px (content-driven; never wider than needed)
- **Height**: Content-driven; typically 40-120px
- **Background**: Transparent; subtle shadow via `BitmapEffect` or `GraphicsComposition`
- **Typography**: Small (11-12pt), clear font; monospaced or app-matching font
- **Color scheme**: Light on dark; accent color (purple/blue) for corrected word; alternatives in muted gray
- **Shadow**: 2px soft shadow to separate from background

### Interaction Model
- **Mouse acceptance**: Click on suggested word → replace and dismiss
- **Keyboard acceptance**: 
  - Up/Down arrows: Navigate alternatives
  - Enter: Accept selected suggestion
  - Esc: Dismiss without replacing
  - Tab: Accept and move focus back to application
- **Click outside**: Dismiss without replacing (popup closes on mouse up outside bounds)
- **Timeout**: Auto-dismiss after 5 seconds if no interaction
- **Immediate undo**: Ctrl+Z after replacement; original text restored

### Z-Order Management
- `Topmost=True` ensures popup stays above application
- But: Does not add to Alt+Tab cycle
- Does not steal focus: `Focusable=False` + `IsHitTestVisible=True` on content only
- On lost focus (application activates): Popup remains visible if still relevant; fades if application switch detected

### DPI Awareness
- Manifest `windowsSettings`: `dpiAwareness="PerMonitorAware"`
- All measurements converted to device-independent units (1/96 inch)
- On high-DPI displays: WPF handles scaling; popup size remains consistent visually
- Monitor boundary checking: popup does not extend beyond monitor edges

### Flicker Prevention
- **Single window instance**: Reuse existing popup; do not create new window per word
- **Double-buffering**: `CacheMode="BitmapCache"` on root element
- **No animation**: No fade-in/out; appear instantly; disappear instantly
- **InvalidateVisual()** only when content changes

### Reduced-Motion Support
- Respect Windows **Ease of Access → Show animations** setting
- If reduced-motion is enabled: instant appear/dismiss; no timing-based auto-dismiss (use 10s timeout or manual only)
- No spring-animations for positioning

### Popup Lifecycle
```
1. Completed-word detection (TSF)
2. Correction engine request (IPC)
3. Decision received
4. If decision action = SUGGEST: Show popup
5. If decision action = AUTO_CORRECT: Apply replacement directly (no popup needed, or optional quick-flash)
6. User interaction: accept/dismiss
7. Timeout: auto-dismiss after configurable period
8. New word: hide previous popup, show new one
9. Application deactivation: fade or dismiss
```

### Implemented selection flow

- A `SUG` response carries an opaque token. The bridge raises `SuggestionOffered`;
  the desktop app marshals it to the Dispatcher and reuses one overlay window.
- Clicking an alternative raises an indexed selection event (duplicates remain
  unambiguous) and calls `TryResolveSuggestion`. Gates are rechecked before the
  bridge returns `ACK\t<token>\tOK\t<replacement>`.
- The native service keeps the captured TSF context alive with an explicit
  reference, waits on a separate `APPLY` connection, verifies the token and that
  the replacement was actually offered, then applies it in a fresh `SELECT`
  edit session. The existing offset/word staleness check still applies.
- Document detach, focus change, pause, timeout or a new offer dismisses the
  pending suggestion. The overlay sets `ShowInTaskbar="False"` and never calls
  `Activate()` or `Focus()`.
- Suggestions render as accessible buttons with stable automation IDs and names.
  Arrow keys move between them only when the overlay already has keyboard focus;
  Enter applies through the indexed event, Escape raises a dismissal request, and
  no global key hook or focus-stealing call is used.
- Caret coordinates are not in the protocol; the managed app anchors near the
  focused UI Automation element, then the pointer, then the work-area centre.

## Trade-offs
- **Positive**: Focus-preserving; no taskbar interference; works across monitors; DPI-aware; reduced-motion compatible; flicker-free; click-through for mouse events; immediate undo
- **Negative**: Requires custom window styling; DPI boundary checking; Z-order gotchas on some WM combinations
- **Risk**: Popup appearing over password fields or sensitive inputs; must check context before showing

## Consequences
- **Positive**: Users can type naturally without interruption; popup is informational, not disruptive; consistent across applications; undo available; accessible via keyboard
- **Negative**: Some users may not notice popup immediately; must balance visibility vs. intrusiveness
- **Mitigation**: Configurable popup timeout; optional auto-correct mode that doesn't show popup; user can disable suggestions entirely

## References
- WPF Windowless window technique
- Topmost window Z-order semantics
- Windows reduced-motion accessibility settings
- Previous TypeRight focus-stealing complaints and flickering issues