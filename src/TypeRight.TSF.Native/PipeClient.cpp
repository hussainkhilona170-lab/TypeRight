#include "TypeRight.TSF.h"

#include <algorithm>
#include <string>
#include <vector>

namespace TypeRight {
namespace PipeClient {
namespace {

constexpr wchar_t kPipePath[] = L"\\\\.\\pipe\\TypeRight.Correction.2";
constexpr DWORD kMaxLineBytes = 2048;
constexpr size_t kMaxWordChars = 128;
constexpr size_t kMaxLanguageBytes = 128;
constexpr size_t kMaxProcessBytes = 512;
constexpr size_t kMaxWordBytes = kMaxWordChars * 4;

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

std::vector<std::string> SplitFields(const char* begin, const char* end)
{
    std::vector<std::string> fields;
    for (const char* p = begin; p < end; )
    {
        const char* tab = std::find(p, end, '\t');
        fields.emplace_back(p, tab);
        p = (tab == end) ? end : tab + 1;
    }
    return fields;
}

} // namespace

bool TryEvaluate(const std::wstring& language,
                 const std::wstring& word,
                 std::wstring& outOpcode,
                 std::wstring& outPayload,
                 DWORD timeoutMs)
{
    outOpcode.clear();
    outPayload.clear();

    // Fail closed: never send oversized or empty user text, and never block
    // the TSF thread indefinitely.
    if (word.empty() || word.size() > kMaxWordChars) return false;

    if (!WaitNamedPipeW(kPipePath, timeoutMs)) return false;

    HANDLE pipe = CreateFileW(kPipePath, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                              OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;

    // EVAL <language> <word> <process> <domain> <element>. The native client
    // cannot resolve domain/UI element yet, so those fields are intentionally
    // empty; the managed bridge fails closed when its policy needs them.
    const std::wstring process = CurrentProcessBaseName();
    const std::string request = "EVAL\t" + SanitizeField(language, kMaxLanguageBytes) +
                                "\t" + SanitizeField(word, kMaxWordBytes) +
                                "\t" + SanitizeField(process, kMaxProcessBytes) +
                                "\t\t\n";
    if (request.size() > kMaxLineBytes)
    {
        CloseHandle(pipe);
        return false;
    }

    OVERLAPPED writeOp = {};
    writeOp.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!writeOp.hEvent)
    {
        CloseHandle(pipe);
        return false;
    }

    BOOL writeResult = WriteFile(pipe, request.data(), (DWORD)request.size(), nullptr, &writeOp);
    DWORD written = 0;
    bool wrote = writeResult != FALSE;
    if (!wrote && GetLastError() == ERROR_IO_PENDING)
        wrote = WaitForOverlapped(pipe, writeOp, timeoutMs, written);
    CloseHandle(writeOp.hEvent);

    if (!wrote)
    {
        CloseHandle(pipe);
        return false;
    }

    char buffer[kMaxLineBytes + 1] = {};
    OVERLAPPED readOp = {};
    readOp.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!readOp.hEvent)
    {
        CloseHandle(pipe);
        return false;
    }

    BOOL readResult = ReadFile(pipe, buffer, kMaxLineBytes, nullptr, &readOp);
    DWORD read = 0;
    bool gotResponse = false;
    if (readResult != FALSE)
    {
        // Synchronous completion still needs the overlapped byte count; without
        // this, a fast bridge response was rejected as an empty read.
        gotResponse = GetOverlappedResult(pipe, &readOp, &read, FALSE) != FALSE;
    }
    else if (GetLastError() == ERROR_IO_PENDING)
    {
        gotResponse = WaitForOverlapped(pipe, readOp, timeoutMs, read);
    }
    CloseHandle(readOp.hEvent);
    CloseHandle(pipe);

    if (!gotResponse || read == 0 || read > kMaxLineBytes) return false;

    const char* end = buffer + read;
    const char* lineEnd = std::find(buffer, end, '\n');
    if (lineEnd == end) return false;
    if (lineEnd > buffer && *(lineEnd - 1) == '\r') --lineEnd;

    const std::vector<std::string> fields = SplitFields(buffer, lineEnd);
    if (fields.empty()) return false;

    const std::string& opcode = fields[0];
    if (opcode == "AUTO" && fields.size() >= 2)
    {
        outOpcode = L"AUTO";
        outPayload = ToWide(fields[1].data(), fields[1].size());
        return !outPayload.empty();
    }
    if (opcode == "SUG" && fields.size() >= 2)
    {
        outOpcode = L"SUG";
        outPayload = ToWide(fields[1].data(), fields[1].size());
        return true;
    }
    if (opcode == "NONE" || opcode == "DENY")
    {
        outOpcode = opcode == "NONE" ? L"NONE" : L"DENY";
        return true;
    }

    // ERR, PONG or anything unknown: no correction to apply.
    return false;
}

} // namespace PipeClient
} // namespace TypeRight
