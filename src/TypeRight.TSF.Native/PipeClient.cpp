#include "TypeRight.TSF.h"

#include <algorithm>
#include <string>
#include <vector>

namespace TypeRight {
namespace PipeClient {
namespace {

// Protocol version 3 adds token-bearing SUG responses and APPLY/ACK selection.
constexpr wchar_t kPipePath[] = L"\\\\.\\pipe\\TypeRight.Correction.3";
constexpr DWORD kMaxLineBytes = 2048;
constexpr size_t kMaxWordChars = 128;
constexpr size_t kMaxLanguageBytes = 128;
constexpr size_t kMaxProcessBytes = 512;
constexpr size_t kMaxWordBytes = kMaxWordChars * 4;
constexpr size_t kMaxSuggestions = 5;
constexpr size_t kMaxTokenChars = 32;

std::string Sanitize(const std::wstring& value)
{
    std::string result;
    result.reserve(value.size());

    int size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), (int)value.size(),
                                   nullptr, 0, nullptr, nullptr);
    if (size <= 0) return result;

    result.resize((size_t)size);
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), (int)value.size(),
                        &result[0], size, nullptr, nullptr);

    std::replace_if(result.begin(), result.end(), [](char c) {
        return c == '\t' || c == '\r' || c == '\n';
    }, ' ');
    return result;
}

// Sanitized UTF-8, capped without splitting a multi-byte sequence.
std::string SanitizeField(const std::wstring& value, size_t maxBytes)
{
    std::string result = Sanitize(value);
    if (result.size() <= maxBytes) return result;

    size_t end = maxBytes;
    while (end > 0 && (static_cast<unsigned char>(result[end]) & 0xC0) == 0x80) --end;
    result.resize(end);
    return result;
}

std::wstring CurrentProcessBaseName()
{
    wchar_t path[MAX_PATH] = {};
    DWORD length = GetModuleFileNameW(nullptr, path, static_cast<DWORD>(MAX_PATH));
    if (length == 0 || length >= MAX_PATH) return std::wstring();

    std::wstring full(path, length);
    size_t slash = full.find_last_of(L"\\/");
    return slash == std::wstring::npos ? full : full.substr(slash + 1);
}

std::wstring ToWide(const char* data, size_t length)
{
    if (!data || length == 0) return std::wstring();

    int size = MultiByteToWideChar(CP_UTF8, 0, data, (int)length, nullptr, 0);
    if (size <= 0) return std::wstring();

    std::wstring result((size_t)size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, data, (int)length, &result[0], size);
    return result;
}

bool WaitForOverlapped(HANDLE pipe, OVERLAPPED& operation, DWORD timeoutMs, DWORD& transferred)
{
    DWORD wait = WaitForSingleObject(operation.hEvent, timeoutMs);
    if (wait != WAIT_OBJECT_0) return false;
    return GetOverlappedResult(pipe, &operation, &transferred, FALSE) != FALSE;
}

HANDLE OpenPipe(DWORD timeoutMs)
{
    if (!WaitNamedPipeW(kPipePath, timeoutMs)) return INVALID_HANDLE_VALUE;
    return CreateFileW(kPipePath, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                       OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
}

bool WriteLine(HANDLE pipe, const std::string& request, DWORD timeoutMs)
{
    OVERLAPPED operation = {};
    operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!operation.hEvent) return false;

    BOOL result = WriteFile(pipe, request.data(), (DWORD)request.size(), nullptr, &operation);
    DWORD transferred = 0;
    bool wrote = result != FALSE;
    if (!wrote && GetLastError() == ERROR_IO_PENDING)
        wrote = WaitForOverlapped(pipe, operation, timeoutMs, transferred);
    CloseHandle(operation.hEvent);
    return wrote;
}

bool ReadLine(HANDLE pipe, std::string& line, DWORD timeoutMs)
{
    char buffer[kMaxLineBytes + 1] = {};
    OVERLAPPED operation = {};
    operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!operation.hEvent) return false;

    BOOL result = ReadFile(pipe, buffer, kMaxLineBytes, nullptr, &operation);
    DWORD read = 0;
    bool gotResponse = false;
    if (result != FALSE)
    {
        // A fast bridge response can complete synchronously; the overlapped
        // byte count is still required.
        gotResponse = GetOverlappedResult(pipe, &operation, &read, FALSE) != FALSE;
    }
    else if (GetLastError() == ERROR_IO_PENDING)
    {
        gotResponse = WaitForOverlapped(pipe, operation, timeoutMs, read);
    }
    CloseHandle(operation.hEvent);

    if (!gotResponse || read == 0 || read > kMaxLineBytes) return false;

    const char* end = buffer + read;
    const char* lineEnd = std::find(buffer, end, '\n');
    if (lineEnd == end) return false;
    if (lineEnd > buffer && *(lineEnd - 1) == '\r') --lineEnd;

    line.assign(buffer, lineEnd);
    return !line.empty();
}

std::vector<std::string> SplitFields(const std::string& line)
{
    std::vector<std::string> fields;
    const char* begin = line.data();
    const char* end = begin + line.size();
    for (const char* p = begin; p < end; )
    {
        const char* tab = std::find(p, end, '\t');
        fields.emplace_back(p, tab);
        p = (tab == end) ? end : tab + 1;
    }
    return fields;
}

bool IsValidToken(const std::wstring& token)
{
    if (token.size() != kMaxTokenChars) return false;
    for (wchar_t c : token)
    {
        bool digit = c >= L'0' && c <= L'9';
        bool lowerHex = c >= L'a' && c <= L'f';
        if (!digit && !lowerHex) return false;
    }
    return true;
}

} // namespace

std::wstring CurrentProcessName()
{
    return CurrentProcessBaseName();
}

bool TryEvaluate(const std::wstring& language,
                 const std::wstring& word,
                 std::wstring& outOpcode,
                 std::wstring& outPayload,
                 std::wstring& outToken,
                 std::vector<std::wstring>& outSuggestions,
                 DWORD timeoutMs)
{
    outOpcode.clear();
    outPayload.clear();
    outToken.clear();
    outSuggestions.clear();

    // Fail closed: never send oversized or empty user text, and never block
    // the TSF thread indefinitely.
    if (word.empty() || word.size() > kMaxWordChars) return false;

    HANDLE pipe = OpenPipe(timeoutMs);
    if (pipe == INVALID_HANDLE_VALUE) return false;

    // EVAL <language> <word> <process> <domain> <element>. The native client
    // cannot resolve domain/UI element yet, so those fields are intentionally
    // empty; the managed bridge fails closed when its policy needs them.
    const std::wstring process = CurrentProcessBaseName();
    const std::string request = "EVAL\t" + SanitizeField(language, kMaxLanguageBytes) +
                                "\t" + SanitizeField(word, kMaxWordBytes) +
                                "\t" + SanitizeField(process, kMaxProcessBytes) +
                                "\t\t\n";
    if (request.size() > kMaxLineBytes || !WriteLine(pipe, request, timeoutMs))
    {
        CloseHandle(pipe);
        return false;
    }

    std::string response;
    bool gotResponse = ReadLine(pipe, response, timeoutMs);
    CloseHandle(pipe);
    if (!gotResponse) return false;

    const std::vector<std::string> fields = SplitFields(response);
    if (fields.empty()) return false;

    const std::string& opcode = fields[0];
    if (opcode == "AUTO" && fields.size() >= 2)
    {
        outOpcode = L"AUTO";
        outPayload = ToWide(fields[1].data(), fields[1].size());
        return !outPayload.empty();
    }
    if (opcode == "SUG" && fields.size() >= 3)
    {
        std::wstring token = ToWide(fields[1].data(), fields[1].size());
        if (!IsValidToken(token)) return false;

        for (size_t i = 2; i < fields.size() && outSuggestions.size() < kMaxSuggestions; ++i)
        {
            std::wstring suggestion = ToWide(fields[i].data(), fields[i].size());
            if (!suggestion.empty() && suggestion.size() <= kMaxWordChars)
                outSuggestions.push_back(suggestion);
        }
        if (outSuggestions.empty()) return false;

        outOpcode = L"SUG";
        outToken = token;
        return true;
    }
    if (opcode == "NONE" || opcode == "DENY")
    {
        outOpcode = opcode == "NONE" ? L"NONE" : L"DENY";
        return true;
    }

    // ERR, PONG, ACK or anything unknown: no correction to apply.
    return false;
}

bool TryApply(const std::wstring& token,
              const std::wstring& process,
              const std::wstring& domain,
              const std::wstring& element,
              std::wstring& outStatus,
              std::wstring& outPayload,
              DWORD timeoutMs)
{
    outStatus.clear();
    outPayload.clear();

    if (!IsValidToken(token)) return false;

    HANDLE pipe = OpenPipe(timeoutMs);
    if (pipe == INVALID_HANDLE_VALUE) return false;

    std::wstring processName = process.empty() ? CurrentProcessBaseName() : process;
    const std::string request = "APPLY\t" + SanitizeField(token, kMaxTokenChars) +
                                "\t" + SanitizeField(processName, kMaxProcessBytes) +
                                "\t" + SanitizeField(domain, kMaxProcessBytes) +
                                "\t" + SanitizeField(element, kMaxProcessBytes) + "\n";
    if (request.size() > kMaxLineBytes || !WriteLine(pipe, request, timeoutMs))
    {
        CloseHandle(pipe);
        return false;
    }

    std::string response;
    bool gotResponse = ReadLine(pipe, response, timeoutMs);
    CloseHandle(pipe);
    if (!gotResponse) return false;

    const std::vector<std::string> fields = SplitFields(response);
    if (fields.size() != 4 || fields[0] != "ACK") return false;
    if (ToWide(fields[1].data(), fields[1].size()) != token) return false;

    std::wstring status = ToWide(fields[2].data(), fields[2].size());
    if (status != L"OK" && status != L"DENY" && status != L"NONE") return false;

    std::wstring value = ToWide(fields[3].data(), fields[3].size());
    if (value.empty()) return false;
    if (status == L"OK" && value.size() > kMaxWordChars) return false;

    outStatus = status;
    outPayload = value;
    return true;
}

} // namespace PipeClient
} // namespace TypeRight
