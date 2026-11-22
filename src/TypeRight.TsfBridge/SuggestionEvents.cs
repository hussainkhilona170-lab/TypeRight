using System;
using System.Collections.Generic;
using System.Linq;

namespace TypeRight.TsfBridge;

/// <summary>
/// Raised when the bridge has suggestions that a UI can display. The token is
/// an opaque correlation id; it is never user text.
/// </summary>
public sealed class SuggestionOfferedEventArgs : EventArgs
{
    public string Token { get; }
    public string Language { get; }
    public string Word { get; }
    public string Process { get; }
    public string Domain { get; }
    public string Element { get; }
    public IReadOnlyList<string> Suggestions { get; }

    public SuggestionOfferedEventArgs(string token, string language, string word, string process,
                                      string domain, string element, IReadOnlyList<string> suggestions)
    {
        Token = token;
        Language = language;
        Word = word;
        Process = process;
        Domain = domain;
        Element = element;
        Suggestions = (suggestions ?? Array.Empty<string>()).ToArray();
    }
}

/// <summary>
/// Raised when a pending suggestion resolves, so a UI can dismiss its overlay.
/// </summary>
public sealed class SuggestionResolvedEventArgs : EventArgs
{
    public string Token { get; }
    public string Status { get; }

    public SuggestionResolvedEventArgs(string token, string status)
    {
        Token = token;
        Status = status;
    }
}
