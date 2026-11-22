using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace TypeRight.Privacy
{
    /// <summary>
    /// Local mirror of the enterprise policy surface. The shape follows the
    /// observed managed-policy schema: enrollment token, blocked clients,
    /// blocked domains, confidential mode, DLP, and integration mode.
    /// </summary>
    public sealed class EnterprisePolicy
    {
        private const string PolicyKey = @"SOFTWARE\Policies\TypeRight";

        public string EnrollmentToken { get; set; }
        public string BlockedClients { get; set; }
        public string BlockedDomains { get; set; }
        public bool ConfidentialModeEnabled { get; set; }
        public bool DlpEnabled { get; set; }
        public string IntegrationMode { get; set; } = "full";

        public static EnterprisePolicy Load()
        {
            var policy = new EnterprisePolicy();
            // Machine policy wins; per-user policy is the fallback.
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using (var key = hive.OpenSubKey(PolicyKey))
                {
                    if (key == null) continue;
                    policy.EnrollmentToken = ReadString(key, "TypeRightEnrollmentToken", policy.EnrollmentToken);
                    policy.BlockedClients = ReadString(key, "TypeRightBlockedClients", policy.BlockedClients);
                    policy.BlockedDomains = ReadString(key, "TypeRightBlockedDomains", policy.BlockedDomains);
                    policy.ConfidentialModeEnabled = ReadBool(key, "TypeRightConfidentialModeEnabled", policy.ConfidentialModeEnabled);
                    policy.DlpEnabled = ReadBool(key, "TypeRightDlpEnabled", policy.DlpEnabled);
                    policy.IntegrationMode = ReadString(key, "TypeRightIntegrationMode", policy.IntegrationMode);
                }
            }
            return policy;
        }

        private static string ReadString(RegistryKey key, string name, string fallback)
        {
            object value = key.GetValue(name);
            return value == null ? fallback : value.ToString();
        }

        private static bool ReadBool(RegistryKey key, string name, bool fallback)
        {
            object value = key.GetValue(name);
            if (value is int i) return i != 0;
            if (value is string s && bool.TryParse(s, out bool parsed)) return parsed;
            return fallback;
        }

        public HashSet<string> BlockedClientSet => SplitList(BlockedClients);
        public HashSet<string> BlockedDomainSet => SplitList(BlockedDomains);

        private static HashSet<string> SplitList(string value)
        {
            return new HashSet<string>(
                (value ?? string.Empty)
                    .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Combined policy decision. Every path defaults to "do not correct".
    /// </summary>
    public readonly struct PolicyDecision
    {
        public bool AllowCorrection { get; }
        public IReadOnlyList<string> Reasons { get; }

        public PolicyDecision(bool allowCorrection, IReadOnlyList<string> reasons)
        {
            AllowCorrection = allowCorrection;
            Reasons = reasons ?? Array.Empty<string>();
        }

        public static PolicyDecision Allow => new(true, Array.Empty<string>());
    }

    /// <summary>
    /// Enforces enterprise policy and integration rules before any correction
    /// is requested. No user text is stored or logged by this layer.
    /// </summary>
    public sealed class PolicyEvaluator
    {
        private readonly EnterprisePolicy _policy;
        private readonly IntegrationRuleset _rules;

        public PolicyEvaluator(EnterprisePolicy policy, IntegrationRuleset rules)
        {
            _policy = policy ?? new EnterprisePolicy();
            _rules = rules ?? IntegrationRuleset.LoadDefault();
        }

        /// <summary>
        /// True when any configured policy depends on process, domain or UI-element
        /// context. A request without that context must be denied instead of being
        /// evaluated against missing fields.
        /// </summary>
        public bool RequiresContext =>
            !string.IsNullOrEmpty(_policy.EnrollmentToken) ||
            !string.IsNullOrEmpty(_policy.BlockedClients) ||
            !string.IsNullOrEmpty(_policy.BlockedDomains) ||
            _policy.ConfidentialModeEnabled ||
            _policy.DlpEnabled ||
            !string.Equals(_policy.IntegrationMode, "full", StringComparison.OrdinalIgnoreCase);

        // Count-only accessors for local diagnostics. They never expose policy values.
        public int BlockedClientCount => _policy.BlockedClientSet.Count;
        public int BlockedDomainCount => _policy.BlockedDomainSet.Count;
        public int RuleCount => _rules?.RuleCount ?? 0;
        public int SensitivityRuleCount => _rules?.SensitivityRuleCount ?? 0;

        public PolicyDecision Evaluate(string processName, string domain, string focusedElement)
        {
            var reasons = new List<string>();

            if (_policy.BlockedClientSet.Contains(processName ?? string.Empty))
                reasons.Add("client_blocked_by_policy");

            if (!string.IsNullOrEmpty(domain) && _policy.BlockedDomainSet.Contains(domain))
                reasons.Add("domain_blocked_by_policy");

            if (_policy.IntegrationMode == "limited" && !string.IsNullOrEmpty(domain))
                reasons.Add("limited_integration_mode");

            if (_policy.EnrollmentToken != null && _policy.BlockedClientSet.Count == 0 &&
                string.IsNullOrEmpty(_policy.EnrollmentToken))
                reasons.Add("enrollment_required");

            if (reasons.Count == 0 && _policy.DlpEnabled)
            {
                var decision = _rules.Evaluate(focusedElement);
                if (!decision.AllowCorrection)
                    reasons.Add(decision.IsSensitive ? "sensitive_field" : "integration_blocked");
            }

            return reasons.Count == 0
                ? PolicyDecision.Allow
                : new PolicyDecision(false, reasons);
        }

        public IReadOnlyList<string> DescribeRules() => new[]
        {
            "rules=" + _rules.RuleCount,
            "sensitivity_rules=" + _rules.SensitivityRuleCount,
            "dlp=" + (_policy.DlpEnabled ? "on" : "off"),
            "confidential_mode=" + (_policy.ConfidentialModeEnabled ? "on" : "off"),
            "integration_mode=" + _policy.IntegrationMode
        };
    }
}
