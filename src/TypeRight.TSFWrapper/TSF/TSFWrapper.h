#pragma once

#include <msctf.h>
#include <iostream>

using namespace System;
using namespace System::Runtime::InteropServices;

// Forward declaration for the managed class
ref class TSFIntegration;

// Helper macro for converting between native and managed strings
inline std::wstring marshal_ws(const String^ s)
{
    if (s == nullptr) return std::wstring();
    int len = WideCharToMultiByte(CP_UTF8, 0, s->Data(), -1, NULL, 0, NULL, NULL);
    std::string narrow(len, 0);
    WideCharToMultiByte(CP_UTF8, 0, s->Data(), -1, &narrow[0], len, NULL, NULL);
    return std::wstring(narrow.begin(), narrow.end());
}

inline String^ marshal_cw(const std::wstring& w)
{
    if (w.empty()) return nullptr;
    int len = WideCharToMultiByte(CP_UTF8, 0, reinterpret_cast<const wchar_t*>(w.c_str()), -1, NULL, 0, NULL, NULL);
    System::String^ result = gcnew System::String(nullptr, len - 1, len - 1);
    WideCharToMultiByte(CP_UTF8, 0, reinterpret_cast<const wchar_t*>(w.c_str()), -1, 
                        result->Data(), len, NULL, NULL);
    return result;
}

/// <summary>
/// Native TSF wrapper class that implements ITfCompositionSink
/// </summary>
public ref class TSFWrapper : public ITfCompositionSink
{
public:
    TSFWrapper(void);
    virtual ~TSFWrapper();

    // IUnknown methods
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv);
    STDMETHODIMP_(ULONG) AddRef(void);
    STDMETHODIMP_(ULONG) Release(void);

    // ITfCompositionSink methods
    STDMETHODIMP OnStartComposition(ITfComposition* pComposition);
    STDMETHODIMP OnUpdateComposition(ITfComposition* pComposition);
    STDMETHODIMP OnEndComposition(ITfComposition* pComposition);
    STDMETHODIMP OnSetSelection(ITfRange* pRange, ITfRange* pSel, DWORD dwSSIR, BOOL fActive);

    // Helper methods for sink registration
    HRESULT RegisterWithDocumentMgr(ITfDocumentMgr* pDocMgr);
    void UnregisterFromDocumentMgr(void);

private:
    ULONG _cRef;
    TypeRightEngine^ _engine;  // Managed engine reference
    ITfDocumentMgr^ _docMgr;   // Document manager for sink registration
    DWORD _adviseSink;         // Sink advice cookie
};