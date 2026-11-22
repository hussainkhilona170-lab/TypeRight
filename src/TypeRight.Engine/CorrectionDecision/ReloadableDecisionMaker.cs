#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using TypeRight.Domain.Models;

namespace TypeRight.Engine.CorrectionDecision;

/// <summary>
/// A correction decision maker whose inner pipeline can be recomposed at runtime.
/// Existing holders keep the same stable instance while personal vocabulary changes.
/// </summary>
public interface IReloadableDecisionMaker : ICorrectionDecisionMaker
{
    /// <summary>Rebuilds the pipeline using the current personal-word provider.</summary>
    void Reload();
}

/// <summary>
/// Thread-safe reloadable wrapper around <see cref="EngineComposition.CreateDecisionMaker()"/>.
/// Provider faults fall back to the base vocabulary so a local store failure can never
/// stop correction or surface user data.
/// </summary>
public sealed class ReloadableDecisionMaker : IReloadableDecisionMaker
{
    private readonly Func<IEnumerable<string>> _personalWordsProvider;
    private ICorrectionDecisionMaker _inner;

    public ReloadableDecisionMaker(Func<IEnumerable<string>> personalWordsProvider = null)
    {
        _personalWordsProvider = personalWordsProvider;
        Reload();
    }

    public void Reload()
    {
        IEnumerable<string> personalWords;
        try
        {
            personalWords = _personalWordsProvider?.Invoke() ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            personalWords = Array.Empty<string>();
        }

        Volatile.Write(ref _inner, EngineComposition.CreateDecisionMaker(personalWords));
    }

    public TypeRight.Domain.Models.CorrectionDecision MakeDecision(CorrectionRequest request)
    {
        var decisionMaker = Volatile.Read(ref _inner);
        return decisionMaker == null
            ? TypeRight.Domain.Models.CorrectionDecision.LowConfidencePreserve("engine_not_ready")
            : decisionMaker.MakeDecision(request);
    }
}
