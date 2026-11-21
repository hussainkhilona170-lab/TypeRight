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

        // EVAL: <language> <word> <process> <domain> <element>
        private const int MaxMetadataFieldBytes = 256;
        private const int MaxWordBytes = MaxWordChars * 4;
        private const int MaxSuggestions = 5;

        private static readonly char[] FieldSep = { '\t' };

        /// <summary>
        /// Parses a request line. Returns false if malformed (never throws on untrusted input).
        /// The size check is on UTF-8 bytes, not UTF-16 chars.
        /// </summary>
        public static bool TryParseRequest(string line, out string opcode, out string language, out string word,
                                           out string process, out string domain, out string element)
        {
            opcode = language = word = process = domain = element = null;
            if (string.IsNullOrEmpty(line) || Encoding.UTF8.GetByteCount(line) > MaxLineBytes)
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
        /// Formats a legacy request line for the EVAL operation.
        /// </summary>
        public static string FormatEval(string language, string word)
        {
            return BuildEval(
                SanitizeField(language, MaxMetadataFieldBytes),
                SanitizeField(word, MaxWordBytes));
        }

        /// <summary>
        /// Formats a request line with process/domain/element policy context.
        /// Empty domain/element values are valid; callers that cannot supply context
        /// should use the legacy overload and accept the bridge's fail-closed policy.
        /// </summary>
        public static string FormatEval(string language, string word, string process, string domain, string element)
        {
            return BuildEval(
                SanitizeField(language, MaxMetadataFieldBytes),
                SanitizeField(word, MaxWordBytes),
                SanitizeField(process, MaxMetadataFieldBytes),
                SanitizeField(domain, MaxMetadataFieldBytes),
                SanitizeField(element, MaxMetadataFieldBytes));
        }

        private static string BuildEval(params string[] fields)
        {
            // One tab before every field, and one for the opcode itself is not needed;
            // there are exactly fields.Length separators after the opcode.
            int budget = MaxLineBytes - Encoding.UTF8.GetByteCount(Eval) - fields.Length;
            var sb = new StringBuilder();
            sb.Append(Eval);
            foreach (string field in fields)
            {
                string value = LimitUtf8(field ?? string.Empty, Math.Max(0, budget));
                sb.Append('\t');
                sb.Append(value);
                budget -= Encoding.UTF8.GetByteCount(value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Formats a response line. Suggestions are tab-separated.
        /// </summary>
        public static string FormatAuto(string correctedWord)
        {
            return OkAuto + "\t" + SanitizeField(correctedWord, MaxWordBytes);
        }

        public static string FormatSuggest(IReadOnlyList<string> suggestions)
        {
            if (suggestions == null || suggestions.Count == 0)
                return OkNone;

            var sb = new StringBuilder();
            sb.Append(OkSuggest);
            int added = 0;
            for (int i = 0; i < suggestions.Count && added < MaxSuggestions; i++)
            {
                string suggestion = suggestions[i];
                if (string.IsNullOrEmpty(suggestion)) continue;

                string value = SanitizeField(suggestion, MaxWordBytes);
                if (value.Length == 0) continue;

                sb.Append('\t');
                sb.Append(value);
                added++;
            }
            return added == 0 ? OkNone : sb.ToString();
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
            return Err + "\t" + SanitizeField(code ?? "unknown", MaxMetadataFieldBytes);
        }

        /// <summary>
        /// Removes characters that would corrupt the line protocol and caps the
        /// result by UTF-8 byte count so the MaxLineBytes contract is preserved.
        /// </summary>
        private static string SanitizeField(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value) || maxBytes <= 0)
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            int bytes = 0;
            foreach (char c in value)
            {
                char safe = (c == '\t' || c == '\r' || c == '\n') ? ' ' : c;
                int size = Encoding.UTF8.GetByteCount(safe.ToString());
                if (bytes + size > maxBytes)
                    break;
                sb.Append(safe);
                bytes += size;
            }
            return sb.ToString();
        }

        private static string LimitUtf8(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value) || maxBytes <= 0)
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            int bytes = 0;
            foreach (char c in value)
            {
                int size = Encoding.UTF8.GetByteCount(c.ToString());
                if (bytes + size > maxBytes)
                    break;
                sb.Append(c);
                bytes += size;
            }
            return sb.ToString();
        }
    }
}