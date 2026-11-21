using System;
using System.Collections.Generic;
using TypeRight.Domain.Models;
using TypeRight.Engine;
using TypeRight.Engine.CandidateGeneration;
using TypeRight.Engine.CorrectionDecision;

namespace TypeRight.TSFWrapper
{
    /// <summary>
    /// High-level correction engine accessible from TSF wrapper.
    /// Provides a simple interface to the correction decision engine.
    /// </summary>
    public class TypeRightEngine
    {
        private static readonly Lazy<ICorrectionDecisionMaker> DecisionMaker =
            new(() => EngineComposition.CreateDecisionMaker());

        private static readonly Lazy<ICandidateGenerator> CandidateGenerator =
            new(() => EngineComposition.CreateCandidateGenerator());

        /// <summary>
        /// Correct a word using the full decision pipeline.
        /// </summary>
        /// <param name="word">The word to correct.</param>
        /// <returns>The correction decision.</returns>
        public CorrectionDecision Correct(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return CorrectionDecision.LowConfidencePreserve("Word empty");

            try
            {
                var request = new CorrectionRequest(
                    completedWord: word,
                    adjacentLeft: Array.Empty<string>(),
                    adjacentRight: Array.Empty<string>(),
                    capitalization: CapitalizationInfo.FromOriginal(word),
                    languageVariant: LanguageVariant.Auto,
                    vocabularyVersion: new VocabularyVersion(2, 0));

                return DecisionMaker.Value.MakeDecision(request);
            }
            catch (Exception)
            {
                // Fail closed: a wrapper fault must never change the user's text.
                return CorrectionDecision.LowConfidencePreserve("engine_fault");
            }
        }

        /// <summary>
        /// Generate candidate corrections for a misspelled word.
        /// </summary>
        /// <param name="misspelled">The word that may be misspelled.</param>
        /// <param name="maxCandidates">Maximum number of candidates.</param>
        /// <returns>List of candidate results.</returns>
        public IReadOnlyList<CandidateResult> GenerateCandidates(string misspelled, int maxCandidates = 10)
        {
            if (string.IsNullOrWhiteSpace(misspelled) || maxCandidates <= 0)
                return Array.Empty<CandidateResult>();

            try
            {
                return CandidateGenerator.Value.GenerateCandidates(
                    misspelled,
                    Array.Empty<string>(),
                    LanguageVariant.Auto,
                    maxCandidates);
            }
            catch (Exception)
            {
                return Array.Empty<CandidateResult>();
            }
        }

        /// <summary>
        /// Get the current vocabulary version.
        /// </summary>
        public VocabularyVersion VocabularyVersion => new(2, 0);

        /// <summary>
        /// Get language variant from TSF context.
        /// </summary>
        /// <param name="pRangePointer">TSF range pointer.</param>
        public LanguageVariant GetLanguageVariantFromContext(IntPtr pRangePointer)
        {
            // The native TSF text service resolves the thread language before
            // sending a request; this legacy shim cannot inspect an ITfRange.
            return LanguageVariant.Auto;
        }
    }
}
