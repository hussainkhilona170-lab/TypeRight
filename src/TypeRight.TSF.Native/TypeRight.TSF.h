#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef UNICODE
#define UNICODE
#endif

#include <windows.h>
#include <objbase.h>
#include <shlobj.h>
#include <msctf.h>
#include <string>
#include <unordered_map>
#include <vector>
#include <atomic>
#include <new>

#include "Guids.h"

namespace TypeRight {

// ---------------------------------------------------------------------------
// Diagnostics: event-only logging. NEVER logs user text.
// ---------------------------------------------------------------------------
namespace Diagnostics {
void Init();
void Shutdown();
void Event(const wchar_t* event, HRESULT hr = S_OK, unsigned long extra = 0);
}

// ---------------------------------------------------------------------------
// Named pipe client to the managed correction bridge. Bounded and cancellable.
// ---------------------------------------------------------------------------
namespace PipeClient {
std::wstring CurrentProcessName();

// AUTO writes outPayload; SUG writes outToken plus outSuggestions.
bool TryEvaluate(const std::wstring& language,
                 const std::wstring& word,
                 std::wstring& outOpcode,
                 std::wstring& outPayload,
                 std::wstring& outToken,
                 std::vector<std::wstring>& outSuggestions,
                 DWORD timeoutMs = 200);

// Native-initiated long poll for a user selection. The reply is an ACK with a
// closed-set status and either an offered replacement or a reason code.
bool TryApply(const std::wstring& token,
              const std::wstring& process,
              const std::wstring& domain,
              const std::wstring& element,
              std::wstring& outStatus,
              std::wstring& outPayload,
              DWORD timeoutMs = 6000);
}

// ---------------------------------------------------------------------------
// Pending word capture handed to the worker thread. Contains NO user text in
// diagnostics; the word itself only crosses the local pipe.
// ---------------------------------------------------------------------------
struct PendingRequest
{
    ITfContext* context;        // borrowed; AddRef'd for the lifetime of the request
    ITfDocumentMgr* document;   // borrowed; AddRef'd
    std::wstring word;
    std::wstring language;
    long startOffset;
    long endOffset;
};

// ---------------------------------------------------------------------------
// CTextService: the real TSF text input processor.
// ---------------------------------------------------------------------------
class CTextService :
    public ITfTextInputProcessorEx,
    public ITfKeyEventSink,
    public ITfThreadMgrEventSink,
    public ITfTextEditSink,
    public ITfEditSession
{
public:
    CTextService();
    virtual ~CTextService();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ITfTextInputProcessor
    STDMETHODIMP Activate(ITfThreadMgr* pitf, TfClientId tid) override;
    STDMETHODIMP Deactivate() override;

    // ITfTextInputProcessorEx
    STDMETHODIMP ActivateEx(ITfThreadMgr* pitf, TfClientId tid, DWORD dwFlags) override;

    // ITfKeyEventSink
    STDMETHODIMP OnSetFocus(BOOL fForeground) override;
    STDMETHODIMP OnTestKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
    STDMETHODIMP OnKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
    STDMETHODIMP OnTestKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
    STDMETHODIMP OnKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
    STDMETHODIMP OnPreservedKey(ITfContext* pic, REFGUID rguid, BOOL* pfEaten) override;

    // ITfThreadMgrEventSink
    STDMETHODIMP OnInitDocumentMgr(ITfDocumentMgr* pdim) override;
    STDMETHODIMP OnUninitDocumentMgr(ITfDocumentMgr* pdim) override;
    STDMETHODIMP OnSetFocus(ITfDocumentMgr* pdimFocus, ITfDocumentMgr* pdimPrevFocus) override;
    STDMETHODIMP OnPushContext(ITfContext* pic) override;
    STDMETHODIMP OnPopContext(ITfContext* pic) override;

    // ITfTextEditSink
    STDMETHODIMP OnEndEdit(ITfContext* pic, TfEditCookie ecReadOnly, ITfEditRecord* pEditRecord) override;

    // ITfEditSession
    STDMETHODIMP DoEditSession(TfEditCookie ec) override;

private:
    HRESULT AdviseSinks(ITfThreadMgr* pitf);
    void UnadviseSinks();
    HRESULT AttachDocument(ITfDocumentMgr* pdim);
    void DetachDocument();

    HRESULT CaptureCompletedWord(ITfContext* pic, TfEditCookie ec);
    HRESULT DispatchRequest(PendingRequest* request);
    static unsigned WINAPI WorkerThread(LPVOID param);

    // Takes ownership of request (including its COM references).
    HRESULT ApplySuggestion(PendingRequest* request, const std::wstring& payload);

    // Releases any suggestions waiting on a user click when the document changes.
    void InvalidatePendingRequests();

    bool IsExcludedApplication();
    void BumpCounter(const char* name);

private:
    ULONG _refCount;
    ITfThreadMgr* _threadMgr;
    TfClientId _clientId;
    bool _active;

    ITfDocumentMgr* _document;
    ITfContext* _context;

    DWORD _cookieThreadMgrSink;
    DWORD _cookieTextEditSink;
    ITfSource* _sourceThreadMgr;
    ITfSource* _sourceContext;

    // Suggestions waiting for selection, keyed by opaque bridge token.
    std::unordered_map<std::wstring, PendingRequest*> _pending;
    CRITICAL_SECTION _pendingLock;
};

// ---------------------------------------------------------------------------
// COM class factory
// ---------------------------------------------------------------------------
class CClassFactory : public IClassFactory
{
public:
    CClassFactory();
    virtual ~CClassFactory();

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;
    STDMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv) override;
    STDMETHODIMP LockServer(BOOL fLock) override;

private:
    ULONG _refCount;
};

} // namespace TypeRight
