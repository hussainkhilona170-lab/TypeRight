#nullable disable
namespace TypeRight.Engine.Tokenization;

/// <summary>
/// Tokenizes text into words and provides word boundary detection.
/// Responsible for identifying completed words from text input.
/// </summary>
public interface ITokenizer
{
    /// <summary>
    /// Tokenizes a text segment into words and whitespace/punctuation.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <returns>A list of tokens, each being either a word or a non-word segment.</returns>
    IReadOnlyList<TokenSegment> Tokenize(string text);

    /// <summary>
    /// Identifies the token index for the word at the given character offset.
    /// </summary>
    /// <param name="text">The full text.</param>
    /// <param name="charOffset">Character offset position.</param>
    /// <returns>The index of the token at the offset, or -1 if none.</returns>
    int GetWordIndexAtOffset(string text, int charOffset);

    /// <summary>
    /// Determines if the given offset is at a word boundary (after a completed word).
    /// </summary>
    /// <param name="text">The full text.</param>
    /// <param name="charOffset">Character offset position.</param>
    /// <returns>true if the offset is at a word boundary.</returns>
    bool IsWordBoundary(string text, int charOffset);
}

/// <summary>
/// A token segment, either a word or a non-word (punctuation/whitespace) segment.
/// </summary>
public readonly struct TokenSegment
{
    /// <summary>
    /// The text of the segment.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Whether this segment is a word (as opposed to punctuation/whitespace).
    /// </summary>
    public bool IsWord { get; }

    /// <summary>
    /// Creates a new TokenSegment.
    /// </summary>
    public TokenSegment(string text, bool isWord)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        IsWord = isWord;
    }

    /// <summary>
    /// The completed word represented by this segment, if it is a word.
    /// </summary>
    public string? WordValue => IsWord ? Text : null;
}

/// <summary>
/// Provides Damerau-Levenshtein distance calculation with transpositions.
/// </summary>
public static class DamerauLevenshtein
{
    /// <summary>
    /// Calculates the Damerau-Levenshtein distance between two strings.
    /// Includes transposition of adjacent characters.
    /// </summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <param name="costInsertion">Cost of insertion (default 1).</param>
    /// <param name="costDeletion">Cost of deletion (default 1).</param>
    /// <param name="costSubstitution">Cost of substitution (default 1).</param>
    /// <param name="costTransposition">Cost of transposition (default 1).</param>
    /// <returns>The edit distance.</returns>
    public static int Distance(
        string a,
        string b,
        int costInsertion = 1,
        int costDeletion = 1,
        int costSubstitution = 1,
        int costTransposition = 1)
    {
        if (a == b) return 0;
        if (string.IsNullOrEmpty(a)) return b.Length * costDeletion;
        if (string.IsNullOrEmpty(b)) return a.Length * costInsertion;

        int n = a.Length;
        int m = b.Length;

        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
            d[i, 0] = i * costDeletion;
        for (int j = 0; j <= m; j++)
            d[0, j] = j * costInsertion;

        int[] lastA = new int[256];
        int[] lastB = new int[256];
        for (int i = 0; i < 256; i++)
        {
            lastA[i] = -1;
            lastB[i] = -1;
        }

        for (int i = 1; i <= n; i++)
        {
            int db = 0;
            for (int j = 1; j <= m; j++)
            {
                int i1 = lastA[b[j - 1]];
                int j1 = lastB[a[i - 1]];

                int cost = (a[i - 1] == b[j - 1]) ? 0 : costSubstitution;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + costDeletion, d[i, j - 1] + costInsertion),
                    d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + costTransposition);
                }

                int tmp = db;
                db = lastB[a[i - 1]];
                lastB[a[i - 1]] = j;
                lastA[b[j - 1]] = i;
            }
        }

        return d[n, m];
    }

    /// <summary>
    /// Calculates a normalized distance (0.0 = identical, 1.0 = completely different).
    /// </summary>
    public static double NormalizedDistance(
        string a,
        string b,
        int costInsertion = 1,
        int costDeletion = 1,
        int costSubstitution = 1)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 0.0;
        if (string.IsNullOrEmpty(a)) return 1.0;
        if (string.IsNullOrEmpty(b)) return 1.0;

        int lenA = a.Length;
        int lenB = b.Length;
        int maxLen = Math.Max(lenA, lenB);
        int dist = Distance(a, b, costInsertion, costDeletion, costSubstitution);
        return (double)dist / maxLen;
    }
}

/// <summary>
/// Tokenizes text into words and provides word boundary detection.
/// </summary>
public class DefaultTokenizer : ITokenizer
{
    /// <summary>
    /// Tokenizes a text segment into words and whitespace/punctuation.
    /// </summary>
    public IReadOnlyList<TokenSegment> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<TokenSegment>();

        var tokens = new List<TokenSegment>();
        int length = text.Length;
        int i = 0;

        while (i < length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(text[i]))
            {
                int start = i;
                while (i < length && char.IsWhiteSpace(text[i])) i++;
                tokens.Add(new TokenSegment(text.Substring(start, i - start), false));
                continue;
            }

            // Read a word (alphanumeric plus apostrophes and hyphens for contractions)
            if (char.IsLetterOrDigit(text[i]) || text[i] == '\'' || text[i] == '-')
            {
                int start = i;
                while (i < length && (char.IsLetterOrDigit(text[i]) || text[i] == '\'' || text[i] == '-')) i++;
                tokens.Add(new TokenSegment(text.Substring(start, i - start), true));
                continue;
            }

            // Single non-word character
            tokens.Add(new TokenSegment(text[i].ToString(), false));
            i++;
        }

        return tokens;
    }

    /// <summary>
    /// Identifies the token index for the word at the given character offset.
    /// </summary>
    public int GetWordIndexAtOffset(string text, int charOffset)
    {
        if (string.IsNullOrEmpty(text) || charOffset < 0 || charOffset >= text.Length)
            return -1;

        int index = 0;
        int position = 0;

        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                continue;
            }

            if (char.IsLetterOrDigit(text[position]) || text[position] == '\'' || text[position] == '-')
            {
                int wordStart = position;
                while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '\'' || text[position] == '-')) position++;

                if (charOffset >= wordStart && charOffset < position)
                    return index;

                index++;
                continue;
            }

            position++;
        }

        return -1;
    }

    /// <summary>
    /// Determines if the given offset is at a word boundary (after a completed word).
    /// </summary>
    public bool IsWordBoundary(string text, int charOffset)
    {
        if (string.IsNullOrEmpty(text) || charOffset < 0 || charOffset > text.Length)
            return false;

        if (charOffset == text.Length) return true;

        // Check if the character at the offset is part of a word
        if (char.IsLetterOrDigit(text[charOffset]) || text[charOffset] == '\'' || text[charOffset] == '-')
            return false;

        // Check if we're after a word
        int wordEnd = charOffset;
        while (wordEnd > 0 && (char.IsLetterOrDigit(text[wordEnd - 1]) || text[wordEnd - 1] == '\'' || text[wordEnd - 1] == '-'))
            wordEnd--;

        // If we stopped at the beginning or at a space, it's a boundary
        return wordEnd == 0 || char.IsWhiteSpace(text[wordEnd - 1]);
    }
}