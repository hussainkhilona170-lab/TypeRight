using System;
using System.Collections.Generic;
using System.Linq;

namespace TypeRight.Suggestions
{
    /// <summary>
    /// Feature inventory modelled on the observed agent switches, reduced to
    /// what a local-only Windows typing companion can honestly provide.
    /// Everything requiring a cloud service stays off by default.
    /// </summary>
    public enum FeatureKey
    {
        Proofreader,
        Clarity,
        Delivery,
        Engagement,
        PersonalDictionary,
        SensitivityGate,
        LocalPhraseRewriter,
        Plagiarism,
        AiDetector,
        Humanizer,
        GenerativeRewrite,
        Citations,
        Translation,
        ToneDetection,
        ResumeBuilder,
        StyleGuideSync,
        AuthorshipMetadata
    }

    /// <summary>
    /// Local, per-user feature gates. Defaults are conservative: only the
    /// offline proofreader and its local suggestion categories are enabled.
    /// </summary>
    public sealed class LocalFeatureGates
    {
        private static readonly Dictionary<FeatureKey, bool> Defaults =
            new Dictionary<FeatureKey, bool>
            {
                [FeatureKey.Proofreader] = true,
                [FeatureKey.Clarity] = true,
                [FeatureKey.Delivery] = true,
                [FeatureKey.Engagement] = true,
                [FeatureKey.PersonalDictionary] = true,
                [FeatureKey.SensitivityGate] = true,

                [FeatureKey.LocalPhraseRewriter] = false,
                [FeatureKey.Plagiarism] = false,
                [FeatureKey.AiDetector] = false,
                [FeatureKey.Humanizer] = false,
                [FeatureKey.GenerativeRewrite] = false,
                [FeatureKey.Citations] = false,
                [FeatureKey.Translation] = false,
                [FeatureKey.ToneDetection] = false,
                [FeatureKey.ResumeBuilder] = false,
                [FeatureKey.StyleGuideSync] = false,
                [FeatureKey.AuthorshipMetadata] = false
            };

        private readonly Dictionary<FeatureKey, bool> _overrides =
            new Dictionary<FeatureKey, bool>();

        public bool IsEnabled(FeatureKey key)
        {
            return _overrides.TryGetValue(key, out bool value) ? value : Defaults[key];
        }

        public void Set(FeatureKey key, bool enabled) => _overrides[key] = enabled;

        public void Reset() => _overrides.Clear();

        /// <summary>
        /// Cloud-dependent features can never be enabled locally, regardless
        /// of overrides. This keeps the local-first guarantee enforceable.
        /// </summary>
        public bool IsLocallyAvailable(FeatureKey key)
        {
            switch (key)
            {
                case FeatureKey.Plagiarism:
                case FeatureKey.AiDetector:
                case FeatureKey.Humanizer:
                case FeatureKey.GenerativeRewrite:
                case FeatureKey.Citations:
                case FeatureKey.Translation:
                case FeatureKey.ResumeBuilder:
                case FeatureKey.StyleGuideSync:
                    return false;
                default:
                    return true;
            }
        }

        public bool IsActive(FeatureKey key) => IsLocallyAvailable(key) && IsEnabled(key);

        public IReadOnlyDictionary<FeatureKey, bool> Snapshot()
        {
            return Enum.GetValues(typeof(FeatureKey))
                .Cast<FeatureKey>()
                .ToDictionary(k => k, IsActive);
        }
    }
}
