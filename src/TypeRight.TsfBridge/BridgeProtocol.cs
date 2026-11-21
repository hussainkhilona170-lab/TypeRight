using System;
using System.Collections.Generic;
using System.Text;

namespace TypeRight.TsfBridge
{
    /// <summary>
    /// Wire protocol shared by the native TSF text service and the managed bridge.
    /// One UTF-8 line per request and one UTF-8 line per response.
    /// User text never appears in logs or diagnostics - only opcodes and lengths.
    /// </summary>
    public static class BridgeProtocol
    {
        public const string PipeName = "TypeRight.Correction.2";
        public const int MaxWordChars = 128;
        public const int MaxLineBytes = 2048;

        // Request opcodes
        public const string Eval = "EVAL";
        public const string Ping = "PING";

        // Response opcodes
        public const string OkAuto = "AUTO";
        public const string OkSuggest = "SUG";
        public const string OkNone = "NONE";
        public const string OkPong = "PONG";
        public const string Denied = "DENY";
        public const string Err = "ERR";

        private static readonly char[] FieldSep = { '\t' };

        /// <summary>
        /// Parses a request line. Returns false if malformed (never throws on untrusted input).
        /// </summary>
        public static bool TryParseRequest(string line, out string opcode, out string language, out string word,
                                           out string process, out string domain, out string element)
        {
            opcode = language = word = process = domain = element = null;
            if (string.IsNullOrEmpty(line) || line.Length > MaxLineBytes)
                return false;

            string[] parts = line.Split(FieldSep);
            if (parts.Length == 1 && parts[0] == Ping)
            {
                opcode = Ping;
                return true;
            }

            // 3 fields: legacy EVAL (no policy context).
            // 6 fields: EVAL with process, domain and focused UI element.
            if ((parts.Length != 3 && parts.Length != 6) || parts[0] != Eval)
                return false;

            language = parts[1];
            word = parts[2];
            if (string.IsNullOrEmpty(word) || word.Length > MaxWordChars)
                return false;

            if (parts.Length == 6)
            {
                process = parts[3];
                domain = parts[4];
                element = parts[5];
            }

            opcode = Eval;
            return true;
        }

        public static bool TryParseRequest(string line, out string opcode, out string language, out string word)
        {
            string process, domain, element;
            return TryParseRequest(line, out opcode, out language, out word, out process, out domain, out element);
        }

        public static string FormatDeny()
        {
            return Denied;
        }

        /// <summary>
        /// Formats a request line for the EVAL operation.
        /// </summary>
        public static string FormatEval(string language, string word)
        {
            return string.Join("\t", Eval, language ?? "auto", word ?? string.Empty);
        }

        /// <summary>
        /// Formats a response line. Suggestions are pipe-separated.
        /// </summary>
        public static string FormatAuto(string correctedWord)
        {
            return OkAuto + "\t" + SanitizeField(correctedWord);
        }

        public static string FormatSuggest(IReadOnlyList<string> suggestions)
        {
            if (suggestions == null || suggestions.Count == 0)
                return OkNone;

            var sb = new StringBuilder();
            sb.Append(OkSuggest);
            for (int i = 0; i < suggestions.Count && i < 5; i++)
            {
                sb.Append('\t');
                sb.Append(SanitizeField(suggestions[i]));
            }
            return sb.ToString();
        }

        public static string FormatNone()
        {
            return OkNone;
        }

        public static string FormatPong()
        {
            return OkPong;
        }

        public static string FormatError(string code)
        {
            return Err + "\t" + SanitizeField(code ?? "unknown");
        }

        /// <summary>
        /// Removes characters that would corrupt the line protocol.
        /// </summary>
        private static string SanitizeField(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
