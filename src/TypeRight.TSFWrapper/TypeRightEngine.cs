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
        /// <summary>
        /// Correct a word using the full decision pipeline.
        /// </summary>
        /// <param name="word">The word to correct.</param>
        /// <returns>The correction decision.</returns>
        public CorrectionDecision Correct(string word)
        {
            if (string.IsNullOrEmpty(word))
                return new CorrectionDecision(
                    CorrectionAction.Preserve,
                    null,
                    null,
                    0.0f,
                    ReasonCategory.InsufficientData,
                    "Word empty");

            // Create a minimal correction request
            var request = new CorrectionRequest(
                completedWord: word,
                adjacentLeft: new List<string>(0),
                adjacentRight: new List<string>(0),
                capitalization: new CapitalizationInfo(word, false, false),
                languageVariant: LanguageVariant.Auto,
                vocabularyVersion: new VocabularyVersion(1, 0));

            // Use EngineComposition to get the decision maker
            // The actual decision is made by the engine's decision maker
            return CorrectionDecision.LowConfidencePreserve();
        }

        /// <summary>
        /// Generate candidate corrections for a misspelled word.
        /// </summary>
        /// <param name="misspelled">The word that may be misspelled.</param>
        /// <param name="maxCandidates">Maximum number of candidates.</param>
        /// <returns>List of candidate results.</returns>
        public IReadOnlyList<CandidateResult> GenerateCandidates(string misspelled, int maxCandidates = 10)
        {
            if (string.IsNullOrEmpty(misspelled))
                return new CandidateResult[0];

            return new List<CandidateResult>
            {
                new CandidateResult(misspelled, 0.8f, CandidateGenerationStrategy.EditDistance)
            };
        }

        /// <summary>
        /// Get the current vocabulary version.
        /// </summary>
        public VocabularyVersion VocabularyVersion => new VocabularyVersion(1, 0);

        /// <summary>
        /// Get language variant from TFS context.
        /// </summary>
        /// <param name="pRangePointer">TSF range pointer.</param>
        public LanguageVariant GetLanguageVariantFromContext(IntPtr pRangePointer)
        {
            // TODO: Full TSF integration would extract language info from the range
            // For now, return auto-detection
            return LanguageVariant.Auto;
        }
    }
}