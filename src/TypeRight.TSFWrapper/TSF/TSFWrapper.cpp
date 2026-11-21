#include "pch.h"
#include "TypeRight.TSFWrapper.h"
#include <msctf.h>
#include <iostream>

// Helper macro for checking HRESULTs
#define HR(x) { HRESULT __hr = (x); if (FAILED(__hr)) { OutputDebugStringA(#"FAILED: "__hr); } }

using namespace System;
using namespace System::Runtime::InteropServices;
using namespace TypeRight::Engine;

namespace TypeRightTSFWrapper
{
    // Forward declarations
    class TSFApplier;
    
    // TSF Composition Sink - receives composition events from TSF
    class TSFApplier : public ITfCompositionSink
    {
    private:
        ULONG _cRef;
        TypeRightEngine^ _engine;
        ITfCompositionViewSink^ _sink;
        ITfDocumentMgr^ _docMgr;
        DWORD _adviseSink;

    public:
        TSFApplier(TypeRightEngine^ engine)
            : _cRef(1), _engine(engine), _adviseSink(0)
        {
        }

        // IUnknown
        STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
        {
            *ppv = NULL;
            if (riid == IID_IUnknown || riid == IID_ITfCompositionSink)
            {
                *ppv = this;
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        STDMETHODIMP_(ULONG) AddRef(void)
        {
            return InterlockedIncrement(&_cRef);
        }

        STDMETHODIMP_(ULONG) Release(void)
        {
            ULONG cRef = InterlockedDecrement(&_cRef);
            if (cRef == 0)
                delete this;
            return cRef;
        }

        // ITfCompositionSink
        STDMETHODIMP OnStartComposition(ITfComposition* pComposition)
        {
            // Called when composition starts (user typing a word that might be corrected)
            if (!pComposition) return E_INVALIDARG;

            // Get the composition range
            CComPtr<ITfRange> spRange;
            HRESULT hr = pComposition->GetRange(&spRange);
            if (FAILED(hr)) return hr;

            // Get the text from the range
            BSTR bstrText = nullptr;
            hr = spRange->GetText(GETTEXT_STYLE_DEFAULT, 0, &bstrText, nullptr);
            if (SUCCEEDED(hr) && bstrText && *bstrText)
            {
                // Marshal BSTR to .NET string and pass to .NET engine
                String^ managedText = Marshal::PtrToStringAuto((IntPtr)bstrText);
                
                // Evaluate the composition - get correction decision
                CorrectionDecision decision = _engine->Correct(managedText);
                
                // Store the decision for later commit (in OnEndComposition)
                // TODO: Also query candidate results if needed for UI update
                
                OutputDebugStringA(("Corrected: " + marshal_as<std::string>(managedText) + " -> " + 
                    marshal_as<std::string>(decision.CorrectedWord ? marshal_as<LPCWSTR>(decision.CorrectedWord) : L"null")).c_str());
            }
            SysFreeString(bstrText);

            return S_OK;
        }

        STDMETHODIMP OnUpdateComposition(ITfComposition* pComposition)
        {
            // Called when composition updates (user typing, TSF fixing, etc.)
            if (!pComposition) return E_INVALIDARG;

            // Get the composition text and re-evaluate
            CComPtr<ITfRange> spRange;
            HRESULT hr = pComposition->GetRange(&spRange);
            if (FAILED(hr)) return hr;

            BSTR bstrText = nullptr;
            hr = spRange->GetText(GETTEXT_STYLE_DEFAULT, 0, &bstrText, nullptr);
            if (SUCCEEDED(hr) && bstrText && *bstrText)
            {
                String^ managedText = Marshal::PtrToStringAuto((IntPtr)bstrText);
                CorrectionDecision decision = _engine->Correct(managedText);
                
                // TODO: Update suggestion overlay with new candidates
                OutputDebugStringA(("Updated: " + marshal_as<std::string>(marshal_as<LPCWSTR>(managedText)) + 
                    " confidence=" + decision.Confidence.ToString()).c_str());
            }
            SysFreeString(bstrText);

            return S_OK;
        }

        STDMETHODIMP OnEndComposition(ITfComposition* pComposition)
        {
            // Called when composition ends (user accepts via Space, Enter, or UI commit)
            // Commit the final corrected text into the document at the composition range
            if (!pComposition) return E_INVALIDARG;

            // TODO: Full implementation would:
            // 1. Get the corrected string from stored decision
            // 2. Create an ITfRange for the composition range
            // 3. Use ITfRange::SetText to replace with corrected text
            // 4. Fire the appropriate TSF events (selection changed, etc.)
            // 5. Hide the suggestion overlay

            // For now, just output debug info and unadvise the sink
            OutputDebugStringA(L"Composition committed - replacing with corrected text");
            
            // Unadvise the sink so it doesn't keep firing
            if (_docMgr && _adviseSink != 0)
            {
                _docMgr->UnadviseSink(_adviseSink);
                _adviseSink = 0;
            }

            return S_OK;
        }

        STDMETHODIMP OnSetSelection(ITfRange* pRange, ITfRange* pSel, DWORD dwSSIR, BOOL fActive)
        {
            // Called when selection changes
            // Can clear suggestions when user moves to a new word
            return S_OK;
        }

        // Helper to register the sink with TSF document manager
        HRESULT RegisterWithDocumentMgr(ITfDocumentMgr* pDocMgr)
        {
            if (!pDocMgr) return E_INVALIDARG;
            
            _docMgr = pDocMgr;
            // Advise the composition sink
            // TF_ADVISE_EDIT tells TSF we want to be notified of composition events
            HRESULT hr = _docMgr->AdviseSink(TF_ADVISE_EDIT, (ITfCompositionSink*)this, &_adviseSink);
            return hr;
        }

        // Helper to unregister the sink
        void UnregisterFromDocumentMgr()
        {
            if (_docMgr && _adviseSink != 0)
            {
                _docMgr->UnadviseSink(_adviseSink);
                _adviseSink = 0;
                _docMgr = nullptr;
            }
        }
    };

    // Main TSF wrapper class
    public ref class TSFIntegration
    {
    private:
        bool _initialized;
        TypeRightEngine^ _engine;
        ITfCompositionSink^ _sink;
        ITfDocumentMgr^ _docMgr;
        DWORD _adviseSink;

    public:
        TSFIntegration()
        {
            _initialized = false;
            _engine = nullptr;
            _sink = nullptr;
            _docMgr = nullptr;
            _adviseSink = 0;
        }

        ~TSFIntegration()
        {
            this->!TSFIntegration();
        }

        !TSFIntegration()
        {
            // Unmanaged cleanup - call native sink unregister
            if (_sink != nullptr)
            {
                // Cast to native and unregister
                // This is called from the unmanaged side via the TSFApplier::UnregisterFromDocumentMgr method
                _sink = nullptr;
            }
            if (_docMgr != nullptr)
            {
                _docMgr = nullptr;
            }
            CoUninitialize();
            _initialized = false;
        }

        bool Initialize()
        {
            if (_initialized)
                return true;

            // Create the correction engine
            _engine = gcnew TypeRightEngine();
            if (_engine == nullptr)
                return false;

            // Initialize TSF
            HR(CoInitializeEx(NULL, COINIT_MULTITHREADED));

            _initialized = true;
            return true;
        }

        void StartListening()
        {
            if (!_initialized)
                Initialize();

            // Create the composition sink
            _sink = gcnew TSFApplier(_engine);

            // Obtain the ITfDocumentMgr from the active TSF view
            // and register the composition sink
            if (_docMgr != nullptr)
            {
                HRESULT hr = _sink->RegisterWithDocumentMgr(_docMgr);
                if (FAILED(hr))
                {
                    OutputDebugStringA((L"TSF RegisterWithDocumentMgr failed: 0x" +
                        std::to_string(hr).c_str()).c_str());
                }
                else
                {
                    OutputDebugStringA(L"TSF sink registered with Document Manager - listening for composition events");
                }
            }
            else
            {
                OutputDebugStringA(L"TSF start listening: No ITfDocumentMgr available - sink not registered");
            }

            OutputDebugStringA("TSF integration started - listening for composition events");
        }

        void StopListening()
        {
            if (_sink != nullptr)
            {
                // Unregister from TSF first (native side handles this)
                _sink->Release();
                _sink = nullptr;
            }
            CoUninitialize();
        }

        // Submit a word for correction through the engine
        CorrectedWord^ CorrectWord(String^ word, LanguageVariant variant)
        {
            // Create correction request
            // Use the engine's decision maker
            return _engine->Correct(word, variant);
        }
    };
}

// Factory function for creating the TSF integration object
TSFIntegration^ CreateTSFIntegration()
{
    return gcnew TSFIntegration();
}