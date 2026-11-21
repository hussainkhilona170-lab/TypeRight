#include "TypeRight.TSF.h"
#include <stdio.h>
#include <stdarg.h>

namespace TypeRight {

extern HINSTANCE g_hInstance;
extern volatile LONG g_lockCount;

// ---------------------------------------------------------------------------
// Diagnostics - event-only. Never writes user text, words, or file contents.
// ---------------------------------------------------------------------------
namespace Diagnostics {
static CRITICAL_SECTION s_cs;
static bool s_initialized = false;

void Init()
{
    if (s_initialized) return;
    InitializeCriticalSection(&s_cs);
    s_initialized = true;
    Event(L"diagnostics_initialized");
}

void Shutdown()
{
    if (!s_initialized) return;
    Event(L"diagnostics_shutdown");
    DeleteCriticalSection(&s_cs);
    s_initialized = false;
}

void Event(const wchar_t* event, HRESULT hr, unsigned long extra)
{
    if (!s_initialized) return;

    wchar_t path[MAX_PATH];
    wchar_t* filePart = nullptr;
    if (!SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, 0, path)))
        return;
    wcscat_s(path, L"\\TypeRight");
    CreateDirectoryW(path, nullptr);
    wcscat_s(path, L"\\tsf-diagnostics.log");

    SYSTEMTIME st;
    GetSystemTime(&st);
    wchar_t line[512];
    int n = swprintf_s(line, 512,
        L"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ\t%s\thr=0x%08lX\tn=%lu\r\n",
        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond,
        st.wMilliseconds, event ? event : L"", (unsigned long)hr, extra);
    if (n <= 0) return;

    EnterCriticalSection(&s_cs);
    HANDLE h = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h != INVALID_HANDLE_VALUE)
    {
        DWORD written = 0;
        WriteFile(h, line, (DWORD)(n * sizeof(wchar_t)), &written, nullptr);
        CloseHandle(h);
    }
    LeaveCriticalSection(&s_cs);

    OutputDebugStringW(line);
}
} // namespace Diagnostics

// ---------------------------------------------------------------------------
// Module
// ---------------------------------------------------------------------------
HINSTANCE g_hInstance = nullptr;
volatile LONG g_lockCount = 0;

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        g_hInstance = (HINSTANCE)hModule;
        DisableThreadLibraryCalls(hModule);
        Diagnostics::Init();
        break;
    case DLL_PROCESS_DETACH:
        Diagnostics::Shutdown();
        break;
    }
    return TRUE;
}

// ---------------------------------------------------------------------------
// CClassFactory
// ---------------------------------------------------------------------------
CClassFactory::CClassFactory() : _refCount(1) {}
CClassFactory::~CClassFactory() {}

STDMETHODIMP CClassFactory::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (riid == IID_IUnknown || riid == IID_IClassFactory)
        *ppv = static_cast<IClassFactory*>(this);
    else
        return E_NOINTERFACE;
    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) CClassFactory::AddRef()
{
    return (ULONG)InterlockedIncrement(&_refCount);
}

STDMETHODIMP_(ULONG) CClassFactory::Release()
{
    ULONG r = (ULONG)InterlockedDecrement(&_refCount);
    if (r == 0) delete this;
    return r;
}

STDMETHODIMP CClassFactory::CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (pUnkOuter) return CLASS_E_NOAGGREGATION;

    CTextService* service = new (std::nothrow) CTextService();
    if (!service) return E_OUTOFMEMORY;

    HRESULT hr = service->QueryInterface(riid, ppv);
    service->Release();
    Diagnostics::Event(L"class_factory_create_instance", hr);
    return hr;
}

STDMETHODIMP CClassFactory::LockServer(BOOL fLock)
{
    if (fLock) InterlockedIncrement(&g_lockCount);
    else InterlockedDecrement(&g_lockCount);
    return S_OK;
}

// ---------------------------------------------------------------------------
// COM exports
// ---------------------------------------------------------------------------
STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (rclsid != CLSID_TypeRightTSF)
        return CLASS_E_CLASSNOTAVAILABLE;

    CClassFactory* factory = new (std::nothrow) CClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    Diagnostics::Event(L"dll_get_class_object", hr);
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return (g_lockCount == 0) ? S_OK : S_FALSE;
}

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------
static HRESULT RegisterComServer()
{
    wchar_t modulePath[MAX_PATH];
    DWORD len = GetModuleFileNameW(g_hInstance, modulePath, MAX_PATH);
    if (len == 0 || len >= MAX_PATH) return HRESULT_FROM_WIN32(GetLastError());

    wchar_t keyPath[MAX_PATH];
    swprintf_s(keyPath, L"CLSID\\{%08lX-%04X-%04X-%02X%02X-%02X%02X%02X%02X%02X%02X}\\InprocServer32",
               TYPERIGHT_CLSID_TSF);

    HKEY hk = nullptr;
    LONG lr = RegCreateKeyExW(HKEY_CLASSES_ROOT, keyPath, 0, nullptr,
                              REG_OPTION_NON_VOLATILE, KEY_SET_VALUE, nullptr, &hk, nullptr);
    if (lr != ERROR_SUCCESS) return HRESULT_FROM_WIN32(lr);

    lr = RegSetValueExW(hk, nullptr, 0, REG_SZ,
                        (const BYTE*)modulePath, (DWORD)((wcslen(modulePath) + 1) * sizeof(wchar_t)));
    if (lr == ERROR_SUCCESS)
        lr = RegSetValueExW(hk, L"ThreadingModel", 0, REG_SZ,
                            (const BYTE*)L"Apartment", (DWORD)(10 * sizeof(wchar_t)));
    RegCloseKey(hk);
    return HRESULT_FROM_WIN32(lr);
}

static HRESULT UnregisterComServer()
{
    wchar_t keyPath[MAX_PATH];
    swprintf_s(keyPath, L"CLSID\\{%08lX-%04X-%04X-%02X%02X-%02X%02X%02X%02X%02X%02X}",
               TYPERIGHT_CLSID_TSF);
    LONG lr = RegDeleteTreeW(HKEY_CLASSES_ROOT, keyPath);
    if (lr != ERROR_SUCCESS && lr != ERROR_FILE_NOT_FOUND)
        return HRESULT_FROM_WIN32(lr);
    return S_OK;
}

STDAPI DllRegisterServer()
{
    Diagnostics::Event(L"register_begin");
    HRESULT hr = RegisterComServer();
    Diagnostics::Event(L"register_com_server", hr);
    if (FAILED(hr)) return hr;

    ITfInputProcessorProfiles* profiles = nullptr;
    hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                          IID_PPV_ARGS(&profiles));
    Diagnostics::Event(L"register_profiles_create", hr);
    if (FAILED(hr)) return hr;

    const LANGID langId = MAKELANGID(LANG_ENGLISH, SUBLANG_NEUTRAL);
    const wchar_t* description = L"TypeRight 2.0 Typing Companion";

    HRESULT hrReg = profiles->Register(CLSID_TypeRightTSF);
    Diagnostics::Event(L"register_tip", hrReg);

    HRESULT hrAdd = profiles->AddLanguageProfile(
        CLSID_TypeRightTSF, langId, GUID_TypeRightProfile,
        description, (ULONG)wcslen(description),
        nullptr, 0, 0);
    Diagnostics::Event(L"register_language_profile", hrAdd);

    HRESULT hrEnable = profiles->EnableLanguageProfile(
        CLSID_TypeRightTSF, langId, GUID_TypeRightProfile, TRUE);
    Diagnostics::Event(L"register_enable_profile", hrEnable);

    HRESULT hrDefault = profiles->EnableLanguageProfileByDefault(
        CLSID_TypeRightTSF, langId, GUID_TypeRightProfile, TRUE);
    Diagnostics::Event(L"register_enable_default", hrDefault);

    profiles->Release();

    if (FAILED(hrAdd)) return hrAdd;
    if (FAILED(hrEnable)) return hrEnable;
    return hrReg;
}

STDAPI DllUnregisterServer()
{
    Diagnostics::Event(L"unregister_begin");
    ITfInputProcessorProfiles* profiles = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                                  IID_PPV_ARGS(&profiles));
    if (SUCCEEDED(hr))
    {
        HRESULT hrUn = profiles->Unregister(CLSID_TypeRightTSF);
        Diagnostics::Event(L"unregister_tip", hrUn);
        profiles->Release();
    }

    HRESULT hrCom = UnregisterComServer();
    Diagnostics::Event(L"unregister_com_server", hrCom);
    return hrCom;
}

} // namespace TypeRight
