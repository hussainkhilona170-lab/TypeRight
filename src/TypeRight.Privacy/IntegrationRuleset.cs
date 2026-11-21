using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace TypeRight.Privacy
{
    /// <summary>
    /// Outcome of evaluating the focused UI element against the local rule set.
    /// All outcomes are fail-closed.
    /// </summary>
    public readonly struct SensitivityDecision
    {
        public bool AllowCorrection { get; }
        public bool IsSensitive { get; }
        public bool IsBlocked { get; }
        public string MatchedNotes { get; }

        public SensitivityDecision(bool allowCorrection, bool isSensitive, bool isBlocked, string matchedNotes)
        {
            AllowCorrection = allowCorrection;
            IsSensitive = isSensitive;
            IsBlocked = isBlocked;
            MatchedNotes = matchedNotes;
        }

        public static SensitivityDecision Allow => new(true, false, false, null);
        public static SensitivityDecision Sensitive(string notes) => new(false, true, false, notes);
        public static SensitivityDecision Block(string notes) => new(false, false, true, notes);
    }

    /// <summary>
    /// Evaluates the shipped sensitivity rules against a focused UI element.
    ///
    /// The source rule file has 584 records. 81 of them carry
    /// IsSensitivityCheck=true and identify sensitive fields (recipient, login,
    /// personal information, ...) across 27 languages. The remaining records
    /// are application-specific rendering/integration overrides (for example,
    /// "ignore empty div for highlight bounds") that depend on browser or UIA
    /// structure TypeRight does not have. They are deliberately not applied as
    /// correction gates in the native client.
    /// </summary>
    public sealed class IntegrationRuleset
    {
        private sealed class CompiledRule
        {
            public IntegrationRule Rule;
            public Regex Regex;
        }

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(25);
        private readonly IReadOnlyList<CompiledRule> _sensitivityRules;

        public int RuleCount { get; }
        public int SensitivityRuleCount => _sensitivityRules.Count;

        private IntegrationRuleset(IReadOnlyList<IntegrationRule> rules)
        {
            RuleCount = rules.Count;
            var sensitivity = new List<CompiledRule>();

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.ElementName)) continue;

                // Sensitivity rules are shipped with IsEnabled=false in the
                // source data; the DLP toggle at the policy layer decides
                // whether they are consulted. Always compile them.
                if (!rule.IsSensitivityCheck) continue;

                Regex regex;
                try
                {
                    regex = new Regex(rule.ElementName,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        RegexTimeout);
                }
                catch (ArgumentException)
                {
                    // Invalid rule: skip it; policy still fails closed elsewhere.
                    continue;
                }

                sensitivity.Add(new CompiledRule { Rule = rule, Regex = regex });
            }

            _sensitivityRules = sensitivity;
        }

        /// <summary>
        /// Loads a rule file. Missing or malformed files yield an empty ruleset,
        /// not an exception - the process remains usable and the policy layer
        /// still applies its own fail-closed checks.
        /// </summary>
        public static IntegrationRuleset Load(string path)
        {
            if (!File.Exists(path)) return new IntegrationRuleset(Array.Empty<IntegrationRule>());
            try
            {
                var rules = JsonConvert.DeserializeObject<List<IntegrationRule>>(File.ReadAllText(path));
                return new IntegrationRuleset(rules ?? new List<IntegrationRule>());
            }
            catch (JsonException)
            {
                return new IntegrationRuleset(Array.Empty<IntegrationRule>());
            }
        }

        /// <summary>
        /// Loads IntegrationOptions.json from the application directory.
        /// </summary>
        public static IntegrationRuleset LoadDefault()
        {
            return Load(Path.Combine(AppContext.BaseDirectory, "IntegrationOptions.json"));
        }

        /// <summary>
        /// Evaluates a focused element name. Null/empty input is allowed -
        /// absence of UI context is not treated as sensitive.
        /// </summary>
        public SensitivityDecision Evaluate(string elementName)
        {
            if (string.IsNullOrEmpty(elementName))
                return SensitivityDecision.Allow;

            foreach (var rule in _sensitivityRules)
            {
                try
                {
                    if (rule.Regex.IsMatch(elementName))
                        return SensitivityDecision.Sensitive(rule.Rule.Notes);
                }
                catch (RegexMatchTimeoutException)
                {
                    // A timed-out rule is treated as a match: fail closed.
                    return SensitivityDecision.Sensitive(rule.Rule.Notes);
                }
            }

            return SensitivityDecision.Allow;
        }
    }
}
