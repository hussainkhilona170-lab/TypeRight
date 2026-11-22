using System;
using System.Linq;
using System.Text;
using TypeRight.TsfBridge;
using Xunit;

namespace TypeRight.TsfBridge.Tests
{
    public class BridgeProtocolTests
    {
        [Fact]
        public void TryParseRequest_ParsesSixFieldEval()
        {
            var line = BridgeProtocol.FormatEval("enus", "teh", "notepad.exe", "example.com", "Email body");

            Assert.True(BridgeProtocol.TryParseRequest(line, out var opcode, out var language, out var word,
                out var process, out var domain, out var element));
            Assert.Equal(BridgeProtocol.Eval, opcode);
            Assert.Equal("enus", language);
            Assert.Equal("teh", word);
            Assert.Equal("notepad.exe", process);
            Assert.Equal("example.com", domain);
            Assert.Equal("Email body", element);
        }

        [Fact]
        public void TryParseRequest_KeepsLegacyThreeFieldEval()
        {
            Assert.True(BridgeProtocol.TryParseRequest("EVAL\tenus\tteh", out var opcode, out var language, out var word));
            Assert.Equal(BridgeProtocol.Eval, opcode);
            Assert.Equal("enus", language);
            Assert.Equal("teh", word);
        }

        [Fact]
        public void FormatEval_SanitizesFieldsAndStaysWithinByteLimit()
        {
            var line = BridgeProtocol.FormatEval(
                "en\tus", "te\rh", "note\npad.exe", "ex\texample.com", "Email\tbody");
            Assert.True(Encoding.UTF8.GetByteCount(line) <= BridgeProtocol.MaxLineBytes);

            Assert.True(BridgeProtocol.TryParseRequest(line, out _, out var language, out var word,
                out var process, out var domain, out var element));
            Assert.Equal("en us", language);
            Assert.Equal("te h", word);
            Assert.Equal("note pad.exe", process);
            Assert.Equal("ex example.com", domain);
            Assert.Equal("Email body", element);
        }

        [Fact]
        public void TryParseRequest_RejectsLineOverUtf8ByteLimit()
        {
            var line = BridgeProtocol.Eval + "\t" + new string('a', BridgeProtocol.MaxLineBytes + 1) + "\tword";

            Assert.False(BridgeProtocol.TryParseRequest(line, out _, out _, out _));
        }

        [Fact]
        public void FormatSuggest_SkipsNullAndEmptySuggestions()
        {
            Assert.Equal("SUG\tone\ttwo",
                BridgeProtocol.FormatSuggest(new string[] { null, "one", "", "two", null }));
            Assert.Equal(BridgeProtocol.OkNone,
                BridgeProtocol.FormatSuggest(new string[] { null, string.Empty }));
        }

        [Fact]
        public void FormatSuggest_CapsSuggestionCountAndSanitizes()
        {
            var result = BridgeProtocol.FormatSuggest(
                new[] { "one\ttwo", "a\rb", "c", "d", "e", "f" });

            Assert.Equal("SUG\tone two\ta b\tc\td\te", result);
        }

        [Fact]
        public void FormatSuggest_WithToken_KeepsTokenAndAlternatives()
        {
            string token = Guid.NewGuid().ToString("N");

            var line = BridgeProtocol.FormatSuggest(token, new[] { "world", "", "word" });

            var parts = line.Split('\t');
            Assert.Equal(BridgeProtocol.OkSuggest, parts[0]);
            Assert.Equal(token, parts[1]);
            Assert.Equal(new[] { "world", "word" }, parts.Skip(2));
        }

        [Fact]
        public void TryParseApply_ValidatesTokenAndFields()
        {
            string token = Guid.NewGuid().ToString("N");
            var line = BridgeProtocol.FormatApply(token, "notepad.exe", "example.com", "Email body");

            Assert.True(BridgeProtocol.TryParseApply(line, out var parsedToken, out var process,
                out var domain, out var element));
            Assert.Equal(token, parsedToken);
            Assert.Equal("notepad.exe", process);
            Assert.Equal("example.com", domain);
            Assert.Equal("Email body", element);

            Assert.False(BridgeProtocol.TryParseApply(
                "APPLY\tnot-a-token\tnotepad.exe\t\t", out _, out _, out _, out _));
        }

        [Fact]
        public void FormatAck_RoundTripsStatusAndValue()
        {
            string token = Guid.NewGuid().ToString("N");

            var ok = BridgeProtocol.FormatAck(token, BridgeProtocol.AckOk, "world");
            Assert.True(BridgeProtocol.TryParseAck(ok, out var okToken, out var okStatus, out var okValue));
            Assert.Equal(token, okToken);
            Assert.Equal(BridgeProtocol.AckOk, okStatus);
            Assert.Equal("world", okValue);

            var denied = BridgeProtocol.FormatAck(token, BridgeProtocol.AckDeny,
                BridgeProtocol.ReasonServicePaused);
            Assert.True(BridgeProtocol.TryParseAck(denied, out _, out var denyStatus, out var denyValue));
            Assert.Equal(BridgeProtocol.AckDeny, denyStatus);
            Assert.Equal(BridgeProtocol.ReasonServicePaused, denyValue);
        }

        [Fact]
        public void IsValidToken_RejectsArbitraryText()
        {
            Assert.False(BridgeProtocol.IsValidToken("world"));
            Assert.False(BridgeProtocol.IsValidToken(new string('a', 32) + "\t"));
            Assert.True(BridgeProtocol.IsValidToken("0123456789abcdef0123456789abcdef"));
        }
    }
}