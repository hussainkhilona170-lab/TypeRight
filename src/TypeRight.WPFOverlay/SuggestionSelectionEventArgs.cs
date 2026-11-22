using System;

namespace TypeRight.WPFOverlay
{
    /// <summary>
    /// Identifies which offered alternative the user clicked. The index is used
    /// for correlation so duplicate suggestion strings remain unambiguous.
    /// </summary>
    public sealed class SuggestionSelectionEventArgs : EventArgs
    {
        public int Index { get; }
        public string Text { get; }

        public SuggestionSelectionEventArgs(int index, string text)
        {
            Index = index;
            Text = text;
        }
    }
}
