namespace TypeRight.WPFOverlay;

/// <summary>
/// Pure index arithmetic for keyboard navigation. Kept independent of WPF so it
/// can be unit-tested without creating a window or capturing keystrokes.
/// </summary>
public static class SuggestionNavigator
{
    public static int MoveNext(int currentIndex, int count)
    {
        if (count <= 0) return -1;
        int next = currentIndex + 1;
        return next >= count ? 0 : next;
    }

    public static int MovePrevious(int currentIndex, int count)
    {
        if (count <= 0) return -1;
        int previous = currentIndex - 1;
        return previous < 0 ? count - 1 : previous;
    }
}
