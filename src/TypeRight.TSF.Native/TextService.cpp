#include "TypeRight.TSF.h"
#include <algorithm>
#include <wctype.h>

namespace TypeRight {

struct WorkerParams
{
    CTextService* service;
    PendingRequest* request;
};

constexpr DWORD kSelectionTimeoutMs = 6000;

// A PendingRequest owns one reference to its context and document from capture
// until the request is deleted. Edit sessions take ownership of that reference.
static void ReleaseRequestResources(PendingRequest* request)
{
    if (!request) return;
    if (request->context) { request->context->Release(); request->context = nullptr; }
    if (request->document) { request->document->Release(); request->document = nullptr; }
}

// ---------------------------------------------------------------------------
// A dedicated edit-session object per correction. RequestEditSession requires
// a fresh ITfEditSession for every edit; reusing one would be unsafe.
// ---------------------------------------------------------------------------
class CApplyEditSession : public ITfEditSession
{
public:
    CApplyEditSession(CTextService* service, PendingRequest* request,
                      const std::wstring& opcode, const std::wstring& payload)
        : _ref(1), _service(service), _request(request), _opcode(opcode), _payload(payload)
    {
    }

    virtual ~CApplyEditSession()
    {
        ReleaseRequestResources(_request);
        delete _request;
    }

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (riid == IID_IUnknown || riid == IID_ITfEditSession)
            *ppv = static_cast<ITfEditSession*>(this);
        else return E_NOINTERFACE;
        AddRef();
        return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return (ULONG)InterlockedIncrement(&_ref); }
    STDMETHODIMP_(ULONG) Release() override
    {
        ULONG r = (ULONG)InterlockedDecrement(&_ref);
        if (r == 0) delete this;
        return r;
    }

    STDMETHODIMP DoEditSession(TfEditCookie ec) override
    {
        ITfContext* context = _request->context;
        ITfRange* start = nullptr;
        ITfRange* wordRange = nullptr;
        ITfRange* selection = nullptr;
        HRESULT hr = S_OK;

        // Re-verify that the text at the recorded offsets is still the word we
        // evaluated. If the user kept typing, do nothing (stale request).
        do
        {
            hr = context->GetStart(&start);
            if (FAILED(hr)) break;

            hr = start->Clone(&wordRange);
            if (FAILED(hr)) break;

            LONG moved = 0;
            hr = wordRange->ShiftEnd(ec, _request->endOffset, &moved, nullptr);
            if (FAILED(hr) || moved != _request->endOffset) { hr = E_FAIL; break; }

            hr = wordRange->ShiftStart(ec, _request->startOffset, &moved, nullptr);
            if (FAILED(hr) || moved != _request->startOffset) { hr = E_FAIL; break; }

            wchar_t current[130] = { 0 };
            LONG cch = 0;
            hr = wordRange->GetText(ec, 0, current, 129, &cch);
            if (FAILED(hr)) break;

            if ((size_t)cch != _request->word.size() ||
                _wcsicmp(current, _request->word.c_str()) != 0)
            {
                Diagnostics::Event(L"edit_session_stale", S_FALSE, (unsigned long)cch);
                hr = S_FALSE;
                break;
            }

            // SUG is only an offer; the explicit SELECT opcode comes from a user click.
            if ((_opcode != L"AUTO" && _opcode != L"SELECT") || _payload.empty())
            {
                Diagnostics::Event(L"edit_session_suggest_only", S_OK, (unsigned long)cch);
                hr = S_FALSE;
                break;
            }

            // Preserve the caret/selection across the replacement.
            hr = context->GetSelection(TF_SELECTION, &selection);
            if (FAILED(hr)) break;

            hr = wordRange->SetText(ec, 0, _payload.c_str(), (LONG)_payload.size());
            if (FAILED(hr)) break;

            if (selection)
                context->SetSelection(TF_SELECTION, selection);

            Diagnostics::Event(L"edit_session_applied", S_OK, (unsigned long)_payload.size());
        } while (false);

        if (selection) selection->Release();
        if (wordRange) wordRange->Release();
        if (start) start->Release();
        return hr;
    }

private:
    ULONG _ref;
    CTextService* _service;
    PendingRequest* _request;
    std::wstring _opcode;
    std::wstring _payload;
};

// ---------------------------------------------------------------------------
// CTextService
// ---------------------------------------------------------------------------
CTextService::CTextService()
    : _refCount(1), _threadMgr(nullptr), _clientId(TF_CLIENTID_NULL), _active(false),
      _document(nullptr), _context(nullptr), _cookieThreadMgrSink(0), _cookieTextEditSink(0),
      _sourceThreadMgr(nullptr), _sourceContext(nullptr)
{
    InitializeCriticalSection(&_pendingLock);
}

CTextService::~CTextService()
{
    DetachDocument();
    UnadviseSinks();
    if (_threadMgr) _threadMgr->Release();
    DeleteCriticalSection(&_pendingLock);
}

STDMETHODIMP CTextService::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;

    if (riid == IID_IUnknown)
        *ppv = static_cast<IUnknown*>(static_cast<ITfTextInputProcessor*>(this));
    else if (riid == IID_ITfTextInputProcessor || riid == IID_ITfTextInputProcessorEx)
        *ppv = static_cast<ITfTextInputProcessorEx*>(this);
    else if (riid == IID_ITfKeyEventSink)
        *ppv = static_cast<ITfKeyEventSink*>(this);
    else if (riid == IID_ITfThreadMgrEventSink)
        *ppv = static_cast<ITfThreadMgrEventSink*>(this);
    else if (riid == IID_ITfTextEditSink)
        *ppv = static_cast<ITfTextEditSink*>(this);
    else if (riid == IID_ITfEditSession)
        *ppv = static_cast<ITfEditSession*>(this);
    else
        return E_NOINTERFACE;

    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) CTextService::AddRef()
{
    return (ULONG)InterlockedIncrement(&_refCount);
}

STDMETHODIMP_(ULONG) CTextService::Release()
{
    ULONG r = (ULONG)InterlockedDecrement(&_refCount);
    if (r == 0) delete this;
    return r;
}

STDMETHODIMP CTextService::Activate(ITfThreadMgr* pitf, TfClientId tid)
{
    return ActivateEx(pitf, tid, 0);
}

STDMETHODIMP CTextService::ActivateEx(ITfThreadMgr* pitf, TfClientId tid, DWORD dwFlags)
{
    if (!pitf) return E_INVALIDARG;
    if (_active) return S_OK;

    _threadMgr = pitf;
    _threadMgr->AddRef();
    _clientId = tid;

    HRESULT hr = AdviseSinks(pitf);
    _active = SUCCEEDED(hr);
    Diagnostics::Event(_active ? L"activate" : L"activate_failed", hr, dwFlags);
    return hr;
}

STDMETHODIMP CTextService::Deactivate()
{
    if (!_active) return S_OK;
    _active = false;
    DetachDocument();
    UnadviseSinks();
    Diagnostics::Event(L"deactivate", S_OK);
    return S_OK;
}

HRESULT CTextService::AdviseSinks(ITfThreadMgr* pitf)
{
    HRESULT hr = pitf->QueryInterface(IID_PPV_ARGS(&_sourceThreadMgr));
    if (FAILED(hr)) return hr;

    hr = _sourceThreadMgr->AdviseSink(IID_ITfThreadMgrEventSink,
                                      static_cast<ITfThreadMgrEventSink*>(this),
                                      &_cookieThreadMgrSink);
    if (FAILED(hr)) return hr;

    ITfKeystrokeMgr* keystroke = nullptr;
    hr = pitf->QueryInterface(IID_PPV_ARGS(&keystroke));
    if (SUCCEEDED(hr))
    {
        hr = keystroke->AdviseKeyEventSink(_clientId,
                                           static_cast<ITfKeyEventSink*>(this),
                                           TRUE);
        Diagnostics::Event(L"advise_key_event_sink", hr);
        keystroke->Release();
    }

    ITfDocumentMgr* docMgr = nullptr;
    if (SUCCEEDED(pitf->GetFocus(&docMgr)) && docMgr)
    {
        AttachDocument(docMgr);
        docMgr->Release();
    }
    return S_OK;
}

void CTextService::UnadviseSinks()
{
    if (_active && _threadMgr)
    {
        ITfKeystrokeMgr* keystroke = nullptr;
        if (SUCCEEDED(_threadMgr->QueryInterface(IID_PPV_ARGS(&keystroke))))
        {
            keystroke->UnadviseKeyEventSink(_clientId);
            keystroke->Release();
        }
    }
    if (_sourceThreadMgr && _cookieThreadMgrSink)
    {
        _sourceThreadMgr->Unadvise(_cookieThreadMgrSink);
        _cookieThreadMgrSink = 0;
    }
    if (_sourceThreadMgr) { _sourceThreadMgr->Release(); _sourceThreadMgr = nullptr; }
}

HRESULT CTextService::AttachDocument(ITfDocumentMgr* pdim)
{
    if (pdim == _document) return S_OK;
    DetachDocument();

    _document = pdim;
    _document->AddRef();

    HRESULT hr = _document->GetTop(&_context);
    if (FAILED(hr) || !_context) return hr;

    hr = _context->QueryInterface(IID_PPV_ARGS(&_sourceContext));
    if (FAILED(hr)) return hr;

    hr = _sourceContext->AdviseSink(IID_ITfTextEditSink,
                                    static_cast<ITfTextEditSink*>(this),
                                    &_cookieTextEditSink);
    Diagnostics::Event(L"advise_text_edit_sink", hr);
    return hr;
}

void CTextService::DetachDocument()
{
    // No suggestion may be applied after the document or focus changes.
    InvalidatePendingRequests();

    if (_sourceContext && _cookieTextEditSink)
    {
        _sourceContext->Unadvise(_cookieTextEditSink);
        _cookieTextEditSink = 0;
    }
    if (_sourceContext) { _sourceContext->Release(); _sourceContext = nullptr; }
    if (_context) { _context->Release(); _context = nullptr; }
    if (_document) { _document->Release(); _document = nullptr; }
}

// --- ITfThreadMgrEventSink -------------------------------------------------
STDMETHODIMP CTextService::OnInitDocumentMgr(ITfDocumentMgr*) { return S_OK; }
STDMETHODIMP CTextService::OnUninitDocumentMgr(ITfDocumentMgr* pdim)
{
    if (pdim == _document) DetachDocument();
    return S_OK;
}
STDMETHODIMP CTextService::OnSetFocus(ITfDocumentMgr* pdimFocus, ITfDocumentMgr*)
{
    if (!_active) return S_OK;
    if (pdimFocus) return AttachDocument(pdimFocus);
    DetachDocument();
    return S_OK;
}
STDMETHODIMP CTextService::OnPushContext(ITfContext*) { return S_OK; }
STDMETHODIMP CTextService::OnPopContext(ITfContext*) { return S_OK; }

// --- ITfKeyEventSink -------------------------------------------------------
STDMETHODIMP CTextService::OnSetFocus(BOOL) { return S_OK; }

STDMETHODIMP CTextService::OnTestKeyDown(ITfContext*, WPARAM wParam, LPARAM, BOOL* pfEaten)
{
    if (pfEaten) *pfEaten = FALSE;
    if (wParam == VK_ESCAPE)
    {
        // Dismissal is handled by the managed overlay; we never consume keys
        // needed by the host application. Fail-closed: do not eat keystrokes.
        Diagnostics::Event(L"dismiss_requested");
    }
    return S_OK;
}

STDMETHODIMP CTextService::OnKeyDown(ITfContext*, WPARAM, LPARAM, BOOL* pfEaten)
{
    if (pfEaten) *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP CTextService::OnTestKeyUp(ITfContext*, WPARAM, LPARAM, BOOL* pfEaten)
{
    if (pfEaten) *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP CTextService::OnKeyUp(ITfContext*, WPARAM, LPARAM, BOOL* pfEaten)
{
    if (pfEaten) *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP CTextService::OnPreservedKey(ITfContext*, REFGUID, BOOL* pfEaten)
{
    if (pfEaten) *pfEaten = FALSE;
    return S_OK;
}

// --- ITfTextEditSink -------------------------------------------------------
STDMETHODIMP CTextService::OnEndEdit(ITfContext* pic, TfEditCookie ecReadOnly, ITfEditRecord*)
{
    if (!_active || !pic || IsExcludedApplication()) return S_OK;
    return CaptureCompletedWord(pic, ecReadOnly);
}

static bool IsWordDelimiter(wchar_t c)
{
    if (c == L'\'' && c != 0) return false; // apostrophe may be part of a word
    return iswspace(c) || iswpunct(c) || c == L'\0';
}

HRESULT CTextService::CaptureCompletedWord(ITfContext* pic, TfEditCookie ec)
{
    ITfRange* selection = nullptr;
    ITfRange* start = nullptr;
    ITfRange* range = nullptr;
    HRESULT hr = S_OK;

    do
    {
        hr = pic->GetSelection(TF_SELECTION, &selection);
        if (FAILED(hr) || !selection) break;

        hr = pic->GetStart(&start);
        if (FAILED(hr) || !start) break;

        hr = start->Clone(&range);
        if (FAILED(hr)) break;

        hr = range->SetEnd(ec, selection, TRUE);
        if (FAILED(hr)) break;

        wchar_t text[8192] = { 0 };
        LONG cch = 0;
        hr = range->GetText(ec, 0, text, 8191, &cch);
        if (FAILED(hr) || cch <= 0) break;

        LONG i = cch;
        while (i > 0 && IsWordDelimiter(text[i - 1])) i--;
        if (i == cch) break; // no delimiter just typed - word not complete yet

        LONG end = i;
        while (i > 0 && (iswalnum(text[i - 1]) || text[i - 1] == L'\'')) i--;
        LONG begin = i;

        LONG wordLen = end - begin;
        if (wordLen < 2 || wordLen > 64) break;

        PendingRequest* request = new (std::nothrow) PendingRequest();
        if (!request) { hr = E_OUTOFMEMORY; break; }

        request->context = pic;
        if (request->context) request->context->AddRef();
        request->document = _document;
        if (request->document) request->document->AddRef();
        request->word.assign(text + begin, wordLen);
        request->startOffset = begin;
        request->endOffset = end;
        request->language = L"auto";

        hr = DispatchRequest(request);
        if (FAILED(hr))
        {
            ReleaseRequestResources(request);
            delete request;
        }
        Diagnostics::Event(L"word_captured", hr, (unsigned long)wordLen);
    } while (false);

    if (range) range->Release();
    if (start) start->Release();
    if (selection) selection->Release();
    return S_OK; // diagnostics are best-effort; never disrupt typing
}

static volatile LONG s_activeWorkers = 0;

HRESULT CTextService::DispatchRequest(PendingRequest* request)
{
    if (InterlockedIncrement(&s_activeWorkers) > 4)
    {
        InterlockedDecrement(&s_activeWorkers);
        Diagnostics::Event(L"request_dropped_busy");
        return E_ABORT;
    }

    // Resolve current language without blocking the edit sink for long.
    ITfInputProcessorProfiles* profiles = nullptr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr,
                                   CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&profiles))))
    {
        LANGID lang = 0;
        if (SUCCEEDED(profiles->GetCurrentLanguage(&lang)))
        {
            if ((lang & 0xFF) == LANG_ENGLISH)
            {
                WORD sub = lang >> 8;
                request->language = (sub == SUBLANG_ENGLISH_UK) ? L"engb" : L"enus";
            }
        }
        profiles->Release();
    }

    WorkerParams* params = new (std::nothrow) WorkerParams();
    if (!params)
    {
        InterlockedDecrement(&s_activeWorkers);
        return E_OUTOFMEMORY;
    }
    params->service = this;
    params->request = request;

    // AddRef the service so it cannot be destroyed while the worker runs.
    AddRef();

    uintptr_t handle = _beginthreadex(nullptr, 0, WorkerThread, params, 0, nullptr);
    if (handle == 0)
    {
        Release();
        delete params;
        ReleaseRequestResources(request);
        delete request;
        InterlockedDecrement(&s_activeWorkers);
        return E_FAIL;
    }
    CloseHandle((HANDLE)handle);
    Diagnostics::Event(L"request_dispatched", S_OK, (unsigned long)request->word.size());
    return S_OK;
}

unsigned WINAPI CTextService::WorkerThread(LPVOID param)
{
    WorkerParams* params = static_cast<WorkerParams*>(param);
    CTextService* service = params->service;
    PendingRequest* request = params->request;
    delete params;

    std::wstring opcode, payload, token;
    std::vector<std::wstring> suggestions;
    bool ok = PipeClient::TryEvaluate(request->language, request->word, opcode,
                                      payload, token, suggestions, 250);
    Diagnostics::Event(ok ? L"pipe_response" : L"pipe_unavailable",
                       ok ? S_OK : E_FAIL,
                       (unsigned long)(ok ? payload.size() + token.size() + suggestions.size() : 0));

    if (ok && opcode == L"AUTO" && !payload.empty() && request->context)
    {
        CApplyEditSession* session = new (std::nothrow)
            CApplyEditSession(service, request, opcode, payload);
        if (session)
        {
            // Async edit session: the call returns immediately and the callback
            // occurs on the thread that owns the document.
            HRESULT hr = request->context->RequestEditSession(
                service->_clientId, session, TF_ES_ASYNC | TF_ES_READWRITE, nullptr);
            Diagnostics::Event(L"edit_session_requested", hr);
            if (FAILED(hr))
                session->Release(); // destructor frees the request
            // On success TSF owns the edit session's initial reference.
            request = nullptr;
        }
    }
    else if (ok && opcode == L"SUG" && !token.empty() && !suggestions.empty())
    {
        bool stored = false;
        EnterCriticalSection(&service->_pendingLock);
        if (service->_active)
        {
            service->_pending[token] = request;
            stored = true;
        }
        LeaveCriticalSection(&service->_pendingLock);

        if (!stored)
        {
            Diagnostics::Event(L"suggestion_service_inactive");
            ReleaseRequestResources(request);
            delete request;
            request = nullptr;
        }
        else
        {
            // Ownership moved to the pending map; release it only after ACK.
            request = nullptr;

            std::wstring status, replacement;
            std::wstring process = PipeClient::CurrentProcessName();
            bool acked = PipeClient::TryApply(token, process, L"", L"",
                                              status, replacement, kSelectionTimeoutMs);

            PendingRequest* selected = nullptr;
            bool contextCurrent = false;
            EnterCriticalSection(&service->_pendingLock);
            auto it = service->_pending.find(token);
            if (it != service->_pending.end())
            {
                selected = it->second;
                service->_pending.erase(it);
                contextCurrent = service->_active && service->_document == selected->document;
            }
            LeaveCriticalSection(&service->_pendingLock);

            if (!selected || !contextCurrent)
            {
                // A document/focus change already invalidated this request.
                Diagnostics::Event(L"apply_stale");
                if (selected)
                {
                    ReleaseRequestResources(selected);
                    delete selected;
                }
            }
            else if (acked && status == L"OK" && !replacement.empty() &&
                     std::find(suggestions.begin(), suggestions.end(), replacement) != suggestions.end())
            {
                Diagnostics::Event(L"apply_ack_ok", S_OK, (unsigned long)replacement.size());
                service->ApplySuggestion(selected, replacement); // takes ownership
            }
            else
            {
                if (!acked)
                    Diagnostics::Event(L"apply_timeout", E_FAIL);
                else
                    Diagnostics::Event(status == L"DENY" ? L"apply_ack_deny" : L"apply_ack_none",
                                       S_OK, (unsigned long)status.size());
                ReleaseRequestResources(selected);
                delete selected;
            }
        }
    }

    delete request;
    InterlockedDecrement(&s_activeWorkers);
    service->Release();
    return 0;
}

HRESULT CTextService::ApplySuggestion(PendingRequest* request, const std::wstring& payload)
{
    if (!request || !request->context || payload.empty())
    {
        ReleaseRequestResources(request);
        delete request;
        Diagnostics::Event(L"apply_invalid_request", E_INVALIDARG);
        return E_INVALIDARG;
    }

    CApplyEditSession* session = new (std::nothrow)
        CApplyEditSession(this, request, L"SELECT", payload);
    if (!session)
    {
        ReleaseRequestResources(request);
        delete request;
        Diagnostics::Event(L"apply_session_failed", E_OUTOFMEMORY);
        return E_OUTOFMEMORY;
    }

    HRESULT hr = request->context->RequestEditSession(
        _clientId, session, TF_ES_ASYNC | TF_ES_READWRITE, nullptr);
    Diagnostics::Event(L"apply_session_requested", hr, (unsigned long)payload.size());
    if (FAILED(hr)) session->Release(); // destructor frees the request
    return hr;
}

void CTextService::InvalidatePendingRequests()
{
    std::vector<PendingRequest*> stale;
    EnterCriticalSection(&_pendingLock);
    for (auto& item : _pending)
        stale.push_back(item.second);
    _pending.clear();
    LeaveCriticalSection(&_pendingLock);

    for (PendingRequest* request : stale)
    {
        ReleaseRequestResources(request);
        delete request;
    }
    if (!stale.empty())
        Diagnostics::Event(L"pending_invalidated", S_OK, (unsigned long)stale.size());
}

bool CTextService::IsExcludedApplication()
{
    HKEY hk = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\TypeRight\\TypeRight2", 0,
                      KEY_READ, &hk) != ERROR_SUCCESS)
        return false;

    wchar_t values[2048] = { 0 };
    DWORD size = sizeof(values);
    LONG lr = RegQueryValueExW(hk, L"ExcludedApplications", nullptr, nullptr,
                               (BYTE*)values, &size);
    RegCloseKey(hk);
    if (lr != ERROR_SUCCESS) return false;

    wchar_t exe[MAX_PATH] = { 0 };
    GetModuleFileNameW(nullptr, exe, MAX_PATH);
    wchar_t* file = wcsrchr(exe, L'\\');
    file = file ? file + 1 : exe;

    for (wchar_t* p = values; *p; )
    {
        size_t n = wcslen(p);
        if (!_wcsicmp(p, file)) return true;
        p += n + 1;
    }
    return false;
}

// Kept to satisfy the declared interface; the per-request CApplyEditSession is
// the object actually handed to RequestEditSession.
STDMETHODIMP CTextService::DoEditSession(TfEditCookie)
{
    Diagnostics::Event(L"service_edit_session_noop");
    return S_FALSE;
}

} // namespace TypeRight
